using Lament.VisualNovel;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class NovelEntryPoint : MonoBehaviour
{
    [SerializeField] private string runtimeScene = "NovelRuntime";
    private bool loading;

    public void StartNewGame()
    {
        NovelPlayer.PendingSave = null;
        LoadRuntime();
    }

    public void ContinueGame() => ContinueFrom(new NovelSaveService());

    public void ContinueFrom(NovelSaveService saves)
    {
        if (loading) return;
        if (saves == null) throw new System.ArgumentNullException(nameof(saves));
        int slot = saves.LatestSlot();
        if (slot < 0) { StartNewGame(); return; }
        if (saves.TryLoad(slot, out var saved, out var error))
        {
            NovelPlayer.PendingSave = saved;
            LoadRuntime();
        }
        else Debug.LogWarning("[VisualNovel] " + error, this);
    }

    private void LoadRuntime()
    {
        if (loading) return;
        if (!Application.CanStreamedLevelBeLoaded(runtimeScene)) { Debug.LogError("剧情场景未加入 Build Settings：" + runtimeScene, this); return; }
        loading = true;
        SceneManager.LoadSceneAsync(runtimeScene);
    }
}
