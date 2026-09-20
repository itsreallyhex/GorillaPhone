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

    /// <summary>Whether saved photos are flipped top to bottom. Auto follows the graphics API; Flip/NoFlip override it if a photo comes out upside down.</summary>
    public enum PhotoFlip { Auto, Flip, NoFlip }

    /// <summary>Which of the game's cameras the phone camera copies its "what to draw" layers from.</summary>
    public enum CameraMaskSource { ThirdPerson, Main }

    /// <summary>All tunable phone settings. Values are read live (.Value), so a config reload takes effect at once.</summary>
    public sealed class PhoneConfig
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<float> Width;
        public readonly ConfigEntry<float> Height;
        public readonly ConfigEntry<float> Thickness;
        public readonly ConfigEntry<float> GrabRadius;
        public readonly ConfigEntry<float> ThrowMultiplier;
        public readonly ConfigEntry<float> MaxThrowSpeed;
        public readonly ConfigEntry<float> ImpactHapticSpeed;
        public readonly ConfigEntry<float> RecallDepth;
        public readonly ConfigEntry<int> Layer;
        public readonly ConfigEntry<TestButton> TestSummonButton;   // TEST ONLY, remove before release

        // Camera
        public readonly ConfigEntry<float> PreviewFps;
        public readonly ConfigEntry<int> PreviewHeight;
        public readonly ConfigEntry<int> PhotoHeight;
        public readonly ConfigEntry<string> PhotoFolder;
        public readonly ConfigEntry<float> CameraFov;
        public readonly ConfigEntry<bool> MirrorSelfie;
        public readonly ConfigEntry<PhotoFlip> PhotoFlipMode;
        public readonly ConfigEntry<CameraMaskSource> CameraMask;
        public readonly ConfigEntry<bool> TriggerShutter;
        public readonly ConfigEntry<float> ShutterVolume;

        public PhoneConfig(ConfigFile c)
        {
            Enabled = c.Bind("Phone", "Enabled", true, "Spawn the phone when you load into the game.");
            // 16 x 28.4 cm is 9:16, the shape of a short-form video, so video, camera and the music panel all fit the screen.
            Width = c.Bind("Phone", "Width", 0.16f,
                new ConfigDescription("Phone width in metres. A real phone is about 0.075; the default is larger so everything fits and it is easy to hold with a gorilla hand.", new AcceptableValueRange<float>(0.05f, 0.5f)));
            Height = c.Bind("Phone", "Height", 0.2844f,
                new ConfigDescription("Phone height in metres. Width x 16 / 9 gives the 9:16 shape short-form video needs.", new AcceptableValueRange<float>(0.08f, 0.8f)));
            Thickness = c.Bind("Phone", "Thickness", 0.014f,
                new ConfigDescription("Phone thickness in metres (the physics box is never thinner than 2 cm).", new AcceptableValueRange<float>(0.006f, 0.05f)));
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

            PreviewFps = c.Bind("Camera", "PreviewFps", 15f,
                new ConfigDescription("How many times per second the live preview on the screen is drawn. Lower is lighter on the game.", new AcceptableValueRange<float>(1f, 60f)));
            PreviewHeight = c.Bind("Camera", "PreviewHeight", 320,
                new ConfigDescription("Height in pixels of the live preview (the width follows the screen shape).", new AcceptableValueRange<int>(64, 1080)));
            PhotoHeight = c.Bind("Camera", "PhotoHeight", 1920,
                new ConfigDescription("Height in pixels of a saved photo (the width follows the screen shape, so 1920 gives about 1080 x 1920).", new AcceptableValueRange<int>(240, 4096)));
            PhotoFolder = c.Bind("Camera", "PhotoFolder", "",
                "Folder photos are saved to. Empty means your Pictures folder, in a GorillaPhone subfolder.");
            CameraFov = c.Bind("Camera", "CameraFov", 75f,
                new ConfigDescription("Vertical field of view of the phone camera, in degrees.", new AcceptableValueRange<float>(30f, 120f)));
            MirrorSelfie = c.Bind("Camera", "MirrorSelfie", true,
                "Mirror the front (selfie) camera in the preview and in the saved photo, like a real phone's mirror preview.");
            PhotoFlipMode = c.Bind("Camera", "PhotoFlip", PhotoFlip.Auto,
                "Auto works out from the graphics API whether saved photos need flipping top to bottom. If a photo comes out upside down, set Flip (or NoFlip if it was flipped by mistake).");
            CameraMask = c.Bind("Camera", "CameraMask", CameraMaskSource.ThirdPerson,
                "Which of the game's cameras the phone camera copies its layers from. ThirdPerson is meant to include your own avatar; if selfies are missing something, try Main.");
            TriggerShutter = c.Bind("Camera", "TriggerShutter", true,
                "While you hold the phone, squeeze the trigger of the holding hand to take a photo.");
            ShutterVolume = c.Bind("Camera", "ShutterVolume", 0.6f,
                new ConfigDescription("Volume of the shutter click.", new AcceptableValueRange<float>(0f, 1f)));
        }
    }
}
