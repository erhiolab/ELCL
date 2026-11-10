using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ErhioLab.Craft.Launcher.Launching;

/// <summary>已脱敏的日志事件. 显示消息与原始记录分开, 以便阅读和排查.</summary>
internal sealed record GameLogLine(string Message, string Level, string Time, string Thread, string Logger, string RawText)
{
    public string LevelLabel => Level switch { "Warning" => "WARN", "Error" => "ERROR", "Debug" => "DEBUG", _ => "INFO" };
    public string Text => $"{Time} [{LevelLabel}]{(Thread.Length == 0 ? "" : " [" + Thread + "]")} {Message}";
    public string Source => string.Join(Environment.NewLine, new[] { Thread, Logger }.Where(value => value.Length > 0));
}

/// <summary>将按行保存的 Log4j XML 与普通文本转换成事件, 分别维护输出和错误流的未完成片段.</summary>
internal sealed partial class GameLogParser
{
    private const int MaximumEventLength = 256 * 1024;
    private readonly Dictionary<string, Frame> _frames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GameLogLine> _last = new(StringComparer.Ordinal);

    private sealed class Frame(string time)
    {
        internal string Time { get; } = time;
        internal StringBuilder Xml { get; } = new();
        internal StringBuilder Raw { get; } = new();
    }

    internal IReadOnlyList<GameLogLine> Parse(IReadOnlyList<string> lines, bool reset = false)
    {
        List<GameLogLine> result = [];
        if (reset)
        {
            result.AddRange(Flush());
            _last.Clear();
        }

        foreach (string raw in lines)
        {
            Match envelope = Envelope().Match(raw);
            string channel = envelope.Success ? envelope.Groups["channel"].Value : "OUT";
            string body = envelope.Success ? envelope.Groups["body"].Value : raw;
            string time = envelope.Success && DateTimeOffset.TryParse(envelope.Groups["time"].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)
                : "";
            body = AnsiSequence().Replace(body, "");
            bool startsEvent = body.TrimStart().StartsWith("<log4j:Event", StringComparison.Ordinal) || body.TrimStart().StartsWith("<Event", StringComparison.Ordinal);
            if (startsEvent)
            {
                if (_frames.Remove(channel, out var interrupted)) result.Add(Plain(interrupted.Xml.ToString(), interrupted.Time, interrupted.Raw.ToString()));
                _frames[channel] = new Frame(time);
            }

            if (_frames.TryGetValue(channel, out var frame))
            {
                frame.Xml.AppendLine(body);
                frame.Raw.AppendLine(raw);
                if (body.Contains("</log4j:Event>", StringComparison.Ordinal) || body.Contains("</Event>", StringComparison.Ordinal))
                {
                    var entry = XmlEvent(frame);
                    result.Add(entry);
                    _last[channel] = entry;
                    _frames.Remove(channel);
                }
                else if (frame.Xml.Length >= MaximumEventLength)
                {
                    result.Add(Plain(frame.Xml.ToString(), frame.Time, frame.Raw.ToString()));
                    _frames.Remove(channel);
                }

                continue;
            }

            // 从大文件尾部打开时可能遇到没有开头的 XML 子元素, 仍提取可读消息.
            if (body.Trim() is "</log4j:Event>" or "</Event>") continue;
            if (body.Contains("<log4j:Message>", StringComparison.Ordinal) || body.Contains("<log4j:Throwable>", StringComparison.Ordinal))
            {
                try
                {
                    var root = ReadXml(body);
                    body = root.Elements().FirstOrDefault()?.Value ?? body;
                }
                catch (XmlException)
                {
                }
            }

            var line = Plain(body, time, raw);
            string trimmed = body.TrimStart();
            if (_last.TryGetValue(channel, out var previous) && (trimmed.StartsWith("at ", StringComparison.Ordinal) || trimmed.StartsWith("Caused by:", StringComparison.Ordinal)))
                line = line with { Level = previous.Level, Thread = previous.Thread, Logger = previous.Logger };
            else _last[channel] = line;
            result.Add(line);
        }

        return result;
    }

    internal IReadOnlyList<GameLogLine> Flush()
    {
        var entries = _frames.Values.Select(frame => Plain(frame.Xml.ToString(), frame.Time, frame.Raw.ToString())).ToArray();
        _frames.Clear();
        return entries;
    }

    private static GameLogLine XmlEvent(Frame frame)
    {
        try
        {
            var entry = ReadXml(frame.Xml.ToString()).Elements().Single();
            string message = entry.Elements().FirstOrDefault(value => value.Name.LocalName == "Message")?.Value ?? "";
            string? throwable = entry.Elements().FirstOrDefault(value => value.Name.LocalName == "Throwable")?.Value;
            if (!string.IsNullOrEmpty(throwable)) message += (message.Length == 0 ? "" : Environment.NewLine) + throwable;
            string time = frame.Time;
            if (long.TryParse((string?)entry.Attribute("timestamp"), CultureInfo.InvariantCulture, out long milliseconds))
            {
                try
                {
                    time = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                }
                catch (ArgumentOutOfRangeException)
                {
                }
            }

            return new GameLogLine(message, Level((string?)entry.Attribute("level")), time,
                (string?)entry.Attribute("thread") ?? "", (string?)entry.Attribute("logger") ?? "", frame.Raw.ToString().TrimEnd());
        }
        catch (Exception error) when (error is XmlException or InvalidOperationException)
        {
            return Plain(frame.Xml.ToString(), frame.Time, frame.Raw.ToString());
        }
    }

    private static XElement ReadXml(string xml)
    {
        using var input = new System.IO.StringReader("<events xmlns:log4j=\"http://jakarta.apache.org/log4j/\">" + xml + "</events>");
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumEventLength * 2
        });
        return XElement.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static GameLogLine Plain(string body, string time, string raw)
    {
        Match match = PlainHeader().Match(body);
        if (match.Success)
        {
            if (match.Groups["time"].Success) time = match.Groups["time"].Value;
            return new GameLogLine(body[match.Length..], Level(match.Groups["level"].Value), time, match.Groups["thread"].Value, "", raw);
        }

        Match level = Severity().Match(body);
        return new GameLogLine(body.TrimEnd(), Level(level.Value), time, "", "", raw);
    }

    private static string Level(string? value) => value?.ToUpperInvariant() switch
    {
        "WARN" or "WARNING" => "Warning", "ERROR" or "FATAL" or "EXCEPTION" => "Error", "DEBUG" or "TRACE" => "Debug", _ => "Info"
    };

    [GeneratedRegex(@"^(?<time>\d{4}-\d{2}-\d{2}T\S+) \[(?<channel>OUT|ERR)\] (?<body>.*)$")]
    private static partial Regex Envelope();

    [GeneratedRegex(@"^\s*(?:\[(?<time>\d{2}:\d{2}:\d{2}(?:[.,]\d+)?)\]\s*)?\[(?<thread>[^\]\r\n]+)/(?<level>TRACE|DEBUG|INFO|WARN|ERROR|FATAL)\]:?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex PlainHeader();

    [GeneratedRegex(@"\b(ERROR|FATAL|WARN|WARNING|DEBUG|TRACE|Exception)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Severity();

    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]")]
    private static partial Regex AnsiSequence();
}
