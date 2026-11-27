using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

public sealed class NovelTestRunner : ICallbacks
{
    private static TestRunnerApi runner;
    [MenuItem("工具/视觉小说/运行核心测试")]
    public static void RunTests()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (runner != null) { Debug.LogWarning("[VisualNovel] 测试正在运行"); return; }
        runner = ScriptableObject.CreateInstance<TestRunnerApi>();
        runner.RegisterCallbacks(new NovelTestRunner());
        runner.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { "Lament.VisualNovel.Tests" } }));
    }
    public void RunStarted(ITestAdaptor tests) { Debug.Log("[VisualNovel] 核心测试开始"); }
    public void RunFinished(ITestResultAdaptor result)
    {
        Directory.CreateDirectory(".codely-cli/test-results");
        TestRunnerApi.SaveResultToFile(result, ".codely-cli/test-results/visual-novel.xml");
        Debug.Log($"[VisualNovel] 测试结束 Passed={result.PassCount} Failed={result.FailCount} Skipped={result.SkipCount}");
        Object.DestroyImmediate(runner); runner = null;
    }
    public void TestStarted(ITestAdaptor test) { }
    public void TestFinished(ITestResultAdaptor result) { if (result.FailCount > 0 && !result.Test.IsSuite) Debug.LogError("[VisualNovel] " + result.Name + ": " + result.Message); }
}
