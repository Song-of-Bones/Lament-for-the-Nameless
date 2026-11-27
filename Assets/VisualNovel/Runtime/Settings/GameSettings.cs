using System;
using UnityEngine;

namespace Lament.VisualNovel
{
    // Player-facing preferences shared by the novel runtime and the settings page.
    public static class GameSettings
    {
        public const float MinTextSpeed = 20f;
        public const float MaxTextSpeed = 120f;
        public const float DefaultTextSpeed = 40f;
        public const float MinAutoDelay = 0.5f;
        public const float MaxAutoDelay = 4f;
        public const float DefaultAutoDelay = 1.5f;

        private const string KeyTextSpeed = "novel_text_speed";
        private const string KeyAutoDelay = "novel_auto_delay";
        private const string KeyMusic = "novel_volume_music";
        private const string KeyAmbient = "novel_volume_ambient";
        private const string KeyVoice = "novel_volume_voice";
        private const string KeySound = "novel_volume_sound";
        private const string KeyFullscreen = "novel_fullscreen";

        private static bool loaded;

        public static event Action Changed;

        public static float TextSpeed { get; private set; }
        public static float AutoDelay { get; private set; }
        public static float MusicVolume { get; private set; }
        public static float AmbientVolume { get; private set; }
        public static float VoiceVolume { get; private set; }
        public static float SoundVolume { get; private set; }
        public static bool Fullscreen { get; private set; }

        public static void Load()
        {
            loaded = true;
            TextSpeed = PlayerPrefs.GetFloat(KeyTextSpeed, DefaultTextSpeed);
            AutoDelay = PlayerPrefs.GetFloat(KeyAutoDelay, DefaultAutoDelay);
            MusicVolume = PlayerPrefs.GetFloat(KeyMusic, 1f);
            AmbientVolume = PlayerPrefs.GetFloat(KeyAmbient, 1f);
            VoiceVolume = PlayerPrefs.GetFloat(KeyVoice, 1f);
            SoundVolume = PlayerPrefs.GetFloat(KeySound, 1f);
            Fullscreen = PlayerPrefs.GetInt(KeyFullscreen, Screen.fullScreen ? 1 : 0) == 1;
            Clamp();
        }

        private static void EnsureLoaded()
        {
            if (!loaded) Load();
        }

        private static void Clamp()
        {
            TextSpeed = Mathf.Clamp(TextSpeed, MinTextSpeed, MaxTextSpeed);
            AutoDelay = Mathf.Clamp(AutoDelay, MinAutoDelay, MaxAutoDelay);
            MusicVolume = Mathf.Clamp01(MusicVolume);
            AmbientVolume = Mathf.Clamp01(AmbientVolume);
            VoiceVolume = Mathf.Clamp01(VoiceVolume);
            SoundVolume = Mathf.Clamp01(SoundVolume);
        }

        public static void SetTextSpeed(float value)
        {
            EnsureLoaded();
            TextSpeed = Mathf.Clamp(value, MinTextSpeed, MaxTextSpeed);
            PlayerPrefs.SetFloat(KeyTextSpeed, TextSpeed);
            Notify();
        }

        public static void SetAutoDelay(float value)
        {
            EnsureLoaded();
            AutoDelay = Mathf.Clamp(value, MinAutoDelay, MaxAutoDelay);
            PlayerPrefs.SetFloat(KeyAutoDelay, AutoDelay);
            Notify();
        }

        public static void SetVolume(NovelAudioBus bus, float value)
        {
            EnsureLoaded();
            value = Mathf.Clamp01(value);
            switch (bus)
            {
                case NovelAudioBus.Music: MusicVolume = value; PlayerPrefs.SetFloat(KeyMusic, value); break;
                case NovelAudioBus.Ambient: AmbientVolume = value; PlayerPrefs.SetFloat(KeyAmbient, value); break;
                case NovelAudioBus.Voice: VoiceVolume = value; PlayerPrefs.SetFloat(KeyVoice, value); break;
                default: SoundVolume = value; PlayerPrefs.SetFloat(KeySound, value); break;
            }
            Notify();
        }

        public static float GetVolume(NovelAudioBus bus)
        {
            EnsureLoaded();
            switch (bus)
            {
                case NovelAudioBus.Music: return MusicVolume;
                case NovelAudioBus.Ambient: return AmbientVolume;
                case NovelAudioBus.Voice: return VoiceVolume;
                default: return SoundVolume;
            }
        }

        public static void SetFullscreen(bool value)
        {
            EnsureLoaded();
            Fullscreen = value;
            PlayerPrefs.SetInt(KeyFullscreen, value ? 1 : 0);
            Screen.fullScreen = value;
            Notify();
        }

        public static void ResetToDefaults()
        {
            EnsureLoaded();
            TextSpeed = DefaultTextSpeed;
            AutoDelay = DefaultAutoDelay;
            MusicVolume = AmbientVolume = VoiceVolume = SoundVolume = 1f;
            Fullscreen = true;
            PlayerPrefs.SetFloat(KeyTextSpeed, TextSpeed);
            PlayerPrefs.SetFloat(KeyAutoDelay, AutoDelay);
            PlayerPrefs.SetFloat(KeyMusic, MusicVolume);
            PlayerPrefs.SetFloat(KeyAmbient, AmbientVolume);
            PlayerPrefs.SetFloat(KeyVoice, VoiceVolume);
            PlayerPrefs.SetFloat(KeySound, SoundVolume);
            PlayerPrefs.SetInt(KeyFullscreen, 1);
            Screen.fullScreen = true;
            Flush();
            Notify();
        }

        public static void Flush() => PlayerPrefs.Save();

        private static void Notify() => Changed?.Invoke();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            loaded = false;
            Changed = null;
        }
    }
}
