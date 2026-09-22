using System;
using System.Collections.Generic;
using BepInEx.Logging;
using GorillaPhone.Phone;
using UnityEngine;

namespace GorillaPhone.Net
{
    /// <summary>
    /// Step 9b: the real networked phone (cosmetic only). Broadcasts this player's own phone position, rotation,
    /// which hand (if any) holds it, and which app is open, to other players over the game's own event channel,
    /// and draws a light, physics-free copy of the phone for every other player whose mod sends the same thing.
    ///
    /// Built after the Network Lab (event code 151) found: up to about 1500 bytes per message echoes fine on this
    /// game's PUN backend, and throughput up to 30 KB/s tested clean with no ceiling found (see CLAUDE.md,
    /// "Network lab"). This beacon is about 35 bytes at 10 a second (about 350 bytes/s): far inside that budget,
    /// so it needs no chunking and no rate throttling beyond the fixed 10 Hz.
    ///
    /// What is NOT built here yet: the "share what I'm watching" picture and sound stream (step 9c/9d) and a
    /// distance gate (send only to nearby peers) -- the lab's finding was that broadcasting this little costs
    /// almost nothing even in a room full of players without the mod (the game ignores an unknown event code,
    /// Verified in source), so a room-level gate (more than one player present) is enough for now.
    ///
    /// Receiving is Unverified beyond this player's own send path: no second modded client has confirmed what
    /// arrives on the other end yet. Every received value is validated before it is used (NaN, huge numbers,
    /// out-of-range bytes) and a peer that stops sending is dropped after a few seconds.
    /// </summary>
    public sealed class PhoneNetSync : IDisposable
    {
        /// <summary>The event code for the real phone beacon. The lab uses 151; the game uses 0 to 14, 100 to 106 and 255; Photon reserves 200 and up.</summary>
        public const byte Code = 152;

        const byte Magic0 = (byte)'G', Magic1 = (byte)'P', Magic2 = (byte)'P', Magic3 = (byte)'H';
        const byte Version = 1;
        const int PayloadLen = 4 + 1 + 1 + 1 + 12 + 16;   // magic, version, flags, appId, position, rotation
        const float SendInterval = 0.1f;   // 10 a second while the phone is out and someone else is in the room
        const float PeerTimeout = 3f;      // a peer that stops sending is dropped (and its visual destroyed) after this long

        sealed class RemotePhone
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public bool Held, HeldLeft;
            public byte AppId;
            public float LastSeen;
            public RemotePhoneVisual Visual;
        }

        readonly ManualLogSource log;
        readonly PhoneConfig cfg;
        readonly Dictionary<int, RemotePhone> peers = new Dictionary<int, RemotePhone>();
        bool subscribed;
        float nextSend;
        float nextPrune;
        int badMessages;
        bool loggedFirstSend;

        public PhoneNetSync(ManualLogSource logger, PhoneConfig config)
        {
            log = logger;
            cfg = config;
        }

        public bool Subscribe()
        {
            if (subscribed) return true;
            if (NetworkSystem.Instance == null) return false;
            NetworkSystem.Instance.OnRaiseEvent += OnEvent;
            subscribed = true;
            log.LogInfo("phone net: listening for other players' phones (event code " + Code + ")");
            return true;
        }

        public void Unsubscribe()
        {
            if (!subscribed) return;
            subscribed = false;
            if (NetworkSystem.Instance != null) NetworkSystem.Instance.OnRaiseEvent -= OnEvent;
        }

        /// <summary>Call every frame. Sends this player's own beacon at 10 Hz when there is something to show and someone to show it to, and prunes peers who stopped sending.</summary>
        public void Tick(PhoneObject phone)
        {
            if (!cfg.NetPhoneEnabled.Value) return;
            Subscribe();

            float now = Time.unscaledTime;
            if (now >= nextSend)
            {
                nextSend = now + SendInterval;
                TrySend(phone);
            }
            if (now >= nextPrune)
            {
                nextPrune = now + 1f;
                Prune(now);
            }
        }

        // ------------------------------------------------------------------ sending

        void TrySend(PhoneObject phone)
        {
            if (phone == null) return;   // nothing to show yet
            var net = NetworkSystem.Instance;
            if (net == null || !net.InRoom || net.RoomPlayerCount <= 1) return;   // no one else in the room to see it

            byte appId = phone.Screen != null ? phone.Screen.NetAppId : (byte)0;
            var payload = new byte[PayloadLen];
            payload[0] = Magic0; payload[1] = Magic1; payload[2] = Magic2; payload[3] = Magic3;
            payload[4] = Version;
            byte flags = 0;
            if (phone.IsHeld) flags |= 1;
            if (phone.IsHeld && phone.HeldLeft) flags |= 2;
            payload[5] = flags;
            payload[6] = appId;
            Vector3 p = phone.transform.position;
            Quaternion r = phone.transform.rotation;
            WriteFloat(payload, 7, p.x); WriteFloat(payload, 11, p.y); WriteFloat(payload, 15, p.z);
            WriteFloat(payload, 19, r.x); WriteFloat(payload, 23, r.y); WriteFloat(payload, 27, r.z); WriteFloat(payload, 31, r.w);

            var opts = new NetEventOptions { Reciever = NetEventOptions.RecieverTarget.others };
            try
            {
                if (net is NetworkSystemFusion) net.NetRaiseEventUnreliable(Code, payload, opts);
                else NetworkSystemRaiseEvent.RaiseEvent(Code, payload, opts, false);
                if (!loggedFirstSend)
                {
                    loggedFirstSend = true;
                    log.LogInfo("phone net: broadcasting this phone's state (" + PayloadLen + " bytes, 10/s) to " + net.RoomPlayerCount + " other player(s) in the room");
                }
            }
            catch (Exception e)
            {
                log.LogWarning("phone net: sending the beacon threw " + e.GetType().Name + ": " + e.Message);
            }
        }

        static void WriteFloat(byte[] b, int offset, float v)
        {
            byte[] f = BitConverter.GetBytes(v);
            if (!BitConverter.IsLittleEndian) Array.Reverse(f);   // Unverified on anything but a little-endian x64 Windows PC; every target so far is
            Array.Copy(f, 0, b, offset, 4);
        }

        static float ReadFloat(byte[] b, int offset)
        {
            if (BitConverter.IsLittleEndian) return BitConverter.ToSingle(b, offset);
            byte[] f = new byte[4];
            Array.Copy(b, offset, f, 0, 4);
            Array.Reverse(f);
            return BitConverter.ToSingle(f, 0);
        }

        // ------------------------------------------------------------------ receiving

        void OnEvent(byte code, object data, int sender)
        {
            if (code != Code) return;
            var net = NetworkSystem.Instance;
            if (net != null && sender == net.LocalPlayerID) return;   // sent to "others" only, but ignore just in case

            byte[] b = data as byte[];
            if (b == null || b.Length < PayloadLen || b[0] != Magic0 || b[1] != Magic1 || b[2] != Magic2 || b[3] != Magic3 || b[4] != Version)
            {
                badMessages++;
                if (badMessages <= 3) log.LogWarning("phone net: an unexpected message on our event code from player " + sender + " (" + (b == null ? "not a byte[]" : b.Length + " bytes") + "), ignored");
                return;
            }

            byte flags = b[5], appId = b[6];
            Vector3 p = new Vector3(ReadFloat(b, 7), ReadFloat(b, 11), ReadFloat(b, 15));
            Quaternion r = new Quaternion(ReadFloat(b, 19), ReadFloat(b, 23), ReadFloat(b, 27), ReadFloat(b, 31));

            // Validate before using: a NaN or huge value from a bad or hostile sender must never reach a Transform.
            if (!IsFinite(p) || !IsFinite(r) || p.sqrMagnitude > 1e8f)
            {
                badMessages++;
                if (badMessages <= 3) log.LogWarning("phone net: an out-of-range beacon from player " + sender + ", ignored");
                return;
            }
            if (r.x == 0f && r.y == 0f && r.z == 0f && r.w == 0f) r = Quaternion.identity;   // a zero quaternion is not a valid rotation
            else r = NormalizeSafe(r);

            RemotePhone rp;
            bool isNew = !peers.TryGetValue(sender, out rp);
            if (isNew) { rp = new RemotePhone(); peers[sender] = rp; }
            rp.Position = p;
            rp.Rotation = r;
            rp.Held = (flags & 1) != 0;
            rp.HeldLeft = (flags & 2) != 0;
            rp.AppId = appId;
            rp.LastSeen = Time.unscaledTime;

            if (isNew) log.LogInfo("phone net: first beacon from player " + sender + " -- their phone is " + Describe(rp));

            if (rp.Visual == null) rp.Visual = RemotePhoneVisual.Create(cfg, log, sender);
            rp.Visual.Apply(rp.Position, rp.Rotation, rp.Held, rp.HeldLeft, rp.AppId);
        }

        static bool IsFinite(Vector3 v) { return !(float.IsNaN(v.x) || float.IsInfinity(v.x) || float.IsNaN(v.y) || float.IsInfinity(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.z)); }
        static bool IsFinite(Quaternion q) { return !(float.IsNaN(q.x) || float.IsInfinity(q.x) || float.IsNaN(q.y) || float.IsInfinity(q.y) || float.IsNaN(q.z) || float.IsInfinity(q.z) || float.IsNaN(q.w) || float.IsInfinity(q.w)); }

        static Quaternion NormalizeSafe(Quaternion q)
        {
            float len = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (len < 1e-4f) return Quaternion.identity;
            float inv = 1f / len;
            return new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
        }

        static string Describe(RemotePhone rp)
        {
            string held = rp.Held ? (rp.HeldLeft ? "held (left hand)" : "held (right hand)") : "set down";
            return held + ", " + PhoneScreen.NetAppName(rp.AppId) + " open";
        }

        void Prune(float now)
        {
            List<int> gone = null;
            foreach (var kv in peers)
            {
                if (now - kv.Value.LastSeen > PeerTimeout)
                {
                    (gone ?? (gone = new List<int>())).Add(kv.Key);
                }
            }
            if (gone == null) return;
            foreach (int id in gone)
            {
                RemotePhone rp = peers[id];
                if (rp.Visual != null) rp.Visual.Destroy();
                peers.Remove(id);
                log.LogInfo("phone net: player " + id + "'s phone stopped sending, removed");
            }
        }

        public void Dispose()
        {
            Unsubscribe();
            foreach (var kv in peers) if (kv.Value.Visual != null) kv.Value.Visual.Destroy();
            peers.Clear();
        }
    }
}
