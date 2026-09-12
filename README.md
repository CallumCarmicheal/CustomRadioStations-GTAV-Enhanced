# Custom Radio Stations for GTA V

> **Enhanced compatibility port v0.5 (2026):** forward-port of the original GPL-3.0 project for GTA V Enhanced using ScriptHookVDotNet Enhanced and the ScriptHookVDotNet3 API. v0.5 replaces the proprietary irrKlang playback backend with the open-source MiniAudioEx/miniaudio stack.

The project provides multiple local custom radio stations/wheels, track metadata, commercials and a virtual "live broadcast" timeline rather than turning the feature into a simple music player.

## Build

This source targets **.NET Framework 4.8 / x64** and the **ScriptHookVDotNet3 API** supplied by ScriptHookVDotNet Enhanced.

Put only this manual dependency in `CustomRadioStations\lib`:

- `ScriptHookVDotNet3.dll` from the exact ScriptHookVDotNet Enhanced release installed in GTA V

Then run:

```powershell
.\build-enhanced.ps1 -Configuration Release
```

MSBuild restores `JAJ.Packages.MiniAudioEx` **3.3.6** from NuGet. The build stages the following under `CustomRadioStations\dist\scripts`:

- `CustomRadioStations.dll`
- `CustomRadioStations.pdb` when generated
- `MiniAudioExNET.dll`
- `miniaudioex.dll` (Windows x64 native runtime)

No irrKlang files or codec plugins are required anymore. MP3, FLAC, WAV and OGG decoding/streaming are supplied by MiniAudioEx/miniaudio.

The original UI assets such as `iconbg.png` / `iconhl.png` are still required if your original CRS distribution uses them; those assets were not committed to the upstream GitHub repository.

## Source layout

- `Audio` - MiniAudioEx integration, playback state and track metadata
- `Configuration` - global/wheel settings and INI parsing
- `Game` - ScriptHookVDotNet input, events, native radio calls and vehicle state
- `Infrastructure` - runtime paths, logging and process-lifetime state
- `Radio` - station catalog discovery, playlists and wheel/station associations
- `Scripts` - the two SHVDN script entry points
- `UI` - selector-wheel rendering and shared wheel state
- `Utilities` - general and GTA math helpers

`RadioCatalogLoader` owns filesystem discovery. It recognizes MP3, FLAC, WAV and OGG files plus Windows shortcuts, builds wheels in deterministic folder order, and leaves gameplay event handling to `MainScript`.

## Port status

The source no longer pattern-scans or patches GTA memory, no longer depends on the deprecated SHVDN2 compatibility API, and no longer depends on proprietary irrKlang. The custom-station core has been hardened against missing/malformed stations, failed audio files, null radio-wheel state, vehicle transition edge cases and optional native-wheel failures.

The MiniAudioEx backend preserves the old station-facing millisecond API while internally converting to MiniAudioEx PCM cursors. Pause/resume, seek, station continuity, looping, per-sound volume and global volume are retained. `AudioContext.Update()` is driven from the existing game tick.