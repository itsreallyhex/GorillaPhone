using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using GorillaPhone.Phone;
using UnityEngine;

namespace GorillaPhone.Net
{
    /// <summary>
    /// A small, off-by-default lab that finds out what the game's own event channel tolerates, before anything is built on it.
    /// The channel is the game's NetworkSystem event (a Photon Fusion RPC with a byte array; the game refuses payloads over Fusion's
    /// message size and only logs an error). The lab sends test messages with its own event code and logs what comes back:
    ///  - a size ladder (which payload sizes get through) and a short rate ladder (how many messages a second keep getting through),
    ///  - messages are addressed to "all", which should echo to the sender, so the size limit can be found alone;
    ///  - a second player running the lab with [Network] NetLab on logs what really arrives (sender, size, counts).
    /// It only sends when asked (the setting NetLabRun flips on), in any room unless [Network] AllowAnyRoom is turned off (then only in a
    /// MODDED room), sends nothing but test bytes, stops if you leave the room, and totals well under 100 KB per run. The game's own receiver ignores every event code except its own (Verified in source).
    /// </summary>
    public sealed class NetLab
    {
        /// <summary>The event code the lab uses. The game uses 0 to 14, 100 to 106 and 255; Photon reserves 200 and up.</summary>
        public const byte Code = 151;

        // The first PUN run passed everything up to 1024 bytes, so the ladder now goes on to find where messages stop (or get split).
        // Found in game (two runs, same result): 64 to 1500 bytes echo fine at 115 to 160 ms, and the 2000 byte unreliable message got no
        // echo and the player was out of the room right after it, so the ladder stops at 1500 and the throughput ladder can run.
        static readonly int[] Sizes = { 64, 256, 512, 768, 1024, 1200, 1500 };
        // The rate ladder: messages a second x bytes each, for RateSeconds. The small steps passed at 100% in the first run; the 1000 byte
        // steps (10, 20 and 30 KB a second) find the throughput a picture stream could use. A group of steps stops early if fewer than 60% echo back.
        static readonly int[][] Steps = { new[] { 5, 200 }, new[] { 10, 200 }, new[] { 20, 200 }, new[] { 30, 200 }, new[] { 10, 1000 }, new[] { 20, 1000 }, new[] { 30, 1000 } };
        const float RateSeconds = 3f;

        readonly ManualLogSource log;
        readonly PhoneConfig cfg;
        bool subscribed;
        readonly HashSet<int> echoed = new HashSet<int>();     // own messages that came back: key = test * 100000 + seq
        readonly Dictionary<int, int> echoLen = new Dictionary<int, int>();
        readonly Dictionary<int, float> sentAt = new Dictionary<int, float>();   // when each own message was sent (real time), to time the round trip
        readonly Dictionary<int, float> rtt = new Dictionary<int, float>();      // round trip in seconds per echoed message (frame-resolution)
        readonly Dictionary<int, int> rxCount = new Dictionary<int, int>();    // messages received per other player
        readonly Dictionary<int, long> rxBytes = new Dictionary<int, long>();
        float nextRxLog;

        public bool Running { get; private set; }

        public NetLab(ManualLogSource logger, PhoneConfig config)
        {
            log = logger;
            cfg = config;
        }

        /// <summary>Listen for lab messages (from other players, and our own echoes). Call each frame until it returns true.</summary>
        public bool Subscribe()
        {
            if (subscribed) return true;
            if (NetworkSystem.Instance == null) return false;
            NetworkSystem.Instance.OnRaiseEvent += OnEvent;
            subscribed = true;
            log.LogInfo("net lab: listening for lab messages (event code " + Code + "); network backend " + NetworkSystem.Instance.GetType().Name);
            return true;
        }

        public void Unsubscribe()
        {
            if (!subscribed) return;
            subscribed = false;
            if (NetworkSystem.Instance != null) NetworkSystem.Instance.OnRaiseEvent -= OnEvent;
        }

        // ------------------------------------------------------------------ receiving

        void OnEvent(byte code, object data, int sender)
        {
            if (code != Code) return;
            byte[] b = data as byte[];
            int len = b != null ? b.Length : -1;
            if (b == null || len < 8 || b[0] != (byte)'G' || b[1] != (byte)'P' || b[2] != (byte)'L' || b[3] != (byte)'B')
            {
                log.LogInfo("net lab: a lab-code message from player " + sender + " with an unexpected payload (" + (data == null ? "null" : data.GetType().Name) + ", " + len + " bytes)");
                return;
            }
            int test = b[4];
            int seq = b[5] | (b[6] << 8);
            var net = NetworkSystem.Instance;
            if (net != null && sender == net.LocalPlayerID)
            {
                int key = test * 100000 + seq;
                echoed.Add(key);
                echoLen[key] = len;
                float t0;
                if (sentAt.TryGetValue(key, out t0)) rtt[key] = Time.realtimeSinceStartup - t0;
                return;
            }
            int n; long bytes;
            rxCount.TryGetValue(sender, out n); rxBytes.TryGetValue(sender, out bytes);
            rxCount[sender] = n + 1; rxBytes[sender] = bytes + len;
            if (n == 0) log.LogInfo("net lab: first message from player " + sender + ": test " + test + ", seq " + seq + ", " + len + " bytes");
            if (Time.unscaledTime >= nextRxLog)
            {
                nextRxLog = Time.unscaledTime + 2f;
                log.LogInfo("net lab: from player " + sender + " so far " + rxCount[sender] + " messages, " + rxBytes[sender] + " bytes (last: test " + test + ", seq " + seq + ", " + len + " bytes)");
            }
        }

        // ------------------------------------------------------------------ sending

        /// <summary>Why the lab may not send right now, or null if it may.</summary>
        string WhyNot()
        {
            var net = NetworkSystem.Instance;
            if (net == null) return "the game's network system is not ready";
            if (!net.InRoom) return "you are not in a room (join a room first)";
            if (cfg.NetLabAnyRoom.Value) return null;   // [Network] AllowAnyRoom is on (the default)
            string mode = net.GameModeString;
            if (mode == null || mode.IndexOf("MODDED_", StringComparison.Ordinal) < 0) return "this is not a modded room (game mode: " + mode + ") and [Network] AllowAnyRoom is off";
            return null;
        }

        bool Send(byte test, int seq, int size, bool reliable)
        {
            var net = NetworkSystem.Instance;
            var payload = new byte[size];
            payload[0] = (byte)'G'; payload[1] = (byte)'P'; payload[2] = (byte)'L'; payload[3] = (byte)'B';
            payload[4] = test; payload[5] = (byte)(seq & 0xFF); payload[6] = (byte)((seq >> 8) & 0xFF); payload[7] = (byte)(size / 16);
            for (int i = 8; i < size; i++) payload[i] = (byte)(i * 31);
            var opts = new NetEventOptions { Reciever = NetEventOptions.RecieverTarget.all };
            sentAt[test * 100000 + seq] = Time.realtimeSinceStartup;
            try
            {
                // Fusion overrides the NetRaiseEvent methods; on PUN they are empty in the base class (Verified in source, and the
                // first lab run on PUN sent nothing), so PUN goes through the same static call the game's RoomSystem uses.
                if (net is NetworkSystemFusion)
                {
                    if (reliable) net.NetRaiseEventReliable(Code, payload, opts);
                    else net.NetRaiseEventUnreliable(Code, payload, opts);
                }
                else NetworkSystemRaiseEvent.RaiseEvent(Code, payload, opts, reliable);
                return true;
            }
            catch (Exception e)
            {
                log.LogWarning("net lab: sending " + size + " bytes threw " + e.GetType().Name + ": " + e.Message);
                return false;
            }
        }

        /// <summary>One run of the size ladder and the rate ladder. Started by the plugin when [Network] NetLabRun turns on.</summary>
        public IEnumerator Run()
        {
            Running = true;
            string why = WhyNot();
            if (why != null) { log.LogWarning("net lab: not running: " + why); Running = false; yield break; }
            var net = NetworkSystem.Instance;
            log.LogInfo("net lab: run starting. backend " + net.GetType().Name + ", players in room " + net.RoomPlayerCount + ", my player id " + net.LocalPlayerID
                        + ". Sizes " + Sizes[0] + " to " + Sizes[Sizes.Length - 1] + " bytes, then " + Steps.Length + " rate steps from " + Steps[0][0] * Steps[0][1] + " to " + Steps[Steps.Length - 1][0] * Steps[Steps.Length - 1][1] + " bytes a second.");
            echoed.Clear(); echoLen.Clear(); sentAt.Clear(); rtt.Clear();
            long totalSent = 0;

            // ---- size ladder: one unreliable message per size, then a reliable one at 256, waiting for the echo of each
            int seq = 0, echoedSizes = 0;
            int largestEcho = 0;
            foreach (int size in Sizes)
            {
                if (WhyNot() != null) { log.LogWarning("net lab: stopped, left the room"); Running = false; yield break; }
                seq++;
                bool sent = Send(1, seq, size, false);
                totalSent += size;
                yield return new WaitForSecondsRealtime(0.7f);
                bool back = echoed.Contains(1 * 100000 + seq);
                if (back) { echoedSizes++; largestEcho = Math.Max(largestEcho, size); }
                log.LogInfo("net lab: size " + size + " bytes: send " + (sent ? "ok" : "FAILED") + ", echo " + (back ? "came back (" + echoLen[1 * 100000 + seq] + " bytes, round trip " + (rtt[1 * 100000 + seq] * 1000f).ToString("0") + " ms)" : "did NOT come back"));
            }
            seq++;
            Send(2, seq, 256, true);
            totalSent += 256;
            yield return new WaitForSecondsRealtime(0.7f);
            log.LogInfo("net lab: reliable 256 bytes: echo " + (echoed.Contains(2 * 100000 + seq) ? "came back" : "did NOT come back"));

            bool canMeasureAlone = echoedSizes > 0;
            log.LogInfo("net lab: size result: " + (canMeasureAlone ? "the largest size that echoed back was " + largestEcho + " bytes" : "NO echo at all, so own messages do not come back; a second player must run the lab to see what arrives"));

            // ---- rate ladder: a steady stream of small messages; each step needs almost everything to come back before the next, faster one
            int rateSeq = 1000;
            int skipBytes = -1;   // a message size that already failed: its remaining steps are skipped
            foreach (int[] step in Steps)
            {
                int rate = step[0], RateBytes = step[1];
                if (RateBytes == skipBytes) continue;
                if (WhyNot() != null) { log.LogWarning("net lab: stopped, left the room"); Running = false; yield break; }
                byte test = (byte)(RateBytes == 200 ? 3 : 4);
                int sentN = 0, gotBefore = CountEchoes(test);
                float rttSum = 0f, rttMax = 0f; int rttN = 0, firstSeq = rateSeq + 1;
                float interval = 1f / rate, t0 = Time.unscaledTime, next = t0;
                while (Time.unscaledTime - t0 < RateSeconds)
                {
                    if (Time.unscaledTime >= next)
                    {
                        rateSeq++; sentN++;
                        Send(test, rateSeq, RateBytes, false);
                        totalSent += RateBytes;
                        next += interval;
                        if (next < Time.unscaledTime - 0.5f) next = Time.unscaledTime;   // the game hitched: do not send a burst to catch up
                    }
                    yield return null;
                }
                yield return new WaitForSecondsRealtime(1.0f);   // let the last ones come back
                int got = CountEchoes(test) - gotBefore;
                float pct = sentN > 0 ? 100f * got / sentN : 0f;
                for (int s = firstSeq; s <= rateSeq; s++)
                {
                    float r;
                    if (rtt.TryGetValue(test * 100000 + s, out r)) { rttSum += r; rttN++; if (r > rttMax) rttMax = r; }
                }
                log.LogInfo("net lab: rate " + rate + "/s x " + RateBytes + " bytes (" + rate * RateBytes + " bytes/s) for " + RateSeconds + " s: sent " + sentN + ", echoed " + got + " (" + pct.ToString("0") + "%)"
                            + (rttN > 0 ? ", round trip average " + (rttSum / rttN * 1000f).ToString("0") + " ms, worst " + (rttMax * 1000f).ToString("0") + " ms" : ""));
                if (canMeasureAlone && pct < 60f) { log.LogInfo("net lab: too many lost at " + rate + "/s x " + RateBytes + " bytes, skipping the rest of the " + RateBytes + " byte steps"); skipBytes = RateBytes; }
                yield return new WaitForSecondsRealtime(3f);
            }
            log.LogInfo("net lab: run finished, about " + totalSent + " bytes sent in total");
            Running = false;
        }

        int CountEchoes(int test)
        {
            int n = 0;
            foreach (int k in echoed) if (k / 100000 == test) n++;
            return n;
        }
    }
}
