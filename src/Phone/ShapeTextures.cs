using System;
using UnityEngine;

namespace GorillaPhone.Phone
{
    /// <summary>
    /// The screen's button pictures, drawn in code (no image files in the DLL). Each shape is sampled
    /// several times per pixel so its edges are smooth. The sample function gets coordinates where the
    /// texture's height spans -0.5 to 0.5 (y up); the width spans -0.5 to 0.5 times the aspect ratio.
    /// </summary>
    public static class ShapeTextures
    {
        static readonly Color Clear = new Color(1f, 1f, 1f, 0f);
        static readonly Color White = Color.white;
        static readonly Color DarkDisc = new Color(0.08f, 0.08f, 0.10f, 0.55f);

        static Texture2D Make(string name, int w, int h, Func<float, float, Color> sample)
        {
            const int N = 3;
            float aspect = (float)w / h;
            var px = new Color32[w * h];
            for (int j = 0; j < h; j++)
            {
                for (int i = 0; i < w; i++)
                {
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    for (int sj = 0; sj < N; sj++)
                    {
                        for (int si = 0; si < N; si++)
                        {
                            float x = ((i + (si + 0.5f) / N) / w - 0.5f) * aspect;
                            float y = (j + (sj + 0.5f) / N) / h - 0.5f;
                            Color c = sample(x, y);
                            r += c.r * c.a;
                            g += c.g * c.a;
                            b += c.b * c.a;
                            a += c.a;
                        }
                    }
                    px[j * w + i] = a > 0f ? new Color(r / a, g / a, b / a, a / (N * N)) : Clear;
                }
            }
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        static Texture2D Make(string name, int size, Func<float, float, Color> sample)
        {
            return Make(name, size, size, sample);
        }

        // ------------------------------------------------------------------ shape tests

        static bool InRect(float x, float y, float cx, float cy, float hx, float hy)
        {
            return Mathf.Abs(x - cx) <= hx && Mathf.Abs(y - cy) <= hy;
        }

        /// <summary>A rounded rectangle centred on the origin with half sizes hx, hy and corner radius r.</summary>
        static bool InRoundRect(float x, float y, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(x) - (hx - r), qy = Mathf.Abs(y) - (hy - r);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r <= 0f;
        }

        static bool InTriangle(float x, float y, Vector2 a, Vector2 b, Vector2 c)
        {
            var p = new Vector2(x, y);
            float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos);
        }

        static float Cross(Vector2 p, Vector2 a, Vector2 b)
        {
            return (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
        }

        // ------------------------------------------------------------------ camera page buttons

        /// <summary>The shutter: a white ring around a white disc.</summary>
        public static Texture2D Shutter(int size)
        {
            return Make("GP_Shutter", size, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                return (r <= 0.5f && r >= 0.43f) || r <= 0.35f ? White : Clear;
            });
        }

        /// <summary>A dark translucent disc with a white glyph, the look of the small round buttons.</summary>
        static Texture2D RoundButton(string name, int size, Func<float, float, bool> glyph)
        {
            return Make(name, size, (x, y) =>
            {
                if (x * x + y * y > 0.25f) return Clear;
                return glyph(x, y) ? White : DarkDisc;
            });
        }

        /// <summary>The camera flip button: a circular arrow.</summary>
        public static Texture2D Flip(int size)
        {
            const float R = 0.22f, half = 0.035f;
            const float a0 = 60f * Mathf.Deg2Rad, a1 = 330f * Mathf.Deg2Rad;   // the arc runs counter-clockwise from a0 to a1
            var tip = new Vector2(R * Mathf.Cos(a1), R * Mathf.Sin(a1));
            var tangent = new Vector2(-Mathf.Sin(a1), Mathf.Cos(a1));
            var radial = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
            Vector2 pa = tip + radial * 0.075f, pb = tip - radial * 0.075f, pc = tip + tangent * 0.10f;

            return RoundButton("GP_Flip", size, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                if (Mathf.Abs(r - R) <= half)
                {
                    float ang = Mathf.Atan2(y, x);
                    if (ang < 0f) ang += 2f * Mathf.PI;
                    if (ang >= a0 && ang <= a1) return true;
                }
                return InTriangle(x, y, pa, pb, pc);
            });
        }

        public static Texture2D Plus(int size)
        {
            return RoundButton("GP_Plus", size, (x, y) => InRect(x, y, 0f, 0f, 0.22f, 0.035f) || InRect(x, y, 0f, 0f, 0.035f, 0.22f));
        }

        public static Texture2D Minus(int size)
        {
            return RoundButton("GP_Minus", size, (x, y) => InRect(x, y, 0f, 0f, 0.22f, 0.035f));
        }

        /// <summary>The music page's play button: a triangle, a little right of centre so it looks centred.</summary>
        public static Texture2D Play(int size)
        {
            Vector2 a = new Vector2(-0.10f, -0.20f), b = new Vector2(-0.10f, 0.20f), c = new Vector2(0.21f, 0f);
            return RoundButton("GP_Play", size, (x, y) => InTriangle(x, y, a, b, c));
        }

        /// <summary>The music page's pause button: two bars.</summary>
        public static Texture2D Pause(int size)
        {
            return RoundButton("GP_Pause", size, (x, y) => InRect(x, y, -0.09f, 0f, 0.05f, 0.19f) || InRect(x, y, 0.09f, 0f, 0.05f, 0.19f));
        }

        /// <summary>The home button: a small house.</summary>
        public static Texture2D Home(int size)
        {
            Vector2 a = new Vector2(-0.27f, 0.03f), b = new Vector2(0.27f, 0.03f), c = new Vector2(0f, 0.28f);
            return RoundButton("GP_Home", size, (x, y) =>
            {
                if (InTriangle(x, y, a, b, c)) return true;
                bool body = InRect(x, y, 0f, -0.11f, 0.17f, 0.14f);
                bool door = InRect(x, y, 0f, -0.18f, 0.05f, 0.07f);
                return body && !door;
            });
        }

        /// <summary>A pill (a rounded label background) with the given pixel size.</summary>
        public static Texture2D Chip(int w, int h)
        {
            return Make("GP_Chip", w, h, (x, y) =>
                InRoundRect(x, y, 0.5f * w / h, 0.5f, 0.5f) ? new Color(0.08f, 0.08f, 0.10f, 0.6f) : Clear);
        }

        // ------------------------------------------------------------------ home page

        /// <summary>A white rounded square; tint it with the material colour for each app.</summary>
        public static Texture2D AppIcon(int size)
        {
            return Make("GP_AppIcon", size, (x, y) => InRoundRect(x, y, 0.5f, 0.5f, 0.2f) ? White : Clear);
        }

        public static Texture2D GlyphCamera(int size)
        {
            return Make("GP_GlyphCamera", size, (x, y) =>
            {
                float yy = y + 0.02f;
                bool body = InRoundRect(x, yy, 0.28f, 0.19f, 0.05f);
                float r = Mathf.Sqrt(x * x + yy * yy);
                bool glyph = (body && r >= 0.14f) || r < 0.08f || InRect(x, y, -0.12f, 0.20f, 0.08f, 0.04f);
                return glyph ? White : Clear;
            });
        }

        public static Texture2D GlyphMusic(int size)
        {
            return Make("GP_GlyphMusic", size, (x, y) =>
            {
                float dx = x + 0.08f, dy = y + 0.16f;
                bool head = dx * dx + dy * dy <= 0.10f * 0.10f;
                bool stem = InRect(x, y, 0.02f, 0.03f, 0.02f, 0.19f);
                bool flag = InRect(x, y, 0.10f, 0.16f, 0.08f, 0.05f);
                return head || stem || flag ? White : Clear;
            });
        }

        public static Texture2D GlyphVideo(int size)
        {
            Vector2 a = new Vector2(-0.11f, -0.19f), b = new Vector2(-0.11f, 0.19f), c = new Vector2(0.20f, 0f);
            return Make("GP_GlyphVideo", size, (x, y) => InTriangle(x, y, a, b, c) ? White : Clear);
        }

        // ------------------------------------------------------------------ phone details

        /// <summary>A white disc; tint it with the material colour (lens glass, flash, the island's camera dot).</summary>
        public static Texture2D Disc(int size)
        {
            return Make("GP_Disc", size, (x, y) => x * x + y * y <= 0.25f ? White : Clear);
        }

        /// <summary>A white pill (fully rounded ends) of the given pixel size; tint it black for the dynamic island.</summary>
        public static Texture2D Pill(int w, int h)
        {
            return Make("GP_Pill", w, h, (x, y) => InRoundRect(x, y, 0.5f * w / h, 0.5f, 0.5f) ? White : Clear);
        }

        /// <summary>
        /// The logo on the back: a yellow banana (a crescent between two circles) with a brown stem and tip.
        /// The crescent's tips are at about (0.39, -0.07) and (-0.07, 0.39).
        /// </summary>
        public static Texture2D Banana(int size)
        {
            Color yellow = new Color(0.99f, 0.84f, 0.18f, 1f);
            Color brown = new Color(0.36f, 0.23f, 0.09f, 1f);
            return Make("GP_Banana", size, (x, y) =>
            {
                float d1 = x * x + y * y;                              // big circle, radius 0.40 around the origin
                float dx = x - 0.10f, dy = y - 0.10f;
                float d2 = dx * dx + dy * dy;                          // the circle that is cut out, radius 0.34
                bool crescent = d1 <= 0.40f * 0.40f && d2 >= 0.34f * 0.34f;

                // Stem at the upper tip, dark tip at the other end.
                bool stem = InRect(x, y, -0.09f, 0.43f, 0.022f, 0.045f);
                float tx = x - 0.395f, ty = y + 0.072f;
                bool tip = tx * tx + ty * ty <= 0.035f * 0.035f;
                float sx = x + 0.072f, sy = y - 0.395f;
                bool stemBase = sx * sx + sy * sy <= 0.03f * 0.03f;

                if (stem || tip || stemBase) return brown;
                return crescent ? yellow : Clear;
            });
        }

        // ------------------------------------------------------------------ gallery

        /// <summary>A round button with a triangle pointing in the direction (dx, dy).</summary>
        static Texture2D ArrowButton(string name, int size, float dx, float dy)
        {
            var dir = new Vector2(dx, dy);
            var perp = new Vector2(-dy, dx);
            Vector2 tip = dir * 0.20f, baseCentre = -dir * 0.10f;
            Vector2 a = baseCentre + perp * 0.17f, b = baseCentre - perp * 0.17f;
            return RoundButton(name, size, (x, y) => InTriangle(x, y, tip, a, b));
        }

        public static Texture2D ArrowUp(int size) { return ArrowButton("GP_ArrowUp", size, 0f, 1f); }
        public static Texture2D ArrowDown(int size) { return ArrowButton("GP_ArrowDown", size, 0f, -1f); }
        public static Texture2D ArrowLeft(int size) { return ArrowButton("GP_ArrowLeft", size, -1f, 0f); }
        public static Texture2D ArrowRight(int size) { return ArrowButton("GP_ArrowRight", size, 1f, 0f); }

        /// <summary>The "back to all photos" button: four small squares.</summary>
        public static Texture2D GridButton(int size)
        {
            return RoundButton("GP_Grid", size, (x, y) =>
                InRect(x, y, -0.085f, 0.085f, 0.065f, 0.065f) || InRect(x, y, 0.085f, 0.085f, 0.065f, 0.065f) ||
                InRect(x, y, -0.085f, -0.085f, 0.065f, 0.065f) || InRect(x, y, 0.085f, -0.085f, 0.065f, 0.065f));
        }

        /// <summary>The delete button: a small bin with a lid and slots.</summary>
        public static Texture2D Trash(int size)
        {
            return RoundButton("GP_Trash", size, (x, y) =>
            {
                bool lid = InRect(x, y, 0f, 0.125f, 0.17f, 0.025f);
                bool handle = InRect(x, y, 0f, 0.18f, 0.06f, 0.02f);
                bool body = InRect(x, y, 0f, -0.04f, 0.13f, 0.14f);
                bool slot = InRect(x, y, -0.05f, -0.04f, 0.017f, 0.09f) || InRect(x, y, 0f, -0.04f, 0.017f, 0.09f) || InRect(x, y, 0.05f, -0.04f, 0.017f, 0.09f);
                return lid || handle || (body && !slot);
            });
        }

        /// <summary>A white rounded rectangle of the given pixel size (a card for the delete question).</summary>
        public static Texture2D Card(int w, int h)
        {
            return Make("GP_Card", w, h, (x, y) => InRoundRect(x, y, 0.5f * w / h, 0.5f, 0.12f) ? White : Clear);
        }

        /// <summary>The Gallery app glyph: a picture frame with a sun and two mountains.</summary>
        public static Texture2D GlyphGallery(int size)
        {
            Vector2 m1a = new Vector2(-0.22f, -0.15f), m1b = new Vector2(-0.06f, 0.07f), m1c = new Vector2(0.10f, -0.15f);
            Vector2 m2a = new Vector2(-0.02f, -0.15f), m2b = new Vector2(0.11f, -0.02f), m2c = new Vector2(0.24f, -0.15f);
            return Make("GP_GlyphGallery", size, (x, y) =>
            {
                bool frame = InRoundRect(x, y, 0.32f, 0.26f, 0.06f) && !InRoundRect(x, y, 0.27f, 0.21f, 0.03f);
                float sx = x - 0.13f, sy = y - 0.09f;
                bool sun = sx * sx + sy * sy <= 0.055f * 0.055f;
                bool inner = InRect(x, y, 0f, 0f, 0.26f, 0.20f);
                bool mountains = inner && (InTriangle(x, y, m1a, m1b, m1c) || InTriangle(x, y, m2a, m2b, m2c));
                return frame || sun || mountains ? White : Clear;
            });
        }
    }
}
