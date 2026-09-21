using System;
using GorillaPhone.Video;

namespace GorillaPhone.Audio
{
    /// <summary>
    /// Feeds the video voice's streaming AudioClip from the browser's audio buffer. Unity calls <see cref="Fill"/> on its audio thread
    /// whenever the source needs more sound, BEFORE its own 3D processing, so distance falloff, panning, the low-pass filter and the
    /// game's spatializer all apply as they do to any clip. (A first version overwrote the sound in OnAudioFilterRead instead: in game
    /// Unity passed that filter the source's already-mixed output, 2 channels for a mono clip, so the sound was muffled by walls but
    /// had no position.) The audio thread only touches the ring buffer and plain fields; it calls no Unity API.
    /// </summary>
    public sealed class VideoAudioFeed
    {
        /// <summary>The streaming clip's sample rate; the ring resamples the browser's rate to this.</summary>
        public const int Rate = 48000;

        public volatile AudioRing Ring;
        public volatile bool Active;

        /// <summary>The size of the first block Unity asked for, for the log (0 until then).</summary>
        public volatile int SeenSamples;

        /// <summary>The clip's read callback: fill the mono block.</summary>
        public void Fill(float[] data)
        {
            if (SeenSamples == 0) SeenSamples = data.Length;
            AudioRing ring = Ring;
            if (!Active || ring == null)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }
            ring.Read(data, 1, data.Length, Rate);
        }
    }
}
