using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ProjectBuilder
{
    private const string OutputRoot = "Builds";
    private const string ReceiptPath = OutputRoot + "/build-receipt.json";

    [Serializable]
    private sealed class Receipt
    {
        public string target;
        public string result;
        public string output;
        public long sizeBytes;
        public string finishedUtc;
        public int errors;
        public int warnings;
    }

    [MenuItem("工具/项目/仅打包 Windows")]
    public static void BuildWindows()
    {
        Write(Build(BuildTarget.StandaloneWindows64, "Windows/Lament-the-Nameless.exe"));
    }

    [MenuItem("工具/项目/仅打包 Android")]
    public static void BuildAndroid()
    {
        Write(Build(BuildTarget.Android, "Android/Lament-the-Nameless.apk"));
    }

    private static void Write(string line)
    {
        Directory.CreateDirectory(OutputRoot);
        File.WriteAllText(ReceiptPath, line);
        if (line.Contains("\"result\": \"Succeeded\"")) Debug.Log("[Build] " + line);
        else Debug.LogError("[Build] " + line);
    }

    private static string Build(BuildTarget target, string relativeOutput)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先停止播放");
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0) return "FAILED " + target + "：Build Settings 没有启用场景";
        var output = Path.Combine(OutputRoot, relativeOutput);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        if (target == BuildTarget.Android)
        {
            EditorUserBuildSettings.buildAppBundle = false;
            // The project points at Keys/lament-release.keystore, which is not in the repo and has no
            // stored passwords. Fall back to debug signing so a testable APK can be produced.
            if (PlayerSettings.Android.useCustomKeystore)
            {
                Debug.LogWarning("[Build] 自定义密钥库缺少文件/密码，本次改用调试签名（不可用于上架）");
                PlayerSettings.Android.useCustomKeystore = false;
            }
        }
        try
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = target,
                options = BuildOptions.None
            });
            var summary = report.summary;
            return JsonUtility.ToJson(new Receipt
            {
                target = target.ToString(),
                result = summary.result.ToString(),
                output = output,
                sizeBytes = File.Exists(output) ? new FileInfo(output).Length : 0,
                finishedUtc = DateTime.UtcNow.ToString("O"),
                errors = summary.totalErrors,
                warnings = summary.totalWarnings
            });
        }
        catch (Exception ex)
        {
            return "FAILED " + target + "：" + ex.Message;
        }
    }
}
