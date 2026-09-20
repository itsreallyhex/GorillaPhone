using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace GorillaPhone.Photo
{
    /// <summary>One photo file in the gallery.</summary>
    public sealed class PhotoInfo
    {
        public string Path;
        public string Name;
        public DateTime Time;
        public bool Selfie;
        /// <summary>The game's active zones when the photo was taken, comma separated (raw enum names), or empty when unknown (old photos).</summary>
        public string Zones = "";
        public int Width;
        public int Height;
    }

    /// <summary>A decoded, shrunk photo. Rgba is ready for a Unity texture: the first row is the BOTTOM of the picture.</summary>
    public sealed class LoadResult
    {
        public string Path;
        public int TargetHeight;
        public int Width;
        public int Height;
        public byte[] Rgba;
        public string Error;
    }

    /// <summary>Photo files, names and stored details. No Unity calls, so it runs on a background thread and can be tested outside the game.</summary>
    public static class PhotoLibrary
    {
        static readonly Regex NameRx = new Regex(@"^GorillaPhone_(\d{8})_(\d{6})_(\d{3})(_selfie)?\.png$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        static readonly Dictionary<string, string> Friendly = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "forest", "Forest" }, { "city", "City" }, { "basement", "Basement" }, { "canyon", "Canyon" },
            { "beach", "Beach" }, { "mountain", "Mountain" }, { "skyJungle", "Sky Jungle" }, { "cave", "Cave" },
            { "cityWithSkyJungle", "City and Sky Jungle" }, { "tutorial", "Tutorial" }, { "rotating", "Rotating" },
            { "Metropolis", "Metropolis" }, { "cityNoBuildings", "City" }, { "attic", "Attic" }, { "arcade", "Arcade" },
            { "bayou", "Bayou" }, { "customMaps", "Custom Map" }, { "monkeBlocks", "Monke Blocks" }, { "mall", "Mall" },
            { "mines", "Mines" }, { "arena", "Arena" }, { "hoverboard", "Hoverboard" }, { "critters", "Critters" },
            { "ghostReactor", "Ghost Reactor" }, { "monkeBlocksShared", "Monke Blocks (shared)" },
            { "ghostReactorTunnel", "Ghost Reactor Tunnel" }, { "ranked", "Ranked" }, { "ghostReactorDrill", "Ghost Reactor Drill" },
            { "forestWithCity", "Forest and City" }, { "GTFC", "GTFC" }, { "SilverbackStudios", "Silverback Studios" },
            { "VIMExperience1", "VIM Experience" }, { "VIMExperience2", "VIM Experience" }, { "VIMExperience3", "VIM Experience" },
            { "VIMExperience4", "VIM Experience" }, { "drill", "Drill" }, { "eventZone", "Event Zone" }, { "spaceMap", "Space Map" }
        };

        // ------------------------------------------------------------------ stored details

        public static string BuildComment(bool selfie, string zones)
        {
            return "v=1;camera=" + (selfie ? "selfie" : "rear") + ";zones=" + (zones ?? "");
        }

        public static void ParseComment(string comment, ref bool selfie, ref string zones)
        {
            if (string.IsNullOrEmpty(comment)) return;
            foreach (string part in comment.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string key = part.Substring(0, eq), value = part.Substring(eq + 1);
                if (key == "camera") selfie = value == "selfie";
                else if (key == "zones") zones = value;
            }
        }

        /// <summary>Raw zone names ("forest,skyJungle") as a readable map name. Empty when there is nothing to show.</summary>
        public static string FriendlyZones(string rawZones)
        {
            if (string.IsNullOrEmpty(rawZones)) return "";
            var names = new List<string>();
            foreach (string z in rawZones.Split(','))
            {
                string t = z.Trim();
                if (t.Length == 0 || string.Equals(t, "none", StringComparison.OrdinalIgnoreCase)) continue;
                string f;
                if (!Friendly.TryGetValue(t, out f)) f = Prettify(t);   // a zone added by a later game update
                if (!names.Contains(f)) names.Add(f);
            }
            return string.Join(" + ", names.ToArray());
        }

        /// <summary>"someNewZone" becomes "Some New Zone".</summary>
        static string Prettify(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (i == 0) sb.Append(char.ToUpperInvariant(c));
                else
                {
                    if (char.IsUpper(c) && !char.IsUpper(s[i - 1])) sb.Append(' ');
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ scanning

        /// <summary>The time in a GorillaPhone file name, or the fallback (the file's time) for any other name.</summary>
        public static DateTime ParseTime(string fileName, DateTime fallback, out bool selfie)
        {
            selfie = false;
            Match m = NameRx.Match(fileName);
            if (!m.Success) return fallback;
            selfie = m.Groups[4].Success;
            try
            {
                DateTime t = DateTime.ParseExact(m.Groups[1].Value + m.Groups[2].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                return t.AddMilliseconds(int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        /// <summary>Every PNG directly in the folder, newest first. Reads each file's header and text chunks only.</summary>
        public static List<PhotoInfo> Scan(string folder)
        {
            var list = new List<PhotoInfo>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return list;

            foreach (string path in Directory.GetFiles(folder, "*.png", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var p = new PhotoInfo { Path = path, Name = System.IO.Path.GetFileName(path) };
                    bool selfie;
                    p.Time = ParseTime(p.Name, File.GetLastWriteTime(path), out selfie);
                    p.Selfie = selfie;

                    PngInfo info;
                    if (!PngReader.TryReadInfo(path, out info)) continue;   // not a readable PNG: it could never be shown
                    p.Width = info.Width;
                    p.Height = info.Height;
                    ParseComment(info.Comment, ref p.Selfie, ref p.Zones);
                    list.Add(p);
                }
                catch (Exception)
                {
                    // one unreadable file must not hide the others
                }
            }
            list.Sort((a, b) =>
            {
                int c = b.Time.CompareTo(a.Time);
                return c != 0 ? c : string.CompareOrdinal(b.Name, a.Name);
            });
            return list;
        }

        // ------------------------------------------------------------------ delete

        /// <summary>
        /// "Deletes" a photo by moving it into a Deleted folder next to the others, so nothing is lost.
        /// The scan only looks at the top folder, so a moved photo disappears from the gallery.
        /// </summary>
        public static bool MoveToDeleted(string path, out string error)
        {
            error = null;
            try
            {
                string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), "Deleted");
                Directory.CreateDirectory(dir);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                string ext = System.IO.Path.GetExtension(path);
                string dest = System.IO.Path.Combine(dir, name + ext);
                for (int i = 2; File.Exists(dest); i++) dest = System.IO.Path.Combine(dir, name + "_" + i + ext);
                File.Move(path, dest);
                return true;
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }
    }

    /// <summary>
    /// One background thread for everything the gallery does with files: scanning, deleting and decoding
    /// photos. Decoding is newest-request-first, so what is on screen loads before what was scrolled past.
    /// Results come back as actions that the main thread runs from Pump(); nothing here calls Unity.
    /// </summary>
    public sealed class PhotoWorker : IDisposable
    {
        readonly object gate = new object();
        readonly Queue<Action> fileOps = new Queue<Action>();
        readonly List<Action> decodes = new List<Action>();
        readonly HashSet<string> pending = new HashSet<string>();
        readonly ConcurrentQueue<Action> results = new ConcurrentQueue<Action>();
        readonly Thread thread;
        bool stop;

        public PhotoWorker()
        {
            thread = new Thread(Run) { IsBackground = true, Name = "GorillaPhone gallery", Priority = ThreadPriority.BelowNormal };
            thread.Start();
        }

        void Run()
        {
            while (true)
            {
                Action job;
                lock (gate)
                {
                    while (!stop && fileOps.Count == 0 && decodes.Count == 0) Monitor.Wait(gate);
                    if (stop) return;
                    if (fileOps.Count > 0) job = fileOps.Dequeue();
                    else
                    {
                        int last = decodes.Count - 1;
                        job = decodes[last];
                        decodes.RemoveAt(last);
                    }
                }
                try { job(); }
                catch (Exception) { /* a failed job must not stop the worker */ }
            }
        }

        public void Scan(string folder, Action<List<PhotoInfo>> done)
        {
            lock (gate)
            {
                fileOps.Enqueue(() =>
                {
                    List<PhotoInfo> list = PhotoLibrary.Scan(folder);
                    results.Enqueue(() => done(list));
                });
                Monitor.Pulse(gate);
            }
        }

        public void Delete(string path, Action<bool, string> done)
        {
            lock (gate)
            {
                fileOps.Enqueue(() =>
                {
                    string error;
                    bool ok = PhotoLibrary.MoveToDeleted(path, out error);
                    results.Enqueue(() => done(ok, error));
                });
                Monitor.Pulse(gate);
            }
        }

        /// <summary>Decodes a photo and shrinks it to at most targetHeight pixels tall. False if the same request is already waiting.</summary>
        public bool Load(string path, int targetHeight, Action<LoadResult> done)
        {
            string key = path + "|" + targetHeight;
            lock (gate)
            {
                if (!pending.Add(key)) return false;
                decodes.Add(() =>
                {
                    LoadResult r = Decode(path, targetHeight);
                    lock (gate) pending.Remove(key);
                    results.Enqueue(() => done(r));
                });
                Monitor.Pulse(gate);
            }
            return true;
        }

        static LoadResult Decode(string path, int targetHeight)
        {
            int w, h;
            byte[] px;
            PngInfo info;
            string err;
            if (!PngReader.TryDecode(path, out w, out h, out px, out info, out err))
                return new LoadResult { Path = path, TargetHeight = targetHeight, Error = err };

            int th = Math.Min(targetHeight, h);
            int tw = Math.Max(1, (int)((long)w * th / h));
            byte[] small = (th == h && tw == w) ? px : PhotoSaver.Downscale(px, w, h, tw, th);

            // A Unity texture's first row is the bottom of the picture.
            var flipped = new byte[small.Length];
            int stride = tw * 4;
            for (int y = 0; y < th; y++) Buffer.BlockCopy(small, y * stride, flipped, (th - 1 - y) * stride, stride);
            return new LoadResult { Path = path, TargetHeight = targetHeight, Width = tw, Height = th, Rgba = flipped };
        }

        /// <summary>Runs up to max finished results on the calling (main) thread. Returns how many ran.</summary>
        public int Pump(int max)
        {
            int n = 0;
            Action a;
            while (n < max && results.TryDequeue(out a))
            {
                try { a(); }
                catch (Exception) { /* keep the queue moving */ }
                n++;
            }
            return n;
        }

        public void Dispose()
        {
            lock (gate)
            {
                stop = true;
                Monitor.PulseAll(gate);
            }
        }
    }
}
