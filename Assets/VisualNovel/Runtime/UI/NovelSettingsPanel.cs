using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Lament.VisualNovel
{
    // Binds the settings page controls to GameSettings and applies changes immediately.
    public sealed class NovelSettingsPanel : MonoBehaviour
    {
        [SerializeField] private Slider textSpeed;
        [SerializeField] private Slider autoDelay;
        [SerializeField] private Slider musicVolume;
        [SerializeField] private Slider ambientVolume;
        [SerializeField] private Slider voiceVolume;
        [SerializeField] private Slider soundVolume;
        [SerializeField] private Toggle fullscreen;
        [SerializeField] private Button reset;
        [SerializeField] private TMP_Text textSpeedValue;
        [SerializeField] private TMP_Text autoDelayValue;
        [SerializeField] private TMP_Text musicValue;
        [SerializeField] private TMP_Text ambientValue;
        [SerializeField] private TMP_Text voiceValue;
        [SerializeField] private TMP_Text soundValue;

        private bool loading;

        private void OnEnable()
        {
            GameSettings.Load();
            loading = true;
            textSpeed.SetValueWithoutNotify(GameSettings.TextSpeed);
            autoDelay.SetValueWithoutNotify(GameSettings.AutoDelay);
            musicVolume.SetValueWithoutNotify(GameSettings.MusicVolume);
            ambientVolume.SetValueWithoutNotify(GameSettings.AmbientVolume);
            voiceVolume.SetValueWithoutNotify(GameSettings.VoiceVolume);
            soundVolume.SetValueWithoutNotify(GameSettings.SoundVolume);
            fullscreen.SetIsOnWithoutNotify(GameSettings.Fullscreen);
            loading = false;
            RefreshLabels();

            textSpeed.onValueChanged.AddListener(OnTextSpeed);
            autoDelay.onValueChanged.AddListener(OnAutoDelay);
            musicVolume.onValueChanged.AddListener(OnMusic);
            ambientVolume.onValueChanged.AddListener(OnAmbient);
            voiceVolume.onValueChanged.AddListener(OnVoice);
            soundVolume.onValueChanged.AddListener(OnSound);
            fullscreen.onValueChanged.AddListener(OnFullscreen);
            reset.onClick.AddListener(OnReset);
            GameSettings.Changed += RefreshLabels;
        }

        private void OnDisable()
        {
            textSpeed.onValueChanged.RemoveListener(OnTextSpeed);
            autoDelay.onValueChanged.RemoveListener(OnAutoDelay);
            musicVolume.onValueChanged.RemoveListener(OnMusic);
            ambientVolume.onValueChanged.RemoveListener(OnAmbient);
            voiceVolume.onValueChanged.RemoveListener(OnVoice);
            soundVolume.onValueChanged.RemoveListener(OnSound);
            fullscreen.onValueChanged.RemoveListener(OnFullscreen);
            reset.onClick.RemoveListener(OnReset);
            GameSettings.Changed -= RefreshLabels;
            GameSettings.Flush();
        }

        private void OnTextSpeed(float value) { if (!loading) GameSettings.SetTextSpeed(value); }
        private void OnAutoDelay(float value) { if (!loading) GameSettings.SetAutoDelay(value); }
        private void OnMusic(float value) { if (!loading) GameSettings.SetVolume(NovelAudioBus.Music, value); }
        private void OnAmbient(float value) { if (!loading) GameSettings.SetVolume(NovelAudioBus.Ambient, value); }
        private void OnVoice(float value) { if (!loading) GameSettings.SetVolume(NovelAudioBus.Voice, value); }
        private void OnSound(float value) { if (!loading) GameSettings.SetVolume(NovelAudioBus.Sound, value); }
        private void OnFullscreen(bool value) { if (!loading) GameSettings.SetFullscreen(value); }

        private void OnReset()
        {
            GameSettings.ResetToDefaults();
            loading = true;
            textSpeed.SetValueWithoutNotify(GameSettings.TextSpeed);
            autoDelay.SetValueWithoutNotify(GameSettings.AutoDelay);
            musicVolume.SetValueWithoutNotify(GameSettings.MusicVolume);
            ambientVolume.SetValueWithoutNotify(GameSettings.AmbientVolume);
            voiceVolume.SetValueWithoutNotify(GameSettings.VoiceVolume);
            soundVolume.SetValueWithoutNotify(GameSettings.SoundVolume);
            fullscreen.SetIsOnWithoutNotify(GameSettings.Fullscreen);
            loading = false;
            RefreshLabels();
        }

        private void RefreshLabels()
        {
            if (textSpeedValue != null) textSpeedValue.text = Mathf.RoundToInt(GameSettings.TextSpeed) + " 字/秒";
            if (autoDelayValue != null) autoDelayValue.text = GameSettings.AutoDelay.ToString("0.0") + " 秒";
            if (musicValue != null) musicValue.text = Percent(GameSettings.MusicVolume);
            if (ambientValue != null) ambientValue.text = Percent(GameSettings.AmbientVolume);
            if (voiceValue != null) voiceValue.text = Percent(GameSettings.VoiceVolume);
            if (soundValue != null) soundValue.text = Percent(GameSettings.SoundVolume);
        }

        private static string Percent(float value) => Mathf.RoundToInt(value * 100f) + "%";
    }
}
