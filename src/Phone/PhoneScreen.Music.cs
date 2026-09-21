using GorillaPhone.Audio;
using TMPro;
using UnityEngine;

namespace GorillaPhone.Phone
{
    /// <summary>
    /// The Music app for now: a small sound test page. A play / pause button starts a looping chime that comes from the
    /// phone in 3D (quieter with distance, muffled when a wall is between the phone and you), volume - and +, and a live
    /// "muffled" indicator. Later this page becomes the real Music app (what is playing on the PC).
    /// </summary>
    public sealed partial class PhoneScreen
    {
        static readonly Color MusicTint = new Color(0.95f, 0.35f, 0.45f, 1f);

        Transform musicRoot;
        Part musicBg, musicHomePart, musicRing, musicPlay, musicMinus, musicPlus;
        TextMeshPro musicTitle, musicSub, musicSub2, musicStatus, musicVolLabel, musicVolValue, musicHint1, musicHint2;
        Button musicHomeBtn, musicPlayBtn, musicMinusBtn, musicPlusBtn;
        Texture2D playTex, pauseTex;
        Texture2D shownPlayTex;

        void BuildMusic()
        {
            musicRoot = NewNode("Music", root);
            playTex = Track(ShapeTextures.Play(128));
            pauseTex = Track(ShapeTextures.Pause(128));

            musicBg = Opaque(musicRoot, "Background", new Color(0.05f, 0.06f, 0.10f, 1f), null, 0);
            musicHomePart = Glass(musicRoot, "HomeButton", Color.white, Track(ShapeTextures.Home(128)), 10);
            musicRing = Glass(musicRoot, "PlayRing", MusicTint, Track(ShapeTextures.Disc(128)), 9);
            musicPlay = Glass(musicRoot, "Play", Color.white, playTex, 10);
            musicMinus = Glass(musicRoot, "VolumeDown", Color.white, Track(ShapeTextures.Minus(128)), 10);
            musicPlus = Glass(musicRoot, "VolumeUp", Color.white, Track(ShapeTextures.Plus(128)), 10);

            musicTitle = PhoneText.Create(log, musicRoot, "Title", "Music", 0.01f, 0.1f, TextColor, 30);
            musicSub = PhoneText.Create(log, musicRoot, "Sub", "Sound test", 0.01f, 0.1f, TextColor, 30);
            musicSub2 = PhoneText.Create(log, musicRoot, "Sub2", "A looping chime, not your PC's audio yet", 0.01f, 0.1f, DimText, 30);
            musicStatus = PhoneText.Create(log, musicRoot, "Status", "Paused", 0.01f, 0.1f, TextColor, 30);
            musicVolLabel = PhoneText.Create(log, musicRoot, "VolumeLabel", "Volume", 0.01f, 0.1f, DimText, 30);
            musicVolValue = PhoneText.Create(log, musicRoot, "VolumeValue", "50%", 0.01f, 0.1f, TextColor, 30);
            musicHint1 = PhoneText.Create(log, musicRoot, "Hint1", "Put the phone behind a wall", 0.01f, 0.1f, DimText, 30);
            musicHint2 = PhoneText.Create(log, musicRoot, "Hint2", "and the sound should muffle", 0.01f, 0.1f, DimText, 30);

            musicHomeBtn = AddButton(Page.Music, GoHome, 0.4f);
            musicPlayBtn = AddButton(Page.Music, delegate { if (phone.Sound != null) phone.Sound.Toggle(); }, 0.4f);
            musicMinusBtn = AddButton(Page.Music, delegate { if (phone.Sound != null) phone.Sound.AdjustVolume(-0.1f); }, 0.15f);
            musicPlusBtn = AddButton(Page.Music, delegate { if (phone.Sound != null) phone.Sound.AdjustVolume(0.1f); }, 0.15f);

            musicRoot.gameObject.SetActive(false);
        }

        void LayoutMusic(float sw, float sh, float margin)
        {
            if (musicRoot == null) return;
            float hy = sh * 0.5f - 0.19f * sw;   // the header row, below the island (as in the Gallery)
            float bd = 0.14f * sw;

            Place(musicBg, 0f, 0f, sw, sh, -0.0004f);
            Place(musicHomePart, -0.36f * sw, hy, bd, bd, -0.0010f);
            SetRect(musicHomeBtn, -0.36f * sw, hy, bd, margin);
            PhoneText.Resize(musicTitle, 0.06f * sw, 0.4f * sw);
            PhoneText.Place(musicTitle, 0f, hy, -0.0020f);

            PhoneText.Resize(musicSub, 0.05f * sw, 0.8f * sw);
            PhoneText.Place(musicSub, 0f, 0.56f * sw, -0.0020f);
            PhoneText.Resize(musicSub2, 0.032f * sw, 0.94f * sw);
            PhoneText.Place(musicSub2, 0f, 0.49f * sw, -0.0020f);

            float py = 0.20f * sw, pd = 0.40f * sw;
            Place(musicRing, 0f, py, pd * 1.16f, pd * 1.16f, -0.0008f);
            Place(musicPlay, 0f, py, pd, pd, -0.0010f);
            SetRect(musicPlayBtn, 0f, py, pd * 1.16f, margin);

            PhoneText.Resize(musicStatus, 0.055f * sw, 0.9f * sw);
            PhoneText.Place(musicStatus, 0f, -0.12f * sw, -0.0020f);

            float vy = -0.38f * sw, vd = 0.16f * sw;
            PhoneText.Resize(musicVolLabel, 0.036f * sw, 0.4f * sw);
            PhoneText.Place(musicVolLabel, 0f, -0.27f * sw, -0.0020f);
            Place(musicMinus, -0.28f * sw, vy, vd, vd, -0.0010f);
            Place(musicPlus, 0.28f * sw, vy, vd, vd, -0.0010f);
            SetRect(musicMinusBtn, -0.28f * sw, vy, vd, margin);
            SetRect(musicPlusBtn, 0.28f * sw, vy, vd, margin);
            PhoneText.Resize(musicVolValue, 0.07f * sw, 0.3f * sw);
            PhoneText.Place(musicVolValue, 0f, vy, -0.0020f);

            PhoneText.Resize(musicHint1, 0.034f * sw, 0.94f * sw);
            PhoneText.Place(musicHint1, 0f, -0.62f * sw, -0.0020f);
            PhoneText.Resize(musicHint2, 0.034f * sw, 0.94f * sw);
            PhoneText.Place(musicHint2, 0f, -0.68f * sw, -0.0020f);
        }

        void OpenMusic()
        {
            SetPage(Page.Music);
        }

        void UpdateMusic()
        {
            PhoneAudio snd = phone.Sound;
            if (snd == null) return;

            string status = snd.Loading ? "Loading..." : snd.Playing ? (snd.Muffled ? "Playing (muffled by a wall)" : "Playing") : "Paused";
            PhoneText.Set(musicStatus, status);
            PhoneText.Set(musicVolValue, Mathf.RoundToInt(snd.Volume * 100f) + "%");

            Texture2D want = snd.Playing ? pauseTex : playTex;   // the button shows what pressing it will do
            if (want != shownPlayTex)
            {
                shownPlayTex = want;
                PhoneMaterials.SetTexture(musicPlay.M, want);
            }
        }
    }
}
