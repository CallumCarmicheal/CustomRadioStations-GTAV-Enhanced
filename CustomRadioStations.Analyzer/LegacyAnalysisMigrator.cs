using CustomRadioStations;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.IO;

namespace CustomRadioStations.Analyzer {
    internal static class LegacyAnalysisMigrator {
        internal const string FileName = "station.analysis.json";

        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc
        };

        internal static int Merge(string stationDirectory, string analysisRoot, IEnumerable<ResolvedMediaSource> sources,
            StationAnalysisSettings settings, Action<string> warningSink = null) {
            StationAnalysis legacy;
            if (!TryLoad(stationDirectory, out legacy, warningSink))
                return 0;
            if (legacy.Settings == null || legacy.Settings.RequiresAudioRescan(settings)) {
                if (warningSink != null)
                    warningSink("Skipping legacy analysis in '" + stationDirectory + "' because its silence-analysis settings differ from this run.");
                return 0;
            }

            StationAnalysis central = StationAnalysisLoader.LoadOrCreate(analysisRoot, warningSink);
            int merged = 0;
            foreach (ResolvedMediaSource source in sources ?? new ResolvedMediaSource[0]) {
                TrackAnalysis existing;
                if (!legacy.Tracks.TryGetValue(source.AnalysisKey, out existing) || existing == null || !existing.IsCurrentFor(source.FilePath))
                    continue;

                try {
                    string key = AudioAnalysisIdentity.CreateAnalysisKey(source);
                    TrackAnalysis current;
                    if (!central.Tracks.TryGetValue(key, out current) || current == null || current.AnalyzedUtc < existing.AnalyzedUtc) {
                        existing.FilePath = Path.GetFullPath(source.FilePath);
                        central.Tracks[key] = existing;
                        merged++;
                    }
                } catch (Exception ex) {
                    if (warningSink != null)
                        warningSink("Could not migrate analysis for '" + source.FilePath + "': " + ex.Message);
                }
            }

            if (merged > 0)
                StationAnalysisLoader.SaveAtomic(analysisRoot, central);

            string legacyPath = Path.Combine(stationDirectory, FileName);
            try {
                File.Delete(legacyPath);
            } catch (Exception ex) {
                if (warningSink != null)
                    warningSink("Merged legacy analysis but could not delete '" + legacyPath + "': " + ex.Message);
            }
            return merged;
        }

        private static bool TryLoad(string stationDirectory, out StationAnalysis analysis, Action<string> warningSink) {
            analysis = null;
            string path = Path.Combine(stationDirectory, FileName);
            if (!File.Exists(path))
                return false;

            try {
                analysis = JsonConvert.DeserializeObject<StationAnalysis>(File.ReadAllText(path), SerializerSettings);
                if (analysis == null || analysis.Version != 1) {
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
                    warningSink("Could not load legacy '" + path + "': " + ex.Message);
                analysis = null;
                return false;
            }
        }
    }
}
