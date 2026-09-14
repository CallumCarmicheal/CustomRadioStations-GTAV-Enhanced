## Settings UI v40 notes

Destructive settings actions now have a distinct warning treatment. **Reset Settings to Defaults** is labelled `RESET`, uses a restrained red warning surface/title instead of the normal character accent, and the confirmation dialog highlights the destructive **YES** choice in the same warning palette. Harmless maintenance actions such as Reload Stations and Reload Settings retain the standard CRS/GTA accent treatment.

## Settings UI v39 notes

Keyboard navigation now has first-class PC shortcuts: number keys `1` through `6` (including the numpad) jump directly to the corresponding settings category, while `Home` and `End` jump to the first or last selectable setting on the current page. These keys are treated as in-menu navigation even if the user assigns one as the global open-settings shortcut, preventing the binding from unexpectedly closing the menu while it is open.

## Settings UI v38 notes

The settings footer now adapts to constrained 4:3 / safe-zone layouts. Wide layouts keep the compact single-row control strip, while narrower layouts split the control hints across two lines and use slightly smaller hint text so controller glyphs and keyboard/mouse instructions do not collide. The footer gained a small amount of vertical room without reducing the nine visible settings rows.

## Settings UI v37 notes

The settings UI and settings model now share one canonical set of built-in defaults. Default indicators, per-item reset actions, **Reset Settings to Defaults**, newly-created `settings.json` files, and runtime startup defaults all resolve through the same values, preventing UI/default drift when defaults are changed in future versions.


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

Track labels are derived from the station's logical playback timeline rather than cached as a last-known title. When the wheel opens or selection moves to a station, the mod projects that station's current programme item and logical position immediately, even if its MiniAudio source is not active. The programme timeline uses each source's effective playable length after manual `start`/`end` bounds and analyzer-detected audio bounds are applied. Continuous CUE/tracklist metadata is then resolved against the projected raw source position, so the wheel can show the correct sub-track before the delayed audio-switch action runs.

Physical duration is read during the same TagLib metadata pass already used for Artist/Title, so this does not add another file scan. Once a source is actually opened by MiniAudio its decoder-reported length replaces the metadata estimate in the timeline. If a malformed/unusual file has no discoverable duration, broadcast preview still advances through the known part of the programme and stops when it reaches the first unknown-length item instead of guessing past it; normal playback resolves that duration when the source is opened.

Volume controls change the master volume by 5% immediately when pressed. Holding a volume control pauses briefly, then repeats in 5% steps until released. The final value is persisted after input settles rather than writing `settings.json` for every repeated step.


### In-game settings menu

- Settings pages use non-interactive section headers to group related controls without adding extra controller focus stops.

CRS now includes the first controller/keyboard/mouse settings-menu foundation under `UI/Settings`. The menu uses a 720-high virtual canvas with a capped centred width so it remains readable on 16:9, 21:9 and 32:9 displays rather than stretching across an ultrawide screen. Its horizontal bounds also respect GTA's configured safe-zone scale, which keeps the centred panel inside the usable display area on narrower/TV layouts.

Default access:

- Controller: `gamepadControls.openSettings = "ScriptSelect"` (Xbox Back/View-style button) while the custom radio wheel is open.
- Keyboard: `keyboardControls.openSettings = "F10"`; this can open the menu directly after CRS has loaded.

Both bindings are persisted in `settings.json` and can now be rebound from the Controls page. Select the keyboard/controller binding, press the replacement key/button, or use Escape / B to cancel capture. The controller value is presented with friendly button names such as `View / Back / Select` instead of exposing the internal GTA `ScriptSelect` label.

The menu is designed controller-first while supporting keyboard and GTA's native mouse cursor controls. D-pad/left stick and arrow keys navigate rows, left/right changes values, A/Enter activates, B/Escape backs out, LB/RB or Q/E changes category, and X/Backspace resets a modified option to its default. Each menu opening resets stale pointer/repeat state, so controller or keyboard entry cannot be stolen by an old cursor position or held-input timer. Mouse movement switches the menu into pointer mode; rows can be clicked, sliders and the scrollbar use captured click-and-drag behavior, category tabs have hover/click feedback, choice values expose clickable previous/next arrows, the wheel scrolls the settings list, and a pointer-only header close button provides a conventional PC exit target without displaying a misleading X / Square cue in controller mode. Controller footer hints use GTA input tokens so the game can draw its normal button glyphs. Modified settings are marked in the list, the footer shows the configured default value, and reset hints only appear when the selected value actually differs from its default. The category rail has a dedicated header aligned with the content header and shows the currently active input mode, making controller/keyboard/mouse hand-off visible without consuming the main settings pane. Categories display a small count when they contain settings that differ from defaults, while the content header reports the automatic-save state (`AUTO-SAVE ON` / `SAVING...`). Mouse users get a contextual `RESTORE DEFAULT` button for modified values, and binding-capture dialogs expose a clickable Cancel button in pointer mode. Read-only capability rows remain visible for context but are skipped by controller/keyboard navigation, and use a `BUILT-IN` badge so they cannot be mistaken for toggles.

Current pages are Playback, Audio, Radio, Controls, Quality of Life and Advanced. The Advanced `Reload Settings` action now reads `settings.json` without first saving over it, so external edits can actually be reloaded; only menu changes still inside the short auto-save debounce window are discarded. Runtime-backed settings include pause-menu playback, background playback, master volume, wheel slow motion, custom-wheel default preference, help text, station activation delay, Unicode mode, controller deadzone and radial hysteresis. Controller menu hold/repeat timing is configurable from the Controls page and persists as `ui.menuHoldDelayMs` (400 ms by default) and `ui.menuRepeatRateMs` (100 ms by default). `ui.rememberSettingsPage` defaults to `true`; when enabled the menu reopens on the last category used, while disabling it always opens on Playback. Changes apply immediately and are persisted after a short debounce rather than rewriting `settings.json` on every held input repeat. If an Advanced reload rebuilds the wheel collection while Settings is open, CRS now remembers the wheel/category/item that launched the menu and restores that context when returning to the still-held radio wheel. Wheel, category and item names are resolved first after a reload so reordered station definitions do not send the user to the wrong entry; saved indexes are only used as a fallback if an original name no longer exists.

`general.playInPauseMenu` and `general.playWhileInBackground` both default to `false`, preserving the existing GTA-like behavior. They feed the same pause coordinator already used by playback, so enabling either option changes the actual MiniAudio pause policy rather than only changing the UI.

### F4 console API

A small public `CRSAPI` facade is exposed in the global namespace for ScriptHookVDotNet's F4 C# console. Read-only properties return live snapshots, while mutating commands are queued onto the main radio script tick so MiniAudio, GTA natives, wheel state and the playback timeline are never mutated from the console evaluator thread.

Examples:

```csharp
return CRSAPI.CurrentStation;
return CRSAPI.CurrentTrack;
return CRSAPI.Position;
return CRSAPI.Duration;

CRSAPI.NextSong();
CRSAPI.PreviousSong();
CRSAPI.RestartSong();
CRSAPI.Seek(30);
CRSAPI.SeekRelative(-10);
CRSAPI.SeekPercent(50);

CRSAPI.Pause();
CRSAPI.Play();
CRSAPI.TogglePause();

CRSAPI.NextStation();
CRSAPI.PreviousStation();
CRSAPI.SetStation("Nocturne");
CRSAPI.SetStation(0);

return CRSAPI.Status();
return CRSAPI.TrackInfo();
return CRSAPI.Timeline();
return CRSAPI.BoundsInfo();
return CRSAPI.DumpProgramme();
return CRSAPI.Stations();
```

`CRSAPI.Position` and `CRSAPI.Duration` refer to the current logical song. For a continuous CUE/tracklist source this means the current sub-track, not the full backing mix. `CRSAPI.Track.MediaPosition` / `MediaDuration` and `BoundsInfo()` expose the containing trimmed media source when lower-level debugging is needed. Public `CRSTrackInfo` and `CRSStationInfo` objects are immutable snapshots; the API intentionally does not expose mutable `RadioStation`, `SoundFile` or MiniAudio objects.

Commands return a short `Queued: ...` string immediately. The actual mutation is processed on the next Custom Radio Stations tick (normally within the script's 10 ms interval), and `CRSAPI.LastResult` contains the most recent execution result.


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

### Unicode wheel text

Custom wheel labels use GTA's native text renderer whenever the active Western font can represent the text. `graphics.unicodeTextMode` defaults to `Auto`; when a label contains characters outside the known-safe Western set, CRS renders the complete label once into a transparent texture and reuses the cached texture. This avoids the missing-glyph boxes GTA shows for titles such as `好き？ Suki!` while preserving the original native path for ordinary English/Latin titles.

The bitmap fallback first looks for `graphics.unicodeFont`, then for a supported font in `scripts\Custom Radio Stations\Fonts`, and finally for suitable CJK fonts already installed by Windows. A bundled/private font is preferred because it gives consistent coverage without installing anything globally. TTF is the safest format for the .NET Framework/GDI+ renderer. Private font candidates are probed with the actual vector-outline operation before use, so an unsupported OpenType/CFF font is skipped in favour of the next usable font instead of failing title-by-title. See `Fonts\README.txt` for suggested filenames and licensing notes.

The fallback keeps GDI+'s alternate-font fallback enabled, so an uncommon glyph missing from the
primary family can still be sourced from another installed Windows font while the complete label is
composited into one bitmap. ScriptHookV itself does not expose per-texture deletion: a texture made
with `createTexture` is released when scripts reload. CRS therefore keeps generated filenames
immutable (to avoid stale ScriptHookV textures), bounds its managed and disk caches, and only creates
a native texture when a Unicode label is actually shown. Over a very long single game session the
native texture table can still grow by one entry for each distinct Unicode label encountered; there
is no safe standalone SHVDN API to reclaim those entries individually.

The fallback is measured, not monospaced: GDI+ calculates each line's typographic advance and the renderer separately measures the real glyph-outline bounds before allocating the texture. The exact floating-point advance is retained for alignment instead of being rounded to a texture pixel. This covers proportional Latin, full-width forms, half-width Katakana and mixed CJK/Latin correctly, including outline/shadow overhang. Wrapped station/item descriptions use the same renderer: wrapping is measured with the selected fallback font and split at Unicode text-element boundaries. The .NET Framework text elements are additionally merged for modern emoji ZWJ chains, variation selectors, skin-tone modifiers, regional-indicator flags and half-width Katakana voiced marks, so those sequences are not cut in half. Basic Japanese/CJK kinsoku rules also keep closing punctuation and small kana off the start of a new line and opening brackets off the previous line end. Its line height is calibrated from GTA's selected native font slot so it occupies the same native text scale, although the visible glyph proportions can differ slightly because the fallback typeface is not Chalet London. Textures are rasterized from vertical output resolution and drawn through GTA's 720-high scaled UI canvas; raster density rounds upward in 0.25x buckets so window resizing cannot create a new native texture for every individual output height, and aspect ratio does not stretch the glyphs.

```json
{
  "graphics": {
    "unicodeTextMode": "Auto",
    "unicodeFont": ""
  }
}
```

Modes are `Auto`, `NativeOnly`, and `BitmapFallback`. Generated PNGs use immutable content/style hashes under `scripts\Custom Radio Stations\cache\text`; the cache is bounded and stale entries are pruned. If the private renderer or font fails, CRS logs the failure and falls back to GTA's original text path instead of breaking the radio wheel. Rockstar's separate vehicle-dashboard `dashboard` Scaleform remains GTA-owned and therefore still follows the game's active font library.

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

The optional root-level `audio-analysis.json` is generated data shared by every station. Audio is keyed by a stable sampled-content fingerprint rather than its path, so cached analysis remains reusable when a song is renamed or moved between stations. Logical segments from `file/start/end` or split CUE entries add stable segment bounds to that fingerprint, allowing several independently normalized songs to share one physical file. Whole-file entries record file identity, physical `durationMs`, `audioStartMs`, `audioEndMs`, integrated LUFS, true peak and the precomputed playback `gainDb`. Segmented entries additionally record absolute `sourceStartMs` / `sourceEndMs` bounds for the region that was analyzed; `audioStartMs` / `audioEndMs` are the silence-trimmed audible bounds inside that region. Deleting this file is safe: stations fall back to their normal unprocessed behaviour.

### Audio analyzer

Builds are staged under `CustomRadioStations\dist\tools\CustomRadioStations.Analyzer`. The analyzer uses FFmpeg outside GTA; FFmpeg is never loaded by the game mod.

```powershell
CustomRadioStations.Analyzer.exe "C:\path\to\station"
CustomRadioStations.Analyzer.exe "C:\path\to\Custom Radio Stations" --jobs 4
```

Useful options include `--target-lufs -16`, `--silence-threshold -50`, `--minimum-silence 400`, `--padding 75`, `--no-trim`, `--no-normalize`, `--force`, and `--ffmpeg <path>`.

Analysis is parallel and resumable. Every completed analysis item (whole file or logical segment) is written atomically to the root `audio-analysis.json`; Ctrl+C cancels active FFmpeg jobs while preserving all completed results. A later run automatically skips files already present under the same content fingerprint. If the analyzer finds a legacy per-station `station.analysis.json`, it merges matching current entries into the central cache and then deletes the legacy file. The game mod itself reads only `audio-analysis.json` and never loads legacy station sidecars.

- Advanced includes a guarded **Reset Settings to Defaults** action with a controller/keyboard/mouse confirmation dialog; it never changes station definitions or music files.

- Switching from mouse to keyboard/controller snaps focus away from non-interactive section/info rows before processing Select/Enter.


## Settings UI v41

Keyboard input now wins over incidental mouse movement in the same frame.


## Settings UI v42

Settings auto-save now reports `SAVED` after a successful write and `SAVE FAILED` if settings.json cannot be written; failures remain dirty for a later retry instead of being silently treated as saved.



## Settings UI v43

Mouse-mode activation now uses a 3-virtual-pixel threshold, keeping pointer/controller switching consistent across aspect ratios and ultrawide displays. Slider default detection now compares the actual configured value rather than treating any value within half a step as the default.



## Settings UI v44

The active page header now shows `PAGE n / 6` and, when applicable, the number of modified settings on that page, making changed state visible without scanning individual rows.



## Settings UI v45

Slider left/right input now normalizes manually edited off-step values to the next valid step in the requested direction (for example, 32% becomes 35% with Right or 30% with Left).



## Settings UI v46

The settings panel now respects GTA safe-zone margins vertically as well as horizontally. At tighter safe-zone settings the footer compresses and automatically uses the compact two-line control-hint layout while retaining all nine visible settings rows.



## Settings UI v47

Mouse slider and scrollbar drags now retain pointer capture when the cursor leaves the settings panel, clamping naturally to the control edges until the button is released.



## Settings UI v48

Settings footer status messages now distinguish errors visually: failed save/reload/reset/actions use the restrained danger treatment, while successful and informational statuses retain the normal CRS/GTA accent.

