using System;
using System.IO;
using Lament.VisualNovel;
using UnityEditor;
using UnityEngine;

public static class NovelLuaDemo
{
    public const string StoryPath = "Assets/视觉小说/剧情/Lua演出示例.asset";
    public const string ScriptPath = "Assets/视觉小说/Lua/spiral_echo.lua.txt";

    [MenuItem("工具/视觉小说/创建Lua演出示例")]
    public static void Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("先停止播放");
        var existing = AssetDatabase.LoadAssetAtPath<NovelStoryAsset>(StoryPath);
        if (existing != null) { Selection.activeObject = existing; return; }
        var script = AssetDatabase.LoadAssetAtPath<TextAsset>(ScriptPath);
        if (script == null) throw new InvalidOperationException("缺少Lua示例脚本");
        var story = ScriptableObject.CreateInstance<NovelStoryAsset>();
        story.storyId = "lua-effect-demo"; story.displayName = "Lua自定义螺旋演出示例";
        AssetDatabase.CreateAsset(story, StoryPath);
        // Small solid-color fixture, not generated art and not a placeholder for a finished character.
        var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        texture.name = "Lua工程演示色块";
        var pixels = new Color[32 * 32];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        texture.SetPixels(pixels); texture.Apply();
        AssetDatabase.AddObjectToAsset(texture, story);
        var sprite = Sprite.Create(texture, new Rect(0, 0, 32, 32), Vector2.one * .5f);
        sprite.name = "Lua工程演示精灵"; AssetDatabase.AddObjectToAsset(sprite, story);
        story.resources.Add(new NovelResourceDefinition { id = "lua_demo", image = sprite });
        var lua = ScriptableObject.CreateInstance<NovelNodeAsset>();
        lua.name = lua.nodeId = "spiral"; lua.type = NovelNodeType.LuaEffect; lua.luaScript = script; lua.luaTimeout = 15;
        var end = ScriptableObject.CreateInstance<NovelNodeAsset>();
        end.name = end.nodeId = "finished"; end.type = NovelNodeType.Dialogue;
        end.text = "Lua螺旋效果已结束，临时图片已清理。可在剧情编辑器重启预览。";
        var stop = ScriptableObject.CreateInstance<NovelNodeAsset>();
        stop.name = stop.nodeId = "end"; stop.type = NovelNodeType.End; stop.returnToLobby = false;
        lua.next = end; end.next = stop;
        AssetDatabase.AddObjectToAsset(lua, story); AssetDatabase.AddObjectToAsset(end, story); AssetDatabase.AddObjectToAsset(stop, story);
        story.entryNode = lua; story.nodes.Add(lua); story.nodes.Add(end); story.nodes.Add(stop);
        EditorUtility.SetDirty(story); AssetDatabase.SaveAssets();
        Selection.activeObject = story;
        Debug.Log("[VisualNovel] Lua演出示例已创建，不改正式剧情");
    }
}

[InitializeOnLoad]
public static class NovelLuaPlaytest
{
    private const string Key = "Lament.VisualNovel.LuaTest";
    private static int step;
    private static double next;
    private static double deadline;
    private static NovelSaveData saved;
    private static Vector2 pausedPosition;
    private static string result;

    static NovelLuaPlaytest()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            {
                step = 0; result = "";
                next = EditorApplication.timeSinceStartup + .2;
                deadline = next + 60;
                Application.runInBackground = true;
                EditorApplication.update += Observe;
            }
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                SessionState.SetBool(Key, false); EditorApplication.update -= Observe;
            }
        };
    }

    [MenuItem("工具/视觉小说/运行Lua演出播放测试")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("先停止播放");
        var story = AssetDatabase.LoadAssetAtPath<NovelStoryAsset>(NovelLuaDemo.StoryPath);
        if (story == null) throw new InvalidOperationException("请先创建Lua演出示例");
        SessionState.SetBool(Key, true);
        try { NovelStoryPreview.Play(story, story.entryNode); }
        catch { SessionState.SetBool(Key, false); throw; }
        if (!EditorApplication.isPlayingOrWillChangePlaymode) SessionState.SetBool(Key, false);
    }
    private static void Check(bool condition, string text)
    {
        if (!condition) throw new InvalidOperationException(text);
        result += "PASS " + text + "\n";
    }
    private static void Observe()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < next) return;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Lua播放测试超时 步骤=" + step);
            var player = UnityEngine.Object.FindAnyObjectByType<NovelPlayer>();
            var view = UnityEngine.Object.FindAnyObjectByType<NovelPresentationController>();
            if (player?.Session == null || view == null) return;
            switch (step)
            {
                case 0:
                    if (player.Session.Story.storyId != "lua-effect-demo" || view.LuaSurface.Count != 12) return;
                    Check(player.Session.Status == NovelPlaybackStatus.LuaEffect && view.LuaRunning, "Lua节点阻塞剧情并运行螺旋脚本");
                    foreach (var graphic in view.LuaSurface.Root.GetComponentsInChildren<UnityEngine.UI.Graphic>())
                        Check(!graphic.raycastTarget, "Lua临时图像不拦截鼠标");
                    Check(view.LuaSurface.Count == 12, "Lua实际生成12个临时图片");
                    saved = player.Session.Snapshot(); player.Suspended = true;
                    pausedPosition = ((RectTransform)view.LuaSurface.Root.GetChild(0)).anchoredPosition;
                    step++; next = EditorApplication.timeSinceStartup + .5; return;
                case 1:
                    Check(((RectTransform)view.LuaSurface.Root.GetChild(0)).anchoredPosition == pausedPosition, "暂停冻结Lua逐帧演出");
                    player.Suspended = false; step++; next = EditorApplication.timeSinceStartup + .3; return;
                case 2:
                    Check(((RectTransform)view.LuaSurface.Root.GetChild(0)).anchoredPosition != pausedPosition, "恢复后Lua继续运算");
                    Directory.CreateDirectory(".codely-cli/test-results");
                    ScreenCapture.CaptureScreenshot(".codely-cli/test-results/visual-novel-lua.png");
                    step++; break;
                case 3:
                    player.Resume(saved);
                    Check(view.LuaSurface.Count == 0, "读档取消旧Lua对象");
                    Check(player.Session.Status == NovelPlaybackStatus.LuaEffect && view.LuaRunning, "读档重启Lua入口而非序列化VM");
                    step++; break;
                case 4:
                    Check(view.LuaSurface.Count == 12, "重播重建临时对象且不叠加泄漏");
                    view.enabled = false;
                    Check(view.LuaSurface.Count == 0 && !view.LuaRunning, "表现组件停用立即清理Lua");
                    view.enabled = true;
                    Check(view.LuaRunning, "表现组件重新启用后重启入口不死锁");
                    step++; break;
                case 5:
                    if (player.Session.Status == NovelPlaybackStatus.LuaEffect) return;
                    Check(player.Session.Current.nodeId == "finished", "Lua正常返回后推进剧情");
                    Check(view.LuaSurface.Count == 0 && !view.LuaRunning, "Lua结束清理全部临时层");
                    Finish(true); return;
            }
            next = EditorApplication.timeSinceStartup + .15;
        }
        catch (Exception ex) { result += "FAIL " + ex + "\n"; Finish(false); }
    }
    private static void Finish(bool passed)
    {
        EditorApplication.update -= Observe; SessionState.SetBool(Key, false);
        Directory.CreateDirectory(".codely-cli/test-results");
        File.WriteAllText(".codely-cli/test-results/visual-novel-lua.txt", result);
        if (passed) Debug.Log("[VisualNovel] Lua播放测试全部通过\n" + result);
        else Debug.LogError("[VisualNovel] Lua播放测试失败\n" + result);
        EditorApplication.ExitPlaymode();
    }
}
