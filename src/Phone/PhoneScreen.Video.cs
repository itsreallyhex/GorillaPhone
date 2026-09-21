using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GorillaPhone.Video;
using TMPro;
using UnityEngine;

namespace GorillaPhone.Phone
{
    /// <summary>
    /// The Video app: short vertical videos on the phone. The phone starts the player's own Edge or Chrome (see BrowserHost) and shows
    /// its picture here. A tap is a click, a quick flick up or down moves to the next or previous video (the arrow keys), a slower or
    /// sideways drag scrolls the page, and "Drag" mode makes the finger a mouse (sliders, a captcha). Back and Reload are in the header,
    /// the volume in the footer. The page's sound comes out of the phone (3D, muffled by walls) through PhoneAudio's video voice.
    /// The browser starts the first time the app opens, and closes with the game.
    /// </summary>
    public sealed partial class PhoneScreen
    {
        const float VidHeaderH = 0.26f;      // header and footer heights, as fractions of the screen width
        const float VidFooterH = 0.20f;
        const float VidMargin = 0.006f;      // tighter than other pages: the header buttons sit close together
        const float SwipeMinDist = 0.03f;    // a flick must travel at least 3 cm ...
        const float SwipeMaxTime = 0.7f;     // ... within this many seconds
        const float VidTapMaxTime = 0.8f;

        struct VTouch
        {
            public Vector2 Start, Last;
            public float T0, NextMove;
            public bool Left, Down;
        }

        Transform videoRoot;
        Part vidBg, vidView, vidHeader, vidFooter, vidHomePart, vidBackChip, vidReloadChip, vidModeChip, vidMinus, vidPlus;
        TextMeshPro vidBackText, vidReloadText, vidModeText, vidVolValue;
        readonly TextMeshPro[] vidStatus = new TextMeshPro[3];
        Button vidHomeBtn, vidBackBtn, vidReloadBtn, vidModeBtn, vidMinusBtn, vidPlusBtn;
        BrowserHost host;
        Texture2D vidTex;
        long vidSeq;
        float nextVidFrame, nextVidStats, vidReopenAfter, vidWaitSince;
        bool vidGotFrame, vidDragMode;
        float vTop, vH;                      // the picture's top edge and height on the screen (metres, screen space)
        readonly VTouch[] vtouch = new VTouch[2];

        // ImageConversion.LoadImage also has a ReadOnlySpan overload, which a net48 project cannot compile against (kit reference 02):
        // call the byte[] version through a delegate made by reflection.
        static readonly Func<Texture2D, byte[], bool, bool> loadImage = MakeLoadImage();

        static Func<Texture2D, byte[], bool, bool> MakeLoadImage()
        {
            var m = typeof(ImageConversion).GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
            return m == null ? null : (Func<Texture2D, byte[], bool, bool>)Delegate.CreateDelegate(typeof(Func<Texture2D, byte[], bool, bool>), m);
        }

        // ------------------------------------------------------------------ setup

        void BuildVideo()
        {
            videoRoot = NewNode("Video", root);
            Texture2D chipTex = Track(ShapeTextures.Chip(192, 64));
            var bar = new Color(0.05f, 0.06f, 0.10f, 1f);

            vidBg = Opaque(videoRoot, "Background", new Color(0.02f, 0.02f, 0.03f, 1f), null, 0);
            vidView = Opaque(videoRoot, "View", Color.white, null, 1);
            vidHeader = Opaque(videoRoot, "Header", bar, null, 5);
            vidFooter = Opaque(videoRoot, "Footer", bar, null, 5);
            vidHomePart = Glass(videoRoot, "HomeButton", Color.white, Track(ShapeTextures.Home(128)), 10);
            vidBackChip = Glass(videoRoot, "BackChip", Color.white, chipTex, 10);
            vidReloadChip = Glass(videoRoot, "ReloadChip", Color.white, chipTex, 10);
            vidModeChip = Glass(videoRoot, "ModeChip", Color.white, chipTex, 10);
            vidMinus = Glass(videoRoot, "VolumeDown", Color.white, Track(ShapeTextures.Minus(128)), 10);
            vidPlus = Glass(videoRoot, "VolumeUp", Color.white, Track(ShapeTextures.Plus(128)), 10);

            vidBackText = PhoneText.Create(log, videoRoot, "BackText", "Back", 0.01f, 0.1f, TextColor, 31);
            vidReloadText = PhoneText.Create(log, videoRoot, "ReloadText", "Reload", 0.01f, 0.1f, TextColor, 31);
            vidModeText = PhoneText.Create(log, videoRoot, "ModeText", "Swipe", 0.01f, 0.1f, TextColor, 31);
            vidVolValue = PhoneText.Create(log, videoRoot, "VolumeValue", "50%", 0.01f, 0.1f, TextColor, 30);
            for (int i = 0; i < vidStatus.Length; i++) vidStatus[i] = PhoneText.Create(log, videoRoot, "Status" + i, "", 0.01f, 0.1f, TextColor, 30);

            vidHomeBtn = AddButton(Page.Video, GoHome, 0.4f);
            vidBackBtn = AddButton(Page.Video, delegate { if (host != null) host.Back(); }, 0.5f);
            vidReloadBtn = AddButton(Page.Video, VideoReload, 0.8f);
            vidModeBtn = AddButton(Page.Video, delegate { vidDragMode = !vidDragMode; log.LogInfo("phone video: mode " + (vidDragMode ? "drag" : "swipe")); }, 0.4f);
            vidMinusBtn = AddButton(Page.Video, delegate { if (phone.Sound != null) phone.Sound.AdjustVolume(-0.1f); }, 0.15f);
            vidPlusBtn = AddButton(Page.Video, delegate { if (phone.Sound != null) phone.Sound.AdjustVolume(0.1f); }, 0.15f);

            vidView.T.gameObject.SetActive(false);   // shown once a picture has arrived
            videoRoot.gameObject.SetActive(false);
        }

        void LayoutVideo(float sw, float sh, float margin)
        {
            if (videoRoot == null) return;
            float hh = VidHeaderH * sw, fh = VidFooterH * sw;
            vTop = sh * 0.5f - hh;
            vH = sh - hh - fh;

            Place(vidBg, 0f, 0f, sw, sh, -0.0004f);
            Place(vidView, 0f, vTop - vH * 0.5f, sw, vH, -0.0006f);
            Place(vidHeader, 0f, sh * 0.5f - hh * 0.5f, sw, hh, -0.0008f);
            Place(vidFooter, 0f, -sh * 0.5f + fh * 0.5f, sw, fh, -0.0008f);

            float m = VidMargin;
            float hy = sh * 0.5f - 0.19f * sw;   // the header's row of buttons, below the island
            float bd = 0.13f * sw, ch = 0.10f * sw;
            Place(vidHomePart, -0.38f * sw, hy, bd, bd, -0.0010f);
            SetRect(vidHomeBtn, -0.38f * sw, hy, bd, m);
            ChipAt(vidBackChip, vidBackText, vidBackBtn, -0.08f * sw, hy, 0.26f * sw, ch, m);
            ChipAt(vidReloadChip, vidReloadText, vidReloadBtn, 0.27f * sw, hy, 0.26f * sw, ch, m);

            float fy = -sh * 0.5f + 0.10f * sw;   // the footer's row: mode, volume
            ChipAt(vidModeChip, vidModeText, vidModeBtn, -0.29f * sw, fy, 0.30f * sw, ch, m);
            Place(vidMinus, 0.10f * sw, fy, bd, bd, -0.0010f);
            Place(vidPlus, 0.39f * sw, fy, bd, bd, -0.0010f);
            SetRect(vidMinusBtn, 0.10f * sw, fy, bd, m);
            SetRect(vidPlusBtn, 0.39f * sw, fy, bd, m);
            PhoneText.Resize(vidVolValue, 0.05f * sw, 0.16f * sw);
            PhoneText.Place(vidVolValue, 0.245f * sw, fy, -0.0020f);

            for (int i = 0; i < vidStatus.Length; i++)
            {
                PhoneText.Resize(vidStatus[i], 0.045f * sw, 0.94f * sw);
                PhoneText.Place(vidStatus[i], 0f, vTop - vH * 0.5f + (1 - i) * 0.07f * sw, -0.0020f);
            }
        }

        void ChipAt(Part chip, TextMeshPro text, Button btn, float x, float y, float w, float h, float margin)
        {
            Place(chip, x, y, w, h, -0.0010f);
            PhoneText.Resize(text, 0.05f * phone.ScreenSize.x, w * 0.9f);
            PhoneText.Place(text, x, y, -0.0020f);
            btn.Center = new Vector2(x, y);
            btn.Half = new Vector2(w * 0.5f + margin, h * 0.5f + margin);
        }

        // ------------------------------------------------------------------ opening, closing, starting the browser

        void OpenVideo()
        {
            SetPage(Page.Video);
        }

        /// <summary>Called by SetPage. The browser starts the first time the page opens; the picture and sound run only while it is open.</summary>
        void VideoPageChanged(Page p)
        {
            // The video plays for as long as the Video app is the open app, even while the screen sleeps (the phone lying face down or far
            // away, as in your hand it is not): only the PICTURE follows the screen. Going Home pauses it.
            bool onPage = p == Page.Video;
            if (onPage) EnsureVideo();
            if (host != null)
            {
                host.Audio.SetDelayMs(cfg.VideoAudioDelayMs.Value);
                host.SetPicture(onPage && screenOn);
                host.SetPlaying(onPage);
            }
            if (phone.Sound != null) phone.Sound.SetVideo(host != null ? host.Audio : null, onPage && host != null && cfg.VideoHookAudio.Value);
            if (onPage && screenOn) vidWaitSince = Time.unscaledTime;
        }

        void EnsureVideo()
        {
            if (!cfg.VideoEnabled.Value) return;
            if (host == null) host = new BrowserHost();
            BrowserHost.State s = host.Status;
            if (s == BrowserHost.State.Idle || (s == BrowserHost.State.Failed && Time.unscaledTime >= vidReopenAfter)) StartHost();
        }

        void StartHost()
        {
            vidReopenAfter = Time.unscaledTime + 3f;
            vidGotFrame = false;
            vidSeq = 0;
            if (vidView != null) vidView.T.gameObject.SetActive(false);

            float sw = phone.ScreenSize.x;
            int vw = cfg.VideoViewportWidth.Value;
            var o = new BrowserHost.Options();
            o.StartUrl = cfg.VideoStartUrl.Value;
            o.BrowserPath = cfg.VideoBrowserPath.Value;
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GorillaPhone");
            o.ProfileDir = Path.Combine(dir, "browser-profile");
            o.PidFile = Path.Combine(dir, "browser.pid");
            o.ViewW = vw;
            o.ViewH = Mathf.Max(200, Mathf.RoundToInt(vw * (vH > 0f ? vH / sw : 1.32f)));   // the picture area's shape, so nothing is stretched
            o.FrameW = cfg.VideoFrameWidth.Value;
            o.Mobile = cfg.VideoMobile.Value;
            o.HookAudio = cfg.VideoHookAudio.Value;
            o.FrameIntervalMs = Mathf.RoundToInt(1000f / Mathf.Max(5f, cfg.VideoFps.Value));
            o.TapAssist = cfg.VideoTapAssist.Value;
            log.LogInfo("phone video: starting the browser, page " + o.ViewW + "x" + o.ViewH + ", pictures " + o.FrameW + " px wide, start page " + o.StartUrl);
            host.Start(o);
        }

        void VideoReload()
        {
            if (host == null) { EnsureVideo(); return; }
            BrowserHost.State s = host.Status;
            if (s == BrowserHost.State.Failed || s == BrowserHost.State.Idle)
            {
                host.Stop();
                StartHost();
                host.SetPicture(page == Page.Video && screenOn);
                host.SetPlaying(page == Page.Video);
            }
            else host.Reload();
        }

        void DestroyVideo()
        {
            if (host != null) host.Stop();
            if (vidTex != null) Destroy(vidTex);
        }

        // ------------------------------------------------------------------ per frame

        void DrainVideoLog()
        {
            if (host == null) return;
            string line;
            while (host.TryDequeueLog(out line)) log.LogInfo("phone video: " + line);
        }

        void UpdateVideo()
        {
            if (host == null)
            {
                SetStatus(cfg.VideoEnabled.Value ? "Starting..." : "The Video app is turned off in the config.");
                return;
            }
            BrowserHost.State st = host.Status;
            if (st == BrowserHost.State.Ready) host.Audio.SetDelayMs(cfg.VideoAudioDelayMs.Value);

            // The newest picture, at most Fps times a second (decoding a jpeg costs a millisecond or two of the main thread).
            if (Time.unscaledTime >= nextVidFrame)
            {
                byte[] jpg;
                if (host.TryTakeFrame(ref vidSeq, out jpg))
                {
                    nextVidFrame = Time.unscaledTime + 1f / Mathf.Max(5f, cfg.VideoFps.Value);
                    if (vidTex == null)
                    {
                        vidTex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                        vidTex.wrapMode = TextureWrapMode.Clamp;
                        PhoneMaterials.SetTexture(vidView.M, vidTex);
                    }
                    if (loadImage != null && loadImage(vidTex, jpg, false) && !vidGotFrame)
                    {
                        vidGotFrame = true;
                        vidView.T.gameObject.SetActive(true);
                        log.LogInfo("phone video: first frame " + vidTex.width + "x" + vidTex.height);
                    }
                }
            }

            string msg = null;
            if (st == BrowserHost.State.Failed)
            {
                msg = host.Message;
                if (msg.IndexOf("Reload", StringComparison.Ordinal) < 0) msg += " Tap Reload to try again.";
            }
            else if (!vidGotFrame)
            {
                msg = host.Message;
                if (st == BrowserHost.State.Ready && Time.unscaledTime - vidWaitSince > 10f)
                    msg += " Nothing yet? Look at the browser window on your desktop.";
            }
            SetStatus(msg);

            if (phone.Sound != null) PhoneText.Set(vidVolValue, Mathf.RoundToInt(phone.Sound.Volume * 100f) + "%");
            PhoneText.Set(vidModeText, vidDragMode ? "Drag" : "Swipe");

            if (Time.unscaledTime >= nextVidStats)
            {
                nextVidStats = Time.unscaledTime + 5f;
                int f, b;
                host.TakeStats(out f, out b);
                if (f + b > 0)
                    log.LogInfo("phone video: in 5 s " + f + " pictures and " + b + " sound blocks; sound buffered " + host.Audio.BufferedMs + " ms, gaps " + host.Audio.Underruns);
            }
        }

        /// <summary>Up to three centred lines over the picture area, or none when msg is null.</summary>
        void SetStatus(string msg)
        {
            string[] lines = msg == null ? new string[0] : Wrap(msg, 24, vidStatus.Length);
            for (int i = 0; i < vidStatus.Length; i++) PhoneText.Set(vidStatus[i], i < lines.Length ? lines[i] : "");
        }

        static string[] Wrap(string s, int maxChars, int maxLines)
        {
            var lines = new List<string>();
            var cur = new StringBuilder();
            foreach (string word in s.Split(' '))
            {
                if (cur.Length > 0 && cur.Length + 1 + word.Length > maxChars)
                {
                    lines.Add(cur.ToString());
                    cur.Length = 0;
                }
                if (cur.Length > 0) cur.Append(' ');
                cur.Append(word);
            }
            if (cur.Length > 0) lines.Add(cur.ToString());
            if (lines.Count > maxLines) lines.RemoveRange(maxLines, lines.Count - maxLines);
            return lines.ToArray();
        }

        // ------------------------------------------------------------------ touch (a finger that pressed into the picture)

        bool VideoWantsTouch(Vector3 at)
        {
            return page == Page.Video && host != null && host.Status == BrowserHost.State.Ready && at.y < vTop && at.y > vTop - vH;
        }

        /// <summary>A screen position as a point in the page (CSS pixels).</summary>
        bool ToPage(Vector2 p, out int x, out int y)
        {
            x = y = 0;
            if (host == null || host.ViewW <= 0 || vH <= 0f) return false;
            float sw = phone.ScreenSize.x;
            float u = Mathf.Clamp01((p.x + sw * 0.5f) / sw);
            float v = Mathf.Clamp01((vTop - p.y) / vH);
            x = Mathf.RoundToInt(u * (host.ViewW - 1));
            y = Mathf.RoundToInt(v * (host.ViewH - 1));
            return true;
        }

        void VideoTouchBegin(int h, Vector3 at, bool left)
        {
            vtouch[h] = new VTouch { Start = at, Last = at, T0 = Time.unscaledTime, Left = left, Down = true };
            int x, y;
            if (vidDragMode && ToPage(at, out x, out y)) host.MouseDown(x, y);
        }

        void VideoTouchMove(int h, Vector3 p)
        {
            if (!vtouch[h].Down) return;
            vtouch[h].Last = p;
            if (!vidDragMode || Time.unscaledTime < vtouch[h].NextMove) return;
            vtouch[h].NextMove = Time.unscaledTime + 0.033f;
            int x, y;
            if (ToPage(p, out x, out y)) host.MouseMove(x, y, true);
        }

        void VideoTouchEnd(int h, Vector3 p)
        {
            if (!vtouch[h].Down) return;
            VTouch t = vtouch[h];
            vtouch[h].Down = false;
            if (host == null) return;
            int x, y;
            if (vidDragMode)
            {
                if (ToPage(t.Last, out x, out y)) host.MouseUp(x, y);
                return;
            }

            float dx = t.Last.x - t.Start.x, dy = t.Last.y - t.Start.y;
            float dur = Time.unscaledTime - t.T0;
            if (dx * dx + dy * dy < TapSlop * TapSlop)
            {
                // Stayed in the dead zone: a tap is a click at the spot where the finger touched.
                if (dur <= VidTapMaxTime && ToPage(t.Start, out x, out y))
                {
                    host.Tap(x, y);
                    phone.Haptic(t.Left, 0.2f, 0.03f);
                    log.LogInfo("phone video: tap " + x + "," + y);
                }
                return;
            }
            if (dur <= SwipeMaxTime && Mathf.Abs(dy) >= SwipeMinDist && Mathf.Abs(dy) > 1.5f * Mathf.Abs(dx))
            {
                // A quick flick up or down: the next or previous video.
                if (dy > 0f) host.NextVideo(); else host.PreviousVideo();
                phone.Haptic(t.Left, 0.25f, 0.04f);
                log.LogInfo("phone video: swipe " + (dy > 0f ? "next" : "previous"));
                return;
            }
            // Anything slower or sideways scrolls the page by the distance the finger moved (finger up = page down).
            if (ToPage(t.Start, out x, out y))
            {
                float pxPerMetre = host.ViewH / vH;
                host.Wheel(x, y, dx * pxPerMetre, dy * pxPerMetre);
                log.LogInfo("phone video: scroll " + (dy * pxPerMetre).ToString("0") + " px at " + x + "," + y);
            }
        }

        /// <summary>A touch that ends because the page or screen changed: let go of the mouse if the finger was acting as one.</summary>
        void VideoTouchCancel(int h)
        {
            if (!vtouch[h].Down) return;
            vtouch[h].Down = false;
            int x, y;
            if (vidDragMode && host != null && ToPage(vtouch[h].Last, out x, out y)) host.MouseUp(x, y);
        }
    }
}
