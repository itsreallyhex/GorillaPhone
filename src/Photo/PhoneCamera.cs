using System;
using System.Collections.Concurrent;
using System.IO;
using BepInEx.Logging;
using GorillaPhone.Phone;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace GorillaPhone.Photo
{
    public enum CameraMode { Rear, Front }

    /// <summary>
    /// The phone's camera. One Unity camera that sits on the back (rear) or above the screen (front,
    /// selfie). It is switched off and drawn by hand: a small preview a few times a second while the
    /// screen faces you, and one full-size render only when a photo is taken. The GPU readback is
    /// asynchronous and the PNG encoding and file write happen on a background thread.
    /// </summary>
    public sealed class PhoneCamera : MonoBehaviour
    {
        PhoneConfig cfg;
        ManualLogSource log;
        PhoneObject phone;

        Camera cam;
        RenderTexture preview;
        CameraMode mode = CameraMode.Rear;
        Vector3 phoneSize = new Vector3(0.16f, 0.2844f, 0.014f);
        Vector3 lensLocal;
        float aspect = 9f / 16f;

        // Live settings: they start at the config values and can be changed from the phone (zoom, quality).
        // They last until the game is restarted; the config values stay the defaults.
        float fov;
        int previewHeight, photoHeight;
        static readonly int[] PreviewSteps = { 180, 320, 540, 720 };
        static readonly int[] PhotoSteps = { 1280, 1920, 2560, 3840 };
        public static readonly string[] QualityNames = { "Low", "Med", "High", "Ultra" };
        int qualityIndex = 1;

        float nextRender, lastShot;
        bool photoBusy, renderFailed, loggedFirstRender;
        readonly ConcurrentQueue<PhotoResult> finished = new ConcurrentQueue<PhotoResult>();

        AudioSource clickSource;
        AudioClip clickClip;
        Texture2D thumb;

        public RenderTexture Preview { get { return preview; } }
        public CameraMode Mode { get { return mode; } }
        public Texture2D Thumbnail { get { return thumb; } }
        /// <summary>Set by the screen: true while the camera app is open on a lit screen. The preview is only drawn then.</summary>
        public bool PreviewEnabled { get; set; }
        public float Fov { get { return fov; } }
        public int PreviewHeightNow { get { return previewHeight; } }
        public int PhotoHeightNow { get { return photoHeight; } }
        public string QualityName { get { return QualityNames[qualityIndex]; } }
        /// <summary>True once the preview has been drawn at least once, so the screen never shows an empty texture.</summary>
        public bool HasFrame { get; private set; }
        /// <summary>True when the preview should be shown mirrored (selfie mirror).</summary>
        public bool MirrorPreview { get { return mode == CameraMode.Front && cfg.MirrorSelfie.Value; } }

        public event Action ThumbnailChanged;
        public event Action ModeChanged;

        // ------------------------------------------------------------------ setup

        public void Init(PhoneConfig config, ManualLogSource logger, PhoneObject owner)
        {
            cfg = config;
            log = logger;
            phone = owner;

            ResetLiveSettings();
            // Editing these in the config file resets the live values to what was written.
            cfg.CameraFov.SettingChanged += (s, e) => ResetLiveSettings();
            cfg.PreviewHeight.SettingChanged += (s, e) => ResetLiveSettings();
            cfg.PhotoHeight.SettingChanged += (s, e) => ResetLiveSettings();

            var go = new GameObject("GP_PhoneCamera");
            go.transform.SetParent(transform, false);
            cam = go.AddComponent<Camera>();
            cam.enabled = false;   // drawn by hand with Render()
            cam.depth = -100f;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.useOcclusionCulling = false;
            cam.nearClipPlane = 0.03f;
            CopyFromGameCamera();
        }

        void CopyFromGameCamera()
        {
            Camera src = null;
            try
            {
                var tagger = GorillaTagger.Instance;
                GameObject g = cfg.CameraMask.Value == CameraMaskSource.ThirdPerson ? tagger.thirdPersonCamera : tagger.mainCamera;
                if (g != null) src = g.GetComponentInChildren<Camera>(true);
                if (src == null && tagger.mainCamera != null) src = tagger.mainCamera.GetComponentInChildren<Camera>(true);
            }
            catch (Exception e) { log.LogWarning("phone camera: could not read the game's camera: " + e.Message); }

            if (src != null)
            {
                cam.cullingMask = src.cullingMask;
                cam.clearFlags = src.clearFlags;
                cam.backgroundColor = src.backgroundColor;
                cam.farClipPlane = Mathf.Max(50f, src.farClipPlane);
                log.LogInfo("phone camera copies layers from '" + src.name + "' (mask " + src.cullingMask + ", clear " + src.clearFlags + ", far " + cam.farClipPlane + ")");
            }
            else
            {
                cam.cullingMask = ~0;
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.farClipPlane = 300f;
                log.LogWarning("phone camera: no game camera found to copy from, drawing every layer");
            }
        }

        /// <summary>Called by the phone whenever its size changes.</summary>
        public void Layout(Vector3 size, Vector3 lensLocalPosition)
        {
            phoneSize = size;
            lensLocal = lensLocalPosition;
            aspect = (size.x * 0.92f) / (size.y * 0.95f);   // the screen is 92% x 95% of the phone
            if (preview == null || Mathf.Abs((float)preview.width / preview.height - aspect) > 0.01f) BuildPreview();
            PlaceCamera();
        }

        void BuildPreview()
        {
            if (preview != null)
            {
                if (cam != null && cam.targetTexture == preview) cam.targetTexture = null;
                preview.Release();
                Destroy(preview);
            }
            int h = Mathf.Clamp(previewHeight, 64, 1080);
            int w = Mathf.Max(16, Mathf.RoundToInt(h * aspect));
            preview = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32)
            {
                name = "GP_Preview",
                useMipMap = false,
                filterMode = FilterMode.Bilinear
            };
            preview.Create();
            nextRender = 0f;
        }

        void PlaceCamera()
        {
            if (cam == null) return;
            Transform t = cam.transform;
            if (mode == CameraMode.Rear)
            {
                // At the camera bump on the back, looking out of the back (+Z).
                t.localPosition = lensLocal + new Vector3(0f, 0f, 0.006f);
                t.localRotation = Quaternion.identity;
            }
            else
            {
                // In the dynamic island (the camera dot on its right side), looking out of the front (-Z), toward you.
                // Must match PhoneScreen's island: 0.075 and 0.055 of the screen width, from the centre and from the top.
                float sw = phoneSize.x * 0.92f, sh = phoneSize.y * 0.95f;
                t.localPosition = new Vector3(0.075f * sw, sh * 0.5f - 0.055f * sw, -(phoneSize.z * 0.5f + 0.004f));
                t.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }
            cam.fieldOfView = fov;
        }

        void ResetLiveSettings()
        {
            fov = Mathf.Clamp(cfg.CameraFov.Value, 20f, 120f);
            previewHeight = cfg.PreviewHeight.Value;
            photoHeight = cfg.PhotoHeight.Value;
            // The quality name shown on the phone is the preset closest to the configured preview height.
            int best = 0;
            for (int i = 1; i < PreviewSteps.Length; i++)
                if (Mathf.Abs(PreviewSteps[i] - previewHeight) < Mathf.Abs(PreviewSteps[best] - previewHeight)) best = i;
            qualityIndex = best;
        }

        /// <summary>Zoom in (narrower field of view) or out, in steps. The range is 20 to 120 degrees.</summary>
        public void ZoomStep(bool zoomIn)
        {
            fov = Mathf.Clamp(fov * (zoomIn ? 0.85f : 1f / 0.85f), 20f, 120f);
            if (cam != null) cam.fieldOfView = fov;
            nextRender = 0f;   // show the new zoom at once
        }

        /// <summary>Cycles Low, Med, High, Ultra: the live preview and the saved photo both get sharper.</summary>
        public void CycleQuality()
        {
            qualityIndex = (qualityIndex + 1) % QualityNames.Length;
            previewHeight = PreviewSteps[qualityIndex];
            photoHeight = PhotoSteps[qualityIndex];
            BuildPreview();
            log.LogInfo("phone camera quality: " + QualityNames[qualityIndex] + " (preview " + previewHeight + " px, photo " + photoHeight + " px)");
        }

        public void ToggleMode()
        {
            mode = mode == CameraMode.Rear ? CameraMode.Front : CameraMode.Rear;
            PlaceCamera();
            nextRender = 0f;   // draw the new view right away
            log.LogInfo("phone camera: " + (mode == CameraMode.Rear ? "rear" : "front (selfie)"));
            if (ModeChanged != null) ModeChanged();
        }

        // ------------------------------------------------------------------ preview

        void Update()
        {
            PhotoResult r;
            while (finished.TryDequeue(out r)) HandleResult(r);

            if (cam == null || preview == null) return;
            if (preview.height != Mathf.Clamp(previewHeight, 64, 1080)) BuildPreview();
            if (!PreviewWanted()) return;

            float now = Time.unscaledTime;
            if (now < nextRender) return;
            nextRender = now + 1f / Mathf.Max(1f, cfg.PreviewFps.Value);
            RenderTo(preview, false);
        }

        /// <summary>Only draw the preview when someone can see it: the screen faces the head and it is within 3 m.</summary>
        bool PreviewWanted()
        {
            if (!PreviewEnabled || !GorillaTagger.hasInstance) return false;
            Vector3 to = GorillaTagger.Instance.headCollider.transform.position - transform.position;
            float d = to.magnitude;
            if (d > 3f) return false;
            return Vector3.Dot(-transform.forward, to) > -0.05f * d;   // the screen faces -Z
        }

        bool RenderTo(RenderTexture rt, bool isPhoto)
        {
            cam.fieldOfView = fov;
            cam.targetTexture = rt;
            try
            {
                cam.Render();
                if (!isPhoto) HasFrame = true;
                if (!loggedFirstRender)
                {
                    loggedFirstRender = true;
                    log.LogInfo("phone camera: first render ok (" + rt.width + "x" + rt.height + ")");
                }
                return true;
            }
            catch (Exception e)
            {
                if (!renderFailed)
                {
                    renderFailed = true;
                    log.LogError("phone camera: Camera.Render failed (" + e.GetType().Name + ": " + e.Message + "); the preview falls back to an always-on camera");
                    if (!isPhoto) { cam.targetTexture = preview; cam.enabled = true; }
                }
                return false;
            }
        }

        // ------------------------------------------------------------------ photo

        /// <summary>Takes a photo now. False if one is still being saved, or it was too soon after the last.</summary>
        public bool RequestPhoto()
        {
            if (cam == null || photoBusy || Time.unscaledTime - lastShot < 0.6f) return false;

            int h = Mathf.Clamp(photoHeight, 240, 4096);
            int w = Mathf.Max(16, Mathf.RoundToInt(h * aspect) & ~1);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "GP_Photo", useMipMap = false };
            rt.Create();

            bool ok = RenderTo(rt, true);
            cam.targetTexture = preview;
            if (!ok)
            {
                rt.Release();
                Destroy(rt);
                return false;
            }

            bool front = mode == CameraMode.Front;
            bool mirror = MirrorPreview;
            bool flip;
            switch (cfg.PhotoFlipMode.Value)
            {
                case PhotoFlip.Flip: flip = true; break;
                case PhotoFlip.NoFlip: flip = false; break;
                // In game (first selfie, 2026-09-20) a photo came out upside down with the opposite rule, so on a
                // Direct3D-style layout (UV starts at top) the readback rows arrive bottom first and need the flip.
                default: flip = SystemInfo.graphicsUVStartsAtTop; break;
            }

            // The map is recorded now, when the photo is taken, and stored inside the PNG (the gallery shows it).
            string zones = MapInfo.CurrentZones();
            log.LogInfo("photo map (active zones): " + (zones.Length == 0 ? "none" : zones));

            photoBusy = true;
            lastShot = Time.unscaledTime;
            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, req => OnReadback(req, rt, w, h, flip, mirror, front, zones));
            PlayClick();
            return true;
        }

        void OnReadback(AsyncGPUReadbackRequest req, RenderTexture rt, int w, int h, bool flip, bool mirror, bool front, string zones)
        {
            try
            {
                if (req.hasError)
                {
                    log.LogError("phone camera: the GPU readback failed, no photo saved");
                    photoBusy = false;
                    return;
                }
                byte[] copy = req.GetData<byte>().ToArray();   // the native buffer is only valid inside this callback
                var job = new PhotoJob
                {
                    Rgba = copy,
                    Width = w,
                    Height = h,
                    FlipVertical = flip,
                    MirrorHorizontal = mirror,
                    Folder = PhotoFolder(),
                    FileName = "GorillaPhone_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture) + (front ? "_selfie" : "") + ".png",
                    Comment = PhotoLibrary.BuildComment(front, zones)
                };
                PhotoSaver.SaveAsync(job, res => finished.Enqueue(res));
            }
            catch (Exception e)
            {
                log.LogError("phone camera: could not start saving the photo: " + e.GetType().Name + ": " + e.Message);
                photoBusy = false;
            }
            finally
            {
                if (rt != null) { rt.Release(); Destroy(rt); }
            }
        }

        /// <summary>Where photos are saved and where the gallery looks: the PhotoFolder setting, or Pictures\GorillaPhone.</summary>
        public string PhotoFolder()
        {
            string f = cfg.PhotoFolder.Value;
            if (string.IsNullOrEmpty(f) || f.Trim().Length == 0)
                f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "GorillaPhone");
            return f;
        }

        void HandleResult(PhotoResult r)
        {
            photoBusy = false;
            if (r.Error != null)
            {
                log.LogError("phone camera: saving the photo failed: " + r.Error);
                return;
            }
            log.LogInfo("photo saved: " + r.Path + " (" + r.Width + "x" + r.Height + ", " + r.Bytes / 1024 + " KB, " + r.Milliseconds + " ms on the background thread)");
            UpdateThumbnail(r);
        }

        void UpdateThumbnail(PhotoResult r)
        {
            int tw = r.ThumbWidth, th = r.ThumbHeight;
            if (thumb == null || thumb.width != tw || thumb.height != th)
            {
                if (thumb != null) Destroy(thumb);
                thumb = new Texture2D(tw, th, TextureFormat.RGBA32, false)
                {
                    name = "GP_Thumb",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
            }
            // The thumbnail's first row is the top of the picture; a Unity texture's first row is the bottom.
            var flipped = new byte[r.ThumbRgba.Length];
            int stride = tw * 4;
            for (int y = 0; y < th; y++)
                Buffer.BlockCopy(r.ThumbRgba, y * stride, flipped, (th - 1 - y) * stride, stride);
            thumb.LoadRawTextureData(flipped);
            thumb.Apply(false);
            if (ThumbnailChanged != null) ThumbnailChanged();
        }

        // ------------------------------------------------------------------ shutter sound

        void PlayClick()
        {
            float vol = cfg.ShutterVolume.Value;
            if (vol <= 0.001f) return;
            try
            {
                if (clickSource == null)
                {
                    clickClip = MakeClick();
                    clickSource = gameObject.AddComponent<AudioSource>();
                    clickSource.playOnAwake = false;
                    clickSource.spatialBlend = 1f;
                    clickSource.rolloffMode = AudioRolloffMode.Linear;
                    clickSource.minDistance = 0.5f;
                    clickSource.maxDistance = 12f;
                }
                clickSource.PlayOneShot(clickClip, vol);
            }
            catch (Exception e) { log.LogWarning("phone camera: shutter sound failed: " + e.Message); }
        }

        /// <summary>A short synthesized "click": a noise burst and a tick that fade out fast. No audio file in the DLL.</summary>
        static AudioClip MakeClick()
        {
            const int rate = 44100;
            int n = (int)(rate * 0.09f);
            var d = new float[n];
            var rnd = new System.Random(7);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Exp(-t * 70f);
                float noise = (float)(rnd.NextDouble() * 2.0 - 1.0) * 0.6f;
                float tick = Mathf.Sin(2f * Mathf.PI * 1800f * t) * 0.5f;
                d[i] = (noise + tick) * env;
            }
            var clip = AudioClip.Create("GP_Click", n, 1, rate, false);
            // AudioClip.SetData has an overload taking ReadOnlySpan, which a net48 project cannot compile against
            // (kit reference 02), so the float[] version is called by reflection.
            var setData = typeof(AudioClip).GetMethod("SetData", new[] { typeof(float[]), typeof(int) });
            if (setData != null) setData.Invoke(clip, new object[] { d, 0 });
            return clip;
        }

        void OnDestroy()
        {
            if (preview != null) { preview.Release(); Destroy(preview); }
            if (thumb != null) Destroy(thumb);
            if (clickClip != null) Destroy(clickClip);
        }
    }
}
