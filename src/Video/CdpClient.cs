using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace GorillaPhone.Video
{
    public sealed class CdpException : Exception
    {
        public CdpException(string message) : base(message) { }
    }

    /// <summary>
    /// A client for the browser's remote-debugging PIPE (not a socket): JSON messages each ended by a zero byte, over two streams.
    /// Writing never blocks the caller: <see cref="Post"/> only queues, a writer thread does the I/O. <see cref="Call"/> waits for the
    /// answer and must only be used from a background thread. Events arrive on the reader thread through <see cref="Event"/>;
    /// a handler must be quick (acknowledging a frame is a Post).
    /// </summary>
    public sealed class CdpClient : IDisposable
    {
        sealed class Waiter
        {
            public readonly ManualResetEvent Done = new ManualResetEvent(false);
            public Dictionary<string, object> Message;
            public string Failure;
        }

        readonly Stream write, read;
        readonly object lk = new object();
        readonly Queue<byte[]> outgoing = new Queue<byte[]>();
        readonly Dictionary<int, Waiter> waiting = new Dictionary<int, Waiter>();
        int nextId = 1;
        bool closed;

        /// <summary>The session of the page target (from Target.attachToTarget with flatten); put on every command that asks for it.</summary>
        public volatile string SessionId;

        /// <summary>An event: the method (for example Page.screencastFrame) and its params object.</summary>
        public event Action<string, Dictionary<string, object>> Event;
        /// <summary>The pipe ended (the browser exited or was closed). Raised once.</summary>
        public event Action Closed;

        public bool IsClosed { get { lock (lk) return closed; } }

        public CdpClient(Stream toBrowser, Stream fromBrowser)
        {
            write = toBrowser;
            read = fromBrowser;
            new Thread(ReadLoop) { IsBackground = true, Name = "GorillaPhone CDP read" }.Start();
            new Thread(WriteLoop) { IsBackground = true, Name = "GorillaPhone CDP write" }.Start();
        }

        // ------------------------------------------------------------------ sending

        /// <summary>Queue a command and return at once. The answer is ignored. paramsJson is a JSON object text or null.</summary>
        public void Post(string method, string paramsJson, bool session = true)
        {
            Enqueue(NextId(), method, paramsJson, session);
        }

        /// <summary>Send a command and wait for its result object. Throws CdpException on an error, a timeout or a closed pipe. Background threads only.</summary>
        public Dictionary<string, object> Call(string method, string paramsJson, bool session = true, int timeoutMs = 10000)
        {
            var w = new Waiter();
            int id = NextId();
            lock (lk)
            {
                if (closed) throw new CdpException("the browser connection is closed");
                waiting[id] = w;
            }
            Enqueue(id, method, paramsJson, session);
            if (!w.Done.WaitOne(timeoutMs))
            {
                lock (lk) waiting.Remove(id);
                throw new CdpException(method + " timed out");
            }
            if (w.Failure != null) throw new CdpException(method + ": " + w.Failure);
            return MiniJson.Obj(w.Message, "result") ?? new Dictionary<string, object>();
        }

        int NextId()
        {
            lock (lk) return nextId++;
        }

        void Enqueue(int id, string method, string paramsJson, bool session)
        {
            var sb = new StringBuilder(96 + (paramsJson != null ? paramsJson.Length : 0));
            sb.Append("{\"id\":").Append(id).Append(",\"method\":\"").Append(method).Append('"');
            if (paramsJson != null) sb.Append(",\"params\":").Append(paramsJson);
            string sid = SessionId;
            if (session && sid != null) sb.Append(",\"sessionId\":\"").Append(sid).Append('"');
            sb.Append('}');
            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
            lock (lk)
            {
                if (closed) return;
                outgoing.Enqueue(bytes);
                Monitor.Pulse(lk);
            }
        }

        void WriteLoop()
        {
            try
            {
                while (true)
                {
                    byte[] msg;
                    lock (lk)
                    {
                        while (outgoing.Count == 0 && !closed) Monitor.Wait(lk);
                        if (closed) return;
                        msg = outgoing.Dequeue();
                    }
                    write.Write(msg, 0, msg.Length);
                    write.WriteByte(0);
                    write.Flush();
                }
            }
            catch { Shutdown(); }
        }

        // ------------------------------------------------------------------ receiving

        void ReadLoop()
        {
            var ms = new MemoryStream();
            var buf = new byte[65536];
            try
            {
                while (true)
                {
                    int n = read.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    int start = 0;
                    for (int i = 0; i < n; i++)
                    {
                        if (buf[i] != 0) continue;
                        ms.Write(buf, start, i - start);
                        start = i + 1;
                        string text = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
                        ms.SetLength(0);
                        try { Handle(text); }
                        catch (FormatException) { }   // one unreadable message must not end the connection
                    }
                    ms.Write(buf, start, n - start);
                }
            }
            catch { }
            Shutdown();
        }

        void Handle(string text)
        {
            var d = MiniJson.Parse(text) as Dictionary<string, object>;
            if (d == null) return;
            object idObj;
            if (d.TryGetValue("id", out idObj) && idObj is double)
            {
                int id = (int)(double)idObj;
                Waiter w;
                lock (lk)
                {
                    if (!waiting.TryGetValue(id, out w)) return;
                    waiting.Remove(id);
                }
                w.Message = d;
                var err = MiniJson.Obj(d, "error");
                if (err != null) w.Failure = MiniJson.Str(err, "message") ?? "error";
                w.Done.Set();
                return;
            }
            string method = MiniJson.Str(d, "method");
            if (method == null) return;
            var h = Event;
            if (h != null) h(method, MiniJson.Obj(d, "params") ?? new Dictionary<string, object>());
        }

        void Shutdown()
        {
            List<Waiter> stuck;
            lock (lk)
            {
                if (closed) return;
                closed = true;
                stuck = new List<Waiter>(waiting.Values);
                waiting.Clear();
                Monitor.PulseAll(lk);
            }
            foreach (Waiter w in stuck) { w.Failure = "the browser connection closed"; w.Done.Set(); }
            var c = Closed;
            if (c != null) c();
        }

        public void Dispose()
        {
            Shutdown();
            try { write.Dispose(); } catch { }
            try { read.Dispose(); } catch { }
        }
    }
}
