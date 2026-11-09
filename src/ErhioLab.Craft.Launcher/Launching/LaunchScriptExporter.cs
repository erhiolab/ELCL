using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

using ErhioLab.Craft.Launcher.Core.Minecraft;
using ErhioLab.Craft.Launcher.Localization;
using ErhioLab.Craft.Launcher.Settings;

namespace ErhioLab.Craft.Launcher.Launching;

/// <summary>导出包含启动数据的单个批处理文件, 不执行命令或记录完整参数.</summary>
internal static class LaunchScriptExporter
{
    internal static async Task WriteAsync(string path, string supportDirectory, ProcessStartInfo start, LauncherSettings settings,
        MinecraftLaunchPlaceholderContext context, CancellationToken token)
    {
        string command = MinecraftLaunchPlaceholders.Expand(settings.PreLaunchCommand, context);
        if (MinecraftLaunchPlaceholders.GetUnresolvedTokens(command).Count > 0) throw new InvalidOperationException(LocalizationManager.Get("gameLaunch.unknownCommand"));
        Directory.CreateDirectory(supportDirectory);
        var data = new
        {
            Executable = start.FileName, Arguments = string.Join(" ", start.ArgumentList.Select(QuoteArgument)),
            start.WorkingDirectory, PreLaunchCommand = command, settings.WaitForPreLaunchCommand,
            Priority = settings.GamePriority switch { GameProcessPriority.High => "AboveNormal", GameProcessPriority.Low => "BelowNormal", _ => "Normal" },
            Title = MinecraftLaunchPlaceholders.Expand(settings.GameWindowTitle, context),
            Maximized = settings.WindowMode == GameWindowMode.Maximized
        };
        // 参数放在批处理文件内的 Base64 数据段, 避开 cmd 的长度/转义限制, 不生成附属脚本.
        string payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(data));
        var batch = new StringBuilder("@echo off\r\nsetlocal DisableDelayedExpansion\r\nset \"ELCL_SCRIPT_FILE=%~f0\"\r\n");
        batch.AppendLine("\"%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoLogo -NoProfile -ExecutionPolicy Bypass -Command \"$s=[IO.File]::ReadAllText($env:ELCL_SCRIPT_FILE,[Text.Encoding]::UTF8);$m=':ELCL_'+'POWERSHELL';& ([ScriptBlock]::Create($s.Substring($s.IndexOf($m)+$m.Length)))\"");
        batch.Append("exit /b %errorlevel%\r\n:ELCL_DATA\r\n");
        for (int offset = 0; offset < payload.Length; offset += 120) batch.Append(payload.AsSpan(offset, Math.Min(120, payload.Length - offset))).Append("\r\n");
        batch.Append(":ELCL_POWERSHELL\r\n").Append(PowerShellScript);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, batch.ToString(), new UTF8Encoding(false), token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally
        {
            File.Delete(temporary);
        }

        App.Logger.Information("Launch", $"单文件启动脚本已导出: {path}, 本地库目录={supportDirectory}. 未执行启动命令或游戏进程.");
    }

    // Windows CommandLineToArgvW/Java 的反斜杠和引号规则; 参数经内嵌数据传递.
    internal static string QuoteArgument(string value)
    {
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\')
            {
                slashes++;
                continue;
            }

            if (character == '"') result.Append('\\', slashes * 2 + 1).Append('"');
            else result.Append('\\', slashes).Append(character);
            slashes = 0;
        }

        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    private const string PowerShellScript = """
                                            $ErrorActionPreference = 'Stop'
                                            try {
                                                $source = [IO.File]::ReadAllText($env:ELCL_SCRIPT_FILE, [Text.Encoding]::UTF8)
                                                $dataMarker = ':ELCL_' + 'DATA'
                                                $codeMarker = ':ELCL_' + 'POWERSHELL'
                                                $payload = $source.Substring($source.IndexOf($dataMarker) + $dataMarker.Length)
                                                $payload = $payload.Substring(0, $payload.IndexOf($codeMarker)).Trim()
                                                $launch = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
                                                if (-not [string]::IsNullOrWhiteSpace($launch.PreLaunchCommand)) {
                                                    $before = New-Object System.Diagnostics.ProcessStartInfo
                                                    $before.FileName = $env:ComSpec
                                                    $before.Arguments = '/D /S /C "' + $launch.PreLaunchCommand + '"'
                                                    $before.WorkingDirectory = $launch.WorkingDirectory
                                                    $before.UseShellExecute = $false
                                                    $command = [System.Diagnostics.Process]::Start($before)
                                                    if ($launch.WaitForPreLaunchCommand) {
                                                        $command.WaitForExit()
                                                        if ($command.ExitCode -ne 0) { exit $command.ExitCode }
                                                    }
                                                }
                                                $start = New-Object System.Diagnostics.ProcessStartInfo
                                                $start.FileName = $launch.Executable
                                                $start.Arguments = $launch.Arguments
                                                $start.WorkingDirectory = $launch.WorkingDirectory
                                                $start.UseShellExecute = $false
                                                $game = [System.Diagnostics.Process]::Start($start)
                                                try { $game.PriorityClass = [Enum]::Parse([System.Diagnostics.ProcessPriorityClass], $launch.Priority) } catch { }
                                                if ($launch.Maximized -or -not [string]::IsNullOrWhiteSpace($launch.Title)) {
                                                    Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class ElclWindow { [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool SetWindowText(IntPtr window, string title); [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr window, int command); }'
                                                    $deadline = [DateTime]::UtcNow.AddSeconds(60)
                                                    while (-not $game.HasExited -and [DateTime]::UtcNow -lt $deadline) {
                                                        $game.Refresh()
                                                        if ($game.MainWindowHandle -ne [IntPtr]::Zero) {
                                                            if ($launch.Title) { [void][ElclWindow]::SetWindowText($game.MainWindowHandle, $launch.Title) }
                                                            if ($launch.Maximized) { [void][ElclWindow]::ShowWindowAsync($game.MainWindowHandle, 3) }
                                                            break
                                                        }
                                                        Start-Sleep -Milliseconds 250
                                                    }
                                                }
                                                $game.WaitForExit()
                                                exit $game.ExitCode
                                            } catch {
                                                Write-Error $_
                                                exit 1
                                            }
                                            """;
}
