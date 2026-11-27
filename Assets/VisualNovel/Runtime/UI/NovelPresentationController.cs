using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Lament.VisualNovel
{
    public interface INovelPresentation
    {
        void Present(NovelSession session, bool restoring);
        void ExecuteCommand(NovelStoryAsset story, NovelNodeAsset node);
    }

    public sealed class NovelPresentationController : MonoBehaviour, INovelPresentation
    {
        public TMP_Text speaker;
        public TMP_Text body;
        public TMP_Text status;
        public TMP_Text historyText;
        public Image background;
        public Image[] portraits;
        public Image overlay;
        public Button advance;
        public Button choiceTemplate;
        public RectTransform choiceRoot;
        public GameObject historyPanel;
        public GameObject slotsPanel;
        public TMP_Text[] slotLabels;
        public AudioSource music;
        public AudioSource voice;
        public AudioSource sound;
        [Min(1)] public float charactersPerSecond = 35;
        public bool VoicePlaying => voice != null && voice.isPlaying;
        private NovelPlayer player;
        private int visit = -1;
        private float visible;
        private bool textFinished;
        private string currentMusic;
        private bool voicePaused;
        private NovelStageEffects effects;
        public NovelStageEffects Effects => effects;
        private NovelAudioChannels audioChannels;
        public NovelAudioChannels AudioChannels => audioChannels;
        private NovelNameInput nameInput;
        public NovelNameInput NameInput => nameInput;
        private NovelLuaEffect lua;
        private NovelLuaSurface luaSurface;
        public NovelLuaSurface LuaSurface => luaSurface;
        public bool LuaRunning => lua != null && lua.Running;

        public void ResetPresentation()
        {
            visit = -1;
            lua?.Cancel();
            effects?.CancelAll();
            audioChannels?.Reset();
            if (nameInput != null) nameInput.Panel.SetActive(false);
            voicePaused = false;
            voice.Stop();
            music.Stop();
            sound.Stop();
            currentMusic = null;
            overlay.color = Color.clear;
        }

        public void Bind(NovelPlayer source)
        {
            player = source;
            if (portraits.Length < NovelPresentationState.PortraitCapacity)
            {
                int previousCount = portraits.Length;
                System.Array.Resize(ref portraits, NovelPresentationState.PortraitCapacity);
                for (int i = previousCount; i < portraits.Length; i++)
                {
                    portraits[i] = Instantiate(portraits[0], portraits[0].transform.parent);
                    portraits[i].name = "扩展立绘槽" + i;
                    portraits[i].transform.SetSiblingIndex(overlay.transform.GetSiblingIndex());
                    portraits[i].rectTransform.anchorMin = new Vector2(.15f + (i - previousCount) * .2f, .2f);
                    portraits[i].rectTransform.anchorMax = new Vector2(.45f + (i - previousCount) * .2f, .95f);
                    portraits[i].rectTransform.offsetMin = portraits[i].rectTransform.offsetMax = Vector2.zero;
                    portraits[i].raycastTarget = false;
                    portraits[i].enabled = false;
                }
            }
            effects = new NovelStageEffects(background, portraits, overlay);
            audioChannels = new NovelAudioChannels(music, voice, sound);
            nameInput = new NovelNameInput(slotsPanel.transform.parent, body.font, SubmitName);
            luaSurface = new NovelLuaSurface(this, () => player.Session?.Story);
            lua = new NovelLuaEffect(luaSurface, () => player.Session.CompleteLuaEffect(), message => player.Session.FailLuaEffect(message));
            player.Message += ShowMessage;
            advance.onClick.AddListener(player.Continue);
            historyPanel.SetActive(false);
            slotsPanel.SetActive(false);
            choiceTemplate.gameObject.SetActive(false);
            var layout = choiceRoot.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                layout.childControlWidth = true;
                layout.childForceExpandWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandHeight = false;
            }
            var choiceImage = choiceTemplate.GetComponent<Image>();
            if (choiceImage != null)
            {
                choiceImage.raycastTarget = true;
                choiceTemplate.targetGraphic = choiceImage;
            }
            var size = choiceTemplate.GetComponent<LayoutElement>();
            if (size != null) { size.minHeight = 72; size.preferredHeight = 72; size.flexibleWidth = 1; }
        }

        public void Present(NovelSession session, bool restoring)
        {
            if (restoring || visit != session.Visit)
            {
                lua?.Cancel();
                visit = session.Visit;
                visible = 0;
                textFinished = false;
                if (voice != null) voice.Stop();
                voicePaused = false;
                if (session.Status == NovelPlaybackStatus.Dialogue)
                {
                    var character = session.Story.FindCharacter(session.Current.characterId);
                    speaker.text = session.FormatText(character?.displayName);
                    speaker.color = character?.nameColor ?? Color.white;
                    body.text = session.FormatText(session.Current.text);
                    body.ForceMeshUpdate();
                    body.maxVisibleCharacters = session.State.textComplete ? int.MaxValue : 0;
                    if (!restoring && session.Current.voice != null) { voice.clip = session.Current.voice; voice.Play(); }
                }
                else { speaker.text = ""; body.text = session.FormatText(session.Current.text); body.maxVisibleCharacters = int.MaxValue; }
                if (session.Status == NovelPlaybackStatus.TextInput)
                    nameInput.Show(session.FormatText(session.Current.text), session.Variables.Get(session.Current.inputVariable).text, session.Current.inputMaxLength);
                else nameInput.Panel.SetActive(false);
            }
            if (session.Status == NovelPlaybackStatus.Dialogue && session.State.textComplete)
            {
                body.maxVisibleCharacters = int.MaxValue;
                textFinished = true;
            }
            foreach (Transform child in choiceRoot)
                if (child.gameObject != choiceTemplate.gameObject) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            for (int i = 0; i < session.Choices.Count; i++)
            {
                int index = i;
                var button = Instantiate(choiceTemplate, choiceRoot);
                button.gameObject.SetActive(true);
                button.GetComponentInChildren<TMP_Text>().text = session.FormatText(session.Choices[i].text);
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => player.SelectChoice(index));
                if (i == 0) button.Select();
            }
            if (session.Choices.Count > 0) LayoutRebuilder.ForceRebuildLayoutImmediate(choiceRoot);
            advance.interactable = session.Status == NovelPlaybackStatus.Dialogue || (session.Status == NovelPlaybackStatus.Wait && session.Current.waitForInput);
            ApplyState(session.Story, session.State.presentation, restoring);
            ApplySpeakerPlaceholder(session);
            if (session.Status == NovelPlaybackStatus.LuaEffect && !lua.Running)
                lua.Start(session.Current.luaScript.text, session.Story.storyId + "/" + session.Current.nodeId + "/" + session.Current.luaScript.name, session.Current.luaTimeout);
        }

        private void ApplySpeakerPlaceholder(NovelSession session)
        {
            // Only fills an empty centre slot; scripted portrait commands always win.
            const int slot = 3;
            if (slot >= portraits.Length) return;
            if (session.Status != NovelPlaybackStatus.Dialogue) return;
            var character = session.Story.FindCharacter(session.Current.characterId);
            if (character?.portrait == null) return;
            if (!string.IsNullOrEmpty(session.State.presentation.portraits[slot])) return;
            if (effects != null && (effects.Busy("portrait" + slot) || effects.Busy("pose" + slot))) return;
            portraits[slot].sprite = character.portrait;
            portraits[slot].color = Color.white;
            portraits[slot].enabled = true;
        }

        private void Update()
        {
            if (player == null || player.Session == null) return;
            lua?.Tick(Time.unscaledDeltaTime, player.Suspended);
            effects?.Tick(Time.unscaledDeltaTime, player.Suspended);
            audioChannels?.Tick(Time.unscaledDeltaTime, player.Suspended);
            if (player.Suspended)
            {
                if (!voicePaused && voice.isPlaying) { voice.Pause(); voicePaused = true; }
                return;
            }
            if (voicePaused) { voice.UnPause(); voicePaused = false; }
            var session = player.Session;
            if (session.Status == NovelPlaybackStatus.Dialogue && !textFinished)
            {
                visible += GameSettings.TextSpeed * Time.unscaledDeltaTime;
                body.maxVisibleCharacters = Mathf.FloorToInt(visible);
                if (visible >= body.textInfo.characterCount) { textFinished = true; session.CompleteText(); }
            }
            if (session.Status == NovelPlaybackStatus.TextInput) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.spaceKey.wasPressedThisFrame) player.Continue();
            if (keyboard.f5Key.wasPressedThisFrame) player.Save(NovelSaveService.QuickSlot);
            if (keyboard.f9Key.wasPressedThisFrame) player.Load(NovelSaveService.QuickSlot);
        }

        private void ApplyState(NovelStoryAsset story, NovelPresentationState state, bool restoring)
        {
            state.EnsureDefaults();
            if (restoring) effects.Restore(story, state);
            else
            {
                if (!effects.Busy("background"))
                {
                    background.sprite = story.FindResource(state.background)?.image;
                    background.color = background.sprite != null ? Color.white : new Color(.035f, .04f, .055f);
                }
                for (int i = 0; i < portraits.Length; i++)
                {
                    if (!effects.Busy("portrait" + i))
                    {
                        portraits[i].sprite = story.FindResource(state.portraits[i])?.image;
                        portraits[i].enabled = portraits[i].sprite != null;
                    }
                    if (!effects.Busy("pose" + i) && !effects.Busy("portrait" + i) && !effects.Busy("shake")) effects.ApplyPose(i, state.poses[i]);
                }
                if (!effects.Busy("overlay")) overlay.color = state.overlay;
                if (!effects.Busy("backgroundPose") && !effects.Busy("shake")) effects.ApplyBackgroundPose(state.backgroundPose);
                if (!effects.Busy("cg"))
                {
                    effects.CG.sprite = story.FindResource(state.cg)?.image;
                    effects.CG.color = Color.white; effects.CG.enabled = effects.CG.sprite != null;
                }
            }
            audioChannels.Apply(story, state, restoring);
            if (restoring || currentMusic != state.music)
            {
                currentMusic = state.music;
                music.Stop();
                music.clip = story.FindResource(state.music)?.audio;
                if (music.clip != null) music.Play();
            }
        }

        public void ExecuteCommand(NovelStoryAsset story, NovelNodeAsset node)
        {
            if (node.command == NovelCommandType.Sound)
            {
                var clip = story.FindResource(node.resourceId)?.audio;
                if (clip != null) sound.PlayOneShot(clip);
            }
            effects.Execute(story, node);
            audioChannels.Execute(node);
        }

        public void SubmitName()
        {
            if (player.Suspended) return;
            if (!player.Session.SubmitText(nameInput.Field.text))
                ShowMessage("请输入1～" + player.Session.Current.inputMaxLength + "个字符，不含标签、花括号或换行");
        }

        public void ToggleAuto() { player.SetAutoMode(!player.AutoMode); ShowMessage(player.AutoMode ? "自动播放：开" : "自动播放：关"); }
        public void ToggleSkip() { player.SetSkipMode(!player.SkipMode); ShowMessage(player.SkipMode ? "快进：仅已读文本" : "快进：关"); }
        public void OpenHistory()
        {
            var text = new StringBuilder();
            foreach (var entry in player.Session.State.history) text.AppendLine(entry.speaker).AppendLine(entry.text).AppendLine();
            historyText.text = text.ToString();
            historyPanel.SetActive(true);
            player.Suspended = true;
        }
        public void OpenSlots()
        {
            var saves = new NovelSaveService();
            for (int i = 0; i < slotLabels.Length; i++)
                slotLabels[i].text = saves.TryLoad(i + 1, out var saved, out _) ? $"槽位 {i + 1}  {saved.storyName}\n{saved.savedUtc} / {saved.nodeId}" : $"槽位 {i + 1}（空或不可用）";
            slotsPanel.SetActive(true);
            player.Suspended = true;
        }
        public void ClosePanels() { historyPanel.SetActive(false); slotsPanel.SetActive(false); player.Suspended = false; }
        public void SaveSlot(int slot) { player.Save(slot); OpenSlots(); }
        public void LoadSlot(int slot) { player.Load(slot); ClosePanels(); }
        public void QuickSave() => player.Save(0);
        public void QuickLoad() => player.Load(0);
        public void ReturnToLobby() => player.ReturnToLobby();
        private void ShowMessage(string message) { if (status != null) status.text = message; }
        private void OnEnable()
        {
            if (lua != null && player?.Session?.Status == NovelPlaybackStatus.LuaEffect) Present(player.Session, true);
        }
        private void OnDisable() { lua?.Cancel(); }
        private void OnDestroy() { lua?.Cancel(); audioChannels?.Dispose(); if (player != null) player.Message -= ShowMessage; }
    }
}
