using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace GorillaPhone.Video
{
    /// <summary>
    /// The Video app's engine. It starts the player's own Edge (or Chrome) with an isolated profile and drives it over the browser's
    /// private debugging pipe (no network socket): the page's picture arrives as screencast frames, the page's sound arrives through an
    /// injected script (the browser itself then stays silent), and taps, swipes and drags go back as input events. The browser is a
    /// separate window on the desktop (needed for a first login); it keeps rendering while the game covers it because of the launch flags.
    /// Everything slow runs on background threads; the public members are safe to call from the game's main thread and never wait.
    /// Pure System code (no Unity), so it is tested outside the game. See the kit's reference 04 for how it was found to work.
    /// </summary>
    public sealed class BrowserHost
    {
        public enum State { Idle, Starting, Ready, Failed }

        public sealed class Options
        {
            public string StartUrl = "https://www.tiktok.com/foryou";
            public string BrowserPath = "";
            public string ProfileDir;          // the browser's own profile (a login persists here)
            public string PidFile;             // remembers the browser we started, so a leftover one can be closed next time
            public int ViewW = 540, ViewH = 711;   // the page's size in CSS pixels
            public int FrameW = 540;               // width of the pictures sent back
            public bool Mobile;
            public bool HookAudio = true;
            public int WindowW = 560, WindowH = 1000;
            public int FrameIntervalMs = 33;       // the browser is asked for no more than one picture per this many milliseconds
            public int TapAssist = 24;             // a tap that misses snaps to a clickable thing within this many page pixels (0 = off)
        }

        readonly object sync = new object();
        readonly Queue<string> logs = new Queue<string>();
        readonly AudioRing ring = new AudioRing();
        State state = State.Idle;
        string message = "";
        Options opt;
        CdpClient cdp;
        Process proc;
        byte[] frame;
        long frameSeq;
        int framesRx, audioBlocks;
        volatile bool streaming, playing, attached, stopping, gotFrame;   // streaming = the picture is wanted, playing = the page's media and sound are wanted
        int streamTick;   // Environment.TickCount when the picture stream was last (re)started
        int nextAckTick;  // frames are acknowledged no faster than Options.FrameIntervalMs, which is what limits how many the browser sends
        IntPtr prevForeground;
        static bool exitHooked;

        public AudioRing Audio { get { return ring; } }
        public State Status { get { lock (sync) return state; } }
        public string Message { get { lock (sync) return message; } }
        public bool HasFrames { get { lock (sync) return frameSeq > 0; } }
        public int ViewW { get { return opt != null ? opt.ViewW : 0; } }
        public int ViewH { get { return opt != null ? opt.ViewH : 0; } }

        // ------------------------------------------------------------------ small public API

        /// <summary>Start the browser on a background thread. Does nothing if it is already starting or running.</summary>
        public void Start(Options options)
        {
            lock (sync)
            {
                if (state == State.Starting || state == State.Ready) return;
                state = State.Starting;
                message = "Starting the browser...";
                opt = options;
                stopping = false;
                attached = false;
                frame = null;
                frameSeq = 0;
            }
            ring.Clear();
            prevForeground = GetForegroundWindow();
            HookProcessExit();
            new Thread(Run) { IsBackground = true, Name = "GorillaPhone browser start" }.Start();
        }

        /// <summary>Close the browser and forget the connection.</summary>
        public void Stop()
        {
            stopping = true;
            attached = false;
            streaming = false;
            CdpClient c = cdp;
            cdp = null;
            if (c != null) { try { c.Dispose(); } catch { } }
            KillBrowser();
            lock (sync) { if (state != State.Failed) { state = State.Idle; message = ""; } }
        }

        /// <summary>Picture and playing together (the Video page is open and the screen is on).</summary>
        public void SetStreaming(bool on)
        {
            SetPicture(on);
            SetPlaying(on);
        }

        /// <summary>Send or stop the picture (the phone's screen is on and shows the page).</summary>
        public void SetPicture(bool on)
        {
            if (streaming == on) return;
            streaming = on;
            ApplyPicture();
        }

        /// <summary>Let the page's media play and its sound reach the phone, or pause them and drop the sound. Independent of the picture: a phone on the ground keeps playing.</summary>
        public void SetPlaying(bool on)
        {
            if (playing == on) return;
            playing = on;
            if (!on) ring.Clear();
            ApplyPlaying();
        }

        public bool TryTakeFrame(ref long seq, out byte[] jpg)
        {
            lock (sync)
            {
                if (frame == null || frameSeq == seq) { jpg = null; return false; }
                seq = frameSeq;
                jpg = frame;
                return true;
            }
        }

        public bool TryDequeueLog(out string line)
        {
            lock (sync)
            {
                if (logs.Count == 0) { line = null; return false; }
                line = logs.Dequeue();
                return true;
            }
        }

        /// <summary>Frames and sound blocks received since the last call.</summary>
        public void TakeStats(out int frames, out int blocks)
        {
            frames = Interlocked.Exchange(ref framesRx, 0);
            blocks = Interlocked.Exchange(ref audioBlocks, 0);
        }

        // ------------------------------------------------------------------ input (viewport CSS pixels)

        static string N(double v) { return v.ToString("0.#", CultureInfo.InvariantCulture); }

        void Mouse(string type, int x, int y, string button, int buttons, int clickCount)
        {
            CdpClient c = cdp;
            if (c == null || !attached) return;
            c.Post("Input.dispatchMouseEvent", "{\"type\":\"" + type + "\",\"x\":" + x + ",\"y\":" + y + ",\"button\":\"" + button + "\",\"buttons\":" + buttons +
                                                 ",\"clickCount\":" + clickCount + "}");
        }

        public void MouseMove(int x, int y, bool down) { Mouse("mouseMoved", x, y, down ? "left" : "none", down ? 1 : 0, 0); }
        public void MouseDown(int x, int y) { Mouse("mouseMoved", x, y, "none", 0, 0); Mouse("mousePressed", x, y, "left", 1, 1); }
        public void MouseUp(int x, int y) { Mouse("mouseReleased", x, y, "left", 0, 1); }
        /// <summary>
        /// A click. With TapAssist on, a tap that lands on nothing clickable first snaps to the nearest clickable spot within a few pixels
        /// (small buttons such as a popup's close cross are hard to hit with a finger in VR); the page decides what is clickable (a pointer cursor).
        /// </summary>
        public void Tap(int x, int y)
        {
            Options o = opt;
            if (o == null || o.TapAssist <= 0) { MouseDown(x, y); MouseUp(x, y); return; }
            ThreadPool.QueueUserWorkItem(delegate
            {
                int cx = x, cy = y;
                try
                {
                    string s = Eval("window.__gpSnap?window.__gpSnap(" + x + "," + y + "," + o.TapAssist + "):''", 800);
                    int comma = s == null ? -1 : s.IndexOf(',');
                    int sx, sy;
                    if (comma > 0 && int.TryParse(s.Substring(0, comma), out sx) && int.TryParse(s.Substring(comma + 1), out sy)) { cx = sx; cy = sy; }
                }
                catch (CdpException) { }   // no answer in time: click where the finger was
                if (cx != x || cy != y) Log("tap " + x + "," + y + " snapped to " + cx + "," + cy);
                MouseDown(cx, cy);
                MouseUp(cx, cy);
            });
        }

        public void Wheel(int x, int y, double dx, double dy)
        {
            CdpClient c = cdp;
            if (c == null || !attached) return;
            c.Post("Input.dispatchMouseEvent", "{\"type\":\"mouseWheel\",\"x\":" + x + ",\"y\":" + y + ",\"deltaX\":" + N(dx) + ",\"deltaY\":" + N(dy) + "}");
        }

        /// <summary>A key press. name is the DOM key name (ArrowDown), vk the Windows virtual key code (40).</summary>
        public void Key(string name, int vk)
        {
            CdpClient c = cdp;
            if (c == null || !attached) return;
            string common = "\"key\":\"" + name + "\",\"code\":\"" + name + "\",\"windowsVirtualKeyCode\":" + vk + ",\"nativeVirtualKeyCode\":" + vk;
            c.Post("Input.dispatchKeyEvent", "{\"type\":\"rawKeyDown\"," + common + "}");
            c.Post("Input.dispatchKeyEvent", "{\"type\":\"keyUp\"," + common + "}");
        }

        /// <summary>Run a script in the page and return its string result. Waits for the answer: background threads only (tests and diagnostics).</summary>
        public string Eval(string expression, int timeoutMs = 10000)
        {
            CdpClient c = cdp;
            if (c == null || !attached) return null;
            var r = c.Call("Runtime.evaluate", "{\"expression\":" + MiniJson.Quote(expression) + ",\"returnByValue\":true}", true, timeoutMs);
            return MiniJson.Str(MiniJson.Obj(r, "result"), "value");
        }

        /// <summary>Restart the picture stream. A screencast can be lost when the page swaps to a new process while it navigates; this brings it back.</summary>
        public void KickStream()
        {
            CdpClient c = cdp;
            if (c == null || !attached || !streaming || c.IsClosed) return;
            Options o = opt;
            gotFrame = false;
            streamTick = Environment.TickCount;
            c.Post("Page.stopScreencast", "{}");
            c.Post("Page.startScreencast", "{\"format\":\"jpeg\",\"quality\":70,\"maxWidth\":" + o.FrameW + ",\"maxHeight\":4000,\"everyNthFrame\":1}");
        }

        /// <summary>
        /// The first screencast can be lost while the page is still swapping to a new process (a fresh profile redirects and loads slowly:
        /// found by the desktop test, no pictures for 30 s until the stream was toggled). Until a picture arrives, restart the stream every 1.5 s.
        /// </summary>
        void WatchStream(CdpClient c)
        {
            while (!stopping && !c.IsClosed)
            {
                Thread.Sleep(1000);
                if (!streaming || !attached || gotFrame) continue;
                if (unchecked(Environment.TickCount - streamTick) < 1500) continue;
                Log("no picture " + (unchecked(Environment.TickCount - streamTick) / 1000) + " s after the stream started, restarting it");
                KickStream();
            }
        }

        public void NextVideo() { Key("ArrowDown", 40); }
        public void PreviousVideo() { Key("ArrowUp", 38); }

        public void Reload()
        {
            CdpClient c = cdp;
            if (c != null && attached) c.Post("Page.reload", "{}");
        }

        /// <summary>Go back one page in the browser's history (the page has no back button of its own).</summary>
        public void Back()
        {
            CdpClient c = cdp;
            if (c == null || !attached) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var h = c.Call("Page.getNavigationHistory", "{}");
                    int cur = MiniJson.Int(h, "currentIndex", 0);
                    List<object> entries = MiniJson.List(h, "entries");
                    if (entries == null || cur <= 0 || cur - 1 >= entries.Count) { Log("back: nothing earlier in the history"); return; }
                    int id = MiniJson.Int(entries[cur - 1], "id", -1);
                    if (id >= 0) c.Call("Page.navigateToHistoryEntry", "{\"entryId\":" + id + "}");
                    Log("back: went to entry " + (cur - 1));
                }
                catch (Exception e) { Log("back failed: " + e.Message); }
            });
        }

        // ------------------------------------------------------------------ starting

        void Run()
        {
            try { Launch(); }
            catch (Exception e)
            {
                Log("failed: " + e);
                Fail(e.Message);
                KillBrowser();
            }
        }

        void Launch()
        {
            Options o = opt;
            string exe = FindBrowser(o.BrowserPath);
            if (exe == null) throw new InvalidOperationException("No Edge or Chrome was found. Install Microsoft Edge, or set BrowserPath under [Video] in the config.");
            Directory.CreateDirectory(o.ProfileDir);
            KillStale(o.PidFile);

            string args =
                "--user-data-dir=\"" + o.ProfileDir + "\" --no-first-run --no-default-browser-check --disable-sync " +
                "--disable-features=CalculateNativeWinOcclusion --disable-backgrounding-occluded-windows --disable-renderer-backgrounding " +
                "--autoplay-policy=no-user-gesture-required " +
                "--window-position=60,30 --window-size=" + o.WindowW + "," + o.WindowH + " --app=about:blank";
            Stream toBrowser, fromBrowser;
            proc = StartWithPipes(exe, args, out toBrowser, out fromBrowser);
            Log("browser started (" + exe + "), pid " + proc.Id);
            try
            {
                File.WriteAllText(o.PidFile, proc.Id + ";" + proc.StartTime.ToUniversalTime().Ticks);
            }
            catch { }

            cdp = new CdpClient(toBrowser, fromBrowser);
            cdp.Event += OnEvent;
            cdp.Closed += delegate { if (!stopping) Fail("The browser window was closed. Tap Reload to open it again."); };
            CdpClient c = cdp;

            string targetId = null;
            for (int tries = 0; tries < 60 && targetId == null; tries++)
            {
                if (stopping) return;
                try
                {
                    var t = c.Call("Target.getTargets", null, false, 3000);
                    List<object> infos = MiniJson.List(t, "targetInfos");
                    if (infos != null)
                        foreach (object ti in infos)
                            if (MiniJson.Str(ti, "type") == "page") { targetId = MiniJson.Str(ti, "targetId"); break; }
                }
                catch (CdpException) { }
                if (targetId == null) Thread.Sleep(500);
            }
            if (targetId == null) throw new InvalidOperationException("The browser started but did not answer on its debugging pipe.");
            var att = c.Call("Target.attachToTarget", "{\"targetId\":" + MiniJson.Quote(targetId) + ",\"flatten\":true}", false);
            c.SessionId = MiniJson.Str(att, "sessionId");
            if (c.SessionId == null) throw new InvalidOperationException("The browser did not give a session for the page.");
            Log("attached");

            c.Call("Page.enable", "{}");
            c.Call("Runtime.enable", "{}");
            if (o.HookAudio)
            {
                c.Call("Runtime.addBinding", "{\"name\":\"" + PageScripts.AudioBinding + "\"}");
                c.Call("Page.addScriptToEvaluateOnNewDocument", "{\"source\":" + MiniJson.Quote(PageScripts.AudioHook) + "}");
            }
            if (o.TapAssist > 0) c.Call("Page.addScriptToEvaluateOnNewDocument", "{\"source\":" + MiniJson.Quote(PageScripts.TapAssist) + "}");
            double dsf = o.FrameW > o.ViewW ? o.FrameW / (double)o.ViewW : 1.0;
            c.Call("Emulation.setDeviceMetricsOverride", "{\"width\":" + o.ViewW + ",\"height\":" + o.ViewH + ",\"deviceScaleFactor\":" + N(dsf) + ",\"mobile\":" + (o.Mobile ? "true" : "false") + "}");
            if (o.Mobile) c.Call("Emulation.setUserAgentOverride", "{\"userAgent\":" + MiniJson.Quote(PageScripts.MobileUserAgent) + "}");

            var nav = c.Call("Page.navigate", "{\"url\":" + MiniJson.Quote(o.StartUrl) + "}", true, 30000);
            string navError = MiniJson.Str(nav, "errorText");
            if (navError != null) Log("the page could not load: " + navError);

            lock (sync) { state = State.Ready; message = navError != null ? "The page could not load (" + navError + "). Is the internet on?" : "Loading the page..."; }
            attached = true;
            ApplyStreaming();
            new Thread(delegate () { WatchStream(c); }) { IsBackground = true, Name = "GorillaPhone stream watchdog" }.Start();
            RestoreFocus();
        }

        void ApplyStreaming()
        {
            ApplyPicture();
            ApplyPlaying();
        }

        void ApplyPicture()
        {
            CdpClient c = cdp;
            if (c == null || !attached || c.IsClosed) return;
            if (streaming)
            {
                gotFrame = false;
                streamTick = Environment.TickCount;
                c.Post("Page.startScreencast", "{\"format\":\"jpeg\",\"quality\":70,\"maxWidth\":" + opt.FrameW + ",\"maxHeight\":4000,\"everyNthFrame\":1}");
            }
            else c.Post("Page.stopScreencast", "{}");
        }

        void ApplyPlaying()
        {
            CdpClient c = cdp;
            if (c == null || !attached || c.IsClosed || !opt.HookAudio) return;
            if (playing)
            {
                c.Post("Runtime.evaluate", "{\"expression\":" + MiniJson.Quote(PageScripts.Resume) + "}");
                // Log what the page's media are doing a moment later, so a report of "no sound" can be diagnosed from the log.
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        Thread.Sleep(1500);
                        if (playing && attached) Log("page media 1.5 s after playing started: " + Eval("window.__gpState?window.__gpState():'(no hook yet)'"));
                    }
                    catch { }
                });
            }
            else
            {
                c.Post("Runtime.evaluate", "{\"expression\":" + MiniJson.Quote(PageScripts.Pause) + "}");
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        Thread.Sleep(1500);
                        if (!playing && attached) Log("page media 1.5 s after pausing: " + Eval("window.__gpState?window.__gpState():'(no hook yet)'"));
                    }
                    catch { }
                });
            }
        }

        // ------------------------------------------------------------------ events from the browser (reader thread)

        void OnEvent(string method, Dictionary<string, object> p)
        {
            switch (method)
            {
                case "Page.screencastFrame":
                {
                    CdpClient c = cdp;
                    if (c != null)
                    {
                        // Without the ack the browser stops sending, so the ack is also the throttle: no faster than one per FrameIntervalMs.
                        string ack = "{\"sessionId\":" + MiniJson.Int(p, "sessionId", 0) + "}";
                        int now = Environment.TickCount;
                        int wait = unchecked(nextAckTick - now);
                        if (wait <= 0 || wait > 1000) { nextAckTick = now + opt.FrameIntervalMs; c.Post("Page.screencastFrameAck", ack); }
                        else
                        {
                            nextAckTick += opt.FrameIntervalMs;
                            ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(wait); c.Post("Page.screencastFrameAck", ack); });
                        }
                    }
                    string data = MiniJson.Str(p, "data");
                    if (data == null || !streaming) return;
                    byte[] jpg = Convert.FromBase64String(data);
                    lock (sync) { frame = jpg; frameSeq++; }
                    Interlocked.Increment(ref framesRx);
                    if (!gotFrame)
                    {
                        gotFrame = true;
                        Log("first picture " + unchecked(Environment.TickCount - streamTick) + " ms after the stream started");
                    }
                    break;
                }
                case "Runtime.bindingCalled":
                {
                    if (MiniJson.Str(p, "name") != PageScripts.AudioBinding || !playing) return;
                    string payload = MiniJson.Str(p, "payload");
                    int comma = payload == null ? -1 : payload.IndexOf(',');
                    if (comma <= 0) return;
                    double rate;
                    if (!double.TryParse(payload.Substring(0, comma), NumberStyles.Float, CultureInfo.InvariantCulture, out rate)) return;
                    byte[] pcm = Convert.FromBase64String(payload.Substring(comma + 1));
                    ring.Write(pcm, pcm.Length, (int)rate);
                    Interlocked.Increment(ref audioBlocks);
                    break;
                }
                case "Page.frameNavigated":
                {
                    // The main frame moved to a new page: a screencast started earlier may be gone, so start it again.
                    if (streaming && attached && MiniJson.Obj(p, "frame") != null && MiniJson.Str(MiniJson.Obj(p, "frame"), "parentId") == null) KickStream();
                    break;
                }
                case "Inspector.detached":
                case "Target.targetCrashed":
                    if (!stopping) Fail("The browser page stopped. Tap Reload to try again.");
                    break;
            }
        }

        void Fail(string why)
        {
            lock (sync) { state = State.Failed; message = why; }
            attached = false;
            Log("failed: " + why);
        }

        void Log(string line)
        {
            lock (sync)
            {
                if (logs.Count < 200) logs.Enqueue(line);
            }
        }

        // ------------------------------------------------------------------ finding, starting and stopping the browser process

        static string FindBrowser(string configured)
        {
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;
            string x86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            string pf = Environment.GetEnvironmentVariable("ProgramFiles");
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(x86)) candidates.Add(Path.Combine(x86, @"Microsoft\Edge\Application\msedge.exe"));
            if (!string.IsNullOrEmpty(pf)) candidates.Add(Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"));
            if (!string.IsNullOrEmpty(pf)) candidates.Add(Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"));
            if (!string.IsNullOrEmpty(x86)) candidates.Add(Path.Combine(x86, @"Google\Chrome\Application\chrome.exe"));
            if (!string.IsNullOrEmpty(local)) candidates.Add(Path.Combine(local, @"Google\Chrome\Application\chrome.exe"));
            foreach (string s in candidates) if (File.Exists(s)) return s;
            return null;
        }

        /// <summary>Close a browser this mod started in an earlier run that is still alive (the pid file also holds its start time, so a reused pid is never touched).</summary>
        void KillStale(string pidFile)
        {
            try
            {
                if (string.IsNullOrEmpty(pidFile) || !File.Exists(pidFile)) return;
                string[] parts = File.ReadAllText(pidFile).Split(';');
                int pid = int.Parse(parts[0], CultureInfo.InvariantCulture);
                long ticks = long.Parse(parts[1], CultureInfo.InvariantCulture);
                Process p = null;
                try { p = Process.GetProcessById(pid); } catch { }
                if (p != null && p.StartTime.ToUniversalTime().Ticks == ticks)
                {
                    Log("closing a leftover browser from an earlier run, pid " + pid);
                    TaskKill(pid);
                    p.WaitForExit(4000);
                }
            }
            catch { }
            finally { try { File.Delete(pidFile); } catch { } }
        }

        static void TaskKill(int pid)
        {
            try
            {
                Process.Start(new ProcessStartInfo("taskkill", "/PID " + pid + " /T /F") { UseShellExecute = false, CreateNoWindow = true });
            }
            catch { }
        }

        void KillBrowser()
        {
            Process p = proc;
            proc = null;
            if (p != null)
            {
                try { if (!p.HasExited) { TaskKill(p.Id); p.Kill(); } } catch { }
            }
            Options o = opt;
            if (o != null && !string.IsNullOrEmpty(o.PidFile)) { try { File.Delete(o.PidFile); } catch { } }
        }

        void HookProcessExit()
        {
            if (exitHooked) return;
            exitHooked = true;
            // The game closing must close the browser too (the pipe closing also ends it, this is the belt to those braces).
            AppDomain.CurrentDomain.ProcessExit += delegate { try { Stop(); } catch { } };
        }

        void RestoreFocus()
        {
            // The browser's window took focus when it opened; give it back to the game.
            IntPtr prev = prevForeground;
            if (prev == IntPtr.Zero) return;
            Thread.Sleep(800);
            if (GetForegroundWindow() == prev) { Log("focus stayed with the game"); return; }
            keybd_event(0x12, 0, 0, UIntPtr.Zero);   // a synthesized Alt press lets SetForegroundWindow through
            keybd_event(0x12, 0, 2, UIntPtr.Zero);
            bool ok = SetForegroundWindow(prev);
            Log("gave focus back to the game: " + ok);
        }

        // ------------------------------------------------------------------ CreateProcess with two inheritable pipes (the same on any runtime)

        [StructLayout(LayoutKind.Sequential)]
        struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; public int bInheritHandle; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct STARTUPINFO
        {
            public int cb;
            public string lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CreatePipe(out IntPtr read, out IntPtr write, ref SECURITY_ATTRIBUTES sa, int size);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetHandleInformation(IntPtr h, int mask, int flags);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CreateProcessW(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, int flags, IntPtr env, string dir,
                                          ref STARTUPINFO si, out PROCESS_INFORMATION pi);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        static Process StartWithPipes(string exe, string args, out Stream toBrowser, out Stream fromBrowser)
        {
            var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)), bInheritHandle = 1 };
            IntPtr aRead, aWrite, bRead, bWrite;
            if (!CreatePipe(out aRead, out aWrite, ref sa, 0)) throw new InvalidOperationException("CreatePipe failed (" + Marshal.GetLastWin32Error() + ")");
            if (!CreatePipe(out bRead, out bWrite, ref sa, 0)) throw new InvalidOperationException("CreatePipe failed (" + Marshal.GetLastWin32Error() + ")");
            // The browser gets aRead (it reads our commands) and bWrite (it writes its answers); our own ends must not be inherited.
            SetHandleInformation(aWrite, 1, 0);
            SetHandleInformation(bRead, 1, 0);

            var si = new STARTUPINFO { cb = Marshal.SizeOf(typeof(STARTUPINFO)), dwFlags = 1, wShowWindow = 4 };   // STARTF_USESHOWWINDOW, SW_SHOWNOACTIVATE
            var cmd = new StringBuilder("\"" + exe + "\" " + args + " --remote-debugging-pipe --remote-debugging-io-pipes=" + aRead.ToInt64() + "," + bWrite.ToInt64());
            PROCESS_INFORMATION pi;
            bool ok = CreateProcessW(null, cmd, IntPtr.Zero, IntPtr.Zero, true, 0, IntPtr.Zero, null, ref si, out pi);
            int err = Marshal.GetLastWin32Error();
            CloseHandle(aRead);    // the browser has its own copies now
            CloseHandle(bWrite);
            if (!ok)
            {
                CloseHandle(aWrite);
                CloseHandle(bRead);
                throw new InvalidOperationException("Could not start the browser (Windows error " + err + ").");
            }
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
            toBrowser = new FileStream(new SafeFileHandle(aWrite, true), FileAccess.Write, 4096, false);
            fromBrowser = new FileStream(new SafeFileHandle(bRead, true), FileAccess.Read, 4096, false);
            return Process.GetProcessById(pi.dwProcessId);
        }
    }
}
