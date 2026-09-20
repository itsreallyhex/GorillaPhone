using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace GorillaPhone.Phone
{
    /// <summary>
    /// The details on the back of the phone: a raised camera plate at the top left (as seen from the
    /// back) with three black square lenses in a triangle and a flash, and a small banana logo.
    /// Purely visual: no colliders, all drawn in code. Positions are given as they look from the back
    /// (u to the viewer's right, v up); the phone's local +X is the viewer's left when looking at the back.
    /// </summary>
    public sealed class PhoneBack
    {
        static readonly Color CoreColor = new Color(0.18f, 0.18f, 0.21f, 1f);
        static readonly Color PlateEdge = new Color(0.34f, 0.34f, 0.38f, 1f);
        static readonly Color PlateFace = new Color(0.20f, 0.20f, 0.23f, 1f);
        static readonly Color LensBlack = new Color(0.02f, 0.02f, 0.03f, 1f);
        static readonly Color Glass = new Color(0.05f, 0.07f, 0.13f, 1f);
        static readonly Color Shine = new Color(0.55f, 0.62f, 0.85f, 0.8f);
        static readonly Color FlashColor = new Color(0.95f, 0.93f, 0.78f, 1f);

        // Lens centres inside the plate, as a fraction of the plate size, seen from the back (u right, v up).
        static readonly Vector2[] LensUV = { new Vector2(-0.22f, 0.22f), new Vector2(-0.22f, -0.22f), new Vector2(0.22f, 0f) };

        readonly Transform node;
        readonly List<Texture2D> textures = new List<Texture2D>();
        readonly List<Material> materials = new List<Material>();
        readonly Transform core, plateEdge, plateFace, flash, banana;
        readonly Transform[] lensSq = new Transform[3], glass = new Transform[3], shine = new Transform[3];

        /// <summary>Where the rear camera sits: the main lens, on the plate, in the phone's local space.</summary>
        public Vector3 MainLens { get; private set; }

        public PhoneBack(Transform parent, ManualLogSource log, Mesh cube)
        {
            var go = new GameObject("GP_Back");
            go.layer = 0;
            go.transform.SetParent(parent, false);
            node = go.transform;

            Texture2D square = Track(ShapeTextures.AppIcon(128));
            Texture2D disc = Track(ShapeTextures.Disc(64));
            Texture2D bananaTex = Track(ShapeTextures.Banana(256));

            core = MakeCube("PlateCore", cube, Track(PhoneMaterials.Opaque(log, CoreColor, null)));
            plateEdge = MakeQuad("PlateEdge", Track(PhoneMaterials.Transparent(log, PlateEdge, square)), 2);
            plateFace = MakeQuad("PlateFace", Track(PhoneMaterials.Transparent(log, PlateFace, square)), 3);
            for (int i = 0; i < 3; i++)
            {
                lensSq[i] = MakeQuad("Lens" + i, Track(PhoneMaterials.Transparent(log, LensBlack, square)), 4);
                glass[i] = MakeQuad("LensGlass" + i, Track(PhoneMaterials.Transparent(log, Glass, disc)), 5);
                shine[i] = MakeQuad("LensShine" + i, Track(PhoneMaterials.Transparent(log, Shine, disc)), 6);
            }
            flash = MakeQuad("Flash", Track(PhoneMaterials.Transparent(log, FlashColor, disc)), 4);
            banana = MakeQuad("Banana", Track(PhoneMaterials.Transparent(log, Color.white, bananaTex)), 1);
        }

        Texture2D Track(Texture2D t)
        {
            textures.Add(t);
            return t;
        }

        Material Track(Material m)
        {
            materials.Add(m);
            return m;
        }

        Transform MakeCube(string name, Mesh cube, Material mat)
        {
            var go = new GameObject(name);
            go.layer = 0;
            go.transform.SetParent(node, false);
            go.AddComponent<MeshFilter>().sharedMesh = cube;
            Setup(go.AddComponent<MeshRenderer>(), mat, 0);
            return go.transform;
        }

        /// <summary>A quad that faces the back (+Z): the primitive faces -Z, so it is turned half a turn about Y.</summary>
        Transform MakeQuad(string name, Material mat, int sortingOrder)
        {
            var go = new GameObject(name);
            go.layer = 0;
            go.transform.SetParent(node, false);
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = PhoneMaterials.Quad();
            Setup(go.AddComponent<MeshRenderer>(), mat, sortingOrder);
            return go.transform;
        }

        static void Setup(MeshRenderer mr, Material mat, int sortingOrder)
        {
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingOrder = sortingOrder;
        }

        static void Put(Transform t, float x, float y, float z, float w, float h)
        {
            t.localPosition = new Vector3(x, y, z);
            t.localScale = new Vector3(w, h, 1f);
        }

        /// <summary>Sizes and places everything for a phone of this size (width, height, thickness in metres).</summary>
        public void Layout(Vector3 size)
        {
            float w = size.x, h = size.y, t = size.z;
            float m = 0.36f * w;            // the camera plate is a rounded square about a third of the phone's width
            float plateT = 0.020f * w;      // how far it stands out of the back
            float cx = w * 0.5f - 0.05f * w - m * 0.5f;   // top left as seen from the back = local +X
            float cy = h * 0.5f - 0.05f * w - m * 0.5f;
            float zBody = t * 0.5f;
            float zTop = zBody + plateT;

            // A raised core gives the plate some depth from the side; the rounded faces sit on top of it.
            core.localPosition = new Vector3(cx, cy, zBody + plateT * 0.5f);
            core.localScale = new Vector3(m * 0.72f, m * 0.72f, plateT);
            Put(plateEdge, cx, cy, zTop + 0.0002f, m, m);
            Put(plateFace, cx, cy, zTop + 0.0008f, m * 0.94f, m * 0.94f);

            float l = 0.36f * m;            // each lens square
            for (int i = 0; i < 3; i++)
            {
                float lx = cx - LensUV[i].x * m;   // u to the viewer's right is local -X
                float ly = cy + LensUV[i].y * m;
                Put(lensSq[i], lx, ly, zTop + 0.0014f, l, l);
                Put(glass[i], lx, ly, zTop + 0.0019f, l * 0.68f, l * 0.68f);
                Put(shine[i], lx - 0.16f * l, ly + 0.16f * l, zTop + 0.0024f, l * 0.16f, l * 0.16f);
                if (i == 0) MainLens = new Vector3(lx, ly, zTop);
            }
            Put(flash, cx - 0.24f * m, cy + 0.30f * m, zTop + 0.0014f, 0.13f * m, 0.13f * m);

            // The logo, a little below the middle of the back.
            Put(banana, 0f, -0.06f * h, zBody + 0.0004f, 0.30f * w, 0.30f * w);
        }

        public void Dispose()
        {
            foreach (Texture2D t in textures) if (t != null) Object.Destroy(t);
            foreach (Material m in materials) if (m != null) Object.Destroy(m);
        }
    }
}
