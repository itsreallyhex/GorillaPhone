using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Rendering;

namespace GorillaPhone.Phone
{
    /// <summary>
    /// Materials for the phone's screen parts. Uses the same shader chain the kit verified in game
    /// (URP Unlit first, then Sprites/Default, then Unlit/Color) and its transparent recipe.
    /// </summary>
    public static class PhoneMaterials
    {
        static readonly string[] ShaderNames = { "Universal Render Pipeline/Unlit", "Sprites/Default", "Unlit/Color" };
        static Shader shader;
        static Mesh quad;

        public static Shader FindShader(ManualLogSource log)
        {
            if (shader != null) return shader;
            foreach (string n in ShaderNames)
            {
                shader = Shader.Find(n);
                if (shader != null) { log.LogInfo("phone screen shader: " + n); return shader; }
            }
            // Last resort: borrow the shader of a temporary primitive.
            var t = GameObject.CreatePrimitive(PrimitiveType.Quad);
            shader = t.GetComponent<Renderer>().sharedMaterial.shader;
            Object.DestroyImmediate(t);
            log.LogInfo("phone screen shader: primitive default " + (shader != null ? shader.name : "null"));
            return shader;
        }

        /// <summary>A unit quad (faces -Z, seen from its -Z side, u to the right, v up) borrowed from a temporary primitive.</summary>
        public static Mesh Quad()
        {
            if (quad != null) return quad;
            var t = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad = t.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(t);
            return quad;
        }

        public static Material Opaque(ManualLogSource log, Color color, Texture texture)
        {
            var m = new Material(FindShader(log));
            SetColor(m, color);
            SetTexture(m, texture);
            return m;
        }

        /// <summary>Alpha-blended, no depth write, drawn after opaque things. Sort several of them with Renderer.sortingOrder.</summary>
        public static Material Transparent(ManualLogSource log, Color color, Texture texture)
        {
            var m = new Material(FindShader(log));
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = 3000;
            SetColor(m, color);
            SetTexture(m, texture);
            return m;
        }

        public static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public static void SetTexture(Material m, Texture t)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t);
        }

        /// <summary>Flips the texture left to right without touching the mesh (scale -1, offset 1; the texture clamps).</summary>
        public static void SetMirror(Material m, bool mirror)
        {
            var scale = mirror ? new Vector2(-1f, 1f) : Vector2.one;
            var offset = mirror ? new Vector2(1f, 0f) : Vector2.zero;
            if (m.HasProperty("_BaseMap")) { m.SetTextureScale("_BaseMap", scale); m.SetTextureOffset("_BaseMap", offset); }
            if (m.HasProperty("_MainTex")) { m.SetTextureScale("_MainTex", scale); m.SetTextureOffset("_MainTex", offset); }
        }
    }
}
