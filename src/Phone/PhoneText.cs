using System;
using BepInEx.Logging;
using TMPro;
using UnityEngine;

namespace GorillaPhone.Phone
{
    /// <summary>
    /// World-space TextMeshPro labels for the screen (the 3D TextMeshPro component, no Canvas), built
    /// the way the kit's wrist watch did it. Text sizes are given in real metres, so they follow the
    /// phone's size. If the game has no usable font asset the screen simply has no text.
    /// </summary>
    public static class PhoneText
    {
        // In game (kit, 2026-09-19): a line of TextMeshPro text at a 0.01 object scale is about
        // fontSize * 0.115 cm tall, and its box needs roughly 40% more height than one line or the line is hidden.
        const float CmPerFontSize = 0.115f;

        static TMP_FontAsset font;
        static bool warned;
        static bool logged;

        static TMP_FontAsset FindFont(ManualLogSource log)
        {
            if (font != null) return font;
            try { font = TMP_Settings.defaultFontAsset; }
            catch (Exception) { /* TMP_Settings.instance can be missing */ }
            if (font == null)
            {
                var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (all != null && all.Length > 0) font = all[0];
            }
            if (font == null)
            {
                if (!warned) { warned = true; log.LogError("phone text: no TextMeshPro font asset found; the screen will have no text"); }
            }
            else if (!logged)
            {
                logged = true;
                log.LogInfo("phone text font: " + font.name);
            }
            return font;
        }

        /// <summary>A centered single-line label. lineHeight and width are in metres. Returns null if there is no font.</summary>
        public static TextMeshPro Create(ManualLogSource log, Transform parent, string name, string text, float lineHeight, float width, Color color, int sortingOrder)
        {
            TMP_FontAsset f = FindFont(log);
            if (f == null) return null;

            var go = new GameObject(name);
            go.layer = 0;
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * 0.01f;   // the text rect is then measured in centimetres

            var t = go.AddComponent<TextMeshPro>();
            t.font = f;
            t.fontSize = lineHeight * 100f / CmPerFontSize;
            t.alignment = TextAlignmentOptions.Center;
            t.color = color;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.rectTransform.sizeDelta = new Vector2(width * 100f, lineHeight * 100f * 1.5f);
            t.sortingOrder = sortingOrder;
            t.text = text;
            return t;
        }

        /// <summary>Re-sizes a label (metres); used when the phone's size changes.</summary>
        public static void Resize(TextMeshPro t, float lineHeight, float width)
        {
            if (t == null) return;
            t.fontSize = lineHeight * 100f / CmPerFontSize;
            t.rectTransform.sizeDelta = new Vector2(width * 100f, lineHeight * 100f * 1.5f);
        }

        /// <summary>Puts a label's centre at (x, y, z) in its parent's space.</summary>
        public static void Place(TextMeshPro t, float x, float y, float z)
        {
            if (t != null) t.transform.localPosition = new Vector3(x, y, z);
        }

        /// <summary>Changes the text only when it differs, so unchanged labels cost nothing.</summary>
        public static void Set(TextMeshPro t, string s)
        {
            if (t != null && t.text != s) t.text = s;
        }
    }
}
