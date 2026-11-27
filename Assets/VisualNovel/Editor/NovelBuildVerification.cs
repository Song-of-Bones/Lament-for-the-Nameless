using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class NovelBuildVerification
{
    private const string OutputDirectory = ".codely-cli/test-results/build";
    private static bool running;
    private static BuildTarget pendingTarget;
    private static string pendingFilename;

    [Serializable]
    private sealed class Receipt
    {
        public string result;
        public string platform;
        public string output;
        public string completedUtc;
        public int errors;
        public int warnings;
    }

    [MenuItem("工具/视觉小说/运行当前平台构建验证")]
    public static void Run()
    {
        if (running || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("请等待编译/构建完成并停止播放");
        var target = EditorUserBuildSettings.activeBuildTarget;
        string filename;
        switch (target)
        {
            case BuildTarget.StandaloneLinux64: filename = "NovelFramework.x86_64"; break;
            case BuildTarget.StandaloneWindows64: filename = "NovelFramework.exe"; break;
            case BuildTarget.StandaloneOSX: filename = "NovelFramework.app"; break;
            default: throw new InvalidOperationException("仅验证当前桌面平台，不自动切换平台或安装模块");
        }
        if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
            throw new InvalidOperationException("当前平台未安装构建支持");
        running = true;
        pendingTarget = target;
        pendingFilename = filename;
        EditorApplication.update += BuildPending;
    }

    private static void BuildPending()
    {
        EditorApplication.update -= BuildPending;
        Build(pendingTarget, pendingFilename);
    }

    private static void Build(BuildTarget target, string filename)
    {
        var receipt = new Receipt { platform = target.ToString(), output = Path.Combine(OutputDirectory, filename) };
        try
        {
            Directory.CreateDirectory(OutputDirectory);
            File.WriteAllText(Path.Combine(OutputDirectory, "verification-status.txt"), "RUNNING " + DateTime.UtcNow.ToString("O"));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = receipt.output,
                target = target,
                options = BuildOptions.Development
            });
            receipt.result = report.summary.result.ToString();
            receipt.errors = report.summary.totalErrors;
            receipt.warnings = report.summary.totalWarnings;
            if (report.summary.result == BuildResult.Succeeded) Debug.Log("[VisualNovel] 当前平台构建验证成功：" + receipt.output);
            else Debug.LogError("[VisualNovel] 当前平台构建验证失败：" + receipt.result);
        }
        catch (Exception ex)
        {
            receipt.result = "Exception";
            receipt.errors = 1;
            Debug.LogException(ex);
        }
        finally
        {
            receipt.completedUtc = DateTime.UtcNow.ToString("O");
            Directory.CreateDirectory(OutputDirectory);
            File.WriteAllText(Path.Combine(OutputDirectory, "verification.json"), JsonUtility.ToJson(receipt, true));
            File.WriteAllText(Path.Combine(OutputDirectory, "verification-status.txt"), receipt.result);
            running = false;
        }
    }
}
