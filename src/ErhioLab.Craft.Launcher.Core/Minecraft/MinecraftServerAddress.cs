using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>Java 版服务器的主机与端口.</summary>
public sealed record MinecraftServerAddress
{
    public const int DefaultPort = 25565;
    public string Host { get; }
    public int Port { get; }
    public bool HasExplicitPort { get; }

    private MinecraftServerAddress(string host, int port, bool hasExplicitPort) => (Host, Port, HasExplicitPort) = (host, port, hasExplicitPort);

    /// <summary>解析域名、IP、host:port 或带方括号的 IPv6 地址.</summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out MinecraftServerAddress? address)
    {
        address = null;
        if (value?.Trim() is not { Length: > 0 and <= 320 } input || input.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) return false;
        string host = input;
        string? portText = null;
        if (input[0] == '[')
        {
            int close = input.IndexOf(']');
            if (close < 2 || !IPAddress.TryParse(input[1..close], out var ip) || !input[1..close].Contains(':')) return false;
            host = ip.ToString();
            if (close < input.Length - 1)
            {
                if (input[close + 1] != ':') return false;
                portText = input[(close + 2)..];
            }
        }
        else if (input.IndexOf(':') is >= 0 and var separator && separator == input.LastIndexOf(':'))
        {
            host = input[..separator];
            portText = input[(separator + 1)..];
        }

        int port = DefaultPort;
        if (portText is not null && (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535)) return false;
        if (IPAddress.TryParse(host, out var literal)) host = literal.ToString();
        else
        {
            try
            {
                host = new IdnMapping().GetAscii(host);
            }
            catch (ArgumentException)
            {
                return false;
            }

            if (Uri.CheckHostName(host) != UriHostNameType.Dns || host.Length > 253 || host.StartsWith('-')) return false;
        }

        address = new(host, port, portText is not null);
        return true;
    }

    /// <summary>返回适合写入游戏服务器列表或快速加入参数的地址.</summary>
    public override string ToString()
    {
        string host = Host.Contains(':') ? "[" + Host + "]" : Host;
        return HasExplicitPort ? host + ":" + Port.ToString(CultureInfo.InvariantCulture) : host;
    }
}
