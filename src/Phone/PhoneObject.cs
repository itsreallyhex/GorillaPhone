using System;
using BepInEx.Logging;
using GorillaLocomotion;
using GorillaPhone.Audio;
using GorillaPhone.Photo;
using UnityEngine;

namespace GorillaPhone.Phone
{
    /// <summary>
    /// The phone as a physical object in the world: a Rigidbody slab you can grab with either hand,
    /// throw and drop. Local only: no PhotonView, nothing networked.
    ///
    /// Collision: the collider lives on a layer the player cannot stand on, and its layer overrides
    /// make it touch only the layers the player itself walks on (floor and walls). It never touches
    /// hands, head, body, other players or trigger volumes.
    /// </summary>
    public sealed class PhoneObject : MonoBehaviour
    {
        static readonly Color BodyColor = new Color(0.10f, 0.10f, 0.12f, 1f);
        static readonly Color HoverColor = new Color(0.22f, 0.32f, 0.46f, 1f);
        static readonly Color ScreenColor = new Color(0.02f, 0.02f, 0.03f, 1f);
        static readonly string[] ShaderNames = { "Universal Render Pipeline/Unlit", "Sprites/Default", "Unlit/Color" };

        PhoneConfig cfg;
        ManualLogSource log;
        Rigidbody rb;
        BoxCollider col;
        Transform bodyT, screenT;
        Material bodyMat, screenMat;
        PhoneBack back;
        readonly ThrowTracker tracker = new ThrowTracker();

        bool held, heldLeft, hover, inputReady, prevL, prevR, loggedHandScale;
        Vector3 appliedSize;
        PhoneCamera pcam;
        PhoneScreen screen;
        PhoneAudio sound;

        /// <summary>The phone's sound (the Music page controls it).</summary>
        public PhoneAudio Sound { get { return sound; } }
        /// <summary>The phone's screen (the network beacon reads which app is open from it).</summary>
        public PhoneScreen Screen { get { return screen; } }

        /// <summary>True while a hand holds the phone, and which hand.</summary>
        public bool IsHeld { get { return held; } }
        public bool HeldLeft { get { return heldLeft; } }
        /// <summary>The visible screen: its width and height, and its Z position in the phone's own space (the viewer is on the -Z side).</summary>
        public Vector2 ScreenSize { get; private set; }
        public float ScreenZ { get; private set; }
        float lastImpact, nextBoundsCheck;
        float chordStart = -1f;
        int impactLogs;
        int locoMask;          // the layers the phone may touch: what the player walks on
        int embeddedChecks;    // consecutive checks that found the phone inside geometry

        // ------------------------------------------------------------------ creation

        /// <summary>Builds the phone and puts it in front of the player. Returns null (and logs why) if it cannot.</summary>
        public static PhoneObject Create(PhoneConfig cfg, ManualLogSource log)
        {
            var player = GTPlayer.Instance;
            if (player == null || !GorillaTagger.hasInstance) return null;

            int loco = player.locomotionEnabledLayers.value;
            if (loco == 0) loco = GTPlayer.LocomotionEnabledLayers.value;
            int blocked = loco | player.hoverboardLocomotionLayers.value;

            int layer = PickLayer(cfg.Layer.Value, blocked);
            if (layer < 0)
            {
                log.LogError("phone: no layer outside the player's walkable layers is free; not spawning");
                return null;
            }
            if (layer != cfg.Layer.Value)
                log.LogWarning("phone: layer " + cfg.Layer.Value + " is walkable for the player, using layer " + layer + " instead");

            var root = new GameObject("GorillaPhone");
            try
            {
                root.layer = layer;
                DontDestroyOnLoad(root);
                var phone = root.AddComponent<PhoneObject>();
                phone.Init(cfg, log, loco, layer);
                phone.Recall(false);
                return phone;
            }
            catch (Exception e)
            {
                log.LogError("phone: could not be built: " + e);
                Destroy(root);
                return null;
            }
        }

        static int PickLayer(int preferred, int blockedMask)
        {
            int[] order = { preferred, 3, 30, 31, 29, 28 };
            foreach (int l in order)
                if (l >= 0 && l < 32 && (blockedMask & (1 << l)) == 0) return l;
            return -1;
        }

        void Init(PhoneConfig config, ManualLogSource logger, int locoMask, int layer)
        {
            cfg = config;
            log = logger;
            this.locoMask = locoMask;

            rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = 0.2f;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.1f;
            rb.useGravity = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            // A thin object needs more solver work to be pushed out of a surface cleanly.
            rb.solverIterations = 12;
            rb.solverVelocityIterations = 4;

            col = gameObject.AddComponent<BoxCollider>();
            col.sharedMaterial = new PhysicsMaterial("GorillaPhone")
            {
                bounciness = 0.35f,
                dynamicFriction = 0.5f,
                staticFriction = 0.6f,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Maximum
            };
            col.includeLayers = locoMask;
            col.excludeLayers = ~locoMask;

            Mesh cube, quad;
            Shader shader;
            GetBuiltIns(out cube, out quad, out shader);
            bodyT = MakePart("Body", cube, shader, BodyColor, out bodyMat);
            screenT = MakePart("Screen", quad, shader, ScreenColor, out screenMat);
            back = new PhoneBack(transform, log, cube);
            ApplySize();

            // The camera and the screen live on the same object and follow its size (ApplySize lays them out).
            pcam = gameObject.AddComponent<PhoneCamera>();
            pcam.Init(cfg, log, this);
            sound = gameObject.AddComponent<PhoneAudio>();
            sound.Init(cfg, log, this, locoMask);   // sound is blocked by the same layers the phone collides with: what the player walks on
            screen = gameObject.AddComponent<PhoneScreen>();
            screen.Init(cfg, log, this, pcam);
            ApplySize();

            log.LogInfo("phone built: layer " + layer + " (" + LayerMask.LayerToName(layer) + "), collides only with mask " + locoMask
                        + ", matrix says layer " + layer + " vs Default ignored=" + Physics.GetIgnoreLayerCollision(layer, 0));
        }

        void GetBuiltIns(out Mesh cube, out Mesh quad, out Shader shader)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube = c.GetComponent<MeshFilter>().sharedMesh;
            Shader fallback = c.GetComponent<Renderer>().sharedMaterial.shader;
            DestroyImmediate(c);

            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad = q.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(q);

            shader = null;
            foreach (string n in ShaderNames)
            {
                shader = Shader.Find(n);
                if (shader != null) { log.LogInfo("phone shader: " + n); break; }
            }
            if (shader == null)
            {
                shader = fallback;
                log.LogInfo("phone shader: primitive default " + (fallback != null ? fallback.name : "null"));
            }
        }

        Transform MakePart(string name, Mesh mesh, Shader shader, Color color, out Material mat)
        {
            var go = new GameObject(name);
            go.layer = 0;   // rendered on Default so the game's cameras draw it; the collider is on the root only
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mat = new Material(shader);
            SetColor(mat, color);
            mr.sharedMaterial = mat;
            return go.transform;
        }

        static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        Vector3 WantedSize()
        {
            return new Vector3(cfg.Width.Value, cfg.Height.Value, cfg.Thickness.Value);
        }

        void ApplySize()
        {
            Vector3 size = WantedSize();
            appliedSize = size;
            float s = size.x / 0.075f;   // size relative to a real phone's width; small details scale with it
            // The collider is never thinner than 2 cm, even if the drawn slab is, so it cannot tunnel through thin floors.
            col.size = new Vector3(size.x, size.y, Mathf.Max(size.z, 0.02f));
            rb.mass = 0.2f * s;

            bodyT.localPosition = Vector3.zero;
            bodyT.localScale = size;
            // The screen faces -Z (a Quad is seen from its -Z side), one face-plate thickness proud of the body.
            ScreenZ = -(size.z * 0.5f + 0.0004f * s);
            ScreenSize = new Vector2(size.x * 0.92f, size.y * 0.95f);
            screenT.localPosition = new Vector3(0f, 0f, ScreenZ);
            screenT.localScale = new Vector3(ScreenSize.x, ScreenSize.y, 1f);
            // The back (+Z): a camera plate with three lenses and a flash, and the banana logo.
            back.Layout(size);

            if (pcam != null) pcam.Layout(size, back.MainLens);
            if (screen != null) screen.Layout();
        }

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            if ((WantedSize() - appliedSize).sqrMagnitude > 1e-8f) ApplySize();

            var poller = ControllerInputPoller.instance;
            var rig = VRRig.LocalRig;
            if (poller == null || rig == null || !GorillaTagger.hasInstance) return;

            Transform lh = HandT(rig, true), rh = HandT(rig, false);
            bool gl = poller.leftGrab, gr = poller.rightGrab;
            if (!inputReady)
            {
                // A grip already held when the phone appears must not count as a press.
                prevL = gl; prevR = gr; inputReady = true;
                return;
            }
            bool pressL = gl && !prevL, pressR = gr && !prevR;
            prevL = gl; prevR = gr;

            bool inL = InRange(lh), inR = InRange(rh);

            if (held)
            {
                bool stillHeld = heldLeft ? gl : gr;
                if (!stillHeld) Release(true);
                else if (heldLeft && pressR && inR) Grab(false, rh);
                else if (!heldLeft && pressL && inL) Grab(true, lh);
            }
            else
            {
                if (pressL && inL) Grab(true, lh);
                else if (pressR && inR) Grab(false, rh);
            }

            SetHover(!held && (inL || inR));
            Chord(poller);
            TestSummon(poller);   // TEST ONLY, remove before release
            BoundsCheck();
        }

        void LateUpdate()
        {
            // Sampled after the frame's movement; a constant one-frame lag does not change the velocity.
            if (held) tracker.Add(Time.time, transform.position, transform.rotation);
        }

        static Transform HandT(VRRig rig, bool left)
        {
            VRMap m = left ? rig.leftHand : rig.rightHand;
            return m != null ? m.rigTarget : null;
        }

        bool InRange(Transform hand)
        {
            if (hand == null) return false;
            Vector3 p = hand.position;
            float r = cfg.GrabRadius.Value;
            return (col.ClosestPoint(p) - p).sqrMagnitude <= r * r;
        }

        void SetHover(bool on)
        {
            if (on == hover) return;
            hover = on;
            SetColor(bodyMat, on ? HoverColor : BodyColor);
        }

        // ------------------------------------------------------------------ grab and throw

        void Grab(bool left, Transform hand)
        {
            tracker.Reset();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;
            // Parent to the drawn hand (VRMap.rigTarget) and keep the world pose, so the phone stays
            // exactly where it was grabbed. Parenting avoids the trailing a per-frame copy showed for the watch.
            transform.SetParent(hand, true);
            held = true;
            heldLeft = left;
            Haptic(left, 0.5f, 0.06f);
            if (!loggedHandScale)
            {
                loggedHandScale = true;
                log.LogInfo("phone grabbed with the " + (left ? "left" : "right") + " hand; hand lossyScale " + hand.lossyScale + ", phone lossyScale " + transform.lossyScale);
            }
        }

        void Release(bool throwIt)
        {
            Vector3 v = Vector3.zero, w = Vector3.zero;
            bool have = throwIt && tracker.TryGet(out v, out w);

            transform.SetParent(null, true);
            DontDestroyOnLoad(gameObject);   // parenting moved it into the hand's scene
            held = false;
            rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            // Your drawn hand can be pressed into the floor or a wall; letting go there would leave the
            // phone inside it. Move it clear first, and drop the throw if that happened.
            if (IsEmbedded(transform.position, transform.rotation))
            {
                string what = DescribeOverlaps();
                bool cleared = PushOut();
                log.LogInfo("phone released inside geometry (" + what + "): " + (cleared ? "moved clear" : "no free spot, recalled"));
                if (!cleared) Recall(false);
                have = false;
            }

            if (have)
            {
                v *= cfg.ThrowMultiplier.Value;
                float max = cfg.MaxThrowSpeed.Value;
                if (v.magnitude > max) v = v.normalized * max;
                if (w.magnitude > 60f) w = w.normalized * 60f;
                rb.linearVelocity = v;
                rb.angularVelocity = w;
            }
            log.LogInfo("phone released: " + (have ? "speed " + v.magnitude.ToString("0.0") + " m/s, spin " + w.magnitude.ToString("0.0") + " rad/s" : "no throw"));
        }

        // ------------------------------------------------------------------ haptics

        public void Haptic(bool left, float amplitude, float seconds)
        {
            try
            {
                if (GorillaTagger.hasInstance && ControllerInputPoller.instance != null)
                    GorillaTagger.Instance.StartVibration(left, amplitude, seconds);
            }
            catch (Exception e) { log.LogWarning("phone haptic failed: " + e.Message); }
        }

        void OnCollisionEnter(Collision c)
        {
            float speed = c.relativeVelocity.magnitude;
            if (impactLogs < 6)
            {
                impactLogs++;
                log.LogInfo("phone hit " + c.collider.name + " (layer " + LayerMask.LayerToName(c.collider.gameObject.layer) + ") at " + speed.ToString("0.0") + " m/s");
            }
            if (speed < cfg.ImpactHapticSpeed.Value || Time.time - lastImpact < 0.15f) return;
            lastImpact = Time.time;

            var rig = VRRig.LocalRig;
            if (rig == null) return;
            Transform lh = HandT(rig, true), rh = HandT(rig, false);
            float dl = lh != null ? (lh.position - transform.position).sqrMagnitude : float.MaxValue;
            float dr = rh != null ? (rh.position - transform.position).sqrMagnitude : float.MaxValue;
            if (Mathf.Min(dl, dr) > 4f) return;   // farther than 2 m: you would not feel it
            Haptic(dl <= dr, Mathf.Clamp01(0.2f + speed / 10f), 0.08f);
        }

        // ------------------------------------------------------------------ recall

        void Chord(ControllerInputPoller poller)
        {
            if (poller.leftControllerPrimaryButton && poller.rightControllerPrimaryButton)
            {
                if (chordStart < 0f) chordStart = Time.time;
                else if (chordStart < float.MaxValue && Time.time - chordStart >= 0.6f)
                {
                    chordStart = float.MaxValue;   // once per press
                    Recall(true);
                }
            }
            else chordStart = -1f;
        }

        // TEST ONLY, remove before release (with PhoneConfig.TestButton and its config entry): one press summons the phone.
        bool prevSummon;

        void TestSummon(ControllerInputPoller p)
        {
            bool down;
            switch (cfg.TestSummonButton.Value)
            {
                case TestButton.LeftPrimary: down = p.leftControllerPrimaryButton; break;
                case TestButton.LeftSecondary: down = p.leftControllerSecondaryButton; break;
                case TestButton.RightPrimary: down = p.rightControllerPrimaryButton; break;
                case TestButton.RightSecondary: down = p.rightControllerSecondaryButton; break;
                default: down = false; break;
            }
            if (down && !prevSummon) Recall(true);
            prevSummon = down;
        }

        void BoundsCheck()
        {
            if (held || Time.time < nextBoundsCheck) return;
            nextBoundsCheck = Time.time + 0.5f;
            Vector3 hp = GorillaTagger.Instance.headCollider.transform.position;
            Vector3 p = transform.position;
            if (float.IsNaN(p.x) || p.y < hp.y - cfg.RecallDepth.Value || (p - hp).sqrMagnitude > 300f * 300f)
            {
                log.LogInfo("phone out of bounds at " + p + ", recalling");
                Recall(true);
                return;
            }

            // Stuck inside the ground or a wall for a full second (two checks): push it out.
            if (IsEmbedded(p, transform.rotation))
            {
                if (++embeddedChecks >= 2)
                {
                    embeddedChecks = 0;
                    string what = DescribeOverlaps();
                    bool cleared = PushOut();
                    log.LogInfo("phone was stuck inside geometry at " + p + " (" + what + "): " + (cleared ? "moved clear" : "no free spot, recalled"));
                    if (!cleared) Recall(true);
                }
            }
            else embeddedChecks = 0;
        }

        /// <summary>True if the phone at this pose overlaps something the player can walk on. The box is shrunk a little so resting contact does not count.</summary>
        bool IsEmbedded(Vector3 pos, Quaternion rot)
        {
            return Physics.CheckBox(pos, col.size * 0.4f, rot, locoMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Moves the phone toward the player's head, then straight up, in 5 cm steps until it is clear. False if no free spot within 1.5 m.</summary>
        bool PushOut()
        {
            Vector3 p = transform.position;
            Quaternion r = transform.rotation;
            Vector3 toHead = GorillaTagger.Instance.headCollider.transform.position - p;
            Vector3 dir = toHead.sqrMagnitude > 1e-4f ? toHead.normalized : Vector3.up;

            for (int pass = 0; pass < 2; pass++)
            {
                Vector3 d = pass == 0 ? dir : Vector3.up;
                for (int i = 1; i <= 30; i++)
                {
                    Vector3 cand = p + d * (0.05f * i);
                    if (IsEmbedded(cand, r)) continue;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    transform.position = cand;
                    rb.position = cand;
                    return true;
                }
            }
            return false;
        }

        string DescribeOverlaps()
        {
            var hits = Physics.OverlapBox(transform.position, col.size * 0.4f, transform.rotation, locoMask, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0) return "none";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < hits.Length && i < 3; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(hits[i].name).Append(" [").Append(hits[i].GetType().Name).Append(", layer ").Append(LayerMask.LayerToName(hits[i].gameObject.layer)).Append(']');
            }
            return sb.ToString();
        }

        /// <summary>Puts the phone in front of the player at chest height, screen toward them, and lets it fall.</summary>
        public void Recall(bool haptic)
        {
            if (held) Release(false);
            Transform head = GorillaTagger.Instance.headCollider.transform;
            Vector3 f = head.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();

            Vector3 pos = head.position + f * 0.45f + Vector3.down * 0.25f;
            Quaternion rot = Quaternion.LookRotation(f, Vector3.up);   // +Z away from the player, so the -Z screen faces them
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(pos, rot);
            rb.position = pos;
            rb.rotation = rot;

            if (haptic) { Haptic(true, 0.3f, 0.05f); Haptic(false, 0.3f, 0.05f); }
            log.LogInfo("phone placed at " + pos);
        }

        void OnDestroy()
        {
            if (bodyMat != null) Destroy(bodyMat);
            if (screenMat != null) Destroy(screenMat);
            if (back != null) back.Dispose();
        }
    }
}
