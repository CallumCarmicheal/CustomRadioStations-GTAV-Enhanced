# Custom Radio Stations for GTA V

> **Enhanced compatibility port v0.4 (2026):** forward-port of the original GPL-3.0 project for GTA V Enhanced using ScriptHookVDotNet Enhanced.

The project provides multiple local custom radio stations/wheels, track metadata, commercials and a virtual "live broadcast" timeline rather than turning the feature into a simple music player.

## Build

This source targets the **ScriptHookVDotNet3 API** supplied by ScriptHookVDotNet Enhanced. 
Put these files in `CustomRadioStations\lib`:

- `ScriptHookVDotNet3.dll` from the exact ScriptHookVDotNet Enhanced release installed in GTA V
- `irrKlang.NET4.dll` from the x64 .NET 4 irrKlang runtime used by the original mod

Then run:

```powershell
.\build-enhanced.ps1 -Configuration Release
```

The build is staged under `CustomRadioStations\dist\scripts` rather than copied into a hard-coded GTA Legacy directory.

For actual runtime playback you also need the original mod's x64 irrKlang native runtime/codecs (normally `irrKlang.dll`, `ikpMP3.dll`, and `ikpFlac.dll` as applicable) and the original UI assets such as `iconbg.png` / `iconhl.png`. They were not committed to the upstream GitHub repository and are therefore not bundled here.

## Port status

The source no longer pattern-scans or patches GTA memory and no longer depends on the deprecated SHVDN2 compatibility API. The custom-station core has been hardened against missing/malformed stations, failed audio files, null radio-wheel state, vehicle transition edge cases and optional native-wheel failures. Native wheel organization now fails open: if Enhanced cannot enumerate/lock native stations, GTA's stock station controls are left alone.