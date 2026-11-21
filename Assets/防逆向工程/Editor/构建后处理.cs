using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 构建后检查：未生成密钥、未使用 IL2CPP、加密资源缺少完整性清单时给出提示。
/// 安全配置.构建严格检查 为 true 时直接让构建失败（CI 建议开启）。
/// </summary>
public class 防逆向构建后处理 : IPostprocessBuildWithReport
{
    public int callbackOrder
    {
        get { return 1000; }
    }

    public void OnPostprocessBuild(BuildReport 报告)
    {
        if (密钥生成器.读取主密钥() == null)
        {
            失败("构建已完成，但从未生成资源密钥；加密资源在运行时将无法解密。" +
                 "请执行 防逆向/生成新密钥，重新加密资源后再构建。");
        }

        if (报告.summary.platform == BuildTarget.Android)
        {
            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP)
            {
                失败("Android 当前未使用 IL2CPP，Assembly-CSharp.dll 可被 dnSpy 直接反编译。" +
                     "请在 Player Settings 中切换为 IL2CPP。");
            }

            if (string.IsNullOrEmpty(安全配置.期望APK签名))
            {
                警告("未填写 安全配置.期望APK签名，运行时不会校验安装包证书。" +
                     "获取方式：keytool -printcert -jarfile <apk>");
            }
        }
        else if (报告.summary.platform == BuildTarget.StandaloneWindows64)
        {
            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone) != ScriptingImplementation.IL2CPP)
            {
                失败("Windows 当前未使用 IL2CPP，托管程序集可被 dnSpy 直接反编译。" +
                     "建议切换为 IL2CPP，并考虑对 exe/dll 使用商业壳。");
            }
        }

        检查清单();

        安全日志.普通("构建输出：" + 报告.summary.outputPath +
                      "\n请确认输出目录中的加密资源已是最新，并且完整性清单已重新生成。");
    }

    private static void 检查清单()
    {
        string 目录 = Path.Combine(Application.dataPath,
            "StreamingAssets/" + 安全配置.资源目录名);

        if (!Directory.Exists(目录)) return;

        string[] 加密文件 = Directory.GetFiles(目录, "*.bytes", SearchOption.AllDirectories);
        if (加密文件.Length == 0) return;

        if (!File.Exists(Path.Combine(目录, 安全配置.清单文件名)))
        {
            失败("加密资源目录中存在 " + 加密文件.Length +
                 " 个 .bytes 文件，但缺少完整性清单 " + 安全配置.清单文件名 + "。" +
                 "请执行 防逆向/加密资源工具 → 生成 / 刷新完整性清单。");
        }
    }

    private static void 失败(string 内容)
    {
        if (安全配置.构建严格检查)
            throw new BuildFailedException("[防逆向] " + 内容);

        Debug.LogError("[防逆向] " + 内容);
    }

    private static void 警告(string 内容)
    {
        Debug.LogWarning("[防逆向] " + 内容);
    }
}
