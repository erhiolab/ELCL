namespace ErhioLab.Craft.Launcher.Core.Minecraft;

/// <summary>servers.dat 中保存的一条服务器, Index 是原始列表中的位置.</summary>
public sealed record MinecraftSavedServer(int Index, string Name, string Address, byte[]? Icon, bool IsHidden);

/// <summary>读写 Java 版客户端在版本目录中的服务器列表.</summary>
public static class MinecraftServerList
{
    private const int MaxBytes = 16 * 1024 * 1024;
    private const int MaxEntries = 4096;
    private static readonly SemaphoreSlim Writer = new(1);
    private static readonly byte[] Empty = [10, 0, 0, 9, 0, 7, .. "servers"u8, 10, 0, 0, 0, 0, 0];

    /// <summary>按原始顺序读取列表, 文件不存在时返回空列表.</summary>
    public static async Task<IReadOnlyList<MinecraftSavedServer>> ReadAsync(string versionDirectory, CancellationToken cancellationToken = default)
        => (await ReadDocumentAsync(ServerListPath(versionDirectory), cancellationToken).ConfigureAwait(false)).Rows;

    /// <summary>在列表末尾加入一台服务器, 返回新条目.</summary>
    public static Task<MinecraftSavedServer> AddAsync(string versionDirectory, string name, string address, CancellationToken cancellationToken = default)
    {
        name = ValidateName(name);
        if (!MinecraftServerAddress.TryParse(address, out _)) throw new ArgumentException("服务器地址无效.", nameof(address));
        return EditAsync(versionDirectory, document => document.Add(name, address.Trim()), cancellationToken);
    }

    /// <summary>按读到的条目修改名称, 不修改地址或其他 NBT 字段.</summary>
    public static Task<MinecraftSavedServer> RenameAsync(string versionDirectory, MinecraftSavedServer server, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        name = ValidateName(name);
        return EditAsync(versionDirectory, document => document.Rename(server, name), cancellationToken);
    }

    /// <summary>从列表移除选中的记录, 不联系服务器.</summary>
    public static async Task RemoveAsync(string versionDirectory, MinecraftSavedServer server, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        await EditAsync(versionDirectory, document => document.Remove(server), cancellationToken).ConfigureAwait(false);
    }

    private static string ValidateName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        name = name.Trim();
        return name.Length is >= 1 and <= 128 && !name.Any(char.IsControl)
            ? name
            : throw new ArgumentException("服务器名称必须包含 1 到 128 个字符, 且不能包含控制字符.", nameof(name));
    }

    private static string ServerListPath(string versionDirectory)
    {
        string directory = Path.GetFullPath(versionDirectory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("不能读取链接目录中的服务器列表.");
        return Path.Combine(directory, "servers.dat");
    }

    private static async Task<Document> ReadDocumentAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("不能读取作为链接的 servers.dat.");
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
            if (input.Length > MaxBytes) throw new InvalidDataException("服务器列表文件过大.");
            var data = new byte[(int)input.Length];
            await input.ReadExactlyAsync(data, token).ConfigureAwait(false);
            return new Document(data, true, token);
        }
        catch (FileNotFoundException)
        {
            return new Document(Empty, false, token);
        }
    }

    private static async Task<MinecraftSavedServer> EditAsync(string directory, Func<Document, (byte[] Content, MinecraftSavedServer Row)> edit, CancellationToken token)
    {
        await Writer.WaitAsync(token).ConfigureAwait(false);
        try
        {
            string path = ServerListPath(directory);
            var document = await ReadDocumentAsync(path, token).ConfigureAwait(false);
            var (content, server) = edit(document);
            if (content.Length > MaxBytes) throw new InvalidDataException("服务器列表文件过大.");
            string temporary = path + ".elcl-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, content, token).ConfigureAwait(false);
                if (document.Exists && !document.HasSameContent(await ReadDocumentAsync(path, token).ConfigureAwait(false)))
                    throw new IOException("服务器列表在写入期间变化, 请刷新后再试.");
                token.ThrowIfCancellationRequested();
                File.Move(temporary, path, overwrite: document.Exists);
                return server;
            }
            finally
            {
                File.Delete(temporary);
            }
        }
        finally
        {
            Writer.Release();
        }
    }

    private sealed class Document : MinecraftNbtReader
    {
        private sealed record Entry(MinecraftSavedServer Row, int Start, int End, int NameStart, int NameEnd);

        private readonly List<Entry> _entries = [];
        private readonly int _listStart;
        private readonly int _listEnd;
        public bool Exists { get; }
        public IReadOnlyList<MinecraftSavedServer> Rows => _entries.Select(entry => entry.Row).ToArray();
        public bool HasSameContent(Document other) => Exists == other.Exists && Bytes.AsSpan().SequenceEqual(other.Bytes);

        internal Document(byte[] data, bool exists, CancellationToken cancellationToken) : base(data, cancellationToken)
        {
            Exists = exists;
            if (Byte() != 10) throw new InvalidDataException("服务器列表不是有效的 NBT 复合标签.");
            SkipString();
            int tag;
            while ((tag = Byte()) != 0)
            {
                Token.ThrowIfCancellationRequested();
                string name = ReadString();
                if (name != "servers")
                {
                    Skip(tag, 1);
                    continue;
                }

                if (tag != 9 || _listStart != 0) throw new InvalidDataException("服务器 NBT 列表无效或重复.");
                _listStart = Offset;
                int type = Byte(), count = Int();
                if (count is < 0 or > MaxEntries || (type != 10 && (type != 0 || count != 0)))
                    throw new InvalidDataException("服务器 NBT 列表长度或类型无效.");
                for (int index = 0; index < count; index++) _entries.Add(ReadEntry(index));
                _listEnd = Offset;
            }

            if (_listStart == 0 || Offset != Bytes.Length) throw new InvalidDataException("服务器列表不完整或包含多余数据.");
        }

        private Entry ReadEntry(int index)
        {
            Token.ThrowIfCancellationRequested();
            int start = Offset, nameStart = -1, nameEnd = -1;
            string? name = null, address = null;
            byte[]? icon = null;
            bool hidden = false;
            int tag;
            while ((tag = Byte()) != 0)
            {
                switch (tag, ReadString())
                {
                    case (8, "name") when name is null:
                        nameStart = Offset;
                        name = ReadString();
                        nameEnd = Offset;
                        break;
                    case (8, "ip") when address is null: address = ReadString(); break;
                    case (8, "icon"): icon = MinecraftServerStatusClient.DecodeIcon(ReadString()); break;
                    case (7, "icon"): icon = MinecraftServerStatusClient.ValidateIcon(Take(Int())); break;
                    case (1, "hidden"): hidden = Byte() != 0; break;
                    default: Skip(tag, 2); break;
                }
            }

            if (name is null || address is null) throw new InvalidDataException("服务器条目缺少名称或地址.");
            return new(new(index, name, address, icon, hidden), start, Offset, nameStart, nameEnd);
        }

        internal (byte[], MinecraftSavedServer) Add(string name, string address)
        {
            if (_entries.Count >= MaxEntries) throw new InvalidDataException("已达到服务器数量上限.");
            using MemoryStream output = new();
            WriteListHeader(output, _entries.Count + 1);
            output.Write(Bytes.AsSpan(_listStart + 5, _listEnd - _listStart - 5));
            output.WriteByte(8);
            WriteNbtString(output, "name");
            WriteNbtString(output, name);
            output.WriteByte(8);
            WriteNbtString(output, "ip");
            WriteNbtString(output, address);
            output.WriteByte(0);
            output.Write(Bytes.AsSpan(_listEnd));
            return (output.ToArray(), new(_entries.Count, name, address, null, false));
        }

        internal (byte[], MinecraftSavedServer) Rename(MinecraftSavedServer expected, string name)
        {
            var entry = RequireEntry(expected);
            using MemoryStream output = new();
            output.Write(Bytes.AsSpan(0, entry.NameStart));
            WriteNbtString(output, name);
            output.Write(Bytes.AsSpan(entry.NameEnd));
            return (output.ToArray(), entry.Row with { Name = name });
        }

        internal (byte[], MinecraftSavedServer) Remove(MinecraftSavedServer expected)
        {
            var entry = RequireEntry(expected);
            using MemoryStream output = new();
            WriteListHeader(output, _entries.Count - 1);
            output.Write(Bytes.AsSpan(_listStart + 5, entry.Start - _listStart - 5));
            output.Write(Bytes.AsSpan(entry.End));
            return (output.ToArray(), entry.Row);
        }

        private Entry RequireEntry(MinecraftSavedServer server)
        {
            if (server.Index < 0 || server.Index >= _entries.Count)
                throw new IOException("服务器列表已经变化, 请刷新后再试.");
            var entry = _entries[server.Index];
            if (entry.Row.Name != server.Name || entry.Row.Address != server.Address || entry.Row.IsHidden != server.IsHidden)
                throw new IOException("服务器列表已经变化, 请刷新后再试.");
            return entry;
        }

        private void WriteListHeader(Stream output, int count)
        {
            output.Write(Bytes.AsSpan(0, _listStart));
            output.WriteByte(10);
            WriteInt(output, count);
        }
    }
}
