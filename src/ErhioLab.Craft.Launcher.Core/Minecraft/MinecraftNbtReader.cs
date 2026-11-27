using System.Buffers.Binary;
using System.Text;

namespace ErhioLab.Craft.Launcher.Core.Minecraft;

internal abstract class MinecraftNbtReader(byte[] data, CancellationToken cancellationToken)
{
    protected readonly byte[] Bytes = data;
    protected readonly CancellationToken Token = cancellationToken;
    protected int Offset;

    protected void Skip(int tag, int depth)
    {
        if (depth > 128) throw new InvalidDataException("NBT 嵌套过深.");
        if (ScalarSize(tag) is > 0 and var width)
        {
            Take(width);
            return;
        }

        switch (tag)
        {
            case 7: SkipArray(Int(), 1); break;
            case 8: SkipString(); break;
            case 9:
                int type = Byte();
                int count = Int();
                if (count < 0 || count > Bytes.Length - Offset || type > 12 || (type == 0 && count != 0))
                    throw new InvalidDataException("NBT 列表长度或类型无效.");
                if (ScalarSize(type) is > 0 and var itemWidth) SkipArray(count, itemWidth);
                else
                    for (int index = 0; index < count; index++)
                    {
                        if (index % 1024 == 0) Token.ThrowIfCancellationRequested();
                        Skip(type, depth + 1);
                    }

                break;
            case 10:
                int child;
                while ((child = Byte()) != 0)
                {
                    Token.ThrowIfCancellationRequested();
                    SkipString();
                    Skip(child, depth + 1);
                }

                break;
            case 11: SkipArray(Int(), 4); break;
            case 12: SkipArray(Int(), 8); break;
            default: throw new InvalidDataException("未知的 NBT 标签类型.");
        }
    }

    private static int ScalarSize(int tag) => tag switch { 1 => 1, 2 => 2, 3 or 5 => 4, 4 or 6 => 8, _ => 0 };

    private void SkipArray(int count, int width)
    {
        if (count < 0 || count > (Bytes.Length - Offset) / width) throw new InvalidDataException("NBT 数组长度无效.");
        Take(count * width);
    }

    protected void SkipString() => Take(BinaryPrimitives.ReadUInt16BigEndian(Take(2)));

    protected string ReadString()
    {
        var input = Take(BinaryPrimitives.ReadUInt16BigEndian(Take(2)));
        // 修改版 UTF-8 允许分开编码空字符和代理对, 同时接受普通 UTF-8 的四字节字符.
        StringBuilder result = new(input.Length);
        int index = 0;
        while (index < input.Length)
        {
            byte first = input[index++];
            if (first < 0x80) result.Append((char)first);
            else if ((first & 0xE0) == 0xC0 && index < input.Length && (input[index] & 0xC0) == 0x80)
                result.Append((char)(((first & 31) << 6) | (input[index++] & 63)));
            else if ((first & 0xF0) == 0xE0 && index + 1 < input.Length && (input[index] & 0xC0) == 0x80 && (input[index + 1] & 0xC0) == 0x80)
                result.Append((char)(((first & 15) << 12) | ((input[index++] & 63) << 6) | (input[index++] & 63)));
            else if (Rune.DecodeFromUtf8(input[(index - 1)..], out var rune, out int consumed) == System.Buffers.OperationStatus.Done)
            {
                result.Append(rune.ToString());
                index += consumed - 1;
            }
            else result.Append('\uFFFD');
        }

        return result.ToString();
    }

    protected int Byte() => Take(1)[0];
    protected int Int() => BinaryPrimitives.ReadInt32BigEndian(Take(4));

    protected ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || count > Bytes.Length - Offset) throw new InvalidDataException("NBT 数据不完整.");
        var value = Bytes.AsSpan(Offset, count);
        Offset += count;
        return value;
    }

    protected static void WriteInt(Stream output, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        output.Write(buffer);
    }

    protected static void WriteNbtString(Stream output, string value)
    {
        int length = value.Sum(c => c is >= (char)1 and <= (char)0x7F ? 1 : c <= 0x7FF ? 2 : 3);
        if (length > ushort.MaxValue) throw new InvalidDataException("文本编码超过 NBT 字符串长度限制.");
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
