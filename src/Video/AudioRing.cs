using System;

namespace GorillaPhone.Video
{
    /// <summary>
    /// A small thread-safe buffer between the browser's sound (written on the pipe's reader thread, 16-bit stereo blocks at the
    /// browser's rate) and the game's audio thread (read as floats at the game's output rate, in any channel count).
    /// It waits until a target amount is buffered before it starts (that delay is also what lines the sound up with the picture,
    /// which arrives a little late), plays silence and re-buffers on an underrun, and drops the oldest sound if it ever grows too far
    /// ahead. Pure System code, so it is tested outside the game.
    /// </summary>
    public sealed class AudioRing
    {
        const int CapacityFrames = 48000 * 2;   // two seconds
        const int MaxExtraMs = 300;             // above the target by this much, the oldest sound is dropped

        readonly object lk = new object();
        readonly float[] left = new float[CapacityFrames];
        readonly float[] right = new float[CapacityFrames];
        int readPos, count;       // next frame to read, frames stored
        double frac;              // position between two source frames (resampling)
        int srcRate = 48000;
        int targetMs = 120;
        bool primed;

        public long TotalWritten { get; private set; }
        public long Underruns { get; private set; }

        /// <summary>How much is waiting, in milliseconds of the browser's sound.</summary>
        public int BufferedMs
        {
            get { lock (lk) return (int)(count * 1000L / srcRate); }
        }

        /// <summary>Wait for this much sound before playing (also the delay between picture and sound).</summary>
        public void SetDelayMs(int ms)
        {
            lock (lk) targetMs = Math.Max(0, Math.Min(1000, ms));
        }

        public void Clear()
        {
            lock (lk)
            {
                readPos = 0; count = 0; frac = 0; primed = false;
            }
        }

        /// <summary>Add interleaved little-endian 16-bit stereo. bytes is the number of bytes to use (a whole number of frames).</summary>
        public void Write(byte[] pcm, int bytes, int rate)
        {
            int frames = bytes / 4;
            if (frames <= 0) return;
            lock (lk)
            {
                if (rate != srcRate && rate >= 8000 && rate <= 192000) srcRate = rate;   // a new rate: what is stored is short and stays as is
                int first = 0;
                if (frames > CapacityFrames) { first = frames - CapacityFrames; frames = CapacityFrames; }   // more than fits: keep the newest
                int free = CapacityFrames - count;
                if (frames > free)
                {
                    int drop = frames - free;   // no room: forget the oldest sound
                    readPos = (readPos + drop) % CapacityFrames;
                    count -= drop;
                }
                int w = (readPos + count) % CapacityFrames;
                for (int i = 0; i < frames; i++)
                {
                    int p = (first + i) * 4;
                    left[w] = BitConverter.ToInt16(pcm, p) / 32768f;
                    right[w] = BitConverter.ToInt16(pcm, p + 2) / 32768f;
                    if (++w == CapacityFrames) w = 0;
                }
                count += frames;
                TotalWritten += frames;
            }
        }

        /// <summary>
        /// Fill dst (interleaved, channels wide) with frames of sound at outRate. Returns how many frames were real sound;
        /// the rest is silence. Mono gets the average of left and right; more than two channels get left and right in the first two.
        /// </summary>
        public int Read(float[] dst, int channels, int frames, int outRate)
        {
            lock (lk)
            {
                int targetFrames = (int)(targetMs * (long)srcRate / 1000);
                if (!primed)
                {
                    if (count >= targetFrames && count > 0) primed = true;
                    else { Array.Clear(dst, 0, frames * channels); return 0; }
                }
                // Too far ahead (the game's clock runs a little slower than the browser's): drop back to the target.
                int maxFrames = targetFrames + (int)(MaxExtraMs * (long)srcRate / 1000);
                if (count > maxFrames)
                {
                    int drop = count - targetFrames;
                    readPos = (readPos + drop) % CapacityFrames;
                    count -= drop;
                }

                // The browser's sound clock and the game's never match exactly (in game the buffer drained about 1% a second until it ran dry every ~10 s).
                // Play a little slower when the buffer is below its target and a little faster when above, at most 3%, so it settles instead of running out.
                double error = targetFrames > 0 ? (count - targetFrames) / (double)targetFrames : 0.0;
                double adjust = Math.Max(-0.03, Math.Min(0.03, error * 0.05));
                double step = srcRate / (double)outRate * (1.0 + adjust);
                int done = 0;
                for (int f = 0; f < frames; f++)
                {
                    if (count < 2)
                    {
                        // Ran dry: play silence for the rest and wait for the buffer to fill again.
                        primed = false;
                        Underruns++;
                        for (int k = f * channels; k < frames * channels; k++) dst[k] = 0f;
                        return done;
                    }
                    int a = readPos, b = readPos + 1 == CapacityFrames ? 0 : readPos + 1;
                    float l = (float)(left[a] + (left[b] - left[a]) * frac);
                    float r = (float)(right[a] + (right[b] - right[a]) * frac);
                    int o = f * channels;
                    if (channels == 1) dst[o] = (l + r) * 0.5f;
                    else
                    {
                        dst[o] = l;
                        dst[o + 1] = r;
                        for (int c = 2; c < channels; c++) dst[o + c] = 0f;
                    }
                    done++;
                    frac += step;
                    while (frac >= 1.0)
                    {
                        frac -= 1.0;
                        readPos = readPos + 1 == CapacityFrames ? 0 : readPos + 1;
                        count--;
                    }
                }
                return done;
            }
        }
    }
}
