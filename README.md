# Custom Radio Stations for GTA V

> **Enhanced compatibility port v0.6 (2026):** GTA V Enhanced, ScriptHookVDotNet Enhanced, the ScriptHookVDotNet3 API, .NET Framework 4.8, and MiniAudioEx 3.3.6.

Custom Radio Stations provides multiple local radio wheels, track metadata, separate commercial breaks, and a virtual live-broadcast timeline. Returning to a broadcast station advances it by the time that elapsed, like a radio broadcast, instead of treating it as a paused music player.

## Build

Place `ScriptHookVDotNet3.dll` from the exact installed ScriptHookVDotNet Enhanced release in `CustomRadioStations\lib`, then run:

```powershell
.\build-enhanced.ps1 -Configuration Release
```

NuGet restores `JAJ.Packages.MiniAudioEx` 3.3.6, `Newtonsoft.Json` 13.0.4, and `TagLibSharp` 2.3.0. Output is staged under `CustomRadioStations\dist\scripts` with the managed and Windows x64 native MiniAudioEx runtimes. MP3, FLAC, OGG and WAV are supported; irrKlang and its codec plugins are not required.

The port includes a .NET Framework 4.8 compatibility shim for MiniAudioEx 3.3.6's context-configuration interop signature. Keep the staged managed and native MiniAudioEx DLLs together with `CustomRadioStations.dll`.

## Station layout

Stations may live directly under `Custom Radio Stations`; these are grouped into one default custom wheel. Optional extra wheel folders may contain another level of station folders. A direct JSON station uses this layout:

```text
scripts\Custom Radio Stations\<StationName>\
├── station.json
├── icon.png
├── tracks\
│   ├── song1.mp3
│   └── Albums\...
└── commercials\
    └── advert1.mp3
```

For multiple custom wheels, use `scripts\Custom Radio Stations\<Wheel>\<StationName>\` instead. Direct stations and nested wheel folders can coexist.

Commercials are kept separate internally and are never added to the normal track pool. If no commercials resolve, music playback continues normally.

### Minimal station

```json
{
  "name": "Vice City FM"
}
```

Omitted track and commercial sources default to `["*"]`. The ID is a deterministic slug derived from the station folder name, so the same station keeps the same future wheel reference.

### Typical station

```json
{
  "id": "vice-city-fm",
  "name": "Vice City FM",
  "description": "80s pop, rock and synth",
  "icon": "icon.png",
  "tracks": ["*"],
  "commercials": ["*"],
  "playback": {
    "mode": "broadcast",
    "shuffle": true,
    "volume": 1.0,
    "loop": true
  },
  "commercialBreaks": {
    "enabled": true,
    "minTracksBetween": 3,
    "maxTracksBetween": 6,
    "minCommercials": 1,
    "maxCommercials": 2
  }
}
```

### Mixed sources

```json
{
  "name": "Underground FM",
  "tracks": [
    "*",
    "Specials",
    "Exclusive Track.mp3",
    "D:\\Shared Music\\Underground",
    "D:\\Rare Music\\*.flac"
  ]
}
```

Each `tracks` entry is relative to the station's `tracks` directory unless absolute. Commercial entries work the same way relative to `commercials`.

- A file includes that supported file.
- A directory recursively includes supported audio below it.
- `*` recursively includes all supported audio below the default root.
- Explicit globs such as `Albums/*.mp3` and absolute globs are supported.
- `/` and `\` are accepted. Results are canonicalized where possible, deduplicated case-insensitively, and sorted deterministically before optional shuffle.
- Missing, malformed, and unsupported sources are logged and skipped without discarding other valid entries.
- An omitted array uses `["*"]`; an explicit empty array means no media. A station with no playable normal tracks is not registered.

`broadcast` is the default and retains virtual live-radio continuity. Each broadcast station receives a random normal track and a random point within that track when the station catalog is initialized. Its cursor advances with elapsed time before the first tune-in, so selecting it sounds like joining an existing broadcast rather than starting a media player. `playlist` retains conventional start/resume semantics. `shuffle` controls initial track ordering, `volume` is a per-station multiplier clamped to 0–1, and `loop` controls whether the programme wraps.

Commercial break ranges are normalized to non-negative values, with each maximum at least its minimum. Break scheduling does nothing when disabled, configured for zero commercials, or no commercial audio exists.

## Track display metadata

For ordinary audio files, the wheel and vehicle dashboard read the embedded Artist and Title tags and display them in GTA's three-line style:

```text
Radio Station
ARTIST
Title
```

Both tags must contain text. If either Artist or Title is absent, invalid, or unreadable, the mod falls back to its existing filename display convention (for example `Artist - Track` or `Track - Artist`, shown as separate lines). Timed `.tracklist.json` metadata continues to take precedence for long mixes and recordings.

Track labels are cached when the station catalog loads and retain the latest known playing title. Moving quickly around the wheel therefore shows each station's station/artist/title text immediately; the configured audio-switch delay does not delay the label.

Volume controls change the master volume by 5% immediately when pressed. Holding a volume control pauses briefly, then repeats in 5% steps until released. The final value is persisted after input settles rather than writing `settings.json` for every repeated step.

Controller radial selection uses a `0.20` stick deadzone and 4 degrees of angular hysteresis by default, preventing selection flicker near the boundary between stations. These can be adjusted with `gamepadControls.radialDeadzone` and `gamepadControls.radialHysteresisDegrees` in `settings.json`.

A custom wheel opens only after GTA accepts the radio-wheel input and activates its own radio HUD. Phone, interaction-menu, pause, and other frontend contexts that consume the normal radio control therefore suppress the custom wheel as well.

## JSON settings

All application-owned configuration is JSON going forward:

- `settings.json` stores global volume, startup, display, graphics, keyboard and gamepad settings. It is created with defaults on first run and runtime volume changes are persisted.
- Optional `wheel.json` inside a wheel folder overrides `iconWidth`, `iconHeight`, and `radius` for that custom wheel.
- Optional `native-wheels.json` organizes GTA's built-in stations. It fails open: absent or invalid configuration leaves the stock wheel usable.
- `station.json` defines new custom stations.
- Optional `song.tracklist.json` sidecars provide timed metadata for long mixes or recordings.

Example `wheel.json`:

```json
{
  "iconWidth": 64,
  "iconHeight": 64,
  "radius": 300.0
}
```

Station artwork uses a 720-high virtual canvas and therefore scales with the game's output resolution. The default 64x64 size becomes 128x128 physical pixels at 1440p while remaining square on ultrawide displays. Existing generated settings that still contain the old 30x30 defaults are upgraded automatically; deliberately customized sizes are preserved. Station descriptions are centered near the bottom of the screen and use a capped text width so the wheel remains readable on ultrawide resolutions such as 5120x1440.

Higher-resolution station artwork can be supplied beside the configured icon without changing `station.json`:

```text
icon.png       # base 128x128 asset
icon.256.png   # 256x256
icon.512.png   # 512x512
icon.1024.png  # 1024x1024
```

CRS selects the smallest available variant large enough for the icon's physical display size, falling back to the largest available variant. Selection is based on vertical resolution, so equivalent-height 4:3, 16:9, 21:9, and 32:9 displays use the same asset quality and visual icon size. PNG headers are validated before DirectX sees them; a missing, malformed, oversized, or rejected texture is disabled and logged without stopping the remaining radio script.

Every station has a translucent dark circular backing, with `iconbg.png` remaining available as a user override. Every custom wheel also includes a permanent **Radio Off** entry at the bottom. Selecting it stops custom playback and switches GTA's vehicle/mobile radio off. The selected entry uses GTA's current protagonist color: blue for Michael, green for Franklin, and orange for Trevor. Other player models retain `graphics.iconHighlightColor` as their fallback. A user-provided `iconhl.png` can still replace the bundled selection-ring artwork while retaining the dynamic character tint.

Example `native-wheels.json`:

```json
{
  "wheels": [
    {
      "name": "Music",
      "stations": ["RADIO_01_CLASS_ROCK", "RADIO_02_POP"]
    }
  ]
}
```

See `NativeStations.log` for the internal station names accepted by `native-wheels.json`.

Example `mix.tracklist.json` for `mix.mp3`:

```json
{
  "tracks": [
    { "startTimeMs": 0, "artist": "Artist One", "title": "Opening Track" },
    { "startTimeMs": 185000, "artist": "Artist Two", "title": "Second Track" }
  ]
}
```

## Legacy station compatibility

Legacy `station.ini` remains supported only for existing user-created radio stations and retains its original folder/file semantics, including top-level tracks, shortcuts, and `[Commercial]` filenames. Loading precedence is strict:

```text
station.json exists -> load JSON only
otherwise station.ini exists -> load the legacy station
```

The two files are never merged, and malformed `station.json` does not silently fall back to INI. Application settings, wheels, native-wheel organization, and tracklist metadata no longer use INI/CFG files.

## Source layout

- `Audio` — MiniAudioEx integration and metadata
- `Configuration` — typed JSON models/loaders and isolated legacy station INI parsing
- `Game` — input, events, native radio calls and vehicle state
- `Infrastructure` — paths, logging and process-lifetime state
- `Radio` — catalog discovery, source resolution, programme scheduling and continuity
- `Scripts` — ScriptHookVDotNet entry points
- `UI` — selector-wheel rendering and state
- `Utilities` — shared helpers

The port retains the process-lifetime reload marker, native-wheel fail-open behavior, station continuity fixes, and startup/shutdown hardening from v0.5.

## Audio analysis sidecar

`station.json` accepts both the existing string form and an object form for tracks/commercials. Existing stations remain valid.

```json
"tracks": [
  "C:\\Music\\normal.mp3",
  {
    "file": "C:\\Music\\long-mix.mp3",
    "start": "20:00",
    "end": "42:15.500",
    "artist": "Custom Artist",
    "title": "Custom Title"
  }
]
```

`start` and `end` accept `MM:SS[.fff]` or `HH:MM:SS[.fff]`. Manual bounds override analyzer-detected silence bounds. Manual `artist`/`title` values override the corresponding ID3 fields; the existing filename display remains the final fallback.

### CUE sheets

CUE sheets can be expanded into independent radio songs or kept as one continuous recording with timed sub-tracks. CUE `INDEX 01` values use the standard `MM:SS:FF` format where `FF` is 1/75th of a second.

Split mode treats every CUE `TRACK` as a separate programme item:

```json
"tracks": [
  {
    "cue": "C:\\Music\\Los Santos Rock Radio.cue",
    "cueMode": "split"
  }
]
```

Each logical track references the same physical audio file but gets its own CUE start/end boundaries, performer/title metadata, broadcast position and audio-analysis identity. Loudness normalization is measured separately for each CUE section, so a loud song elsewhere in a long recording does not change the gain selected for the current song.

Continuous mode keeps the physical recording as one programme item and uses the CUE entries as sub-track metadata:

```json
"tracks": [
  {
    "cue": "C:\\Music\\Los Santos Rock Radio.cue",
    "cueMode": "continuous"
  }
]
```

In continuous mode the analyzer measures/normalizes the whole physical recording once, preserving the relative loudness between its sub-tracks. The wheel/dashboard title changes as playback crosses each CUE index, and next-track input seeks to the next CUE sub-track. The physical audio path always comes from the CUE `FILE` directive; a CUE source cannot also specify `file`. A plain `"mix.cue"` entry defaults to `split`. `cueMode` also accepts `individual`/`tracks` as aliases for split and `single`/`subtracks` as aliases for continuous.

The optional `station.analysis.json` is generated data. Normal files remain keyed by canonical full path. Logical segments from `file/start/end` or split CUE entries use the same full path plus stable segment bounds, allowing several independently normalized songs to share one physical file. Each result records file identity, physical `durationMs`, `audioStartMs`, `audioEndMs`, integrated LUFS, true peak and the precomputed playback `gainDb`. Deleting this file is safe: the station falls back to its normal unprocessed behaviour.

### Audio analyzer

Builds are staged under `CustomRadioStations\dist\tools\CustomRadioStations.Analyzer`. The analyzer uses FFmpeg outside GTA; FFmpeg is never loaded by the game mod.

```powershell
CustomRadioStations.Analyzer.exe "C:\path\to\station"
CustomRadioStations.Analyzer.exe "C:\path\to\Custom Radio Stations" --jobs 4
```

Useful options include `--target-lufs -16`, `--silence-threshold -50`, `--minimum-silence 400`, `--padding 75`, `--no-trim`, `--no-normalize`, `--force`, and `--ffmpeg <path>`.

Analysis is parallel and resumable. Every completed analysis item (whole file or logical segment) is written atomically to `station.analysis.json`; Ctrl+C cancels active FFmpeg jobs while preserving all completed results. A later run automatically skips unchanged files by full path, file size and UTC modification time.
