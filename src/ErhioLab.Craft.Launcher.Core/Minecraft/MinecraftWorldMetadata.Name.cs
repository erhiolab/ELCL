namespace ErhioLab.Craft.Launcher.Core.Minecraft;

internal sealed partial class MinecraftWorldMetadata
{
    internal static Action<Stream>? OpenNameEdit(string path, string value, CancellationToken token)
    {
        var reader = Open(path, token);
        reader.FindCompound("Data");
        int start = -1, end = -1;
        string? current = null;
        int tag;
        while ((tag = reader.Byte()) != 0)
        {
            token.ThrowIfCancellationRequested();
            string name = reader.ReadString();
            if (name != "LevelName")
            {
                reader.Skip(tag, 1);
                continue;
            }

            if (tag != 8 || start >= 0) throw new InvalidDataException("世界名称标签的类型无效或重复.");
            start = reader.Offset;
            current = reader.ReadString();
            end = reader.Offset;
        }

        if (current == value) return null;
        bool insert = start < 0;
        if (insert) start = end = reader.Offset - 1;
        return output => reader.WriteName(output, value, start, end, insert);
    }

    private void WriteName(Stream output, string value, int start, int end, bool insert)
    {
        output.Write(Bytes.AsSpan(0, start));
        if (insert)
        {
            output.WriteByte(8);
            WriteNbtString(output, "LevelName");
        }

        WriteNbtString(output, value);
        output.Write(Bytes.AsSpan(end));
    }
}
