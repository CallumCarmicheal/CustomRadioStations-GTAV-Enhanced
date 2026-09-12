namespace CustomRadioStations {
    /// <summary>
    /// Centralized paths for files stored under GTA's scripts directory.
    /// Keeping these values in one place prevents configuration, logging, and
    /// runtime discovery from silently drifting apart.
    /// </summary>
    internal static class AppPaths {
        internal const string RootDirectory = @"scripts\Custom Radio Stations";
        internal const string SettingsFile = RootDirectory + @"\settings.json";
        internal const string MainLogFile = RootDirectory + @"\CustomRadioStations.log";
        internal const string NativeStationsLogFile = RootDirectory + @"\NativeStations.log";
        internal const string NativeWheelsFile = RootDirectory + @"\native-wheels.json";
        internal const string BackgroundIconFile = RootDirectory + @"\iconbg.png";
        internal const string BundledBackgroundIconFile = RootDirectory + @"\station-background.png";
        internal const string HighlightIconFile = RootDirectory + @"\iconhl.png";
        internal const string BundledHighlightIconFile = RootDirectory + @"\selection-ring.png";
        internal const string NoRadioIconFile = RootDirectory + @"\no-radio.png";
        internal const string WheelSettingsFileName = "wheel.json";
        internal const string StationJsonFileName = "station.json";
        internal const string StationSettingsFileName = "station.ini";
    }
}