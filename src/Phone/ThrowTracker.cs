using UnityEngine;

namespace GorillaPhone.Phone
{
    /// <summary>
    /// Keeps the last few pose samples of a held object and turns them into a release velocity.
    /// No game calls, so it can be tested outside the game.
    /// </summary>
    public sealed class ThrowTracker
    {
        struct Sample
        {
            public float T;
            public Vector3 P;
            public Quaternion R;
        }

        readonly Sample[] buf = new Sample[32];
        int head;
        int count;

        /// <summary>How far back (seconds) the release velocity looks.</summary>
        public float Window = 0.1f;

        /// <summary>Samples closer together than this (seconds) are not enough to measure a speed.</summary>
        public float MinSpan = 0.02f;

        public void Reset()
        {
            head = 0;
            count = 0;
        }

        public void Add(float time, Vector3 pos, Quaternion rot)
        {
            buf[head] = new Sample { T = time, P = pos, R = rot };
            head = (head + 1) % buf.Length;
            if (count < buf.Length) count++;
        }

        /// <summary>Linear velocity (m/s) and angular velocity (rad/s, world axes) over the window. False if there is not enough data.</summary>
        public bool TryGet(out Vector3 velocity, out Vector3 angularVelocity)
        {
            velocity = Vector3.zero;
            angularVelocity = Vector3.zero;
            if (count < 2) return false;

            Sample newest = buf[(head - 1 + buf.Length) % buf.Length];
            Sample oldest = newest;
            bool found = false;
            for (int i = 2; i <= count; i++)
            {
                Sample s = buf[(head - i + buf.Length) % buf.Length];
                if (newest.T - s.T > Window) break;
                oldest = s;
                found = true;
            }

            float dt = newest.T - oldest.T;
            if (!found || dt < MinSpan) return false;

            velocity = (newest.P - oldest.P) / dt;

            Quaternion delta = newest.R * Quaternion.Inverse(oldest.R);
            float angle;
            Vector3 axis;
            delta.ToAngleAxis(out angle, out axis);
            if (angle > 180f) angle -= 360f;
            if (!float.IsNaN(axis.x) && !float.IsInfinity(axis.x))
                angularVelocity = axis * (angle * Mathf.Deg2Rad / dt);
            return true;
        }
    }
}
