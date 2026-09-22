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
        public readonly ConfigEntry<float> PokeReach;
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

        // Home screen
        public readonly ConfigEntry<string> DateFormat;

        // Phone sound
        public readonly ConfigEntry<float> AudioVolume;
        public readonly ConfigEntry<float> MuffleCutoff;
        public readonly ConfigEntry<bool> Spatialize;

        // Video app
        public readonly ConfigEntry<bool> VideoEnabled;
        public readonly ConfigEntry<string> VideoStartUrl;
        public readonly ConfigEntry<string> VideoBrowserPath;
        public readonly ConfigEntry<float> VideoFps;
        public readonly ConfigEntry<int> VideoViewportWidth;
        public readonly ConfigEntry<int> VideoFrameWidth;
        public readonly ConfigEntry<bool> VideoMobile;
        public readonly ConfigEntry<bool> VideoHookAudio;
        public readonly ConfigEntry<int> VideoAudioDelayMs;
        public readonly ConfigEntry<int> VideoTapAssist;

        // Network: the real phone beacon (step 9b)
        public readonly ConfigEntry<bool> NetPhoneEnabled;

        // Network lab (development only, off by default)
        public readonly ConfigEntry<bool> NetLab;
        public readonly ConfigEntry<bool> NetLabRun;
        public readonly ConfigEntry<bool> NetLabAnyRoom;

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
            PokeReach = c.Bind("Phone", "PokeReach", 0.3f,
                new ConfigDescription("How far past your finger's last joint the touch point is, as a fraction of that finger segment. Lower it if presses land beyond where your finger is, raise it if you have to push past the screen.", new AcceptableValueRange<float>(-0.5f, 1.2f)));
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

            DateFormat = c.Bind("Home", "DateFormat", "",
                "How the date is shown on the home screen. Empty uses your PC's own long date format (its language and order). Or give a .NET format such as \"dddd d MMMM\".");

            AudioVolume = c.Bind("Audio", "Volume", 0.5f,
                new ConfigDescription("How loud the phone's sound starts, 0 to 1. The phone's own + and - buttons change it while you play.", new AcceptableValueRange<float>(0f, 1f)));
            MuffleCutoff = c.Bind("Audio", "MuffleCutoff", 800f,
                new ConfigDescription("When something solid is between the phone and your head, the sound is filtered to below this many Hz (lower is more muffled).", new AcceptableValueRange<float>(200f, 5000f)));
            Spatialize = c.Bind("Audio", "Spatialize", false,
                "Send the phone's sound through the game's 3D audio plugin (better direction). Off by default: whether it works with the game's own audio setup is untested.");

            VideoEnabled = c.Bind("Video", "Enabled", true,
                "Let the Video app start a browser. The browser is Microsoft Edge or Google Chrome, started by the phone in its own window with its own profile, and it uses the internet for the site.");
            VideoStartUrl = c.Bind("Video", "StartUrl", "https://www.tiktok.com/foryou",
                "The page the Video app opens. Any site with short vertical videos works best. (Older config files keep the earlier value, https://www.tiktok.com/; if you land on a grid of thumbnails instead of the feed, change it to this new one.)");
            VideoBrowserPath = c.Bind("Video", "BrowserPath", "",
                "Full path to msedge.exe or chrome.exe. Empty means look for Edge, then Chrome, in their usual places.");
            VideoFps = c.Bind("Video", "Fps", 30f,
                new ConfigDescription("Most pictures per second drawn on the phone's screen. Lower is lighter on the game.", new AcceptableValueRange<float>(5f, 60f)));
            VideoViewportWidth = c.Bind("Video", "ViewportWidth", 540,
                new ConfigDescription("Width in pixels the page thinks it has. Bigger shows more of a desktop layout in smaller print; smaller (about 400) suits a phone layout. Applies the next time the browser starts (restart the game).", new AcceptableValueRange<int>(320, 1400)));
            VideoFrameWidth = c.Bind("Video", "FrameWidth", 540,
                new ConfigDescription("Width in pixels of the pictures sent to the phone. Higher is sharper but heavier. Applies the next time the browser starts.", new AcceptableValueRange<int>(240, 1080)));
            VideoMobile = c.Bind("Video", "Mobile", false,
                "Ask the site for its phone layout (the page sees a phone's browser). Off shows the desktop layout. Applies the next time the browser starts.");
            VideoHookAudio = c.Bind("Video", "PhoneSound", true,
                "Play the page's sound from the phone in the game (3D, muffled by walls) instead of from your PC speakers. Off leaves the sound on the PC.");
            VideoAudioDelayMs = c.Bind("Video", "AudioDelayMs", 200,
                new ConfigDescription("How long the sound waits before playing. The picture arrives a little late, so a small delay lines them up; raise it if the sound is ahead of the picture, and it also smooths out gaps.", new AcceptableValueRange<int>(0, 600)));
            VideoTapAssist = c.Bind("Video", "TapAssist", 24,
                new ConfigDescription("A tap that lands on nothing clickable snaps to the nearest clickable spot within this many page pixels (small buttons such as a popup's close cross are hard to hit). 0 turns it off; a big number makes taps jump to the wrong thing.", new AcceptableValueRange<int>(0, 80)));

            NetPhoneEnabled = c.Bind("Network", "NetPhoneEnabled", true,
                "See other modded players' phones: their position, who's holding them and which app is open, and let them see yours. Sends your own phone's state (position, rotation, holder, open app -- never a photo, video or sound) about 10 times a second over the game's own event channel while you're in a room with someone else; players without the mod are unaffected. On by default: a cosmetic phone gives nobody an advantage. Turn off to keep the phone fully local again.");

            NetLab = c.Bind("Network", "NetLab", true,
                "DEVELOPMENT ONLY. Listen for the phone's network test messages (event code 151) and log what arrives. Sends nothing by itself. On by default; nothing about the phone is networked yet.");
            NetLabRun = c.Bind("Network", "NetLabRun", false,
                "DEVELOPMENT ONLY. Set to true (with NetLab on) to run one short test that sends a few dozen small test messages to find the game's message size and rate limits. A trigger, so it stays off by default; it resets to false after it starts.");
            NetLabAnyRoom = c.Bind("Network", "AllowAnyRoom", true,
                "Let the network test run in any room. Turn this off to make it run only in modded rooms (a room whose game mode contains MODDED_).");
        }
    }
}
