using Lament.VisualNovel;
using UnityEngine;

public sealed class NovelSettingsAdapter : MonoBehaviour
{
    public NovelPlayer player;
    public SettingsWindowManager manager;
    private bool wasOpen;

    public void OpenSettings()
    {
        if (player == null || player.Suspended || manager == null) return;
        manager.打开设置("novel_settings", false);
        wasOpen = manager.已打开 || manager.加载中;
        if (wasOpen) player.Suspended = true;
    }

    private void Update()
    {
        if (!wasOpen || manager == null) return;
        if (!manager.已打开 && !manager.加载中) { player.Suspended = false; wasOpen = false; }
    }
}
