using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.IO;

namespace CustomRadioStations {
    public static class StationAnalysisLoader {
        public const int CurrentVersion = 2;
        public const string FileName = "audio-analysis.json";

        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Formatting = Formatting.Indented,
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc
        };

        public static bool TryLoad(string stationDirectory, out StationAnalysis analysis, Action<string> warningSink = null) {
            analysis = null;
            string path = Path.Combine(stationDirectory, FileName);
            if (!File.Exists(path))
                return false;

            try {
                analysis = JsonConvert.DeserializeObject<StationAnalysis>(File.ReadAllText(path), SerializerSettings);
                if (analysis == null || analysis.Version != CurrentVersion) {
                    if (warningSink != null)
                        warningSink("Ignoring unsupported " + FileName + " version in '" + path + "'.");
                    analysis = null;
                    return false;
                }
                analysis.Settings = analysis.Settings ?? new StationAnalysisSettings();
                analysis.Tracks = analysis.Tracks == null
                    ? new Dictionary<string, TrackAnalysis>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, TrackAnalysis>(analysis.Tracks, StringComparer.OrdinalIgnoreCase);
                return true;
            } catch (Exception ex) {
                if (warningSink != null)
                    warningSink("Could not load '" + path + "': " + ex.Message);
                analysis = null;
                return false;
            }
        }

        public static StationAnalysis LoadOrCreate(string stationDirectory, Action<string> warningSink = null) {
            StationAnalysis analysis;
            return TryLoad(stationDirectory, out analysis, warningSink) ? analysis : new StationAnalysis();
        }

        public static void SaveAtomic(string stationDirectory, StationAnalysis analysis) {
            if (analysis == null)
                throw new ArgumentNullException(nameof(analysis));
            Directory.CreateDirectory(stationDirectory);
            string path = Path.Combine(stationDirectory, FileName);
            string tempPath = path + ".tmp";
            analysis.Version = CurrentVersion;
            File.WriteAllText(tempPath, JsonConvert.SerializeObject(analysis, SerializerSettings));

            try {
                if (File.Exists(path))
                    File.Replace(tempPath, path, null);
                else
                    File.Move(tempPath, path);
            } catch {
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(tempPath, path);
            } finally {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
    }
}
