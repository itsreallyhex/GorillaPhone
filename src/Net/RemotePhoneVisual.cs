using System;
using BepInEx.Logging;
using GorillaPhone.Phone;
using TMPro;
using UnityEngine;

namespace GorillaPhone.Net
{
    /// <summary>
    /// A light, physics-free copy of another player's phone: just enough to see it, who holds it and what
    /// app is open. No collider, no Rigidbody, no camera, no sound -- built fresh the first time a peer's
    /// beacon arrives (see PhoneNetSync), then simply moved to their latest reported pose after that.
    /// </summary>
    public sealed class RemotePhoneVisual
    {
        // A bit bluer than the local phone's own body/screen colours, so a remote phone visually reads as "someone else's".
        static readonly Color BodyColor = new Color(0.14f, 0.16f, 0.26f, 1f);
        static readonly Color ScreenColor = new Color(0.05f, 0.06f, 0.11f, 1f);
        static readonly Color LabelColor = new Color(0.85f, 0.88f, 1f, 1f);

        readonly GameObject root;
        readonly TextMeshPro label;
        byte lastAppId = 255;   // not a real NetAppId (0-4), so the first Apply always sets the label text
        bool lastHeld, lastHeldLeft;

        RemotePhoneVisual(GameObject root, TextMeshPro label)
        {
            this.root = root;
            this.label = label;
        }

        /// <summary>Builds the visual, or a no-op stand-in (root stays null) if anything throws, so a bad peer message can never crash the mod.</summary>
        public static RemotePhoneVisual Create(PhoneConfig cfg, ManualLogSource log, int playerId)
        {
            try
            {
                var go = new GameObject("GorillaPhone_Remote_" + playerId);
                UnityEngine.Object.DontDestroyOnLoad(go);

                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                Mesh cube = BuiltInMesh(PrimitiveType.Cube);
                Mesh quad = BuiltInMesh(PrimitiveType.Quad);

                Transform body = MakePart(go.transform, "Body", cube, shader, BodyColor);
                body.localScale = new Vector3(cfg.Width.Value, cfg.Height.Value, cfg.Thickness.Value);

                Transform screen = MakePart(go.transform, "Screen", quad, shader, ScreenColor);
                float z = -(cfg.Thickness.Value * 0.5f + 0.0005f);
                screen.localPosition = new Vector3(0f, 0f, z);
                screen.localScale = new Vector3(cfg.Width.Value * 0.92f, cfg.Height.Value * 0.95f, 1f);

                TextMeshPro lbl = PhoneText.Create(log, go.transform, "Label", "", 0.03f, 0.3f, LabelColor, 50);
                if (lbl != null) PhoneText.Place(lbl, 0f, cfg.Height.Value * 0.5f + 0.035f, 0f);

                return new RemotePhoneVisual(go, lbl);
            }
            catch (Exception e)
            {
                log.LogWarning("phone net: could not build a remote phone visual for player " + playerId + ": " + e.Message);
                return new RemotePhoneVisual(null, null);   // Apply and Destroy become no-ops
            }
        }

        static Mesh BuiltInMesh(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            Mesh m = go.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(go);
            return m;
        }

        static Transform MakePart(Transform parent, string name, Mesh mesh, Shader shader, Color color)
        {
            var go = new GameObject(name);
            go.layer = 0;   // Default, so the game's cameras draw it (same reasoning as the local phone)
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            mr.sharedMaterial = mat;
            return go.transform;
        }

        /// <summary>Moves the visual to the peer's latest reported pose (already validated by the caller) and updates the label if the app or holder changed.</summary>
        public void Apply(Vector3 position, Quaternion rotation, bool held, bool heldLeft, byte appId)
        {
            if (root == null) return;
            root.transform.SetPositionAndRotation(position, rotation);
            if (label != null && (appId != lastAppId || held != lastHeld || heldLeft != lastHeldLeft))
            {
                lastAppId = appId; lastHeld = held; lastHeldLeft = heldLeft;
                string who = held ? (heldLeft ? "Left hand" : "Right hand") : "Set down";
                PhoneText.Set(label, who + " - " + PhoneScreen.NetAppName(appId));
            }
        }

        public void Destroy()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
        }
    }
}
