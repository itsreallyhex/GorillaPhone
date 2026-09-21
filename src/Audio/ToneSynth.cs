using System;

namespace GorillaPhone.Audio
{
    /// <summary>
    /// The phone's test sound, made from maths so no audio file is needed in the DLL: a slow four-chord
    /// arpeggio with bright, harmonic-rich notes (a pure sine would not show a muffling filter working).
    /// Notes ring on into the next ones and wrap around the end, so the loop has no seam.
    /// System only: it runs and is tested outside the game.
    /// </summary>
    public static class ToneSynth
    {
        public const int Rate = 44100;
        public const float Seconds = 4f;

        // C, Am, F, G as four-note arpeggios, in Hz.
        static readonly float[][] Chords =
        {
            new[] { 261.63f, 329.63f, 392.00f, 523.25f },
            new[] { 220.00f, 261.63f, 329.63f, 440.00f },
            new[] { 174.61f, 261.63f, 349.23f, 440.00f },
            new[] { 196.00f, 246.94f, 392.00f, 493.88f },
        };

        /// <summary>One loop of mono samples between -1 and 1 (about 0.6 at the loudest).</summary>
        public static float[] Generate()
        {
            int total = (int)(Rate * Seconds);
            var d = new float[total];
            int noteEvery = total / 16;          // sixteen notes in the loop
            int ring = (int)(Rate * 0.7f);       // each note rings for 0.7 s

            for (int n = 0; n < 16; n++)
            {
                float f = Chords[n / 4][n % 4];
                int start = n * noteEvery;
                for (int i = 0; i < ring; i++)
                {
                    float t = i / (float)Rate;
                    float env = (float)Math.Exp(-t * 4.5) * Math.Min(1f, t / 0.004f);   // fast attack, exponential decay
                    float tail = Math.Min(1f, (ring - i) / (0.02f * Rate));             // fade the last 20 ms to zero
                    float s = 0f;
                    for (int h = 1; h <= 14; h++)
                    {
                        if (f * h >= Rate * 0.5f) break;
                        s += (float)Math.Sin(2.0 * Math.PI * f * h * t) / (float)Math.Pow(h, 0.7);
                    }
                    d[(start + i) % total] += s * env * tail * 0.22f;
                }
            }

            float peak = 0f;
            for (int i = 0; i < total; i++) peak = Math.Max(peak, Math.Abs(d[i]));
            if (peak > 0f)
            {
                float k = 0.6f / peak;
                for (int i = 0; i < total; i++) d[i] *= k;
            }
            return d;
        }
    }
}
