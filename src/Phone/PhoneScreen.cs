using System;
using System.Collections.Generic;
using BepInEx.Logging;
using GorillaPhone.Photo;
using TMPro;
using UnityEngine;

namespace GorillaPhone.Phone
{
    /// <summary>
    /// What is drawn on the phone's screen and how you use it. The screen wakes when the phone is held
    /// or faces you, and shows a home screen with app icons and a clock. The Camera app shows the live
    /// view with a shutter, a flip (rear/front) button, zoom (field of view) buttons, a quality button,
    /// a thumbnail of the last photo and a home button. Music and Video are placeholders for later phases.
    /// Everything is plain quads plus TextMeshPro labels (no Canvas). Buttons are poked with an index
    /// fingertip (no colliders); the holding hand's trigger also takes a photo in the Camera app.
    /// </summary>
    public sealed class PhoneScreen : MonoBehaviour
    {
        enum Page { Home, Camera }

        // Poking, in metres in the screen's own space (the viewer is on the -Z side).
        // A press happens when the fingertip crosses the plane PressDepth in front of the screen, moving toward it, and either
        //  - it moves fast enough (MinPokeSpeed), from any direction, including sliding in from the side, or
        //  - it was armed by hovering more than ArmDepth in front (a slow, careful press).
        // A fingertip resting or sliding along the surface has no inward speed and is not armed by skimming, so it does not press.
        const float ArmDepth = 0.014f;
        const float PressDepth = 0.006f;
        const float MaxThrough = 0.15f;      // crossings that end further than this behind the plane are not a poke
        const float MinPokeSpeed = 0.25f;    // metres per second toward the screen

        static readonly Color TextColor = new Color(0.95f, 0.96f, 1f, 1f);
        static readonly Color DimText = new Color(0.62f, 0.66f, 0.74f, 1f);

        sealed class Part
        {
            public Transform T;
            public Material M;
        }

        sealed class Button
        {
            public Page Page;
            public Vector2 Center;
            public Vector2 Half;
            public float NextAllowed;
            public float Cooldown = 0.4f;
            public Action OnPress;
        }

        sealed class App
        {
            public string Name;
            public bool Ready;
            public Part Icon, Glyph;
            public TextMeshPro Label, Soon;
            public Button Btn;
        }

        PhoneConfig cfg;
        ManualLogSource log;
        PhoneObject phone;
        PhoneCamera pcam;

        Transform root, homeRoot, camRoot;
        Page page = Page.Home;
        bool screenOn;
        float offSince = -100f;
        readonly List<Button> buttons = new List<Button>();
        readonly List<Texture2D> textures = new List<Texture2D>();
        readonly List<Material> materials = new List<Material>();
        readonly bool[] armed = new bool[2];   // 0 = left index, 1 = right index
        readonly Vector3[] prevTip = new Vector3[2];   // last frame's fingertip in screen space
        readonly bool[] prevValid = new bool[2];

        // On every page: the dynamic island (a black pill at the top centre) with the selfie camera in it
        Part island, islandLens;

        // Home page
        Part homeBg;
        TextMeshPro clock;
        float nextClockCheck;
        readonly List<App> apps = new List<App>();

        // Camera page
        Part preview, flash, shutter, flip, homeBtnPart, zoomIn, zoomOut, chip, thumbFrame, thumb;
        TextMeshPro fovLabel, fovValue, chipText;
        Button shutterBtn, flipBtn, homeBtn, zoomInBtn, zoomOutBtn, chipBtn;
        RenderTexture lastPreview;
        bool lastMirror;
        float flashAlpha;
        bool prevTrigger = true;
        float thumbAspect = 9f / 16f;

        // ------------------------------------------------------------------ setup

        public void Init(PhoneConfig config, ManualLogSource logger, PhoneObject owner, PhoneCamera camera)
        {
            cfg = config;
            log = logger;
            phone = owner;
            pcam = camera;

            root = NewNode("GP_ScreenRoot", transform);
            homeRoot = NewNode("Home", root);
            camRoot = NewNode("Camera", root);

            BuildIsland();
            BuildHome();
            BuildCamera();

            pcam.ModeChanged += OnModeChanged;
            pcam.ThumbnailChanged += OnThumbnailChanged;
            OnModeChanged();

            SetPage(Page.Home);
            root.gameObject.SetActive(false);   // Update lights the screen when the phone is held or faces you
        }

        static Transform NewNode(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.layer = 0;   // Default, so the game's cameras draw it
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        Texture2D Track(Texture2D t)
        {
            textures.Add(t);
            return t;
        }

        Material TrackMaterial(Material m)
        {
            materials.Add(m);
            return m;
        }

        Part MakePart(Transform parent, string name, Material mat, int sortingOrder)
        {
            Transform t = NewNode(name, parent);
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = PhoneMaterials.Quad();
            var mr = t.gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingOrder = sortingOrder;
            return new Part { T = t, M = mat };
        }

        Part Opaque(Transform parent, string name, Color c, Texture tex, int order)
        {
            return MakePart(parent, name, TrackMaterial(PhoneMaterials.Opaque(log, c, tex)), order);
        }

        Part Glass(Transform parent, string name, Color c, Texture tex, int order)
        {
            return MakePart(parent, name, TrackMaterial(PhoneMaterials.Transparent(log, c, tex)), order);
        }

        Button AddButton(Page p, Action onPress, float cooldown)
        {
            var b = new Button { Page = p, OnPress = onPress, Cooldown = cooldown };
            buttons.Add(b);
            return b;
        }

        void BuildIsland()
        {
            // Drawn above the page contents (higher sorting order) so it overlaps them like a real one.
            island = Glass(root, "Island", new Color(0.01f, 0.01f, 0.015f, 1f), Track(ShapeTextures.Pill(192, 64)), 40);
            islandLens = Glass(root, "IslandLens", new Color(0.06f, 0.08f, 0.16f, 1f), Track(ShapeTextures.Disc(64)), 41);
        }

        void BuildHome()
        {
            homeBg = Opaque(homeRoot, "Background", new Color(0.05f, 0.06f, 0.10f, 1f), null, 0);
            clock = PhoneText.Create(log, homeRoot, "Clock", "--:--", 0.01f, 0.1f, TextColor, 30);

            Texture2D iconTex = Track(ShapeTextures.AppIcon(128));
            AddApp("Camera", new Color(0.20f, 0.55f, 0.95f), true, iconTex, Track(ShapeTextures.GlyphCamera(128)), OpenCamera);
            AddApp("Music", new Color(0.95f, 0.35f, 0.45f), false, iconTex, Track(ShapeTextures.GlyphMusic(128)), null);
            AddApp("Video", new Color(0.58f, 0.38f, 0.95f), false, iconTex, Track(ShapeTextures.GlyphVideo(128)), null);
        }

        void AddApp(string name, Color tint, bool ready, Texture2D iconTex, Texture2D glyphTex, Action open)
        {
            var app = new App { Name = name, Ready = ready };
            // Apps that do not exist yet are dimmed.
            Color bg = ready ? tint : Color.Lerp(tint, new Color(0.25f, 0.26f, 0.30f, 1f), 0.6f);
            Color fg = ready ? Color.white : new Color(1f, 1f, 1f, 0.55f);
            app.Icon = Glass(homeRoot, name + "Icon", bg, iconTex, 10);
            app.Glyph = Glass(homeRoot, name + "Glyph", fg, glyphTex, 11);
            app.Label = PhoneText.Create(log, homeRoot, name + "Label", name, 0.01f, 0.1f, ready ? TextColor : DimText, 30);
            if (!ready) app.Soon = PhoneText.Create(log, homeRoot, name + "Soon", "soon", 0.01f, 0.1f, DimText, 30);
            app.Btn = AddButton(Page.Home, open, 0.5f);
            apps.Add(app);
        }

        void BuildCamera()
        {
            preview = Opaque(camRoot, "Preview", Color.white, null, 0);
            flash = Glass(camRoot, "Flash", new Color(1f, 1f, 1f, 0f), null, 3);
            shutter = Glass(camRoot, "Shutter", Color.white, Track(ShapeTextures.Shutter(128)), 10);
            flip = Glass(camRoot, "Flip", Color.white, Track(ShapeTextures.Flip(128)), 10);
            homeBtnPart = Glass(camRoot, "HomeButton", Color.white, Track(ShapeTextures.Home(128)), 10);
            zoomIn = Glass(camRoot, "ZoomIn", Color.white, Track(ShapeTextures.Plus(128)), 10);
            zoomOut = Glass(camRoot, "ZoomOut", Color.white, Track(ShapeTextures.Minus(128)), 10);
            chip = Glass(camRoot, "Chip", Color.white, Track(ShapeTextures.Chip(192, 64)), 10);
            thumbFrame = Opaque(camRoot, "ThumbFrame", Color.white, null, 6);
            thumb = Opaque(camRoot, "Thumb", Color.white, null, 7);

            fovLabel = PhoneText.Create(log, camRoot, "FovLabel", "FOV", 0.01f, 0.1f, DimText, 30);
            fovValue = PhoneText.Create(log, camRoot, "FovValue", "75", 0.01f, 0.1f, TextColor, 30);
            chipText = PhoneText.Create(log, camRoot, "ChipText", "Med", 0.01f, 0.1f, TextColor, 31);

            flash.T.gameObject.SetActive(false);
            preview.T.gameObject.SetActive(false);   // shown once the camera has drawn a frame
            thumbFrame.T.gameObject.SetActive(false);
            thumb.T.gameObject.SetActive(false);

            shutterBtn = AddButton(Page.Camera, Shoot, 0.4f);
            flipBtn = AddButton(Page.Camera, delegate { pcam.ToggleMode(); }, 0.4f);
            homeBtn = AddButton(Page.Camera, GoHome, 0.4f);
            zoomInBtn = AddButton(Page.Camera, delegate { pcam.ZoomStep(true); }, 0.15f);
            zoomOutBtn = AddButton(Page.Camera, delegate { pcam.ZoomStep(false); }, 0.15f);
            chipBtn = AddButton(Page.Camera, delegate { pcam.CycleQuality(); }, 0.5f);
        }

        /// <summary>Called by the phone whenever its size changes, and when the thumbnail's shape changes.</summary>
        public void Layout()
        {
            if (root == null) return;
            float sw = phone.ScreenSize.x, sh = phone.ScreenSize.y;
            root.localPosition = new Vector3(0f, 0f, phone.ScreenZ);
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
            const float margin = 0.008f;   // a little forgiveness around each button

            // ---- Dynamic island: a black pill at the top centre, the selfie camera on its right.
            // PhoneCamera puts the front camera at the same spot (0.075 and 0.055 of the screen width).
            float iy = sh * 0.5f - 0.055f * sw;
            Place(island, 0f, iy, 0.26f * sw, 0.075f * sw, -0.0030f);
            Place(islandLens, 0.075f * sw, iy, 0.032f * sw, 0.032f * sw, -0.0034f);

            // ---- Home page
            Place(homeBg, 0f, 0f, sw, sh, -0.0004f);
            PhoneText.Resize(clock, 0.075f * sw, 0.5f * sw);
            PhoneText.Place(clock, 0f, sh * 0.5f - 0.20f * sw, -0.0020f);   // below the island

            float isz = 0.32f * sw;
            for (int i = 0; i < apps.Count; i++)
            {
                App a = apps[i];
                float cx = (i % 2 == 0 ? -0.23f : 0.23f) * sw;
                float cy = sh * 0.5f - 0.50f * sw - (i / 2) * 0.50f * sw;
                Place(a.Icon, cx, cy, isz, isz, -0.0010f);
                Place(a.Glyph, cx, cy, isz, isz, -0.0016f);
                PhoneText.Resize(a.Label, 0.055f * sw, 0.42f * sw);
                PhoneText.Place(a.Label, cx, cy - isz * 0.5f - 0.06f * sw, -0.0020f);
                if (a.Soon != null)
                {
                    PhoneText.Resize(a.Soon, 0.04f * sw, 0.30f * sw);
                    PhoneText.Place(a.Soon, cx, cy - isz * 0.5f - 0.115f * sw, -0.0020f);
                }
                a.Btn.Center = new Vector2(cx, cy);
                a.Btn.Half = new Vector2(isz * 0.5f + margin, isz * 0.5f + margin);
            }

            // ---- Camera page
            Place(preview, 0f, 0f, sw, sh, -0.0004f);
            Place(flash, 0f, 0f, sw, sh, -0.0006f);

            float top = sh * 0.5f - 0.10f * sw;
            float hd = 0.14f * sw;
            Place(homeBtnPart, -0.36f * sw, top, hd, hd, -0.0010f);
            SetRect(homeBtn, -0.36f * sw, top, hd, margin);

            float ch = 0.11f * sw, cw = ch * 3f;
            Place(chip, 0.30f * sw, top, cw, ch, -0.0010f);
            PhoneText.Resize(chipText, 0.05f * sw, 0.30f * sw);
            PhoneText.Place(chipText, 0.30f * sw, top, -0.0020f);
            chipBtn.Center = new Vector2(0.30f * sw, top);
            chipBtn.Half = new Vector2(cw * 0.5f + margin, ch * 0.5f + margin);

            float zx = 0.395f * sw, zd = 0.14f * sw;
            Place(zoomIn, zx, 0.20f * sw, zd, zd, -0.0010f);
            Place(zoomOut, zx, -0.20f * sw, zd, zd, -0.0010f);
            SetRect(zoomInBtn, zx, 0.20f * sw, zd, margin);
            SetRect(zoomOutBtn, zx, -0.20f * sw, zd, margin);
            PhoneText.Resize(fovLabel, 0.035f * sw, 0.16f * sw);
            PhoneText.Place(fovLabel, zx, 0.035f * sw, -0.0020f);
            PhoneText.Resize(fovValue, 0.06f * sw, 0.16f * sw);
            PhoneText.Place(fovValue, zx, -0.03f * sw, -0.0020f);

            float y = -sh * 0.5f + 0.22f * sw;
            float sd = 0.30f * sw, fd = 0.20f * sw;
            Place(shutter, 0f, y, sd, sd, -0.0010f);
            Place(flip, 0.32f * sw, y, fd, fd, -0.0010f);
            SetRect(shutterBtn, 0f, y, sd, margin);
            SetRect(flipBtn, 0.32f * sw, y, fd, margin);

            float tw = 0.20f * sw, th = tw / thumbAspect, m = 0.012f * sw;
            Place(thumbFrame, -0.32f * sw, y, tw + m, th + m, -0.0012f);
            Place(thumb, -0.32f * sw, y, tw, th, -0.0016f);
        }

        static void Place(Part p, float x, float y, float w, float h, float z)
        {
            p.T.localPosition = new Vector3(x, y, z);
            p.T.localScale = new Vector3(w, h, 1f);
        }

        static void SetRect(Button b, float x, float y, float size, float margin)
        {
            b.Center = new Vector2(x, y);
            b.Half = new Vector2(size * 0.5f + margin, size * 0.5f + margin);
        }

        // ------------------------------------------------------------------ pages and power

        void SetPage(Page p)
        {
            page = p;
            homeRoot.gameObject.SetActive(p == Page.Home);
            camRoot.gameObject.SetActive(p == Page.Camera);
            for (int h = 0; h < 2; h++) { armed[h] = false; prevValid[h] = false; }   // a page change is not a poke
            pcam.PreviewEnabled = screenOn && p == Page.Camera;
        }

        void OpenCamera()
        {
            SetPage(Page.Camera);
        }

        void GoHome()
        {
            SetPage(Page.Home);
        }

        /// <summary>The screen is lit while the phone is held, or while it faces you from within about 1.5 m (with some slack so it does not flicker).</summary>
        bool ShouldBeOn()
        {
            if (phone.IsHeld) return true;
            if (!GorillaTagger.hasInstance) return false;
            Vector3 to = GorillaTagger.Instance.headCollider.transform.position - transform.position;
            float d = to.magnitude;
            if (d < 0.01f) return true;
            float facing = Vector3.Dot(-transform.forward, to / d);   // the screen faces -Z
            return screenOn ? (d < 2.0f && facing > 0.05f) : (d < 1.5f && facing > 0.25f);
        }

        void SetScreen(bool on)
        {
            screenOn = on;
            root.gameObject.SetActive(on);
            if (!on) offSince = Time.unscaledTime;
            else if (Time.unscaledTime - offSince > 8f) page = Page.Home;   // a long sleep returns to the home screen
            SetPage(page);
        }

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            if (pcam == null || phone == null) return;

            bool on = ShouldBeOn();
            if (on != screenOn) SetScreen(on);
            if (!screenOn) return;

            if (page == Page.Home) UpdateHome();
            else UpdateCamera();

            var poller = ControllerInputPoller.instance;
            var rig = VRRig.LocalRig;
            if (poller == null || rig == null || !GorillaTagger.hasInstance) return;
            if (page == Page.Camera) TriggerShutter(poller);
            Poke(rig);
        }

        void UpdateHome()
        {
            if (Time.unscaledTime < nextClockCheck) return;
            nextClockCheck = Time.unscaledTime + 0.5f;
            PhoneText.Set(clock, DateTime.Now.ToString("HH:mm"));
        }

        void UpdateCamera()
        {
            RenderTexture rt = pcam.Preview;
            if (rt != lastPreview)
            {
                lastPreview = rt;
                PhoneMaterials.SetTexture(preview.M, rt);
            }
            if (pcam.MirrorPreview != lastMirror) OnModeChanged();
            bool show = rt != null && pcam.HasFrame;
            if (preview.T.gameObject.activeSelf != show) preview.T.gameObject.SetActive(show);

            if (flashAlpha > 0f)
            {
                flashAlpha = Mathf.Max(0f, flashAlpha - Time.unscaledDeltaTime * 6f);
                PhoneMaterials.SetColor(flash.M, new Color(1f, 1f, 1f, flashAlpha * 0.8f));
                if (flashAlpha <= 0f) flash.T.gameObject.SetActive(false);
            }

            PhoneText.Set(fovValue, Mathf.RoundToInt(pcam.Fov).ToString());
            PhoneText.Set(chipText, pcam.QualityName);
        }

        void OnModeChanged()
        {
            lastMirror = pcam.MirrorPreview;
            PhoneMaterials.SetMirror(preview.M, lastMirror);
        }

        void OnThumbnailChanged()
        {
            Texture2D t = pcam.Thumbnail;
            if (t == null) return;
            thumbAspect = (float)t.width / t.height;
            PhoneMaterials.SetTexture(thumb.M, t);
            thumb.T.gameObject.SetActive(true);
            thumbFrame.T.gameObject.SetActive(true);
            Layout();
        }

        // ------------------------------------------------------------------ controls

        void Shoot()
        {
            if (!pcam.RequestPhoto()) return;
            flashAlpha = 1f;
            flash.T.gameObject.SetActive(true);
            phone.Haptic(true, 0.4f, 0.05f);
            phone.Haptic(false, 0.4f, 0.05f);
        }

        /// <summary>While the phone is held in the Camera app, the trigger of the holding hand takes a photo (on the press, not while held down).</summary>
        void TriggerShutter(ControllerInputPoller p)
        {
            if (!phone.IsHeld || !cfg.TriggerShutter.Value)
            {
                prevTrigger = true;   // so a trigger already down when you grab the phone does not take a photo
                return;
            }
            bool down = phone.HeldLeft ? p.leftControllerTriggerButton : p.rightControllerTriggerButton;
            if (down && !prevTrigger) Shoot();
            prevTrigger = down;
        }

        void Poke(VRRig rig)
        {
            float sw = phone.ScreenSize.x, sh = phone.ScreenSize.y;
            float dt = Time.unscaledDeltaTime;
            for (int h = 0; h < 2; h++)
            {
                bool left = h == 0;
                // The hand holding the phone does not poke it.
                if (phone.IsHeld && phone.HeldLeft == left) { armed[h] = false; prevValid[h] = false; continue; }

                Vector3 tip;
                if (!Tip(rig, left, out tip)) { armed[h] = false; prevValid[h] = false; continue; }

                // The fingertip in the screen's own space: z is negative in front of the screen.
                Vector3 p = root.InverseTransformPoint(tip);

                // Hovering well in front of the screen arms a slow press.
                bool overScreen = Mathf.Abs(p.x) <= sw * 0.5f + 0.03f && Mathf.Abs(p.y) <= sh * 0.5f + 0.03f;
                if (!overScreen) armed[h] = false;
                else if (p.z < -ArmDepth) armed[h] = true;

                if (prevValid[h] && dt > 0f)
                {
                    Vector3 q = prevTip[h];
                    // Did the fingertip cross the press plane this frame, moving toward the screen? (Not a teleport.)
                    if (q.z < -PressDepth && p.z >= -PressDepth && p.z < MaxThrough && (p - q).sqrMagnitude < 0.09f)
                    {
                        float speed = (p.z - q.z) / dt;
                        if (armed[h] || speed >= MinPokeSpeed)
                        {
                            armed[h] = false;
                            // Test where the fingertip crossed the plane, not where it ended up, so a fast poke cannot skip a button.
                            float t = (-PressDepth - q.z) / (p.z - q.z);
                            Button b = Hit(Vector3.Lerp(q, p, t));
                            if (b != null && Time.unscaledTime >= b.NextAllowed)
                            {
                                b.NextAllowed = Time.unscaledTime + b.Cooldown;
                                phone.Haptic(left, b.OnPress != null ? 0.35f : 0.12f, 0.04f);   // a lighter buzz for an app that is not there yet
                                if (b.OnPress != null) b.OnPress();
                            }
                        }
                    }
                }
                prevTip[h] = p;
                prevValid[h] = true;
            }
        }

        Button Hit(Vector3 p)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                Button b = buttons[i];
                if (b.Page != page) continue;
                if (Mathf.Abs(p.x - b.Center.x) <= b.Half.x && Mathf.Abs(p.y - b.Center.y) <= b.Half.y) return b;
            }
            return null;
        }

        /// <summary>Index fingertip, projected past the last bone (the projection that worked for the kit's poke buttons in game).</summary>
        static bool Tip(VRRig rig, bool left, out Vector3 tip)
        {
            tip = Vector3.zero;
            VRMapIndex idx = left ? rig.leftIndex : rig.rightIndex;
            if (idx == null || idx.fingerBone2 == null || idx.fingerBone3 == null) return false;
            Vector3 b2 = idx.fingerBone2.position, b3 = idx.fingerBone3.position;
            tip = b3 + (b3 - b2) * 0.8f;
            return true;
        }

        void OnDestroy()
        {
            if (pcam != null)
            {
                pcam.ModeChanged -= OnModeChanged;
                pcam.ThumbnailChanged -= OnThumbnailChanged;
                pcam.PreviewEnabled = false;
            }
            foreach (Texture2D t in textures) if (t != null) Destroy(t);
            foreach (Material m in materials) if (m != null) Destroy(m);
        }
    }
}
