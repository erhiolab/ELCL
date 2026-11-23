using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

using ErhioLab.Craft.Launcher.Core;
using ErhioLab.Craft.Launcher.Core.Minecraft;

namespace ErhioLab.Craft.Launcher.Launching;

internal sealed record GameCrashReport(
    string VersionName,
    string? MinecraftVersion,
    string? JavaVersion,
    int ProcessId,
    int ExitCode,
    bool BeforeWindow,
    DateTimeOffset ExitedAt,
    string LogPath)
{
    private const int AnalysisTailBytes = 512 * 1024;
    internal MinecraftCrashDiagnosis? Diagnosis { get; init; }
    internal IReadOnlyList<MinecraftCrashModKeyword> ModKeywords { get; init; } = [];

    internal string SuggestedFileName => $"ELCL-Minecraft-error-{ExitedAt:yyyyMMdd-HHmmss}-{ProcessId}.zip";

    internal Task ExportAsync(string destination) => Task.Run(() => WriteArchive(destination));

    internal Task<GameCrashReport> AnalyzeAsync() => Task.Run(() =>
    {
        try
        {
            using var stream = OpenLog();
            if (stream is null) return this;
            long length = stream.Length;
            stream.Position = Math.Max(0, length - AnalysisTailBytes);
            var bytes = new byte[(int)(length - stream.Position)];
            int count = stream.ReadAtLeast(bytes, bytes.Length, false);
            int first = 0;
            if (length > AnalysisTailBytes)
            {
                int newline = bytes.AsSpan(0, count).IndexOf((byte)'\n');
                if (newline < 0) return this;
                first = newline + 1;
            }
            string log = Encoding.UTF8.GetString(bytes, first, count - first);
            var diagnosis = MinecraftCrashAnalyzer.Analyze(log);
            return this with
            {
                Diagnosis = diagnosis,
                ModKeywords = diagnosis?.Kind is null or MinecraftCrashKind.ModDependencyConflict or MinecraftCrashKind.MixinFailed
                    ? MinecraftCrashAnalyzer.FindModKeywords(log) : []
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            App.Logger.Error("CrashReport", $"读取实例日志作本地分析失败: PID={ProcessId}.", error);
            return this;
        }
    });

    private void WriteArchive(string destination)
    {
        destination = Path.GetFullPath(destination);
        if (string.Equals(destination, Path.GetFullPath(LogPath), StringComparison.OrdinalIgnoreCase)) throw new IOException("错误报告不能覆盖原始游戏日志.");

        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".elcl-crash-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
            {
                using var log = OpenLog();
                if (log is not null)
                {
                    using var entry = archive.CreateEntry("game.log", CompressionLevel.Fastest).Open();
                    log.CopyTo(entry);
                }

                using var metadata = archive.CreateEntry("report.json", CompressionLevel.Fastest).Open();
                JsonSerializer.Serialize(metadata, new
                {
                    Launcher = LauncherHttpUserAgent.Value,
                    CoreVersion = CoreInfo.Version,
                    VersionName,
                    MinecraftVersion,
                    JavaVersion,
                    ProcessId,
                    ExitCode,
                    BeforeWindow,
                    ExitedAt,
                    Analysis = Diagnosis is not null || ModKeywords.Count > 0 ? new
                    {
                        Kind = Diagnosis?.Kind.ToString(),
                        Diagnosis?.RequiredJavaMajorVersion,
                        ModKeywords = ModKeywords.Select(keyword => new { keyword.Value, Kind = keyword.Kind.ToString() })
                    } : null,
                    GameLog = log is null ? null : "game.log"
                }, JsonSerializerOptions.Web);
            }

            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                App.Logger.Warning("CrashReport", $"无法清理临时报告: {error.Message}");
            }
        }
    }

    private FileStream? OpenLog()
    {
        try
        {
            return new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }
}

internal sealed class GameProcessExitException(string message, GameCrashReport report) : Exception(message)
{
    internal GameCrashReport Report => report;
}
