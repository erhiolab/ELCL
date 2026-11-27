using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

public sealed record MinecraftServerStatus(
    string? Version,
    int? OnlinePlayers,
    int? MaximumPlayers,
    TimeSpan? Latency,
    IReadOnlyList<MinecraftTextSpan> Description,
    byte[]? Icon);

/// <summary>读取 Java 版服务器的状态, 不登录服务器.</summary>
public static class MinecraftServerStatusClient
{
    private const int MaxPacketBytes = 256 * 1024;

    /// <summary>查询状态和往返延迟. 可传入 DNS SRV 重定向解析器.</summary>
    public static async Task<MinecraftServerStatus> PingAsync(MinecraftServerAddress address,
        Func<MinecraftServerAddress, CancellationToken, Task<MinecraftServerAddress?>>? resolveRedirect = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(5));
        var token = deadline.Token;
        try
        {
            var target = resolveRedirect is null ? address : await resolveRedirect(address, token).ConfigureAwait(false) ?? address;
            using TcpClient client = new();
            await client.ConnectAsync(target.Host, target.Port, token).ConfigureAwait(false);
            await using var stream = client.GetStream();
            await WritePacketAsync(stream, Handshake(address), token).ConfigureAwait(false);
            await WritePacketAsync(stream, new byte[] { 0 }, token).ConfigureAwait(false);
            var status = ReadStatus(await ReadPacketAsync(stream, token).ConfigureAwait(false));

            // 只有收到正确的 Pong 才显示延迟, 状态响应本身不依赖 Pong.
            try
            {
                byte[] ping = new byte[9];
                ping[0] = 1;
                long start = Stopwatch.GetTimestamp();
                BinaryPrimitives.WriteInt64BigEndian(ping.AsSpan(1), start);
                await WritePacketAsync(stream, ping, token).ConfigureAwait(false);
                byte[] pong = await ReadPacketAsync(stream, token).ConfigureAwait(false);
                if (pong.AsSpan().SequenceEqual(ping)) status = status with { Latency = Stopwatch.GetElapsedTime(start) };
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested && error is IOException or SocketException or InvalidDataException or OperationCanceledException)
            {
            }

            cancellationToken.ThrowIfCancellationRequested();
            return status;
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException("服务器状态查询超时.", error);
        }
    }

    private static byte[] Handshake(MinecraftServerAddress address)
    {
        using MemoryStream packet = new();
        packet.WriteByte(0);
        WriteVarInt(packet, -1);
        byte[] host = Encoding.UTF8.GetBytes(address.Host);
        WriteVarInt(packet, host.Length);
        packet.Write(host);
        Span<byte> port = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(port, (ushort)address.Port);
        packet.Write(port);
        packet.WriteByte(1);
        return packet.ToArray();
    }

    private static async Task WritePacketAsync(Stream stream, byte[] payload, CancellationToken token)
    {
        using MemoryStream packet = new();
        WriteVarInt(packet, payload.Length);
        packet.Write(payload);
        await stream.WriteAsync(packet.GetBuffer().AsMemory(0, (int)packet.Length), token).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadPacketAsync(Stream stream, CancellationToken token)
    {
        byte[] first = new byte[1];
        uint value = 0;
        for (int index = 0; index < 5; index++)
        {
            await stream.ReadExactlyAsync(first, token).ConfigureAwait(false);
            value |= (uint)(first[0] & 127) << (7 * index);
            if ((first[0] & 128) != 0) continue;
            if ((index == 4 && first[0] > 15) || value is 0 or > MaxPacketBytes) throw new InvalidDataException("服务器状态包长度无效.");
            byte[] packet = new byte[value];
            await stream.ReadExactlyAsync(packet, token).ConfigureAwait(false);
            return packet;
        }

        throw new InvalidDataException("服务器状态包长度无效.");
    }

    private static void WriteVarInt(Stream stream, int value)
    {
        uint remaining = unchecked((uint)value);
        while (remaining >= 128)
        {
            stream.WriteByte((byte)((remaining & 127) | 128));
            remaining >>= 7;
        }

        stream.WriteByte((byte)remaining);
    }

    private static int ReadVarInt(byte[] packet, ref int offset)
    {
        int value = 0;
        for (int index = 0; index < 5 && offset < packet.Length; index++)
        {
            byte current = packet[offset++];
            if (index == 4 && current > 15) break;
            value |= (current & 127) << (index * 7);
            if ((current & 128) == 0) return value;
        }

        throw new InvalidDataException("服务器状态包包含无效的 VarInt.");
    }

    private static MinecraftServerStatus ReadStatus(byte[] packet)
    {
        int offset = 0;
        if (ReadVarInt(packet, ref offset) != 0) throw new InvalidDataException("服务器未返回状态响应.");
        int size = ReadVarInt(packet, ref offset);
        if (size != packet.Length - offset) throw new InvalidDataException("服务器状态 JSON 长度无效.");
        using var doc = JsonDocument.Parse(packet.AsMemory(offset, size));
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("服务器状态不是 JSON 对象.");
        var version = root.TryGetProperty("version", out var v) ? ReadText(v, "name", 100) : null;
        var online = root.TryGetProperty("players", out var players) ? ReadCount(players, "online") : null;
        var max = ReadCount(players, "max");
        var description = root.TryGetProperty("description", out var motd) ? MinecraftPackText.Parse(motd) : [];
        return new(version, online, max, null, description, DecodeIcon(ReadText(root, "favicon", 160_000)));
    }

    private static string? ReadText(JsonElement node, string key, int max) => node.ValueKind == JsonValueKind.Object
                                                                              && node.TryGetProperty(key, out var text) && text.ValueKind == JsonValueKind.String && text.GetString() is { } value && value.Length <= max
        ? value
        : null;

    private static int? ReadCount(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object
                                                                   && node.TryGetProperty(key, out var number) && number.ValueKind == JsonValueKind.Number && number.TryGetInt32(out int value) && value >= 0
        ? value
        : null;

    internal static byte[]? DecodeIcon(string? icon)
    {
        const string prefix = "data:image/png;base64,";
        if (icon is null) return null;
        if (icon.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) icon = icon[prefix.Length..];
        if (icon.Length is 0 or > 160_000) return null;
        try
        {
            return ValidateIcon(Convert.FromBase64String(icon));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    internal static byte[]? ValidateIcon(ReadOnlySpan<byte> bytes) => bytes.Length is >= 33 and <= 120_000 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && bytes[12..16].SequenceEqual("IHDR"u8) && BinaryPrimitives.ReadInt32BigEndian(bytes[16..20]) == 64 && BinaryPrimitives.ReadInt32BigEndian(bytes[20..24]) == 64 ? bytes.ToArray() : null;
}
