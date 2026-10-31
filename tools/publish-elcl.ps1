$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot 'src\ErhioLab.Craft.Launcher\ErhioLab.Craft.Launcher.csproj'
$releasePath = Join-Path $projectRoot 'artifacts\publish'
$outputRoot = Join-Path $projectRoot 'artifacts\staging\publish\win-x64'
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

# 两种模式都发布成功后, 将带版本号的单文件 EXE 放到仓库外层的发布目录.
$stagedFiles = @(Get-ChildItem -LiteralPath $outputRoot -File)
if ($stagedFiles.Count -ne 2) {
    throw "Staging must contain exactly two EXEs: $outputRoot"
}

New-Item -ItemType Directory -Path $releasePath -Force | Out-Null
foreach ($name in 'framework-dependent', 'self-contained') {
    $fileName = "ELCL-$version-win-x64-$name.exe"
    $source = Join-Path $outputRoot $fileName
    $destination = Join-Path $releasePath $fileName
    Copy-Item -LiteralPath $source -Destination $destination -Force
    Write-Host "Release EXE: $destination"
}
