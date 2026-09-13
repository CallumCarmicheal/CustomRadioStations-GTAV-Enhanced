param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release',
    [string]$SHVDN3Path = (Join-Path $PSScriptRoot 'CustomRadioStations\lib\ScriptHookVDotNet3.dll')
)

$ErrorActionPreference = 'Stop'

if (!(Test-Path $SHVDN3Path)) {
    throw "Missing ScriptHookVDotNet3.dll: $SHVDN3Path`nCopy it from the SAME ScriptHookVDotNet Enhanced release installed in GTA V."
}

$msbuild = Get-Command msbuild.exe -ErrorAction SilentlyContinue
if (!$msbuild) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $msbuildPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if ($msbuildPath) { $msbuild = Get-Item $msbuildPath }
    }
}
if (!$msbuild) {
    throw 'MSBuild was not found. Install Visual Studio 2022 or Build Tools with .NET Framework 4.8 targeting support.'
}

$project = Join-Path $PSScriptRoot 'CustomRadioStations\CustomRadioStations.csproj'
$analyzerProject = Join-Path $PSScriptRoot 'CustomRadioStations.Analyzer\CustomRadioStations.Analyzer.csproj'
$distRoot = Join-Path $PSScriptRoot 'CustomRadioStations\dist'
$stage = Join-Path $distRoot 'scripts'
$toolStage = Join-Path $distRoot 'tools\CustomRadioStations.Analyzer'

# Avoid leaving proprietary/stale runtime DLLs from an older audio backend in dist.
if (Test-Path $distRoot) {
    Remove-Item $distRoot -Recurse -Force
}

& $msbuild.FullName $project /restore /m /t:Rebuild "/p:Configuration=$Configuration" "/p:SHVDN3Path=$SHVDN3Path"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$requiredOutputs = @(
    'CustomRadioStations.dll',
    'Newtonsoft.Json.dll',
    'TagLibSharp.dll',
    'MiniAudioExNET.dll',
    'miniaudioex.dll'
)
foreach ($name in $requiredOutputs) {
    $path = Join-Path $stage $name
    if (!(Test-Path $path)) {
        throw "Build succeeded but required staged output is missing: $path"
    }
}

& $msbuild.FullName $analyzerProject /restore /m /t:Rebuild "/p:Configuration=$Configuration" "/p:SHVDN3Path=$SHVDN3Path"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$analyzerOutput = Join-Path $PSScriptRoot "CustomRadioStations.Analyzer\bin\$Configuration"
New-Item -ItemType Directory -Path $toolStage -Force | Out-Null
Get-ChildItem $analyzerOutput -File | Copy-Item -Destination $toolStage -Force

$requiredToolOutputs = @(
    'CustomRadioStations.Analyzer.exe',
    'CustomRadioStations.dll',
    'Newtonsoft.Json.dll',
    'TagLibSharp.dll',
    'Spectre.Console.dll'
)
foreach ($name in $requiredToolOutputs) {
    $path = Join-Path $toolStage $name
    if (!(Test-Path $path)) {
        throw "Analyzer build succeeded but required staged output is missing: $path"
    }
}

Write-Host "`nGame build staged to:"
Write-Host $stage
Write-Host "`nAudio analyzer staged to:"
Write-Host $toolStage
Write-Host "`nGame audio backend: JAJ.Packages.MiniAudioEx 3.3.6 (NuGet, MIT)"
Write-Host "Analyzer: FFmpeg is external and must be beside the analyzer, on PATH, or supplied with --ffmpeg."
