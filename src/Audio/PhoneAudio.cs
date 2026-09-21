using System;
using System.Threading;
using BepInEx.Logging;
using GorillaPhone.Phone;
using GorillaPhone.Video;
using UnityEngine;

namespace GorillaPhone.Audio
{
    /// <summary>
    /// The phone's sound, heard only by the local player: a looping test chime played from the phone in 3D
    /// (louder up close, silent beyond MaxHear metres), with occlusion: a throttled raycast from the phone to
    /// your head, and when something solid is in the way the sound is low-pass filtered and made a little quieter.
    /// Local only: nothing is sent to other players. The chime is generated from maths on a background thread,
    /// so there is no audio file and the main thread never waits for it.
    /// </summary>
    public sealed class PhoneAudio : MonoBehaviour
    {
        const float MaxHear = 12f;         // metres: silent beyond this
        const float MinDistance = 0.3f;    // metres: full volume inside this
        const float CheckEvery = 0.1f;     // seconds between occlusion raycasts (not per frame)
        const float MuffledVolume = 0.6f;  // volume factor while blocked
        const float ClearCutoff = 22000f;  // low-pass fully open
        const float HeadSlack = 0.2f;      // the ray stops this short of the head so the player's own colliders never count

        PhoneConfig cfg;
        ManualLogSource log;
        PhoneObject phone;
        int wallMask;

        AudioSource source;
        AudioLowPassFilter lowpass;
        AudioClip clip;

        volatile float[] pending;          // filled by the background thread
        volatile string genError;

        bool wantPlaying, loggedFirstPlay, blocked, lastLoggedBlocked;
        float volume = 0.5f, cfgVolumeSeen = -1f, cutoff = ClearCutoff, occlusion = 1f;
        float nextCheck, nextLog, checkPlayingAt = -1f;
        readonly RaycastHit[] hits = new RaycastHit[4];

        // The Video app's voice: a second source on its own child object, sharing the chime's volume, wall muffling and rolloff.
        AudioSource vSource;
        AudioLowPassFilter vLow;
        VideoAudioFeed vFeed;   // a plain object: the streaming clip's read callback
        AudioClip vClip;
        bool videoWanted, loggedVideoFormat;

        /// <summary>True while the chime is wanted (it may still be loading for a moment the first time).</summary>
        public bool Playing { get { return wantPlaying; } }
        /// <summary>True until the generated chime is ready.</summary>
        public bool Loading { get { return wantPlaying && clip == null; } }
        /// <summary>True while something solid is between the phone and your head.</summary>
        public bool Muffled { get { return blocked; } }
        /// <summary>The phone's volume, 0 to 1.</summary>
        public float Volume { get { return volume; } }

        public void Init(PhoneConfig config, ManualLogSource logger, PhoneObject owner, int wallLayers)
        {
            cfg = config;
            log = logger;
            phone = owner;
            wallMask = wallLayers;

            // A child object, so the filter belongs to this source alone (the shutter click has its own source on the phone).
            var go = new GameObject("GP_Audio");
            go.layer = 0;
            go.transform.SetParent(transform, false);

            source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;   // a thrown phone would otherwise bend the pitch
            source.priority = 100;
            source.minDistance = MinDistance;
            source.maxDistance = MaxHear;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, RolloffCurve());
            source.volume = 0f;

            lowpass = go.AddComponent<AudioLowPassFilter>();   // added after the source, so it filters this source
            lowpass.cutoffFrequency = ClearCutoff;
            lowpass.enabled = false;

            volume = cfg.AudioVolume.Value;
            cfgVolumeSeen = volume;

            // The chime takes about half a second to synthesise: do it off the main thread.
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { pending = ToneSynth.Generate(); }
                catch (Exception e) { genError = e.Message; }
            });
        }

        /// <summary>The volume falls off smoothly to zero at MaxHear (x is the distance divided by MaxHear).</summary>
        static AnimationCurve RolloffCurve()
        {
            var keys = new Keyframe[11];
            for (int i = 0; i <= 10; i++)
            {
                float t = i / 10f;
                float slope = -2f * (1f - t);
                keys[i] = new Keyframe(t, (1f - t) * (1f - t), slope, slope);
            }
            return new AnimationCurve(keys);
        }

        public void Toggle()
        {
            wantPlaying = !wantPlaying;
            log.LogInfo("phone audio: " + (wantPlaying ? "play" : "pause") + " requested");
        }

        public void AdjustVolume(float delta)
        {
            volume = Mathf.Clamp01(Mathf.Round((volume + delta) * 20f) / 20f);   // steps of 5%
        }

        /// <summary>
        /// Start or stop the Video app's sound: the browser's page sound (buffered in ring) played from the phone in 3D, with the same
        /// volume, distance falloff and wall muffling as the chime. Made on first use.
        /// </summary>
        public void SetVideo(AudioRing ring, bool on)
        {
            if (on && vSource == null) BuildVideoVoice();
            videoWanted = on;
            if (vFeed != null)
            {
                vFeed.Ring = ring;
                vFeed.Active = on;
            }
        }

        void BuildVideoVoice()
        {
            var go = new GameObject("GP_VideoAudio");
            go.layer = 0;
            go.transform.SetParent(transform, false);

            vSource = go.AddComponent<AudioSource>();
            vSource.playOnAwake = false;
            vSource.loop = true;
            vSource.spatialBlend = 1f;
            vSource.dopplerLevel = 0f;
            vSource.priority = 100;
            vSource.minDistance = MinDistance;
            vSource.maxDistance = MaxHear;
            vSource.rolloffMode = AudioRolloffMode.Custom;
            vSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, RolloffCurve());
            vSource.volume = 0f;
            // A MONO streaming clip: Unity pulls its samples through the feed's callback and then does its normal 3D processing (distance,
            // panning, filters, spatializer), exactly as for the chime. (Overwriting the sound in OnAudioFilterRead instead did not work:
            // Unity handed that filter the source's already-processed output, so the sound had no position.)
            vFeed = new VideoAudioFeed();
            vClip = AudioClip.Create("GP_VideoStream", VideoAudioFeed.Rate, 1, VideoAudioFeed.Rate, true, vFeed.Fill);
            vSource.clip = vClip;

            vLow = go.AddComponent<AudioLowPassFilter>();
            vLow.cutoffFrequency = ClearCutoff;
            vLow.enabled = false;
            log.LogInfo("phone audio: video voice made (a mono streaming clip, output rate " + AudioSettings.outputSampleRate + " Hz)");
        }

        void Update()
        {
            if (source == null) return;

            if (genError != null)
            {
                log.LogWarning("phone audio: could not make the chime: " + genError);
                genError = null;
            }
            if (clip == null && pending != null) BuildClip();

            // Editing the volume in the config file resets the live volume (the phone's own + and - last until restart).
            if (!Mathf.Approximately(cfg.AudioVolume.Value, cfgVolumeSeen))
            {
                cfgVolumeSeen = cfg.AudioVolume.Value;
                volume = cfgVolumeSeen;
            }
            if (source.spatialize != cfg.Spatialize.Value)
            {
                source.spatialize = cfg.Spatialize.Value;
                log.LogInfo("phone audio: spatialize = " + source.spatialize);
            }

            if (wantPlaying && clip != null)
            {
                if (!source.isPlaying) StartPlaying();
            }
            else if (!wantPlaying && source.isPlaying)
            {
                source.Stop();
            }

            if (checkPlayingAt > 0f && Time.unscaledTime >= checkPlayingAt)
            {
                checkPlayingAt = -1f;
                log.LogInfo("phone audio: half a second after Play, isPlaying=" + source.isPlaying + ", volume=" + source.volume.ToString("0.00")
                            + ", distance to head=" + HeadDistance().ToString("0.0") + " m");
            }

            if (vSource != null)
            {
                if (videoWanted && !vSource.isPlaying) vSource.Play();
                else if (!videoWanted && vSource.isPlaying) vSource.Stop();
                if (vSource.spatialize != cfg.Spatialize.Value) vSource.spatialize = cfg.Spatialize.Value;
                if (!loggedVideoFormat && vFeed != null && vFeed.SeenSamples != 0)
                {
                    loggedVideoFormat = true;
                    log.LogInfo("phone audio: video voice is being read by Unity in blocks of " + vFeed.SeenSamples + " samples");
                }
            }
            bool videoPlaying = vSource != null && vSource.isPlaying;
            if (!source.isPlaying && !videoPlaying) return;

            if (Time.unscaledTime >= nextCheck)
            {
                nextCheck = Time.unscaledTime + CheckEvery;
                blocked = IsBlocked();
                if (blocked != lastLoggedBlocked && Time.unscaledTime >= nextLog)
                {
                    lastLoggedBlocked = blocked;
                    nextLog = Time.unscaledTime + 1f;
                    log.LogInfo("phone audio: " + (blocked ? "muffled (something is between the phone and your head)" : "clear"));
                }
            }

            // Ease towards the target so a wall appearing or disappearing is not a click. The cutoff moves in log steps (pitch-like).
            float k = 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime);
            float targetCut = blocked ? cfg.MuffleCutoff.Value : ClearCutoff;
            cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(cutoff), Mathf.Log(targetCut), k));
            occlusion = Mathf.Lerp(occlusion, blocked ? MuffledVolume : 1f, k);
            bool filtering = cutoff < ClearCutoff * 0.95f;
            if (lowpass.enabled != filtering) lowpass.enabled = filtering;
            if (filtering) lowpass.cutoffFrequency = cutoff;
            source.volume = volume * occlusion;
            if (vSource != null)
            {
                if (vLow.enabled != filtering) vLow.enabled = filtering;
                if (filtering) vLow.cutoffFrequency = cutoff;
                vSource.volume = volume * occlusion;
            }
        }

        void BuildClip()
        {
            float[] d = pending;
            pending = null;
            clip = AudioClip.Create("GP_Chime", d.Length, 1, ToneSynth.Rate, false);
            // AudioClip.SetData has an overload taking ReadOnlySpan, which a net48 project cannot compile against
            // (kit reference 02), so the float[] version is called by reflection.
            var setData = typeof(AudioClip).GetMethod("SetData", new[] { typeof(float[]), typeof(int) });
            if (setData != null) setData.Invoke(clip, new object[] { d, 0 });
            source.clip = clip;
            log.LogInfo("phone audio: chime ready (" + d.Length + " samples)");
        }

        void StartPlaying()
        {
            source.Play();
            checkPlayingAt = Time.unscaledTime + 0.5f;
            nextCheck = 0f;
            if (loggedFirstPlay) return;
            loggedFirstPlay = true;
            var listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            log.LogInfo("phone audio: first play. AudioListeners=" + listeners.Length + ", AudioListener.volume=" + AudioListener.volume
                        + ", paused=" + AudioListener.pause + ", output rate=" + AudioSettings.outputSampleRate + ", spatialize=" + source.spatialize);
        }

        float HeadDistance()
        {
            if (!GorillaTagger.hasInstance) return -1f;
            return Vector3.Distance(transform.position, GorillaTagger.Instance.headCollider.transform.position);
        }

        /// <summary>Is something solid between the phone and your head? Only the layers the player walks on count, triggers never do.</summary>
        bool IsBlocked()
        {
            if (phone.IsHeld || !GorillaTagger.hasInstance) return false;   // in your hand it is next to you
            Vector3 from = transform.position;
            Vector3 to = GorillaTagger.Instance.headCollider.transform.position - from;
            float dist = to.magnitude;
            if (dist < 0.4f || dist > MaxHear) return false;                 // too close to have a wall between, or too far to hear
            Vector3 dir = to / dist;
            int n = Physics.RaycastNonAlloc(from + dir * 0.03f, dir, hits, dist - HeadSlack - 0.03f, wallMask, QueryTriggerInteraction.Ignore);
            return n > 0;
        }

        void OnDestroy()
        {
            if (clip != null) Destroy(clip);
            if (vClip != null) Destroy(vClip);
        }
    }
}
