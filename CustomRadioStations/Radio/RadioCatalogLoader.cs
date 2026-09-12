using GTA.Math;
using SelectorWheel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace CustomRadioStations
{
    /// <summary>
    /// Discovers wheel and station folders and builds the in-memory radio catalog.
    /// UI interaction remains owned by MainScript; this class only loads data.
    /// </summary>
    internal static class RadioCatalogLoader
    {
        private static readonly HashSet<string> SupportedAudioExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp3", ".wav", ".flac", ".ogg", ".lnk"
            };

        internal static void Reload()
        {
            ResetCatalog();
            EnsureRootDirectory();

            foreach (string wheelDirectory in GetDirectories(AppPaths.RootDirectory))
                LoadWheel(wheelDirectory);
        }

        private static void ResetCatalog()
        {
            WheelVars.RadioWheels.Clear();
            WheelVars.CurrentRadioWheel = null;
            WheelVars.NextQueuedWheel = null;
            StationWheelPair.List.Clear();
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
            var settings = Config.LoadWheelINI(wheelDirectory);
            var wheel = new Wheel(
                "Radio Wheel", wheelDirectory, 0, 0,
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
            wheel.SetCategoryBackgroundIcons(
                AppPaths.BackgroundIconFile, Config.IconBG, Config.IconBgSizeMultiple,
                AppPaths.HighlightIconFile, Config.IconHL, Config.IconHlSizeMultiple);
            wheel.CalculateCategoryPlacement();
            WheelVars.RadioWheels.Add(wheel);
        }

        private static void LoadStation(Wheel wheel, string stationDirectory)
        {
            string stationName = Path.GetFileName(stationDirectory);
            string[] audioFiles = Directory.GetFiles(stationDirectory, "*.*", SearchOption.TopDirectoryOnly)
                .Where(path => SupportedAudioExtensions.Contains(Path.GetExtension(path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (audioFiles.Length == 0)
            {
                Logger.Log("Skipping station without supported audio: " + stationName);
                return;
            }

            Logger.Log("Loading station: " + stationName);
            var category = new WheelCategory(stationName);
            category.AddItem(new WheelCategoryItem(category.Name));
            wheel.AddCategory(category);

            var station = new RadioStation(category, audioFiles);
            if (!station.HasPlayableSounds)
            {
                Logger.Log("Skipping station with no playable audio: " + stationName);
                wheel.RemoveCategory(category);
                return;
            }

            var pair = new StationWheelPair(wheel, category, station);
            pair.LoadStationINI(Path.Combine(stationDirectory, AppPaths.StationSettingsFileName));
            StationWheelPair.List.Add(pair);
        }

        private static string[] GetDirectories(string path)
        {
            return Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
