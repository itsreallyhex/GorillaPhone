using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Logging;
using GorillaLocomotion;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace GorillaPhone.Diag
{
    /// <summary>
    /// Phase 0: read-only probes that answer the plan's open questions. Nothing here keeps state
    /// in the game; the temporary camera and textures are destroyed at the end. The report goes to
    /// BepInEx\GorillaPhone-phase0.txt.
    /// </summary>
    public sealed class PhaseZeroDiag
    {
        readonly ManualLogSource log;
        readonly StringBuilder sb = new StringBuilder();

        public PhaseZeroDiag(ManualLogSource log)
        {
            this.log = log;
        }

        void Line(string s) { sb.AppendLine(s); }
        void Head(string s) { sb.AppendLine(); sb.AppendLine("== " + s); }

        void Safe(string name, Action a)
        {
            try { a(); }
            catch (Exception e) { Line(name + " FAILED: " + e.GetType().Name + " " + e.Message); }
        }

        public IEnumerator Run()
        {
            Line("GorillaPhone phase 0 diagnostics, plugin " + PluginInfo.Version);

            Safe("environment", Environment_);
            Safe("controllers", Controllers);
            Safe("audio", Audio);
            Safe("layers", Layers);
            Safe("raycasts", Raycasts);
            Safe("types", Types);

            yield return MonoTests();

            Head("Frame times (ms per frame; stand still, about 20 seconds)");
            yield return Measure("baseline", 300, null);
            yield return CameraTests();
            yield return UploadTest();
            yield return Measure("baseline again", 300, null);

            Flush();
        }

        // ---------------------------------------------------------------- environment

        void Environment_()
        {
            Head("Environment");
            Line("unity: " + Application.unityVersion);
            Line("graphics: " + SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName);
            Line("supportsAsyncGPUReadback: " + SystemInfo.supportsAsyncGPUReadback);
            Line("targetFrameRate: " + Application.targetFrameRate + ", vSyncCount: " + QualitySettings.vSyncCount);
            Line("fixedDeltaTime: " + Time.fixedDeltaTime + ", timeScale: " + Time.timeScale);
            Line("gravity: " + Physics.gravity);
            Line("runInBackground: " + Application.runInBackground + ", isFocused: " + Application.isFocused);
            Line("process id: " + System.Diagnostics.Process.GetCurrentProcess().Id);
            Line("MyPictures: " + Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            Line("LocalAppData: " + Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            Line("BepInEx root: " + Paths.BepInExRootPath);
        }

        void Controllers()
        {
            Head("Controllers");
            var p = ControllerInputPoller.instance;
            if (p == null) { Line("ControllerInputPoller.instance is null"); return; }
            InputDevice l = p.leftControllerDevice, r = p.rightControllerDevice, h = p.headDevice;
            Line("left:  valid=" + l.isValid + " name=" + l.name + " maker=" + l.manufacturer);
            Line("right: valid=" + r.isValid + " name=" + r.name + " maker=" + r.manufacturer);
            Line("head:  valid=" + h.isValid + " name=" + h.name + " maker=" + h.manufacturer);
        }

        // ---------------------------------------------------------------- audio

        void Audio()
        {
            Head("Audio");
            var cfg = AudioSettings.GetConfiguration();
            Line("outputSampleRate: " + AudioSettings.outputSampleRate);
            Line("speakerMode: " + cfg.speakerMode + ", dspBufferSize: " + cfg.dspBufferSize
                 + ", realVoices: " + cfg.numRealVoices + ", virtualVoices: " + cfg.numVirtualVoices);
            Line("spatializer plugin: '" + AudioSettings.GetSpatializerPluginName() + "'");
            Line("AudioListener.volume: " + AudioListener.volume + ", pause: " + AudioListener.pause);

            var listeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            Line("AudioListeners: " + listeners.Length);
            for (int i = 0; i < listeners.Length && i < 5; i++)
                Line("  listener: " + Path_(listeners[i].transform) + " enabled=" + listeners[i].enabled);

            var sources = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            int playing = 0, spatialized = 0, threeD = 0;
            var groups = new Dictionary<string, int>();
            foreach (var s in sources)
            {
                if (s.isPlaying) playing++;
                if (s.spatialize) spatialized++;
                if (s.spatialBlend > 0.5f) threeD++;
                string g = s.outputAudioMixerGroup != null
                    ? (s.outputAudioMixerGroup.audioMixer != null ? s.outputAudioMixerGroup.audioMixer.name : "?") + "/" + s.outputAudioMixerGroup.name
                    : "(none)";
                int n; groups.TryGetValue(g, out n); groups[g] = n + 1;
            }
            Line("AudioSources: " + sources.Length + " (playing " + playing + ", spatialize=true " + spatialized + ", spatialBlend>0.5 " + threeD + ")");
            foreach (var kv in groups) Line("  mixer group " + kv.Key + ": " + kv.Value);

            int shown = 0;
            foreach (var s in sources)
            {
                if (!s.isPlaying && shown >= 6) continue;
                if (shown >= 14) break;
                Line("  source " + Path_(s.transform) + " blend=" + s.spatialBlend + " spatialize=" + s.spatialize
                     + " rolloff=" + s.rolloffMode + " min=" + s.minDistance + " max=" + s.maxDistance
                     + " playing=" + s.isPlaying);
                shown++;
            }
        }

        static string Path_(Transform t)
        {
            var sbp = new StringBuilder(t.name);
            int depth = 0;
            for (var p = t.parent; p != null && depth < 3; p = p.parent, depth++) sbp.Insert(0, p.name + "/");
            return sbp.ToString();
        }

        // ---------------------------------------------------------------- layers and collisions

        static string MaskNames(int mask)
        {
            var sbm = new StringBuilder();
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                string n = LayerMask.LayerToName(i);
                if (sbm.Length > 0) sbm.Append(", ");
                sbm.Append(i).Append(':').Append(n.Length == 0 ? "?" : n);
            }
            return sbm.ToString();
        }

        void Layers()
        {
            Head("Layers and collisions");
            var pl = GTPlayer.Instance;
            int loco = pl.locomotionEnabledLayers.value;
            int hover = pl.hoverboardLocomotionLayers.value;
            int water = pl.waterLayer.value;
            Line("GTPlayer.locomotionEnabledLayers = " + loco + " -> " + MaskNames(loco));
            Line("GTPlayer.hoverboardLocomotionLayers = " + hover + " -> " + MaskNames(hover));
            Line("GTPlayer.waterLayer = " + water + " -> " + MaskNames(water));

            var col = GorillaTagger.Instance.headCollider;
            Line("headCollider layer " + LayerMask.LayerToName(col.gameObject.layer)
                 + ", excludeLayers=" + col.excludeLayers.value + ", includeLayers=" + col.includeLayers.value);

            Line("Which defined layers each layer collides with (Physics matrix). '*' = not in the player's locomotion mask:");
            for (int a = 0; a < 32; a++)
            {
                string an = LayerMask.LayerToName(a);
                if (an.Length == 0) continue;
                var sbc = new StringBuilder();
                for (int b = 0; b < 32; b++)
                {
                    if (LayerMask.LayerToName(b).Length == 0) continue;
                    if (Physics.GetIgnoreLayerCollision(a, b)) continue;
                    if (sbc.Length > 0) sbc.Append(", ");
                    sbc.Append(LayerMask.LayerToName(b));
                }
                bool inLoco = (loco & (1 << a)) != 0 || (hover & (1 << a)) != 0;
                Line("  " + a + ":" + an + (inLoco ? "" : " *") + " -> " + sbc);
            }
        }

        void Raycasts()
        {
            Head("Raycasts from the head (what the world is made of)");
            Transform headT = GorillaTagger.Instance.headCollider.transform;
            Vector3 head = headT.position;
            Line("head at " + head);
            LogHits("down 6 m", head, Vector3.down, 6f);
            LogHits("forward", head, headT.forward, 12f);
            LogHits("right", head, headT.right, 12f);
            LogHits("back", head, -headT.forward, 12f);
        }

        void LogHits(string label, Vector3 origin, Vector3 dir, float dist)
        {
            var hits = Physics.RaycastAll(origin, dir, dist, ~0, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
            Line(label + ": " + hits.Length + " hits");
            for (int i = 0; i < hits.Length && i < 6; i++)
            {
                var c = hits[i].collider;
                Line("  " + hits[i].distance.ToString("0.00") + " m " + Path_(c.transform)
                     + " layer=" + LayerMask.LayerToName(c.gameObject.layer)
                     + " type=" + c.GetType().Name + " trigger=" + c.isTrigger);
            }
        }

        // ---------------------------------------------------------------- types available

        void Types()
        {
            Head("Types the plan depends on");
            string[] names =
            {
                "UnityEngine.Video.VideoPlayer, UnityEngine.VideoModule",
                "UnityEngine.Rendering.AsyncGPUReadback, UnityEngine.CoreModule",
                "UnityEngine.ImageConversion, UnityEngine.ImageConversionModule",
                "UnityEngine.AudioLowPassFilter, UnityEngine.AudioModule",
                "System.IO.Compression.ZipArchive, System.IO.Compression",
            };
            foreach (var n in names) Line((Type.GetType(n, false) != null ? "present: " : "MISSING: ") + n);
        }

        // ---------------------------------------------------------------- Mono smoke tests

        IEnumerator MonoTests()
        {
            Head("Mono smoke tests (shared memory, named pipe, zip)");
            string result = null;
            var t = new Thread(() => { result = MonoSmoke(); }) { IsBackground = true };
            t.Start();
            float start = Time.realtimeSinceStartup;
            while (t.IsAlive && Time.realtimeSinceStartup - start < 10f) yield return null;
            if (t.IsAlive) Line("TIMED OUT after 10 s (a call hung)");
            else Line(result ?? "(no result)");
        }

        static string MonoSmoke()
        {
            var o = new StringBuilder();
            int pid = System.Diagnostics.Process.GetCurrentProcess().Id;

            try
            {
                string name = "GorillaPhone_p0_" + pid;
                using (var mmf = MemoryMappedFile.CreateNew(name, 4096))
                {
                    using (var w = mmf.CreateViewAccessor()) { w.Write(0, 12345); }
                    using (var mmf2 = MemoryMappedFile.OpenExisting(name))
                    using (var r = mmf2.CreateViewAccessor())
                        o.AppendLine("MemoryMappedFile: OK, second open read back " + r.ReadInt32(0));
                }
            }
            catch (Exception e) { o.AppendLine("MemoryMappedFile: FAILED " + e.GetType().Name + " " + e.Message); }

            try
            {
                string name = "GorillaPhone_p0_pipe_" + pid;
                string serverErr = null;
                int serverGot = -1;
                using (var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None))
                {
                    var st = new Thread(() =>
                    {
                        try
                        {
                            server.WaitForConnection();
                            var b = new byte[4];
                            server.Read(b, 0, 4);
                            serverGot = BitConverter.ToInt32(b, 0);
                            server.Write(new byte[] { 1, 2, 3, 4 }, 0, 4);
                            server.Flush();
                        }
                        catch (Exception e) { serverErr = e.GetType().Name + " " + e.Message; }
                    }) { IsBackground = true };
                    st.Start();

                    using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut))
                    {
                        client.Connect(3000);
                        client.Write(BitConverter.GetBytes(4242), 0, 4);
                        client.Flush();
                        var b = new byte[4];
                        int n = client.Read(b, 0, 4);
                        st.Join(3000);
                        o.AppendLine("NamedPipe: " + (serverErr == null && serverGot == 4242 && n == 4 ? "OK" : "FAILED")
                                     + " (server got " + serverGot + ", client read " + n + " bytes" + (serverErr != null ? ", server error " + serverErr : "") + ")");
                    }
                }
            }
            catch (Exception e) { o.AppendLine("NamedPipe: FAILED " + e.GetType().Name + " " + e.Message); }

            try
            {
                var ms = new MemoryStream();
                using (var za = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    var e = za.CreateEntry("a/b.txt");
                    using (var s = e.Open()) { var d = Encoding.ASCII.GetBytes("hello zip"); s.Write(d, 0, d.Length); }
                }
                ms.Position = 0;
                using (var za = new ZipArchive(ms, ZipArchiveMode.Read))
                using (var sr = new StreamReader(za.Entries[0].Open()))
                    o.AppendLine("ZipArchive: OK, read back '" + sr.ReadToEnd() + "' from " + za.Entries[0].FullName);
            }
            catch (Exception e) { o.AppendLine("ZipArchive: FAILED " + e.GetType().Name + " " + e.Message); }

            return o.ToString();
        }

        // ---------------------------------------------------------------- frame-time probes

        static string Stats(float[] t)
        {
            var s = (float[])t.Clone();
            Array.Sort(s);
            float sum = 0f;
            foreach (var v in s) sum += v;
            float avg = sum / s.Length, p50 = s[s.Length / 2], p95 = s[(int)(s.Length * 0.95f)], p99 = s[(int)(s.Length * 0.99f)], max = s[s.Length - 1];
            int slow = 0;
            foreach (var v in s) if (v > p50 * 1.5f) slow++;
            return "avg " + avg.ToString("0.00") + " | p50 " + p50.ToString("0.00") + " | p95 " + p95.ToString("0.00")
                   + " | p99 " + p99.ToString("0.00") + " | max " + max.ToString("0.00") + " | frames >1.5x p50: " + slow;
        }

        IEnumerator Measure(string label, int frames, Action<int> perFrame)
        {
            for (int i = 0; i < 10; i++) yield return null;
            var t = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                if (perFrame != null)
                {
                    try { perFrame(i); }
                    catch (Exception e) { Line(label + ": error on frame " + i + ": " + e.GetType().Name + " " + e.Message); perFrame = null; }
                }
                yield return null;
                t[i] = Time.unscaledDeltaTime * 1000f;
            }
            Line(label + ": " + Stats(t));
        }

        IEnumerator CameraTests()
        {
            Camera src = null;
            try
            {
                var go = GorillaTagger.Instance.mainCamera;
                if (go != null) src = go.GetComponent<Camera>();
                Line("main camera: " + (src != null ? Path_(src.transform) + " mask=" + MaskNames(src.cullingMask) : "not found"));
            }
            catch (Exception e) { Line("main camera lookup failed: " + e.Message); }

            var camGo = new GameObject("GP_P0_Cam");
            Camera cam = null;
            RenderTexture rt = null;
            try
            {
                cam = camGo.AddComponent<Camera>();
                if (src != null)
                {
                    camGo.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
                    cam.cullingMask = src.cullingMask;
                    cam.clearFlags = src.clearFlags;
                    cam.backgroundColor = src.backgroundColor;
                }
                cam.fieldOfView = 60f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 200f;
                cam.depth = -100f;
                cam.stereoTargetEye = StereoTargetEyeMask.None;
                rt = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
            }
            catch (Exception e) { Line("camera setup FAILED: " + e.GetType().Name + " " + e.Message); }

            if (cam != null && rt != null)
            {
                // A: an enabled second camera rendering every frame at 512x512.
                yield return Measure("second camera 512x512, every frame", 300, null);

                // B: disabled camera, rendered by hand every 3rd frame at 256x256 (what a shutter or a slow preview would do).
                cam.enabled = false;
                cam.targetTexture = null;
                rt.Release();
                rt = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;

                var warnings = new List<string>();
                Application.LogCallback cb = (msg, stack, type) => { if (type != LogType.Log && warnings.Count < 3) warnings.Add(type + ": " + msg); };
                Application.logMessageReceivedThreaded += cb;
                yield return Measure("second camera 256x256, cam.Render() every 3rd frame", 300, i => { if (i % 3 == 0) cam.Render(); });
                Application.logMessageReceivedThreaded -= cb;
                foreach (var w in warnings) Line("  log during Render(): " + w);

                // C: async readback of the render texture (the photo path).
                bool done = false, error = false;
                int length = 0;
                float t0 = Time.realtimeSinceStartup;
                int startFrame = Time.frameCount;
                try
                {
                    cam.Render();
                    AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, req =>
                    {
                        error = req.hasError;
                        if (!req.hasError) length = req.GetData<byte>().Length;
                        done = true;
                    });
                }
                catch (Exception e) { Line("AsyncGPUReadback FAILED: " + e.GetType().Name + " " + e.Message); done = true; error = true; }
                while (!done && Time.realtimeSinceStartup - t0 < 2f) yield return null;
                Line("AsyncGPUReadback 256x256: " + (done ? (error ? "error" : "OK, " + length + " bytes") : "no answer in 2 s")
                     + ", took " + (Time.frameCount - startFrame) + " frames / " + ((Time.realtimeSinceStartup - t0) * 1000f).ToString("0") + " ms");
            }

            if (rt != null) { if (cam != null) cam.targetTexture = null; rt.Release(); UnityEngine.Object.Destroy(rt); }
            UnityEngine.Object.Destroy(camGo);
        }

        IEnumerator UploadTest()
        {
            // The video path: push one 360x640 RGBA frame into a texture every frame.
            Texture2D tex = null;
            byte[] buf = null;
            try
            {
                tex = new Texture2D(360, 640, TextureFormat.RGBA32, false);
                buf = new byte[360 * 640 * 4];
            }
            catch (Exception e) { Line("upload test setup FAILED: " + e.Message); }

            if (tex != null)
            {
                yield return Measure("texture upload 360x640 RGBA32, every frame", 300, i =>
                {
                    buf[(i * 4) % buf.Length] = (byte)i;
                    tex.LoadRawTextureData(buf);
                    tex.Apply(false);
                });
                UnityEngine.Object.Destroy(tex);
            }
        }

        // ---------------------------------------------------------------- output

        void Flush()
        {
            string path = Path.Combine(Paths.BepInExRootPath, "GorillaPhone-phase0.txt");
            try { File.WriteAllText(path, sb.ToString()); }
            catch (Exception e) { log.LogWarning("phase 0: could not write " + path + ": " + e.Message); }
            log.LogInfo("phase 0 finished, report written to " + path);
            log.LogInfo(sb.ToString());
        }
    }
}
