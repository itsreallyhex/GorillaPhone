using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GorillaPhone.Video
{
    /// <summary>
    /// A small JSON reader for the browser's debugging messages (the game's Managed folder has no JavaScriptSerializer).
    /// Objects become Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;, numbers double, plus string, bool and null.
    /// Large strings (a screencast frame is about 20 KB of base64) take a fast path with no per-character work.
    /// </summary>
    public static class MiniJson
    {
        const int MaxDepth = 64;

        public static object Parse(string s)
        {
            int i = 0;
            object v = Value(s, ref i, 0);
            SkipSpace(s, ref i);
            if (i != s.Length) throw new FormatException("extra text after the JSON value at " + i);
            return v;
        }

        /// <summary>The string as a JSON string literal, quotes included.</summary>
        public static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        // ---- small readers for the parsed tree (null-safe, so a missing field is not an exception)

        public static Dictionary<string, object> Obj(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        public static string Str(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as string : null;
        }

        public static int Int(object o, string key, int fallback)
        {
            var d = o as Dictionary<string, object>;
            object v;
            if (d != null && d.TryGetValue(key, out v) && v is double) return (int)(double)v;
            return fallback;
        }

        public static List<object> List(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as List<object> : null;
        }

        // ---- parser

        static void SkipSpace(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\n' || s[i] == '\r' || s[i] == '\t')) i++;
        }

        static object Value(string s, ref int i, int depth)
        {
            if (depth > MaxDepth) throw new FormatException("JSON nested too deeply");
            SkipSpace(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end of JSON");
            char c = s[i];
            if (c == '{') return ReadObject(s, ref i, depth);
            if (c == '[') return ReadArray(s, ref i, depth);
            if (c == '"') return ReadString(s, ref i);
            if (c == 't') { Expect(s, ref i, "true"); return true; }
            if (c == 'f') { Expect(s, ref i, "false"); return false; }
            if (c == 'n') { Expect(s, ref i, "null"); return null; }
            return ReadNumber(s, ref i);
        }

        static void Expect(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw new FormatException("bad JSON word at " + i);
            i += word.Length;
        }

        static object ReadNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            double d;
            if (i == start || !double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new FormatException("bad JSON number at " + start);
            return d;
        }

        static string ReadString(string s, ref int i)
        {
            int start = ++i;   // past the opening quote
            int j = start;
            while (j < s.Length && s[j] != '"' && s[j] != '\\') j++;
            if (j >= s.Length) throw new FormatException("unterminated JSON string");
            if (s[j] == '"')
            {
                i = j + 1;
                return s.Substring(start, j - start);   // no escapes: one copy
            }
            var sb = new StringBuilder();
            sb.Append(s, start, j - start);
            i = j;
            while (true)
            {
                if (i >= s.Length) throw new FormatException("unterminated JSON string");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("bad escape at the end");
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case '/': sb.Append('/'); break;
                    case '\\': sb.Append('\\'); break;
                    case '"': sb.Append('"'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("bad \\u escape");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException("bad escape \\" + e);
                }
            }
        }

        static Dictionary<string, object> ReadObject(string s, ref int i, int depth)
        {
            var d = new Dictionary<string, object>();
            i++;   // {
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipSpace(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("expected a key at " + i);
                string key = ReadString(s, ref i);
                SkipSpace(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("expected ':' at " + i);
                i++;
                d[key] = Value(s, ref i, depth + 1);
                SkipSpace(s, ref i);
                if (i >= s.Length) throw new FormatException("unterminated object");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return d; }
                throw new FormatException("expected ',' or '}' at " + i);
            }
        }

        static List<object> ReadArray(string s, ref int i, int depth)
        {
            var list = new List<object>();
            i++;   // [
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(Value(s, ref i, depth + 1));
                SkipSpace(s, ref i);
                if (i >= s.Length) throw new FormatException("unterminated array");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return list; }
                throw new FormatException("expected ',' or ']' at " + i);
            }
        }
    }
}
