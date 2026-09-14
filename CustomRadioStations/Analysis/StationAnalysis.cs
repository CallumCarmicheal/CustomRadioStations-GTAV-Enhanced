using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.IO;

namespace CustomRadioStations {
    public sealed class StationAnalysisSettings {
        public StationAnalysisSettings() {
            SilenceThresholdDb = -50d;
            MinimumSilenceMs = 400;
            PaddingMs = 75;
            TargetLufs = -16d;
            PeakHeadroomDb = 0.5d;
            MaxGainDb = 12d;
            TrimSilence = true;
            NormalizeLoudness = true;
        }

        [JsonProperty("silenceThresholdDb")]
        public double SilenceThresholdDb { get; set; }

        [JsonProperty("minimumSilenceMs")]
        public int MinimumSilenceMs { get; set; }

        [JsonProperty("paddingMs")]
        public int PaddingMs { get; set; }

        [JsonProperty("targetLufs")]
        public double TargetLufs { get; set; }

        [JsonProperty("peakHeadroomDb")]
        public double PeakHeadroomDb { get; set; }

        [JsonProperty("maxGainDb")]
        public double MaxGainDb { get; set; }

        [JsonProperty("trimSilence")]
        public bool TrimSilence { get; set; }

        [JsonProperty("normalizeLoudness")]
        public bool NormalizeLoudness { get; set; }

        public bool RequiresAudioRescan(StationAnalysisSettings other) {
            if (other == null)
                return true;
            if (TrimSilence != other.TrimSilence)
                return true;
            if (!other.TrimSilence)
                return false;
            return Math.Abs(SilenceThresholdDb - other.SilenceThresholdDb) > 0.0001d ||
                MinimumSilenceMs != other.MinimumSilenceMs ||
                PaddingMs != other.PaddingMs;
        }
    }

    public sealed class TrackAnalysis {
        [JsonProperty("filePath", NullValueHandling = NullValueHandling.Ignore)]
        public string FilePath { get; set; }

        [JsonProperty("fileSize")]
        public long FileSize { get; set; }

        [JsonProperty("lastWriteUtc")]
        public DateTime LastWriteUtc { get; set; }

        [JsonProperty("durationMs")]
        public uint DurationMs { get; set; }

        [JsonProperty("sourceStartMs", NullValueHandling = NullValueHandling.Ignore)]
        public uint? SourceStartMs { get; set; }

        [JsonProperty("sourceEndMs", NullValueHandling = NullValueHandling.Ignore)]
        public uint? SourceEndMs { get; set; }

        [JsonProperty("audioStartMs")]
        public uint AudioStartMs { get; set; }

        [JsonProperty("audioEndMs")]
        public uint AudioEndMs { get; set; }

        [JsonProperty("integratedLufs")]
        public double? IntegratedLufs { get; set; }

        [JsonProperty("truePeakDb")]
        public double? TruePeakDb { get; set; }

        [JsonProperty("gainDb")]
        public double GainDb { get; set; }

        [JsonProperty("analyzedUtc")]
        public DateTime AnalyzedUtc { get; set; }

        public bool IsCurrentFor(string filePath) {
            try {
                var info = new FileInfo(filePath);
                return info.Exists && info.Length == FileSize && info.LastWriteTimeUtc == LastWriteUtc.ToUniversalTime();
            } catch {
                return false;
            }
        }
    }

    public sealed class StationAnalysis {
        public StationAnalysis() {
            Version = StationAnalysisLoader.CurrentVersion;
            Settings = new StationAnalysisSettings();
            Tracks = new Dictionary<string, TrackAnalysis>(StringComparer.OrdinalIgnoreCase);
        }

        [JsonProperty("version")]
        public int Version { get; set; }

        [JsonProperty("analysisSettings")]
        public StationAnalysisSettings Settings { get; set; }

        [JsonProperty("tracks")]
        public Dictionary<string, TrackAnalysis> Tracks { get; set; }

        public TrackAnalysis GetFreshTrack(string filePath) {
            if (Tracks == null || string.IsNullOrWhiteSpace(filePath))
                return null;
            try {
                string key = AudioAnalysisIdentity.CreateAnalysisKey(filePath, null, null);
                TrackAnalysis analysis;
                return Tracks.TryGetValue(key, out analysis) ? analysis : null;
            } catch {
                return null;
            }
        }

        public TrackAnalysis GetFreshTrack(ResolvedMediaSource source) {
            if (Tracks == null || source == null || string.IsNullOrWhiteSpace(source.FilePath))
                return null;
            try {
                string key = AudioAnalysisIdentity.CreateAnalysisKey(source);
                TrackAnalysis analysis;
                return Tracks.TryGetValue(key, out analysis) ? analysis : null;
            } catch {
                return null;
            }
        }
    }
}
