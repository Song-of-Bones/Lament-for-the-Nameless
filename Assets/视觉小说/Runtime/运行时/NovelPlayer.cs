using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Lament.VisualNovel
{
    [DisallowMultipleComponent]
    public sealed class NovelPlayer : MonoBehaviour
    {
        [SerializeField] private NovelStoryAsset story;
        [SerializeField] private NovelPresentationController presentation;
        [SerializeField, Min(0.1f)] private float autoDelay = 1.5f;
        public NovelSession Session { get; private set; }
        public bool AutoMode => clock.AutoMode;
        public bool SkipMode => clock.SkipMode;
        public bool Suspended { get; set; }
        public static NovelSaveData PendingSave { get; set; }
        public event Action<string> Message;
        public event Action StoryEnded;
        [NonSerialized] private NovelSaveService saves;
        private NovelSaveService Saves => saves ?? (saves = new NovelSaveService());
        private readonly NovelPlaybackClock clock = new NovelPlaybackClock();
        private bool restoring;

        public void Configure(NovelStoryAsset source, NovelPresentationController view) { story = source; presentation = view; }

        private void Start()
        {
            Session = new NovelSession();
            Session.Changed += OnChanged;
            Session.Command += OnCommand;
            presentation.Bind(this);
            try
            {
                var pending = PendingSave;
                PendingSave = null;
                if (pending != null) Resume(pending);
                else StartStory(story);
            }
            catch (Exception ex) { Notify(ex.Message); }
        }

        public void StartStory(NovelStoryAsset source, string entryNodeId = null)
        {
            if (Session == null) throw new InvalidOperationException("播放器尚未初始化");
            // Validate the new entry before replacing a running session's presentation.
            var errors = NovelStoryValidator.Validate(source);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            if (!string.IsNullOrEmpty(entryNodeId) && source.FindNode(entryNodeId) == null)
                throw new InvalidOperationException("预览入口节点不存在：" + entryNodeId);
            story = source;
            clock.Reset();
            presentation.ClosePanels();
            presentation.ResetPresentation();
            Session.Start(source, entryNodeId);
        }

        private void Update()
        {
            clock.AutoDelay = autoDelay;
            clock.Tick(Session, Time.unscaledDeltaTime, Suspended, presentation != null && presentation.VoicePlaying);
        }

        public void Continue() { if (!Suspended) Session?.Continue(); }
        public void SelectChoice(int index) { if (!Suspended) Session?.Select(index); }
        public void SetAutoMode(bool enabled) => clock.SetAutoMode(enabled);
        public void SetSkipMode(bool enabled) => clock.SetSkipMode(enabled);

        public void Save(int slot)
        {
            try
            {
                if (Session == null) throw new InvalidOperationException("剧情尚未启动");
                var snapshot = Session.Snapshot();
                Notify(Saves.TrySave(slot, snapshot, out var error) ? "已保存到槽位 " + slot : error);
            }
            catch (Exception ex) { Notify(ex.Message); }
        }

        public void Load(int slot)
        {
            if (!Saves.TryLoad(slot, out var saved, out var error)) { Notify(error); return; }
            try { Resume(saved); Notify("读档成功"); }
            catch (Exception ex) { Notify("无法恢复存档：" + ex.Message); }
        }

        public void Resume(NovelSaveData saved)
        {
            restoring = true;
            try { Session.Restore(story, saved); }
            finally { restoring = false; }
            clock.Reset();
        }

        public void ReturnToLobby()
        {
            if (Application.CanStreamedLevelBeLoaded("游戏大厅")) SceneManager.LoadSceneAsync("游戏大厅");
            else Notify("游戏大厅未加入 Build Settings");
        }

        private void OnChanged()
        {
            if (Session.Status == NovelPlaybackStatus.Faulted) { Notify(Session.Error); return; }
            presentation.Present(Session, restoring);
            if (Session.Status == NovelPlaybackStatus.Ended)
            {
                StoryEnded?.Invoke();
                if (Session.Current.returnToLobby) ReturnToLobby();
            }
        }

        private void OnCommand(NovelNodeAsset node) => presentation.ExecuteCommand(Session.Story, node);
        public void Notify(string text) { Message?.Invoke(text); Debug.Log("[VisualNovel] " + text, this); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { PendingSave = null; }
    }
}
