using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// 主密钥的还原与子密钥派生。
/// 密钥在源码中拆成“异或结果 + 掩码”两段，并各自按字节反序保存，
/// 避免直接以明文形式出现在代码或字符串表中。
/// AES / HMAC 子密钥与密钥编号只在首次使用时派生一次并缓存。
/// </summary>
public static class 密钥仓库
{
    private static readonly object _锁 = new object();

    private static byte[] _主密钥;
    private static bool _已提示缺失;

    private static byte[] _缓存主密钥;
    private static byte[] _缓存Aes密钥;
    private static byte[] _缓存Hmac密钥;
    private static byte _缓存编号;

    public static bool 已就绪
    {
        get { return 取主密钥() != null; }
    }

    public static byte[] 取主密钥()
    {
        if (_主密钥 != null) return _主密钥;

        lock (_锁)
        {
            if (_主密钥 != null) return _主密钥;

            byte[] 密钥 = 还原();
            if (密钥 == null && !_已提示缺失)
            {
                _已提示缺失 = true;
                安全日志.错误("缺少资源密钥：请执行菜单 防逆向/生成新密钥，然后重新加密资源并构建。");
            }
            _主密钥 = 密钥;
        }

        return _主密钥;
    }

    /// <summary>按密钥派生 AES 子密钥（带缓存）。</summary>
    public static byte[] 取Aes密钥(byte[] 主密钥)
    {
        确保派生(主密钥);
        return _缓存Aes密钥;
    }

    /// <summary>按密钥派生 HMAC 子密钥（带缓存）。</summary>
    public static byte[] 取Hmac密钥(byte[] 主密钥)
    {
        确保派生(主密钥);
        return _缓存Hmac密钥;
    }

    /// <summary>密钥编号：取主密钥 SHA-256 的首字节，用于容器头的版本标识，支持密钥轮换。</summary>
    public static byte 密钥编号(byte[] 主密钥)
    {
        确保派生(主密钥);
        return _缓存编号;
    }

    /// <summary>用 HMAC-SHA256 从主密钥派生出用途隔离的子密钥。</summary>
    public static byte[] 派生密钥(byte[] 主密钥, string 用途)
    {
        if (主密钥 == null) throw new InvalidOperationException("主密钥为空。");

        using (var hmac = new HMACSHA256(主密钥))
            return hmac.ComputeHash(Encoding.UTF8.GetBytes("LAMENT|" + 用途 + "|v1"));
    }

    private static void 确保派生(byte[] 主密钥)
    {
        if (主密钥 == null) throw new InvalidOperationException("主密钥为空。");
        if (_缓存主密钥 != null && 相同(_缓存主密钥, 主密钥)) return;

        lock (_锁)
        {
            if (_缓存主密钥 != null && 相同(_缓存主密钥, 主密钥)) return;

            _缓存Aes密钥 = 派生密钥(主密钥, "AES");
            _缓存Hmac密钥 = 派生密钥(主密钥, "HMAC");
            _缓存编号 = 计算编号(主密钥);
            _缓存主密钥 = (byte[])主密钥.Clone();
        }
    }

    private static byte 计算编号(byte[] 主密钥)
    {
        using (var sha = SHA256.Create())
            return sha.ComputeHash(主密钥)[0];
    }

    private static bool 相同(byte[] 甲, byte[] 乙)
    {
        if (甲 == null || 乙 == null || 甲.Length != 乙.Length) return false;

        int 差异 = 0;
        for (int i = 0; i < 甲.Length; i++) 差异 |= 甲[i] ^ 乙[i];
        return 差异 == 0;
    }

    private static byte[] 还原()
    {
        byte[] 甲 = 解析反序十六进制(密钥数据.片段甲);
        byte[] 乙 = 解析反序十六进制(密钥数据.片段乙);
        if (甲 == null || 乙 == null || 甲.Length != 32 || 乙.Length != 32) return null;

        byte[] 密钥 = new byte[32];
        for (int i = 0; i < 密钥.Length; i++) 密钥[i] = (byte)(甲[i] ^ 乙[i]);
        return 密钥;
    }

    private static byte[] 解析反序十六进制(string 十六进制)
    {
        if (string.IsNullOrEmpty(十六进制) || (十六进制.Length & 1) != 0) return null;

        int 长度 = 十六进制.Length / 2;
        byte[] 结果 = new byte[长度];
        for (int i = 0; i < 长度; i++)
        {
            int 高 = 数值(十六进制[i * 2]);
            int 低 = 数值(十六进制[i * 2 + 1]);
            if (高 < 0 || 低 < 0) return null;
            结果[i] = (byte)((高 << 4) | 低);
        }

        Array.Reverse(结果); // 存储时按字节反序，这里还原
        return 结果;
    }

    private static int 数值(char 字符)
    {
        if (字符 >= '0' && 字符 <= '9') return 字符 - '0';
        if (字符 >= 'a' && 字符 <= 'f') return 字符 - 'a' + 10;
        if (字符 >= 'A' && 字符 <= 'F') return 字符 - 'A' + 10;
        return -1;
    }

    /// <summary>进入运行前清空缓存，避免关闭域重载时沿用上一次的密钥。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void 重置缓存()
    {
        lock (_锁)
        {
            _主密钥 = null;
            _已提示缺失 = false;
            _缓存主密钥 = null;
            _缓存Aes密钥 = null;
            _缓存Hmac密钥 = null;
            _缓存编号 = 0;
        }
    }
}
