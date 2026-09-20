using System;
using System.IO;
using System.Threading;

namespace GorillaPhone.Util
{
    /// <summary>
    /// Tells the main thread when the config file changed on disk. The watcher runs on a
    /// background thread and only sets a flag; the main thread does the reload, once, after
    /// the writes have settled. No timer polls the file.
    /// </summary>
    public sealed class ConfigWatcher : IDisposable
    {
        const int SettleMs = 300;

        readonly FileSystemWatcher watcher;
        int changedAt;   // Environment.TickCount of the last event, 0 = none pending

        public ConfigWatcher(string configFilePath)
        {
            string dir = Path.GetDirectoryName(configFilePath);
            watcher = new FileSystemWatcher(dir, Path.GetFileName(configFilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            watcher.Changed += OnEvent;
            watcher.Created += OnEvent;
            watcher.Renamed += OnEvent;
            watcher.EnableRaisingEvents = true;
        }

        void OnEvent(object sender, FileSystemEventArgs e)
        {
            int now = Environment.TickCount;
            Volatile.Write(ref changedAt, now == 0 ? 1 : now);
        }

        /// <summary>True once per change, after the file has been quiet for a short moment. Call from Update.</summary>
        public bool TryConsume()
        {
            int at = Volatile.Read(ref changedAt);
            if (at == 0) return false;
            if (unchecked(Environment.TickCount - at) < SettleMs) return false;
            return Interlocked.CompareExchange(ref changedAt, 0, at) == at;
        }

        public void Dispose()
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
    }
}
