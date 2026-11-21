using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// 加密资源容器。
/// 结构：魔数 "LFLN"(4) + 版本(1) + 标志(1) + 密钥编号(1) + 保留(1) + IV(16) + HMAC-SHA256(32) + AES-256-CBC 密文。
/// 采用先加密后校验（Encrypt-then-MAC）：任何字节被改动都会在解密前被发现。
/// 文件头不是 Unity 的 "UnityFS"，AssetStudio / AssetRipper / UABEA 无法识别。
/// </summary>
public static class 加密文件格式
{
    public const int 头长度 = 56;
    public const byte 当前版本 = 1;

    private const int 编号偏移 = 6;
    private const int 校验偏移 = 24;
    private const int 校验长度 = 32;
    private const byte 标志_AES256CBC = 1;

    private static readonly byte[] 魔数 = { 0x4C, 0x46, 0x4C, 0x4E }; // LFLN
    private static readonly byte[] 空数组 = new byte[0];

    public static byte[] 封装(byte[] 明文, byte[] 主密钥)
    {
        if (明文 == null) throw new ArgumentNullException("明文");
        if (主密钥 == null) throw new InvalidOperationException("缺少主密钥，无法加密。");

        byte[] aes密钥 = 密钥仓库.取Aes密钥(主密钥);
        byte[] hmac密钥 = 密钥仓库.取Hmac密钥(主密钥);
        byte[] iv = 随机字节(16);
        byte[] 密文 = Aes加密(明文, aes密钥, iv);

        byte[] 结果 = new byte[头长度 + 密文.Length];
        Buffer.BlockCopy(魔数, 0, 结果, 0, 4);
        结果[4] = 当前版本;
        结果[5] = 标志_AES256CBC;
        结果[编号偏移] = 密钥仓库.密钥编号(主密钥);
        Buffer.BlockCopy(iv, 0, 结果, 8, 16);
        Buffer.BlockCopy(密文, 0, 结果, 头长度, 密文.Length);

        // 校验区此时仍为全零；解密端按同样的“跳过校验区”规则重算
        using (var hmac = new HMACSHA256(hmac密钥))
        {
            hmac.TransformBlock(结果, 0, 校验偏移, null, 0);
            int 后段 = 校验偏移 + 校验长度;
            hmac.TransformBlock(结果, 后段, 结果.Length - 后段, null, 0);
            hmac.TransformFinalBlock(空数组, 0, 0);
            Buffer.BlockCopy(hmac.Hash, 0, 结果, 校验偏移, 校验长度);
        }

        return 结果;
    }

    public static byte[] 解封(byte[] 文件数据, byte[] 主密钥)
    {
        if (文件数据 == null) throw new ArgumentNullException("文件数据");
        if (主密钥 == null) throw new InvalidOperationException("缺少主密钥，无法解密。");
        if (文件数据.Length < 头长度) throw new InvalidDataException("加密文件长度不足。");

        if (文件数据[0] != 魔数[0] || 文件数据[1] != 魔数[1] ||
            文件数据[2] != 魔数[2] || 文件数据[3] != 魔数[3])
            throw new InvalidDataException("不是有效的加密资源文件。");

        if (文件数据[4] != 当前版本) throw new InvalidDataException("不支持的加密资源版本。");
        if (文件数据[5] != 标志_AES256CBC) throw new InvalidDataException("不支持的加密算法标识。");

        byte 文件编号 = 文件数据[编号偏移];
        byte 当前编号 = 密钥仓库.密钥编号(主密钥);
        if (文件编号 != 当前编号)
            throw new CryptographicException(
                "密钥版本不匹配（文件 " + 文件编号 + " / 当前 " + 当前编号 + "），请重新加密资源。");

        byte[] hmac密钥 = 密钥仓库.取Hmac密钥(主密钥);
        using (var hmac = new HMACSHA256(hmac密钥))
        {
            // 分两段计算，跳过 32 字节校验区，避免为清零而复制整份文件
            hmac.TransformBlock(文件数据, 0, 校验偏移, null, 0);
            int 后段 = 校验偏移 + 校验长度;
            hmac.TransformBlock(文件数据, 后段, 文件数据.Length - 后段, null, 0);
            hmac.TransformFinalBlock(空数组, 0, 0);

            if (!恒定比较(hmac.Hash, 文件数据, 校验偏移))
                throw new CryptographicException("加密资源校验失败，文件可能被篡改。");
        }

        int 密文长度 = 文件数据.Length - 头长度;
        if (密文长度 <= 0 || (密文长度 & 15) != 0) throw new InvalidDataException("加密资源数据损坏。");

        byte[] iv = new byte[16];
        Buffer.BlockCopy(文件数据, 8, iv, 0, 16);

        byte[] 密文 = new byte[密文长度];
        Buffer.BlockCopy(文件数据, 头长度, 密文, 0, 密文长度);

        try
        {
            return Aes解密(密文, 密钥仓库.取Aes密钥(主密钥), iv);
        }
        finally
        {
            Array.Clear(密文, 0, 密文.Length);
        }
    }

    /// <summary>计算 SHA-256，返回小写十六进制，用于完整性清单与缓存命名。</summary>
    public static string 摘要十六进制(byte[] 数据)
    {
        using (var sha = SHA256.Create())
        {
            byte[] 摘要 = sha.ComputeHash(数据);
            var sb = new StringBuilder(摘要.Length * 2);
            for (int i = 0; i < 摘要.Length; i++) sb.Append(摘要[i].ToString("x2"));
            return sb.ToString();
        }
    }

    private static byte[] Aes加密(byte[] 明文, byte[] 密钥, byte[] iv)
    {
        using (var aes = Aes.Create())
        {
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = 密钥;
            aes.IV = iv;
            using (var 加密器 = aes.CreateEncryptor())
                return 加密器.TransformFinalBlock(明文, 0, 明文.Length);
        }
    }

    private static byte[] Aes解密(byte[] 密文, byte[] 密钥, byte[] iv)
    {
        using (var aes = Aes.Create())
        {
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = 密钥;
            aes.IV = iv;
            using (var 解密器 = aes.CreateDecryptor())
                return 解密器.TransformFinalBlock(密文, 0, 密文.Length);
        }
    }

    private static byte[] 随机字节(int 长度)
    {
        byte[] 结果 = new byte[长度];
        using (var 随机 = RandomNumberGenerator.Create())
            随机.GetBytes(结果);
        return 结果;
    }

    private static bool 恒定比较(byte[] 甲, byte[] 乙, int 乙偏移)
    {
        int 差异 = 0;
        for (int i = 0; i < 校验长度; i++) 差异 |= 甲[i] ^ 乙[乙偏移 + i];
        return 差异 == 0;
    }
}
