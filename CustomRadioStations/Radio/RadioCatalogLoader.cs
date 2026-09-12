using GTA.Math;
using SelectorWheel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace CustomRadioStations
{
    internal static class RadioCatalogLoader
    {
        private static readonly HashSet<string> LegacyExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".flac", ".ogg", ".lnk"
        };

        private static readonly HashSet<string> LoadedStationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static void Reload()
        {
            ResetCatalog();
            EnsureRootDirectory();
            foreach (string wheelDirectory in GetDirectories(AppPaths.RootDirectory))
                LoadWheel(wheelDirectory);
        }

        private static void ResetCatalog()
        {
            if (RadioStation.CurrentPlaying != null)
                RadioStation.CurrentPlaying.Stop();
            foreach (StationWheelPair pair in StationWheelPair.List)
                pair.Station.Dispose();

            WheelVars.RadioWheels.Clear();
            WheelVars.CurrentRadioWheel = null;
            WheelVars.NextQueuedWheel = null;
            StationWheelPair.List.Clear();
            LoadedStationIds.Clear();
            RadioStation.CurrentPlaying = null;
            RadioStation.NextQueuedStation = null;
        }

        private static void EnsureRootDirectory()
        {
            if (Directory.Exists(AppPaths.RootDirectory)) return;
            Directory.CreateDirectory(AppPaths.RootDirectory);
            Logger.Log("Created missing custom radio directory: " + AppPaths.RootDirectory);
        }

        private static void LoadWheel(string wheelDirectory)
        {
            Logger.Log("Loading wheel: " + wheelDirectory);
            var settings = Config.LoadWheelSettings(wheelDirectory);
            var wheel = new Wheel("Radio Wheel", wheelDirectory, 0, 0,
                new Size(settings.iconX, settings.iconY), 200, settings.wheelRadius);

            string[] stationDirectories = GetDirectories(wheelDirectory);
            Logger.Log("Station folders found: " + stationDirectories.Length);
            foreach (string stationDirectory in stationDirectories)
                LoadStation(wheel, stationDirectory);

            if (wheel.Categories.Count == 0)
            {
                Logger.Log("Skipping empty wheel: " + Path.GetFileName(wheelDirectory));
                return;
            }

            wheel.Origin = new Vector2(0.5f, 0.45f);
            wheel.SetCategoryBackgroundIcons(AppPaths.BackgroundIconFile, Config.IconBG, Config.IconBgSizeMultiple,
                AppPaths.HighlightIconFile, Config.IconHL, Config.IconHlSizeMultiple);
            wheel.CalculateCategoryPlacement();
            WheelVars.RadioWheels.Add(wheel);
        }

        private static void LoadStation(Wheel wheel, string stationDirectory)
        {
            string jsonPath = Path.Combine(stationDirectory, AppPaths.StationJsonFileName);
            string iniPath = Path.Combine(stationDirectory, AppPaths.StationSettingsFileName);
            StationDefinition definition;

            if (File.Exists(jsonPath))
            {
                // JSON has complete precedence, including when it is malformed.
                if (!StationConfigLoader.TryLoad(stationDirectory, out definition)) return;
                Logger.Log("Loaded JSON station: " + definition.Name + " (" + definition.Id + ")");
            }
            else if (File.Exists(iniPath))
            {
                definition = LoadLegacyDefinition(stationDirectory, iniPath);
                if (definition == null) return;
                Logger.Log("Loaded legacy INI station: " + definition.Name);
            }
            else
            {
                Logger.Log("Skipping station folder without station.json or station.ini: " + stationDirectory);
                return;
            }

            if (!LoadedStationIds.Add(definition.Id))
            {
                Logger.Log("WARNING: Skipping station '" + definition.Name + "' because ID '" + definition.Id + "' is already in use.");
                return;
            }

            var category = new WheelCategory(definition.Name)
            {
                Description = (definition.Description ?? string.Empty).Replace("\\n", "\r\n")
            };
            category.AddItem(new WheelCategoryItem(category.Name));
            wheel.AddCategory(category);

            if (!string.IsNullOrEmpty(definition.IconPath))
                category.CategoryTexture = new Texture(definition.IconPath, wheel.Categories.IndexOf(category));

            var station = new RadioStation(category, definition);
            if (!station.HasPlayableSounds)
            {
                Logger.Log("Skipping station with no playable tracks: " + definition.Name);
                station.Dispose();
                wheel.RemoveCategory(category);
                LoadedStationIds.Remove(definition.Id);
                return;
            }

            var pair = new StationWheelPair(wheel, category, station, stationDirectory, definition.ConfigPath, definition.IsLegacyIni);
            if (definition.IsLegacyIni) pair.ReloadLegacyDescription();
            StationWheelPair.List.Add(pair);
        }

        private static StationDefinition LoadLegacyDefinition(string stationDirectory, string iniPath)
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(stationDirectory, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(path => LegacyExtensions.Contains(Path.GetExtension(path)))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception ex)
            {
                Logger.Log("ERROR: Failed to scan legacy station '" + stationDirectory + "': " + ex.Message);
                return null;
            }

            var tracks = new List<string>();
            var commercials = new List<string>();
            foreach (string file in files)
            {
                if (IsLegacyCommercial(file)) commercials.Add(file);
                else tracks.Add(file);
            }
            if (tracks.Count == 0)
            {
                Logger.Log("Skipping legacy station without playable track files: " + stationDirectory);
                return null;
            }

            string name = Path.GetFileName(stationDirectory);
            return new StationDefinition
            {
                Id = StationConfigLoader.CreateStableId(name),
                Name = name,
                Description = string.Empty,
                ConfigPath = iniPath,
                IsLegacyIni = true,
                Tracks = tracks,
                Commercials = commercials,
                Playback = new PlaybackConfig(),
                CommercialBreaks = new CommercialBreakConfig
                {
                    Enabled = true,
                    MinTracksBetween = 3,
                    MaxTracksBetween = 3,
                    MinCommercials = 1,
                    MaxCommercials = 1
                }
            };
        }

        private static bool IsLegacyCommercial(string path)
        {
            if (Path.GetFileNameWithoutExtension(path).IndexOf("[Commercial]", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
                return false;
            try
            {
                string target = GeneralHelper.GetShortcutTargetFile(path);
                return Path.GetFileNameWithoutExtension(target).IndexOf("[Commercial]", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private static string[] GetDirectories(string path)
        {
            return Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
