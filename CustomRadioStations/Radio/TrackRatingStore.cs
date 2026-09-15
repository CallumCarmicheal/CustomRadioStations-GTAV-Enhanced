using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CustomRadioStations {
    internal struct TrackRatingTarget {
        internal readonly string Key;
        internal readonly string AnalysisKey;
        internal readonly string FilePath;
        internal readonly uint? SubTrackStartMs;
        internal readonly string Artist;
        internal readonly string Title;

        internal TrackRatingTarget(string analysisKey, string filePath, uint? subTrackStartMs, string artist, string title) {
            AnalysisKey = analysisKey ?? string.Empty;
            FilePath = string.IsNullOrWhiteSpace(filePath) ? string.Empty : Path.GetFullPath(filePath);
            SubTrackStartMs = subTrackStartMs;
            Artist = artist ?? string.Empty;
            Title = title ?? string.Empty;
            Key = CreateKey(AnalysisKey, SubTrackStartMs);
        }

        internal static string CreateKey(string analysisKey, uint? subTrackStartMs) {
            if (string.IsNullOrWhiteSpace(analysisKey))
                return string.Empty;
            return subTrackStartMs.HasValue
                ? analysisKey + "|subtrack:" + subTrackStartMs.Value.ToString(CultureInfo.InvariantCulture)
                : analysisKey;
        }
    }

    internal sealed class TrackRatingEntry {
        [JsonProperty("file", NullValueHandling = NullValueHandling.Ignore)]
        public string FilePath { get; set; }

        [JsonProperty("artist", NullValueHandling = NullValueHandling.Ignore)]
        public string Artist { get; set; }

        [JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)]
        public string Title { get; set; }

        [JsonProperty("rating")]
        public float Rating { get; set; }

        [JsonProperty("updatedUtc")]
        public DateTime UpdatedUtc { get; set; }
    }

    internal sealed class LegacyTrackRatingEntry {
        [JsonProperty("source")]
        public string SourcePath { get; set; }

        [JsonProperty("segmentStartMs", NullValueHandling = NullValueHandling.Ignore)]
        public uint? SegmentStartMs { get; set; }

        [JsonProperty("segmentEndMs", NullValueHandling = NullValueHandling.Ignore)]
        public uint? SegmentEndMs { get; set; }

        [JsonProperty("subTrackStartMs", NullValueHandling = NullValueHandling.Ignore)]
        public uint? SubTrackStartMs { get; set; }

        [JsonProperty("artist", NullValueHandling = NullValueHandling.Ignore)]
        public string Artist { get; set; }

        [JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)]
        public string Title { get; set; }

        [JsonProperty("rating")]
        public float Rating { get; set; }

        [JsonProperty("updatedUtc")]
        public DateTime UpdatedUtc { get; set; }
    }

    internal sealed class TrackRatingDocument {
        [JsonProperty("version")]
        public int Version { get; set; } = TrackRatingStore.CurrentVersion;

        [JsonProperty("ratings")]
        public Dictionary<string, TrackRatingEntry> Ratings { get; set; } =
            new Dictionary<string, TrackRatingEntry>(StringComparer.OrdinalIgnoreCase);
    }

    internal static class TrackRatingStore {
        internal const int CurrentVersion = 2;
        private static readonly Dictionary<string, TrackRatingEntry> Entries =
            new Dictionary<string, TrackRatingEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, FingerprintCacheEntry> Fingerprints =
            new Dictionary<string, FingerprintCacheEntry>(StringComparer.OrdinalIgnoreCase);

        private static bool loaded;
        private static bool dirty;
        private static DateTime saveAtUtc;
        private static DateTime retryAtUtc;
        private static bool lastSaveFailed;

        internal static bool LastSaveFailed => lastSaveFailed;

        internal static string CreateAnalysisKey(string filePath, uint? startMs, uint? endMs) {
            string fullPath = Path.GetFullPath(filePath);
            var info = new FileInfo(fullPath);
            if (!info.Exists)
                throw new FileNotFoundException("Cannot create a rating identity for a missing audio file.", fullPath);

            FingerprintCacheEntry cached;
            string fingerprint;
            if (Fingerprints.TryGetValue(fullPath, out cached) && cached.FileSize == info.Length &&
                cached.LastWriteUtc == info.LastWriteTimeUtc) {
                fingerprint = cached.Fingerprint;
            } else {
                fingerprint = AudioAnalysisIdentity.CreateFileFingerprint(fullPath);
                Fingerprints[fullPath] = new FingerprintCacheEntry(info.Length, info.LastWriteTimeUtc, fingerprint);
            }

            return AudioAnalysisIdentity.CreateAnalysisKeyFromFingerprint(fingerprint, startMs, endMs);
        }

        internal static void Load() {
            Entries.Clear();
            dirty = false;
            lastSaveFailed = false;
            saveAtUtc = DateTime.MinValue;
            retryAtUtc = DateTime.MinValue;
            loaded = true;

            if (!File.Exists(AppPaths.TrackRatingsFile))
                return;

            try {
                JObject document = JObject.Parse(File.ReadAllText(AppPaths.TrackRatingsFile));
                int version = document.Value<int?>("version") ?? 1;
                JToken ratings = document["ratings"];
                if (ratings is JObject)
                    LoadHashedRatings((JObject)ratings);
                else if (ratings is JArray)
                    MigratePathRatings((JArray)ratings);

                if (version > CurrentVersion)
                    Logger.Log("WARNING: track-ratings.json was written by a newer format version (" + version + "). Known fields were loaded.");
            } catch (Exception ex) {
                Logger.Log("WARNING: Failed to load track ratings from '" + AppPaths.TrackRatingsFile + "': " + ex.Message);
            }
        }

        internal static float GetRating(TrackRatingTarget target) {
            EnsureLoaded();
            TrackRatingEntry entry;
            return Entries.TryGetValue(target.Key, out entry) ? NormalizeRating(entry.Rating) : 0f;
        }

        internal static float ChangeRating(TrackRatingTarget target, float delta) {
            float current = GetRating(target);
            return SetRating(target, NormalizeRating(current + delta));
        }

        internal static float SetRating(TrackRatingTarget target, float rating) {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(target.Key))
                return 0f;

            float normalized = NormalizeRating(rating);
            if (normalized <= 0f) {
                if (!Entries.Remove(target.Key))
                    return 0f;
                MarkDirty();
                return 0f;
            }

            TrackRatingEntry entry;
            if (!Entries.TryGetValue(target.Key, out entry)) {
                entry = new TrackRatingEntry();
                Entries[target.Key] = entry;
            }

            // The hash/key is authoritative. These fields are only human-readable hints and are
            // deliberately refreshed when the user rates the song again from a new location.
            entry.FilePath = NullIfWhiteSpace(target.FilePath);
            entry.Artist = NullIfWhiteSpace(target.Artist);
            entry.Title = NullIfWhiteSpace(target.Title);
            entry.Rating = normalized;
            entry.UpdatedUtc = DateTime.UtcNow;
            MarkDirty();
            return normalized;
        }

        internal static void Update() {
            if (!dirty)
                return;

            DateTime now = DateTime.UtcNow;
            if (now < saveAtUtc || now < retryAtUtc)
                return;

            if (Save()) {
                dirty = false;
                lastSaveFailed = false;
            } else {
                lastSaveFailed = true;
                retryAtUtc = now.AddSeconds(2);
            }
        }

        internal static void Flush() {
            if (!dirty)
                return;
            if (Save()) {
                dirty = false;
                lastSaveFailed = false;
            } else {
                lastSaveFailed = true;
            }
        }

        internal static float NormalizeRating(float rating) {
            if (float.IsNaN(rating) || float.IsInfinity(rating))
                return 0f;
            float clamped = Math.Max(0f, Math.Min(5f, rating));
            return (float)(Math.Round(clamped * 2f, MidpointRounding.AwayFromZero) / 2d);
        }

        private static void LoadHashedRatings(JObject ratings) {
            foreach (JProperty property in ratings.Properties()) {
                if (string.IsNullOrWhiteSpace(property.Name) || !(property.Value is JObject))
                    continue;

                try {
                    TrackRatingEntry entry = property.Value.ToObject<TrackRatingEntry>();
                    if (entry == null)
                        continue;
                    float normalized = NormalizeRating(entry.Rating);
                    if (normalized <= 0f)
                        continue;
                    entry.Rating = normalized;
                    entry.FilePath = NullIfWhiteSpace(entry.FilePath);
                    Entries[property.Name] = entry;
                } catch (Exception ex) {
                    Logger.Log("WARNING: Ignoring invalid hashed track rating '" + property.Name + "': " + ex.Message);
                }
            }
        }

        private static void MigratePathRatings(JArray ratings) {
            PreserveLegacyBackup();
            int migrated = 0;
            int skipped = 0;
            StationAnalysis analysis;
            if (!StationAnalysisLoader.TryLoad(AppPaths.RootDirectory, out analysis))
                analysis = null;

            foreach (JToken token in ratings) {
                LegacyTrackRatingEntry legacy;
                try {
                    legacy = token.ToObject<LegacyTrackRatingEntry>();
                } catch {
                    skipped++;
                    continue;
                }

                if (legacy == null || string.IsNullOrWhiteSpace(legacy.SourcePath) || NormalizeRating(legacy.Rating) <= 0f) {
                    skipped++;
                    continue;
                }

                try {
                    string fullPath = Path.GetFullPath(legacy.SourcePath);
                    string analysisKey;
                    if (!TryResolveLegacyAnalysisKey(fullPath, legacy.SegmentStartMs, legacy.SegmentEndMs, analysis, out analysisKey)) {
                        skipped++;
                        continue;
                    }
                    string key = TrackRatingTarget.CreateKey(analysisKey, legacy.SubTrackStartMs);
                    Entries[key] = new TrackRatingEntry {
                        FilePath = fullPath,
                        Artist = NullIfWhiteSpace(legacy.Artist),
                        Title = NullIfWhiteSpace(legacy.Title),
                        Rating = NormalizeRating(legacy.Rating),
                        UpdatedUtc = legacy.UpdatedUtc == default(DateTime) ? DateTime.UtcNow : legacy.UpdatedUtc
                    };
                    migrated++;
                } catch (Exception ex) {
                    skipped++;
                    Logger.Log("WARNING: Could not migrate legacy track rating for '" + legacy.SourcePath + "': " + ex.Message);
                }
            }

            if (migrated > 0) {
                dirty = true;
                saveAtUtc = DateTime.UtcNow.AddMilliseconds(300);
                Logger.Log("Migrated " + migrated + " path-based track rating" + (migrated == 1 ? string.Empty : "s") +
                    " to sampled-content hashes.");
            }
            if (skipped > 0)
                Logger.Log("WARNING: " + skipped + " legacy track rating" + (skipped == 1 ? string.Empty : "s") +
                    " could not be migrated because the original file was unavailable or invalid.");
        }

        private static bool TryResolveLegacyAnalysisKey(string fullPath, uint? startMs, uint? endMs,
            StationAnalysis analysis, out string analysisKey) {
            analysisKey = string.Empty;
            if (File.Exists(fullPath)) {
                analysisKey = CreateAnalysisKey(fullPath, startMs, endMs);
                return true;
            }

            if (analysis == null || analysis.Tracks == null || analysis.Tracks.Count == 0)
                return false;

            string suffix = startMs.HasValue || endMs.HasValue
                ? "|segment:" + (startMs.HasValue ? startMs.Value.ToString(CultureInfo.InvariantCulture) : "0") + "-" +
                    (endMs.HasValue ? endMs.Value.ToString(CultureInfo.InvariantCulture) : "eof")
                : null;
            var candidates = analysis.Tracks
                .Where(pair => pair.Value != null && !string.IsNullOrWhiteSpace(pair.Value.FilePath) &&
                    string.Equals(Path.GetFullPath(pair.Value.FilePath), fullPath, StringComparison.OrdinalIgnoreCase))
                .Where(pair => suffix == null
                    ? pair.Key.IndexOf("|segment:", StringComparison.OrdinalIgnoreCase) < 0
                    : pair.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToArray();

            if (candidates.Length != 1)
                return false;
            analysisKey = candidates[0];
            return !string.IsNullOrWhiteSpace(analysisKey);
        }

        private static void PreserveLegacyBackup() {
            try {
                string backupPath = AppPaths.TrackRatingsFile + ".v1.bak";
                if (!File.Exists(backupPath) && File.Exists(AppPaths.TrackRatingsFile))
                    File.Copy(AppPaths.TrackRatingsFile, backupPath, false);
            } catch (Exception ex) {
                Logger.Log("WARNING: Could not preserve the legacy track-ratings backup: " + ex.Message);
            }
        }

        private static bool Save() {
            string temporaryPath = AppPaths.TrackRatingsFile + ".tmp";
            try {
                Directory.CreateDirectory(AppPaths.RootDirectory);
                var document = new TrackRatingDocument {
                    Version = CurrentVersion,
                    Ratings = Entries
                        .Where(pair => pair.Value != null && pair.Value.Rating > 0f)
                        .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
                };

                File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(document, Formatting.Indented));
                if (File.Exists(AppPaths.TrackRatingsFile)) {
                    string backupPath = AppPaths.TrackRatingsFile + ".bak";
                    try {
                        if (File.Exists(backupPath))
                            File.Delete(backupPath);
                        File.Replace(temporaryPath, AppPaths.TrackRatingsFile, backupPath, true);
                        if (File.Exists(backupPath))
                            File.Delete(backupPath);
                    } catch {
                        File.Delete(AppPaths.TrackRatingsFile);
                        File.Move(temporaryPath, AppPaths.TrackRatingsFile);
                    }
                } else {
                    File.Move(temporaryPath, AppPaths.TrackRatingsFile);
                }
                return true;
            } catch (Exception ex) {
                try {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                } catch { }
                Logger.Log("ERROR: Failed to save track ratings to '" + AppPaths.TrackRatingsFile + "': " + ex.Message);
                return false;
            }
        }

        private static void MarkDirty() {
            dirty = true;
            lastSaveFailed = false;
            saveAtUtc = DateTime.UtcNow.AddMilliseconds(300);
            retryAtUtc = DateTime.MinValue;
        }

        private static void EnsureLoaded() {
            if (!loaded)
                Load();
        }

        private static string NullIfWhiteSpace(string value) {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private sealed class FingerprintCacheEntry {
            internal readonly long FileSize;
            internal readonly DateTime LastWriteUtc;
            internal readonly string Fingerprint;

            internal FingerprintCacheEntry(long fileSize, DateTime lastWriteUtc, string fingerprint) {
                FileSize = fileSize;
                LastWriteUtc = lastWriteUtc;
                Fingerprint = fingerprint;
            }
        }
    }
}
