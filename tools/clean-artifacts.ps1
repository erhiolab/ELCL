#requires -Version 5.1
<#
.SYNOPSIS
Previews or removes disposable ELCL build artifacts from this checkout.
.PARAMETER Apply
Permanently removes eligible directories. Omit this switch to preview.
.EXAMPLE
.\tools\clean-artifacts.ps1
.EXAMPLE
.\tools\clean-artifacts.ps1 -Apply
#>
[CmdletBinding(SupportsShouldProcess)]
param([switch] $Apply)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$root = Join-Path $repo 'artifacts'
if (-not (Test-Path -LiteralPath (Join-Path $repo 'ErhioLab.Craft.Launcher.slnx') -PathType Leaf)) {
    throw "Cannot locate the ELCL checkout: $repo"
}
if (-not (Test-Path -LiteralPath $root)) {
    Write-Host 'No artifacts directory to clean.'
    return
}

$rootItem = Get-Item -LiteralPath $root -Force
$rootPrefix = $rootItem.FullName.TrimEnd('\') + '\'
$protectedPaths = @(
    'mcmod-index',
    'build\ErhioLab.Craft.Launcher',
    'build\ErhioLab.Craft.Launcher.Core',
    'obj\ErhioLab.Craft.Launcher',
    'obj\ErhioLab.Craft.Launcher.Core',
    'publish'
) | ForEach-Object { Join-Path $rootItem.FullName $_ }

# 拒绝根目录及上级路径中的重解析点, 避免误入其他目录.
for ($parent = $rootItem; $null -ne $parent; $parent = $parent.Parent) {
    if ($parent -isnot [IO.DirectoryInfo] -or ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Unsafe artifacts root or ancestor: $($parent.FullName)"
    }
}

function Get-ArtifactDirectory([string] $RelativePath) {
    $path = $rootItem.FullName
    foreach ($segment in ($RelativePath -split '\\')) {
        if (-not $segment -or $segment -in '.', '..' -or $segment -match '[:/]') {
            throw "Invalid artifact path: $RelativePath"
        }
        $path = Join-Path $path $segment
        if (-not (Test-Path -LiteralPath $path)) { return }
        $item = Get-Item -LiteralPath $path -Force
        if ($item -isnot [IO.DirectoryInfo] -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            Write-Warning "Skipped non-directory or link: $path"
            return
        }
    }
    if (-not $item.FullName.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Artifact path escaped the checkout: $($item.FullName)"
    }
    $item
}

function Test-ProtectedPath([string] $Path) {
    foreach ($protected in $protectedPaths) {
        if ($Path -eq $protected -or $Path.StartsWith($protected + '\', [StringComparison]::OrdinalIgnoreCase) -or
            $protected.StartsWith($Path + '\', [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Measure-DisposableTree([IO.DirectoryInfo] $Directory, [datetime] $Cutoff) {
    # 不跟随子目录链接, 发现游戏数据/数据库或非生成源码时整项跳过.
    $pending = New-Object 'System.Collections.Generic.Stack[IO.DirectoryInfo]'
    $pending.Push($Directory)
    [long] $bytes = 0
    while ($pending.Count -gt 0) {
        $current = $pending.Pop()
        foreach ($entry in (Get-ChildItem -LiteralPath $current.FullName -Force)) {
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
                $entry.Name -in '.git', '.minecraft', 'ELCL', 'saves', 'level.dat', 'level.dat_old' -or
                $entry.Name -match '\.(?:sqlite3?|db)(?:-wal|-shm|-journal)?$') {
                return @{ Skip = "protected data or link: $($entry.Name)" }
            }
            if ($entry.LastWriteTimeUtc -ge $Cutoff) { return @{ Skip = 'modified in the last 15 minutes' } }
            if ($entry -is [IO.DirectoryInfo]) {
                $pending.Push($entry)
            } else {
                if ($entry.Name -match '\.(?:slnx?|csproj|ps1|py|xaml)$' -or
                    ($entry.Extension -eq '.cs' -and $entry.Name -notmatch '(?:\.g(?:\.i)?|\.AssemblyInfo|\.AssemblyAttributes)\.cs$')) {
                    return @{ Skip = "source file: $($entry.Name)" }
                }
                $bytes += $entry.Length
            }
        }
    }
    return @{ Bytes = $bytes }
}

# 只匹配已知测试项目的输出目录, 不根据 Check 等通用后缀推断.
$testProjects = @(
    'AnalyticsVersionCheck', 'AssociationCheck', 'BackgroundCheck', 'BackgroundWindowMoveCheck', 'Check',
    'CommunityDownloadsCheck', 'Example0', 'Example1', 'Example2', 'Example3', 'ExportCheck', 'ExportWheelCheck',
    'FakeJava', 'FeatureVisibilityCheck', 'GameCrashCheck', 'JavaCatalogCheck', 'JavaIgnoredCheck', 'LocalPacksCheck',
    'ModsPaginationCheck', 'ModsResetCheck', 'Preview', 'WallpaperCheck', 'WallpaperLayoutCheck',
    'WorldDataPackDocsCheck', 'WorldDocsCheck', 'WorldManagementCheck', 'WorldQuickPlayDocsCheck'
)
$candidates = @(
    'staging\publish\core', 'staging\publish\win-x64',
    'mod-prefetch-check-bin', 'mod-prefetch-verification', 'mod-scroll-check-bin', 'mod-scroll-verification',
    'verification\analytics-version-build', 'verification\wallpaper-build',
    'verification\file-association\launcher', 'verification\git-commit\launcher', 'verification\java-catalog\launcher',
    'verification\java-ignored\launcher', 'verification\launcher-background\build', 'verification\licenses\launcher',
    'verification\modpack-export\launcher', 'verification\puzzle-icon\launcher', 'verification\rider-inspections\build',
    'verification\task-button\launcher',
    'verify\download-recovery', 'verify\feature-visibility', 'verify\launcher-log-viewer', 'verify\navigation-gallery',
    'verify\startup-consent', 'verify\wallpaper-background-toggle', 'verify\wallpaper-filters',
    'verify\wallpaper-playback', 'verify\wallpaper-title'
)
foreach ($check in @('download-recovery-check', 'launcher-log-check', 'startup-consent-check', 'wallpaper-catalog-check')) {
    $candidates += "verify\$check\bin", "verify\$check\obj"
}
foreach ($output in @('build', 'obj')) {
    $outputDirectory = Get-ArtifactDirectory $output
    if ($null -eq $outputDirectory) { continue }
    foreach ($child in (Get-ChildItem -LiteralPath $outputDirectory.FullName -Force -Directory)) {
        if ($child.Name -match '^ErhioLab\.Craft\.Launcher_[0-9a-z]{8}_wpftmp$' -or $child.Name -in $testProjects) {
            $candidates += "$output\$($child.Name)"
        }
    }
}

$cutoff = [DateTime]::UtcNow.AddMinutes(-15)
$eligible = 0
$removed = 0
$skipped = 0
$failed = 0
[long] $eligibleBytes = 0
[long] $removedBytes = 0
foreach ($relative in ($candidates | Sort-Object -Unique)) {
    try {
        $directory = Get-ArtifactDirectory $relative
        if ($null -eq $directory) { continue }
        if (Test-ProtectedPath $directory.FullName) { throw "Refusing to touch protected path: $($directory.FullName)" }
        $result = if ($directory.LastWriteTimeUtc -ge $cutoff) {
            @{ Skip = 'modified in the last 15 minutes' }
        } else {
            Measure-DisposableTree $directory $cutoff
        }
        if ($result.Skip) {
            $skipped++
            Write-Host "[KEEP] $relative ($($result.Skip))"
            continue
        }
        $eligible++
        $eligibleBytes += $result.Bytes
        if (-not $Apply) {
            Write-Host ("[PREVIEW] {0} ({1:N2} MiB)" -f $relative, ($result.Bytes / 1MB))
        } elseif ($PSCmdlet.ShouldProcess($directory.FullName, 'Permanently remove generated artifacts')) {
            Remove-Item -LiteralPath $directory.FullName -Recurse -Force -Confirm:$false
            $removed++
            $removedBytes += $result.Bytes
            Write-Host "[REMOVED] $relative"
        }
    } catch {
        $failed++
        Write-Warning "${relative}: $($_.Exception.Message)"
    }
}

Write-Host ("Eligible: {0} ({1:N2} MiB); removed: {2} ({3:N2} MiB); kept by guards: {4}; failed: {5}." -f
    $eligible, ($eligibleBytes / 1MB), $removed, ($removedBytes / 1MB), $skipped, $failed)
if (-not $Apply) { Write-Host 'Preview only. Re-run with -Apply after closing any builds or tests.' }
if ($failed -gt 0) { throw "$failed artifact path(s) could not be processed." }
