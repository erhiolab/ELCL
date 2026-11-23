using System.Buffers.Binary;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

internal sealed partial class MinecraftWorldMetadata
{
    private sealed record PackList(int Start, int End, IReadOnlyList<string> Values);

    internal sealed record PackSelectionDocument(MinecraftWorldDataPackSelection Selection, Action<Stream, MinecraftWorldDataPackSelection> Write);

    internal static PackSelectionDocument OpenPackSelection(string path, CancellationToken token)
    {
        var reader = Open(path, token);
        reader.FindCompound("Data");
        reader.FindCompound("DataPacks");
        PackList? enabled = null, disabled = null;
        int tag;
        while ((tag = reader.Byte()) != 0)
        {
            token.ThrowIfCancellationRequested();
            string name = reader.ReadString();
            if (name is "Enabled" or "Disabled")
            {
                if (tag != 9 || (name == "Enabled" ? enabled : disabled) is not null)
                    throw new InvalidDataException("世界数据包选择列表的类型无效或重复.");
                var list = reader.ReadPackList();
                if (name == "Enabled") enabled = list;
                else disabled = list;
            }
            else reader.Skip(tag, 1);
        }

        if (enabled is null || disabled is null) throw new InvalidDataException("世界缺少完整的数据包启用记录.");
        return new(new(enabled.Values, disabled.Values), (output, selection) => reader.WriteSelection(output, enabled, disabled, selection));
    }

    private void FindCompound(string target)
    {
        int? location = null;
        int tag;
        while ((tag = Byte()) != 0)
        {
            token.ThrowIfCancellationRequested();
            string name = ReadString();
            if (name == target)
            {
                if (tag != 10 || location is not null) throw new InvalidDataException("世界标签的类型无效或重复: " + target);
                location = _offset;
            }

            Skip(tag, 1);
        }

        _offset = location ?? throw new InvalidDataException("世界缺少 NBT 标签: " + target);
    }

    private PackList ReadPackList()
    {
        int start = _offset;
        int type = Byte();
        int count = Int();
        if (count < 0 || count > 100_000 || (type != 8 && (type != 0 || count != 0)))
            throw new InvalidDataException("数据包选择记录不是有效的字符串列表.");
        string[] names = new string[count];
        for (int i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            names[i] = ReadString();
        }

        return new(start, _offset, Array.AsReadOnly(names));
    }

    private void WriteSelection(Stream output, PackList enabled, PackList disabled, MinecraftWorldDataPackSelection selection)
    {
        var entries = new[] { (enabled, selection.Enabled), (disabled, selection.Disabled) };
        int copied = 0;
        // 只重写两个 NBT 列表的内容, 原始数据中的未知标签和字段顺序保持不变.
        foreach (var (old, names) in entries.OrderBy(entry => entry.Item1.Start))
        {
            output.Write(bytes.AsSpan(copied, old.Start - copied));
            output.WriteByte(8);
            WriteInt(output, names.Count);
            foreach (string name in names)
            {
                token.ThrowIfCancellationRequested();
                WriteNbtString(output, name);
            }

            copied = old.End;
        }

        output.Write(bytes.AsSpan(copied));
    }

    private static void WriteInt(Stream output, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        output.Write(bytes);
    }

    private static void WriteNbtString(Stream output, string value)
    {
        // NBT 字符串使用 Java 修改版 UTF-8, 包括空字符和成对的 UTF-16 代理项.
        int length = value.Sum(c => c is >= (char)1 and <= (char)0x7F ? 1 : c <= 0x7FF ? 2 : 3);
        if (length > ushort.MaxValue) throw new InvalidDataException("数据包标识的编码超过 NBT 字符串长度限制.");
        Span<byte> prefix = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(prefix, (ushort)length);
        output.Write(prefix);
        foreach (int c in value)
        {
            if (c is >= 1 and <= 0x7F) output.WriteByte((byte)c);
            else if (c <= 0x7FF)
            {
                output.WriteByte((byte)(0xC0 | (c >> 6)));
                output.WriteByte((byte)(0x80 | (c & 0x3F)));
            }
            else
            {
                output.WriteByte((byte)(0xE0 | (c >> 12)));
                output.WriteByte((byte)(0x80 | ((c >> 6) & 0x3F)));
                output.WriteByte((byte)(0x80 | (c & 0x3F)));
            }
        }
    }
}
