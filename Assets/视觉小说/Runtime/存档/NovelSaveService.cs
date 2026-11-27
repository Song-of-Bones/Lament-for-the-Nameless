using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Lament.VisualNovel
{
    [Serializable]
    public sealed class NovelHistoryEntry
    {
        public string nodeId;
        public string speaker;
        public string text;
    }

    [Serializable]
    public sealed class NovelPresentationState
    {
        public const int PortraitCapacity = 6;
        public string background = "";
        public string[] portraits = { "", "", "" };
        public string music = "";
        public Color overlay = Color.clear;
        public NovelPortraitPose[] poses;
        public string cg = "";
        public string ambient = "";
        public NovelPortraitPose backgroundPose;
        public float[] volumes;

        public void EnsureDefaults()
        {
            if (backgroundPose == null) backgroundPose = new NovelPortraitPose();
            if (volumes == null || volumes.Length != 4) volumes = new[] { 1f, 1f, 1f, 1f };
            if (portraits == null) portraits = new string[PortraitCapacity];
            if (portraits.Length != PortraitCapacity) Array.Resize(ref portraits, PortraitCapacity);
            if (poses == null) poses = new NovelPortraitPose[PortraitCapacity];
            if (poses.Length != PortraitCapacity) Array.Resize(ref poses, PortraitCapacity);
            for (int i = 0; i < poses.Length; i++)
                if (poses[i] == null) poses[i] = new NovelPortraitPose();
        }
    }

    [Serializable]
    public sealed class NovelSaveData
    {
        public int version = 1;
        public int storyVersion;
        public string storyId;
        public string storyName;
        public string nodeId;
        public string savedUtc;
        public bool textComplete;
        public float waitRemaining;
        public List<NovelVariableDefinition> variables = new List<NovelVariableDefinition>();
        public List<string> readNodes = new List<string>();
        public List<string> sampledNodes = new List<string>();
        public List<NovelHistoryEntry> history = new List<NovelHistoryEntry>();
        public NovelPresentationState presentation = new NovelPresentationState();
        public NovelSaveData Copy() => JsonUtility.FromJson<NovelSaveData>(JsonUtility.ToJson(this));
    }

    public sealed class NovelSaveService
    {
        public const int SlotCount = 3;
        public const int QuickSlot = 0;
        private readonly string directory;
        public NovelSaveService(string directory = null)
        {
            this.directory = directory ?? Path.Combine(Application.persistentDataPath, "VisualNovel", "Saves");
        }

        private string PathFor(int slot)
        {
            if (slot < 0 || slot > SlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
            return Path.Combine(directory, "slot-" + slot + ".json");
        }

        public bool TrySave(int slot, NovelSaveData data, out string error)
        {
            error = null;
            try
            {
                var path = PathFor(slot);
                var copy = data.Copy();
                copy.savedUtc = DateTime.UtcNow.ToString("O");
                Directory.CreateDirectory(directory);
                var temp = path + ".tmp";
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(copy, true));
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                error = "写入存档失败：" + ex.Message;
                return false;
            }
        }

        public bool TryLoad(int slot, out NovelSaveData data, out string error)
        {
            data = null;
            error = null;
            try
            {
                var path = PathFor(slot);
                if (!File.Exists(path)) { error = "此槽位暂无存档"; return false; }
                if (new FileInfo(path).Length > 16 * 1024 * 1024) { error = "存档文件过大"; return false; }
                data = JsonUtility.FromJson<NovelSaveData>(File.ReadAllText(path));
                if (data == null || data.version != 1 || string.IsNullOrEmpty(data.storyId) || string.IsNullOrEmpty(data.nodeId) ||
                    data.variables == null || data.readNodes == null || data.history == null || data.presentation == null ||
                    data.presentation.portraits == null || (data.presentation.portraits.Length != 3 && data.presentation.portraits.Length != NovelPresentationState.PortraitCapacity))
                {
                    data = null;
                    error = "存档损坏或版本不受支持";
                    return false;
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                data = null;
                error = "读取存档失败：" + ex.Message;
                return false;
            }
        }

        public int LatestSlot()
        {
            int latest = -1;
            DateTime time = DateTime.MinValue;
            for (int slot = 0; slot <= SlotCount; slot++)
                if (TryLoad(slot, out var data, out _) && DateTime.TryParse(data.savedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var saved) && saved > time)
                { latest = slot; time = saved; }
            return latest;
        }
    }
}
