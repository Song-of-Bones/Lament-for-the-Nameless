using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 生成/读取资源加密主密钥。
/// 原始密钥保存在 Assets/防逆向工程/密钥~ 下（Unity 忽略以 ~ 结尾的目录），
/// 同时把拆分的密钥写入 生成/密钥数据.生成.cs 供运行时还原。
/// 密钥~ 目录必须加入 .gitignore，绝不能提交。
/// </summary>
public static class 密钥生成器
{
    private const string 密钥目录 = "Assets/防逆向工程/密钥~";
    private const string 密钥文件 = "Assets/防逆向工程/密钥~/主密钥.bin";
    private const string 生成文件 = "Assets/防逆向工程/生成/密钥数据.生成.cs";

    [MenuItem("防逆向/生成新密钥", false, 1)]
    public static void 生成新密钥()
    {
        if (!EditorUtility.DisplayDialog(
                "生成新密钥",
                "将生成新的资源加密密钥。\n\n已有的加密资源将无法解密，必须重新加密；下次构建也会使用新密钥。\n\n继续吗？",
                "生成", "取消"))
            return;

        byte[] 主密钥 = 随机(32);
        byte[] 掩码 = 随机(32);

        写入密钥文件(主密钥);
        写入生成代码(主密钥, 掩码);
        AssetDatabase.Refresh();

        Debug.Log("[防逆向] 已生成新密钥，指纹：" + 指纹(主密钥));
        EditorUtility.DisplayDialog("完成",
            "新密钥已生成，指纹：" + 指纹(主密钥) + "\n\n请重新加密所有资源，并重新生成完整性清单。", "好");
    }

    /// <summary>无弹窗生成（供自动化脚本调用）。写入后需要 AssetDatabase.Refresh 触发编译。</summary>
    public static string 直接生成()
    {
        byte[] 主密钥 = 随机(32);
        byte[] 掩码 = 随机(32);

        写入密钥文件(主密钥);
        写入生成代码(主密钥, 掩码);

        return 指纹(主密钥);
    }

    /// <summary>从磁盘读取未拆分的原始主密钥，供编辑器加密工具使用。</summary>
    public static byte[] 读取主密钥()
    {
        try
        {
            if (!File.Exists(密钥文件)) return null;
            byte[] 数据 = File.ReadAllBytes(密钥文件);
            return 数据.Length == 32 ? 数据 : null;
        }
        catch
        {
            return null;
        }
    }

    public static string 指纹(byte[] 密钥)
    {
        if (密钥 == null) return "未生成";

        using (var sha = SHA256.Create())
        {
            byte[] 摘要 = sha.ComputeHash(密钥);
            var sb = new StringBuilder(8);
            for (int i = 0; i < 4; i++) sb.Append(摘要[i].ToString("x2"));
            return sb.ToString().ToUpperInvariant();
        }
    }

    private static void 写入密钥文件(byte[] 主密钥)
    {
        if (!Directory.Exists(密钥目录)) Directory.CreateDirectory(密钥目录);
        File.WriteAllBytes(密钥文件, 主密钥);
    }

    private static void 写入生成代码(byte[] 主密钥, byte[] 掩码)
    {
        byte[] 甲 = new byte[32];
        for (int i = 0; i < 甲.Length; i++) 甲[i] = (byte)(主密钥[i] ^ 掩码[i]);

        var sb = new StringBuilder();
        sb.AppendLine("// 本文件由 防逆向/生成新密钥 自动生成，请勿手动修改。");
        sb.AppendLine("public static class 密钥数据");
        sb.AppendLine("{");
        sb.AppendLine("    public const string 片段甲 = \"" + 反转十六进制(甲) + "\";");
        sb.AppendLine("    public const string 片段乙 = \"" + 反转十六进制(掩码) + "\";");
        sb.AppendLine();
        sb.AppendLine("    public const string 生成时间 = \"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\";");
        sb.AppendLine("    public const string 指纹 = \"" + 指纹(主密钥) + "\";");
        sb.AppendLine("}");

        string 目录 = Path.GetDirectoryName(生成文件);
        if (!string.IsNullOrEmpty(目录) && !Directory.Exists(目录)) Directory.CreateDirectory(目录);

        File.WriteAllText(生成文件, sb.ToString(), new UTF8Encoding(false));
    }

    private static byte[] 随机(int 长度)
    {
        byte[] 结果 = new byte[长度];
        using (var 随机源 = RandomNumberGenerator.Create()) 随机源.GetBytes(结果);
        return 结果;
    }

    private static string 反转十六进制(byte[] 数据)
    {
        var sb = new StringBuilder(数据.Length * 2);
        for (int i = 数据.Length - 1; i >= 0; i--) sb.Append(数据[i].ToString("x2"));
        return sb.ToString();
    }
}
