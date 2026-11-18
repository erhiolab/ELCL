param(
    [ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+$')] [string] $ReleaseTag,
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')] [string] $Repository = 'erhiolab/ELCL',
    [string] $ArtifactsDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts')
)

$ErrorActionPreference = 'Stop'
$publishTimer = [System.Diagnostics.Stopwatch]::StartNew()

function Write-ReleaseSummary {
    param(
        [Parameter(Mandatory)] [string] $Version,
        [Parameter(Mandatory)] [string] $OutputDirectory,
        [Parameter(Mandatory)] [string[]] $FileNames,
        [Parameter(Mandatory)] [TimeSpan] $Elapsed
    )

    Write-Host ''
    Write-Host 'ELCL release summary'
    Write-Host "Version: $Version"
    Write-Host 'Configuration: Release | Platform: Windows x64 | Framework: .NET 10'
    Write-Host "Output: $OutputDirectory"
    Write-Host "Completed (UTC): $([DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss'))"
    Write-Host "Elapsed: $($Elapsed.ToString('c'))"

    [long] $totalBytes = 0
    foreach ($fileName in $FileNames) {
        $file = Get-Item -LiteralPath (Join-Path $OutputDirectory $fileName)
        $sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        $sha512 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA512).Hash
        $totalBytes += $file.Length
        $requirement = if ($fileName -like '*-framework-dependent.exe') {
            'Requires .NET 10 Windows Desktop Runtime'
        } elseif ($fileName -like '*-self-contained.exe') {
            'Includes .NET runtime'
        } else {
            'Reusable .NET 10 core library'
        }

        Write-Host ''
        Write-Host "File: $($file.Name)"
        Write-Host "  Path: $($file.FullName)"
        Write-Host "  Usage: $requirement"
        Write-Host "  Size: $($file.Length) bytes ($(([double]$file.Length / 1MB).ToString('F2', [Globalization.CultureInfo]::InvariantCulture)) MiB)"
        Write-Host "  Modified (UTC): $($file.LastWriteTimeUtc.ToString('yyyy-MM-dd HH:mm:ss'))"
        Write-Host "  SHA-256: $sha256"
        Write-Host "  SHA-512: $sha512"
    }

    Write-Host ''
    Write-Host "Total: $($FileNames.Count) files, $totalBytes bytes ($(([double]$totalBytes / 1MB).ToString('F2', [Globalization.CultureInfo]::InvariantCulture)) MiB)"
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot 'src\ErhioLab.Craft.Launcher\ErhioLab.Craft.Launcher.csproj'
$coreProjectPath = Join-Path $projectRoot 'src\ErhioLab.Craft.Launcher.Core\ErhioLab.Craft.Launcher.Core.csproj'
$releasePath = Join-Path $ArtifactsDirectory 'publish'
$outputRoot = Join-Path $ArtifactsDirectory 'staging\publish\win-x64'
$coreOutputRoot = Join-Path $ArtifactsDirectory 'staging\publish\core'
$dotnetCandidates = @(
    Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    (Get-Command dotnet -ErrorAction SilentlyContinue).Source
) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -Unique
$dotnetPath = $dotnetCandidates | Where-Object {
    @(& $_ --list-sdks) -match '^10\.'
} | Select-Object -First 1

if (-not $dotnetPath) {
    throw '.NET 10 SDK was not found.'
}

$version = (& $dotnetPath msbuild $projectPath -getProperty:Version -nologo).Trim()
if ($LASTEXITCODE -ne 0 -or $version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid ELCL version: $version"
}

if ($ReleaseTag) {
    $coreVersion = (& $dotnetPath msbuild $coreProjectPath -getProperty:Version -nologo).Trim()
    if ($LASTEXITCODE -ne 0 -or $ReleaseTag -cne "v$version" -or $coreVersion -cne $version) {
        throw "Release tag and both project versions must match. Tag: $ReleaseTag; ELCL: $version; ELCLC: $coreVersion."
    }
    $changelogPath = Join-Path $projectRoot "web/docs/changelog/$ReleaseTag.md"
    if (-not (Test-Path -LiteralPath $changelogPath -PathType Leaf)) {
        throw "Missing website changelog: $changelogPath"
    }
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
Get-ChildItem -LiteralPath $outputRoot -File | Remove-Item -Force

function Publish-ElclVariant {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [bool] $SelfContained
    )

    Write-Host "Publishing $Name ..."
    $selfContainedValue = $SelfContained.ToString().ToLowerInvariant()
    & $dotnetPath publish $projectPath `
        --configuration Release `
        --runtime win-x64 `
        --output $outputRoot `
        --property:SelfContained=$selfContainedValue `
        --property:PublishSelfContained=$selfContainedValue `
        --property:PublishSingleFile=true `
        --property:IncludeNativeLibrariesForSelfExtract=true `
        --property:DebugType=none `
        --nologo

    if ($LASTEXITCODE -ne 0) {
        throw "$Name publish failed, dotnet exit code: $LASTEXITCODE"
    }

    $rawExe = Join-Path $outputRoot 'ErhioLab.Craft.Launcher.exe'
    if (-not (Test-Path -LiteralPath $rawExe -PathType Leaf)) {
        throw "$Name did not produce an EXE: $outputRoot"
    }

    $stagedExe = Join-Path $outputRoot "ELCL-$version-win-x64-$Name.exe"
    Move-Item -LiteralPath $rawExe -Destination $stagedExe -Force
    $file = Get-Item -LiteralPath $stagedExe
    Write-Host "Published: $($file.FullName) ($([Math]::Round($file.Length / 1MB, 1)) MiB)"
}

Publish-ElclVariant -Name 'framework-dependent' -SelfContained $false
Publish-ElclVariant -Name 'self-contained' -SelfContained $true

Write-Host 'Building core DLL ...'
& $dotnetPath build $coreProjectPath `
    --configuration Release `
    --output $coreOutputRoot `
    --property:DebugType=none `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "Core DLL build failed, dotnet exit code: $LASTEXITCODE"
}

$coreFileName = 'ErhioLab.Craft.Launcher.Core.dll'
$coreDll = Join-Path $coreOutputRoot $coreFileName
if (-not (Test-Path -LiteralPath $coreDll -PathType Leaf)) {
    throw "Core build did not produce a DLL: $coreDll"
}
Copy-Item -LiteralPath $coreDll -Destination (Join-Path $outputRoot $coreFileName) -Force

# 依赖可能附带原生调试符号等文件, 只校验和复制明确列出的发布产物.
$releaseFiles = @(
    "ELCL-$version-win-x64-framework-dependent.exe"
    "ELCL-$version-win-x64-self-contained.exe"
    $coreFileName
)
foreach ($fileName in $releaseFiles) {
    $source = Join-Path $outputRoot $fileName
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Missing release artifact: $source"
    }
    if ((Get-Item -LiteralPath $source).Length -eq 0) {
        throw "Release artifact is empty: $source"
    }
}

# 全部产物校验成功后再更新发布目录, 不复制 PDB 等暂存附属文件.
New-Item -ItemType Directory -Path $releasePath -Force | Out-Null
foreach ($fileName in $releaseFiles) {
    $source = Join-Path $outputRoot $fileName
    $destination = Join-Path $releasePath $fileName
    Copy-Item -LiteralPath $source -Destination $destination -Force
    Write-Host "Release artifact: $destination"
}

if ($ReleaseTag) {
    $downloadUrl = "https://github.com/$Repository/releases/download/$ReleaseTag"
    $notes = @(
        "[更新日志（中文）](https://elcl.erhio.cn/changelog/$ReleaseTag)"
        ''
        '| 文件 | SHA-256 |'
        '| --- | --- |'
    )
    $checksums = foreach ($fileName in $releaseFiles) {
        $hash = (Get-FileHash -LiteralPath (Join-Path $releasePath $fileName) -Algorithm SHA256).Hash.ToLowerInvariant()
        $notes += '| [{0}]({1}/{0}) | `{2}` |' -f $fileName, $downloadUrl, $hash
        "$hash  $fileName"
    }
    $notes += @('', "[下载 SHA256SUMS.txt]($downloadUrl/SHA256SUMS.txt)")
    $utf8 = [System.Text.UTF8Encoding]::new($false)
    $lf = [string][char]10
    [System.IO.File]::WriteAllText((Join-Path $releasePath 'SHA256SUMS.txt'), ($checksums -join $lf) + $lf, $utf8)
    [System.IO.File]::WriteAllText((Join-Path $releasePath 'release-notes.md'), ($notes -join $lf) + $lf, $utf8)
}

$publishTimer.Stop()
Write-ReleaseSummary -Version $version -OutputDirectory $releasePath -FileNames $releaseFiles -Elapsed $publishTimer.Elapsed
