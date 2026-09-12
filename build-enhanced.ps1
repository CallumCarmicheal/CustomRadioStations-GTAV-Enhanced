param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release',
    [string]$SHVDN2Path = (Join-Path $PSScriptRoot 'CustomRadioStations\lib\ScriptHookVDotNet2.dll'),
    [string]$IrrKlangPath = (Join-Path $PSScriptRoot 'CustomRadioStations\lib\irrKlang.NET4.dll')
)

$ErrorActionPreference = 'Stop'

if (!(Test-Path $SHVDN2Path)) {
    throw "Missing ScriptHookVDotNet2.dll: $SHVDN2Path`nCopy it from the SAME ScriptHookVDotNet Enhanced release installed in GTA V."
}
if (!(Test-Path $IrrKlangPath)) {
    throw "Missing irrKlang.NET4.dll: $IrrKlangPath`nCopy the x64 .NET 4 irrKlang assembly used by the original mod."
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
& $msbuild.FullName $project /m /t:Rebuild "/p:Configuration=$Configuration" "/p:SHVDN2Path=$SHVDN2Path" "/p:IrrKlangPath=$IrrKlangPath"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$stage = Join-Path $PSScriptRoot 'CustomRadioStations\dist\scripts'
$lib = Join-Path $PSScriptRoot 'CustomRadioStations\lib'
foreach ($runtimeName in @('irrKlang.dll','ikpMP3.dll','ikpFlac.dll')) {
    $runtimeFile = Join-Path $lib $runtimeName
    if (Test-Path $runtimeFile) {
        Copy-Item $runtimeFile $stage -Force
    } else {
        Write-Warning "$runtimeName was not found in lib; the original x64 runtime/plugin may still be required in GTA's scripts directory."
    }
}

Write-Host "`nBuild staged to:"
Write-Host $stage
