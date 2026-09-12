# Custom Radio Stations for GTA V

> **Enhanced compatibility port v0.6 (2026):** GTA V Enhanced, ScriptHookVDotNet Enhanced, the ScriptHookVDotNet3 API, .NET Framework 4.8, and MiniAudioEx 3.3.6.

Custom Radio Stations provides multiple local radio wheels, track metadata, separate commercial breaks, and a virtual live-broadcast timeline. Returning to a broadcast station advances it by the time that elapsed, like a radio broadcast, instead of treating it as a paused music player.

## Build

Place `ScriptHookVDotNet3.dll` from the exact installed ScriptHookVDotNet Enhanced release in `CustomRadioStations\lib`, then run:

```powershell
.\build-enhanced.ps1 -Configuration Release
```

NuGet restores `JAJ.Packages.MiniAudioEx` 3.3.6 and `Newtonsoft.Json` 13.0.4. Output is staged under `CustomRadioStations\dist\scripts` with the managed and Windows x64 native MiniAudioEx runtimes. MP3, FLAC, OGG and WAV are supported; irrKlang and its codec plugins are not required.

## Station layout

Stations still live inside wheel folders. A JSON station uses this layout:

```text
scripts\Custom Radio Stations\<Wheel>\<StationName>\
├── station.json
├── icon.png
├── tracks\
│   ├── song1.mp3
│   └── Albums\...
└── commercials\
    └── advert1.mp3
```

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

`broadcast` is the default and retains virtual live-radio continuity. `playlist` retains a conventional paused position while away. `shuffle` controls initial track ordering, `volume` is a per-station multiplier clamped to 0–1, and `loop` controls whether the programme wraps.

Commercial break ranges are normalized to non-negative values, with each maximum at least its minimum. Break scheduling does nothing when disabled, configured for zero commercials, or no commercial audio exists.

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
  "iconWidth": 30,
  "iconHeight": 30,
  "radius": 300.0
}
```

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
