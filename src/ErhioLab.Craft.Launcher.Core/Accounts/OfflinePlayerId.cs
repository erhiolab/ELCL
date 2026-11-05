using System.Security.Cryptography;
using System.Text;

namespace ErhioLab.Craft.Launcher.Core.Accounts;

/// <summary>生成 Minecraft 离线模式使用的确定性玩家 UUID.</summary>
public static class OfflinePlayerId
{
    /// <summary>按 Java 的 nameUUIDFromBytes 规则计算 OfflinePlayer:用户名 的 UUID.</summary>
    public static Guid FromName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return Guid.ParseExact(Convert.ToHexString(hash), "N");
    }
}
