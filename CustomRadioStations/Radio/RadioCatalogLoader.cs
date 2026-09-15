using GTA.Math;

using SelectorWheel;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace CustomRadioStations {
    internal static class RadioCatalogLoader {
        private static readonly HashSet<string> LegacyExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".flac", ".ogg", ".lnk"
        };

        private static readonly HashSet<string> LoadedStationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static void Reload() {
            RuntimeState.CatalogLoadCompleted = false;
            ResetCatalog();
            try {
                EnsureRootDirectory();

                string[] rootDirectories = GetDirectories(AppPaths.RootDirectory);
                string[] directStationDirectories = rootDirectories.Where(IsStationDirectory).ToArray();
                if (directStationDirectories.Length > 0)
                    LoadWheel(AppPaths.RootDirectory, directStationDirectories, "default custom wheel");

                foreach (string wheelDirectory in rootDirectories.Where(directory => !IsStationDirectory(directory)))
                    LoadWheel(wheelDirectory);

                RuntimeState.CatalogLoadCompleted = true;
            } catch {
                // Never expose a half-rebuilt catalog after an unexpected load failure.
                // Dispose any stations/timers/audio created before the exception, then let
                // the caller report the original failure.
                ResetCatalog();
                throw;
            }
        }

        internal static void Shutdown() {
            RuntimeState.CatalogLoadCompleted = false;
            ResetCatalog();
        }

        private static void ResetCatalog() {
            Wheel.ResetTransitions();

            RadioStation playing = RadioStation.CurrentPlaying;
            if (playing != null) {
                try { playing.Stop(); }
                catch (Exception ex) {
                    try { Logger.Log("WARNING: Failed to stop current station during catalog reset: " + ex.Message); } catch { }
                }
            }

            // Dispose every station independently. Script abort/reload can occur while GTA
            // natives or an audio source are already failing; one cleanup error must not
            // prevent the remaining clips/timers from being released.
            foreach (StationWheelPair pair in StationWheelPair.List.ToArray()) {
                try { pair.Station.Dispose(); }
                catch (Exception ex) {
                    try { Logger.Log("WARNING: Failed to dispose station '" + pair.Station.Name + "': " + ex.Message); } catch { }
                }
            }

            WheelVars.RadioWheels.Clear();
            WheelVars.CurrentRadioWheel = null;
            WheelVars.NextQueuedWheel = null;
            StationWheelPair.List.Clear();
            UsedVehiclesManager.Reset();
            LoadedStationIds.Clear();
            RadioStation.CurrentPlaying = null;
            RadioStation.NextQueuedStation = null;
        }

        private static void EnsureRootDirectory() {
            if (Directory.Exists(AppPaths.RootDirectory))
                return;
            Directory.CreateDirectory(AppPaths.RootDirectory);
            Logger.Log("Created missing custom radio directory: " + AppPaths.RootDirectory);
        }

        private static void LoadWheel(string wheelDirectory) {
            LoadWheel(wheelDirectory, GetDirectories(wheelDirectory), Path.GetFileName(wheelDirectory));
        }

        private static void LoadWheel(string wheelDirectory, string[] stationDirectories, string wheelName) {
            Logger.Log("Loading wheel: " + wheelName + " (" + wheelDirectory + ")");
            var settings = Config.LoadWheelSettings(wheelDirectory);
            var wheel = new Wheel("Radio Wheel", wheelDirectory, 0, 0,
                new Size(settings.iconX, settings.iconY), 200, settings.wheelRadius);

            Logger.Log("Station folders found: " + stationDirectories.Length);
            foreach (string stationDirectory in stationDirectories)
                LoadStation(wheel, stationDirectory);

            if (wheel.Categories.Count == 0) {
                Logger.Log("Skipping empty wheel: " + wheelName);
                return;
            }

            AddNoRadioCategory(wheel);

            wheel.Origin = new Vector2(0.5f, 0.45f);
            string highlightIcon = File.Exists(AppPaths.HighlightIconFile)
                ? AppPaths.HighlightIconFile
                : AppPaths.BundledHighlightIconFile;
            string backgroundIcon = File.Exists(AppPaths.BackgroundIconFile)
                ? AppPaths.BackgroundIconFile
                : AppPaths.BundledBackgroundIconFile;
            wheel.SetCategoryBackgroundIcons(backgroundIcon, Config.IconBG, Config.IconBgSizeMultiple,
                highlightIcon, Config.IconHL, Config.IconHlSizeMultiple);
            wheel.HighlightColorProvider = () => CharacterRadioColor.GetCurrent(Config.IconHL);
            wheel.CalculateCategoryPlacement();
            WheelVars.RadioWheels.Add(wheel);
        }

        private static void AddNoRadioCategory(Wheel wheel) {
            var category = new WheelCategory("Radio Off", "Turn off the radio.") {
                IsRadioOff = true
            };
            category.AddItem(new WheelCategoryItem(category.Name));
            if (File.Exists(AppPaths.NoRadioIconFile))
                category.CategoryTexture = new Texture(AppPaths.NoRadioIconFile, wheel.Categories.Count);
            wheel.AddCategory(category);
        }

        private static bool IsStationDirectory(string directory) {
            return File.Exists(Path.Combine(directory, AppPaths.StationJsonFileName)) ||
                File.Exists(Path.Combine(directory, AppPaths.StationSettingsFileName));
        }

        private static void LoadStation(Wheel wheel, string stationDirectory) {
            string jsonPath = Path.Combine(stationDirectory, AppPaths.StationJsonFileName);
            string iniPath = Path.Combine(stationDirectory, AppPaths.StationSettingsFileName);
            StationDefinition definition;

            if (File.Exists(jsonPath)) {
                // JSON has complete precedence, including when it is malformed.
                if (!StationConfigLoader.TryLoad(stationDirectory, out definition))
                    return;
                Logger.Log("Loaded JSON station: " + definition.Name + " (" + definition.Id + ")");
            } else if (File.Exists(iniPath)) {
                definition = LoadLegacyDefinition(stationDirectory, iniPath);
                if (definition == null)
                    return;
                Logger.Log("Loaded legacy INI station: " + definition.Name);
            } else {
                Logger.Log("Skipping station folder without station.json or station.ini: " + stationDirectory);
                return;
            }

            if (!LoadedStationIds.Add(definition.Id)) {
                Logger.Log("WARNING: Skipping station '" + definition.Name + "' because ID '" + definition.Id + "' is already in use.");
                return;
            }

            var category = new WheelCategory(definition.Name) {
                Description = (definition.Description ?? string.Empty).Replace("\\n", "\r\n")
            };
            category.AddItem(new WheelCategoryItem(category.Name));
            wheel.AddCategory(category);

            if (!string.IsNullOrEmpty(definition.IconPath)) {
                int requiredIconPixels = WheelDisplayMetrics.GetRequiredIconPixels(wheel.TextureSize.Width, wheel.TextureSize.Height, GTA.UI.Screen.Resolution.Height);
                string selectedIconPath = StationIconVariantResolver.Resolve(definition.IconPath, requiredIconPixels);
                if (!string.IsNullOrEmpty(selectedIconPath)) {
                    category.CategoryTexture = new Texture(selectedIconPath, wheel.Categories.IndexOf(category));
                    if (!string.Equals(selectedIconPath, definition.IconPath, StringComparison.OrdinalIgnoreCase))
                        Logger.Log("Selected higher-resolution station icon for '" + definition.Name + "': " + selectedIconPath);
                }
            }

            var station = new RadioStation(category, definition);
            if (!station.HasPlayableSounds) {
                Logger.Log("Skipping station with no playable tracks: " + definition.Name);
                station.Dispose();
                wheel.RemoveCategory(category);
                LoadedStationIds.Remove(definition.Id);
                return;
            }

            var pair = new StationWheelPair(wheel, category, station, stationDirectory, definition.ConfigPath, definition.IsLegacyIni);
            // Register ownership before any remaining operation that can throw so the
            // transactional reload cleanup can always find and dispose this station.
            StationWheelPair.List.Add(pair);
            if (definition.IsLegacyIni)
                pair.ReloadLegacyDescription();
        }

        private static StationDefinition LoadLegacyDefinition(string stationDirectory, string iniPath) {
            string[] files;
            try {
                files = Directory.GetFiles(stationDirectory, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(path => LegacyExtensions.Contains(Path.GetExtension(path)))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            } catch (Exception ex) {
                Logger.Log("ERROR: Failed to scan legacy station '" + stationDirectory + "': " + ex.Message);
                return null;
            }

            var tracks = new List<string>();
            var commercials = new List<string>();
            foreach (string file in files) {
                if (IsLegacyCommercial(file))
                    commercials.Add(file);
                else
                    tracks.Add(file);
            }
            if (tracks.Count == 0) {
                Logger.Log("Skipping legacy station without playable track files: " + stationDirectory);
                return null;
            }

            string name = Path.GetFileName(stationDirectory);
            return new StationDefinition {
                Id = StationConfigLoader.CreateStableId(name),
                Name = name,
                Description = string.Empty,
                ConfigPath = iniPath,
                IsLegacyIni = true,
                Tracks = tracks.Select(path => new ResolvedMediaSource(path)).ToArray(),
                Commercials = commercials.Select(path => new ResolvedMediaSource(path)).ToArray(),
                Playback = new PlaybackConfig(),
                CommercialBreaks = new CommercialBreakConfig {
                    Enabled = true,
                    MinTracksBetween = 3,
                    MaxTracksBetween = 3,
                    MinCommercials = 1,
                    MaxCommercials = 1
                }
            };
        }

        private static bool IsLegacyCommercial(string path) {
            if (Path.GetFileNameWithoutExtension(path).IndexOf("[Commercial]", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
                return false;
            try {
                string target = GeneralHelper.GetShortcutTargetFile(path);
                return Path.GetFileNameWithoutExtension(target).IndexOf("[Commercial]", StringComparison.OrdinalIgnoreCase) >= 0;
            } catch { return false; }
        }

        private static string[] GetDirectories(string path) {
            return Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}