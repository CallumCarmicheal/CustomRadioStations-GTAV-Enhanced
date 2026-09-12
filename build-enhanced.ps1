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
$stage = Join-Path $PSScriptRoot 'CustomRadioStations\dist\scripts'

# Avoid leaving proprietary/stale runtime DLLs from an older irrKlang build in dist.
if (Test-Path $stage) {
    Remove-Item $stage -Recurse -Force
}

& $msbuild.FullName $project /restore /m /t:Rebuild "/p:Configuration=$Configuration" "/p:SHVDN3Path=$SHVDN3Path"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$requiredOutputs = @(
    'CustomRadioStations.dll',
    'Newtonsoft.Json.dll',
    'MiniAudioExNET.dll',
    'miniaudioex.dll'
)
foreach ($name in $requiredOutputs) {
    $path = Join-Path $stage $name
    if (!(Test-Path $path)) {
        throw "Build succeeded but required staged output is missing: $path"
    }
}

Write-Host "`nBuild staged to:"
Write-Host $stage
Write-Host "`nAudio backend: JAJ.Packages.MiniAudioEx 3.3.6 (NuGet, MIT)"
