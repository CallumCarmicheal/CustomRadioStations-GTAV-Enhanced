using GTA;

using System.Collections.Generic;

namespace CustomRadioStations {
    internal sealed class StationWheelPair {
        internal static readonly List<StationWheelPair> List = new List<StationWheelPair>();

        internal SelectorWheel.Wheel Wheel {
            get;
        }
        internal SelectorWheel.WheelCategory Category {
            get;
        }
        internal RadioStation Station {
            get;
        }
        internal string StationDirectory {
            get;
        }
        internal string ConfigPath {
            get;
        }
        internal bool IsLegacyIni {
            get;
        }

        internal StationWheelPair(SelectorWheel.Wheel wheel, SelectorWheel.WheelCategory category,
            RadioStation station, string stationDirectory, string configPath, bool isLegacyIni) {
            Wheel = wheel;
            Category = category;
            Station = station;
            StationDirectory = stationDirectory;
            ConfigPath = configPath;
            IsLegacyIni = isLegacyIni;
        }

        internal void ReloadLegacyDescription() {
            if (!IsLegacyIni)
                return;
            Config.ForceDecimal();
            Settings.ScriptSettings config = Settings.ScriptSettings.Load(ConfigPath);
            string description = config.GetValue<string>("GENERAL", "DESCRIPTION", string.Empty);
            Category.Description = (description ?? string.Empty).Replace("\\n", "\r\n");
        }

        internal void RescanStationTracklists() {
            Station.RescanSoundsTracklists();
        }
    }
}