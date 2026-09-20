using BepInEx.Configuration;

namespace GorillaPhone.Phone
{
    /// <summary>TEST ONLY: which single controller button summons the phone. Remove before release.</summary>
    public enum TestButton
    {
        None,
        LeftPrimary,     // X on Quest controllers
        LeftSecondary,   // Y
        RightPrimary,    // A
        RightSecondary   // B
    }

    /// <summary>All tunable phone settings. Values are read live (.Value), so a config reload takes effect at once.</summary>
    public sealed class PhoneConfig
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<float> Scale;
        public readonly ConfigEntry<float> GrabRadius;
        public readonly ConfigEntry<float> ThrowMultiplier;
        public readonly ConfigEntry<float> MaxThrowSpeed;
        public readonly ConfigEntry<float> ImpactHapticSpeed;
        public readonly ConfigEntry<float> RecallDepth;
        public readonly ConfigEntry<int> Layer;
        public readonly ConfigEntry<TestButton> TestSummonButton;   // TEST ONLY, remove before release

        public PhoneConfig(ConfigFile c)
        {
            Enabled = c.Bind("Phone", "Enabled", true, "Spawn the phone when you load into the game.");
            Scale = c.Bind("Phone", "Scale", 1.6f,
                new ConfigDescription("Size multiplier. 1 is a real phone (7.5 x 15.5 cm), which looks small next to a gorilla hand; the default is larger.", new AcceptableValueRange<float>(0.5f, 4f)));
            GrabRadius = c.Bind("Phone", "GrabRadius", 0.15f,
                new ConfigDescription("Metres from your drawn hand to the phone's surface within which pressing grip grabs it.", new AcceptableValueRange<float>(0.03f, 0.5f)));
            ThrowMultiplier = c.Bind("Phone", "ThrowMultiplier", 1f,
                new ConfigDescription("Scales the throw velocity. 1 is your real hand speed.", new AcceptableValueRange<float>(0.2f, 3f)));
            MaxThrowSpeed = c.Bind("Phone", "MaxThrowSpeed", 30f,
                new ConfigDescription("Cap on the throw speed in metres per second.", new AcceptableValueRange<float>(2f, 100f)));
            ImpactHapticSpeed = c.Bind("Phone", "ImpactHapticSpeed", 2f,
                new ConfigDescription("Impact speed (m/s) above which the nearest controller buzzes.", new AcceptableValueRange<float>(0.5f, 20f)));
            RecallDepth = c.Bind("Phone", "RecallDepth", 60f,
                new ConfigDescription("If the phone falls this many metres below your head it is brought back to you. Holding X and A together also recalls it.", new AcceptableValueRange<float>(10f, 500f)));
            Layer = c.Bind("Phone", "Layer", 3,
                new ConfigDescription("Unity layer for the phone's collider. Must not be one the game lets the player stand on; the mod checks and picks another if it is.", new AcceptableValueRange<int>(0, 31)));
            TestSummonButton = c.Bind("Testing", "TestSummonButton", TestButton.LeftSecondary,
                "TEST ONLY (removed before release): one press of this controller button brings the phone to you. On Quest controllers LeftPrimary is X, LeftSecondary is Y, RightPrimary is A, RightSecondary is B.");
        }
    }
}
