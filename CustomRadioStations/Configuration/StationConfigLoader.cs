using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CustomRadioStations {
    public static class StationConfigLoader {
        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        public static StationConfig LoadConfig(string stationDirectory, Action<string> warningSink = null) {
            if (string.IsNullOrWhiteSpace(stationDirectory))
                throw new ArgumentException("Station directory is required.", nameof(stationDirectory));

            string configPath = Path.Combine(stationDirectory, "station.json");
            string folderName = Path.GetFileName(Path.GetFullPath(stationDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            StationConfig config = JsonConvert.DeserializeObject<StationConfig>(File.ReadAllText(configPath), SerializerSettings);
            if (config == null)
                throw new JsonException("The document did not contain a JSON object.");
            if (string.IsNullOrWhiteSpace(config.Name))
                throw new JsonException("Required property 'name' is missing or blank.");
            Normalize(config, folderName, configPath, warningSink ?? (_ => { }));
            return config;
        }

        public static bool TryLoad(string stationDirectory, out StationDefinition definition) {
            return TryLoad(stationDirectory, out definition, message => Logger.Log(message));
        }

        public static bool TryLoad(string stationDirectory, out StationDefinition definition, Action<string> warningSink) {
            definition = null;
            Action<string> warn = warningSink ?? (_ => { });
            string configPath = Path.Combine(stationDirectory, "station.json");
            string folderName = Path.GetFileName(stationDirectory);

            try {
                StationConfig config = LoadConfig(stationDirectory, warn);
                string tracksRoot = Path.Combine(stationDirectory, "tracks");
                string commercialsRoot = Path.Combine(stationDirectory, "commercials");
                var resolver = new MediaSourceResolver(message => warn("WARNING: Station '" + config.Name + "': " + message));

                IReadOnlyList<ResolvedMediaSource> tracks = resolver.Resolve(config.Tracks, tracksRoot, "track");
                IReadOnlyList<ResolvedMediaSource> commercials = resolver.Resolve(config.Commercials, commercialsRoot, "commercial");
                if (tracks.Count == 0) {
                    warn("WARNING: Skipping JSON station '" + config.Name + "' because it resolved no playable tracks.");
                    return false;
                }

                StationAnalysis analysis;
                if (StationAnalysisLoader.TryLoad(stationDirectory, out analysis, message => warn("WARNING: Station '" + config.Name + "': " + message))) {
                    AttachAnalysis(tracks, analysis);
                    AttachAnalysis(commercials, analysis);
                }

                definition = new StationDefinition {
                    Id = config.Id,
                    Name = config.Name.Trim(),
                    Description = config.Description ?? string.Empty,
                    IconPath = ResolveIcon(config.Icon, stationDirectory, config.Name, warn),
                    ConfigPath = configPath,
                    IsLegacyIni = false,
                    Tracks = tracks,
                    Commercials = commercials,
                    Playback = config.Playback,
                    CommercialBreaks = config.CommercialBreaks,
                    Analysis = analysis
                };
                return true;
            } catch (Exception ex) {
                warn("ERROR: Failed to load station.json for '" + folderName + "' in '" +
                    stationDirectory + "' (" + configPath + "): " + ex.Message + " Skipping station.");
                return false;
            }
        }

        public static string CreateStableId(string value) {
            string source = string.IsNullOrWhiteSpace(value) ? "station" : value.Trim();
            string decomposed = source.Normalize(NormalizationForm.FormD);
            var slug = new StringBuilder();
            bool pendingSeparator = false;

            foreach (char character in decomposed) {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
                if (category == UnicodeCategory.NonSpacingMark)
                    continue;

                char lower = char.ToLowerInvariant(character);
                if ((lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9')) {
                    if (pendingSeparator && slug.Length > 0)
                        slug.Append('-');
                    slug.Append(lower);
                    pendingSeparator = false;
                } else {
                    pendingSeparator = true;
                }
            }

            if (slug.Length > 0)
                return slug.ToString();
            return "station-" + StableHash(source).ToString("x8", CultureInfo.InvariantCulture);
        }

        private static void AttachAnalysis(IEnumerable<ResolvedMediaSource> sources, StationAnalysis analysis) {
            if (analysis == null || sources == null)
                return;
            foreach (ResolvedMediaSource source in sources)
                source.Analysis = analysis.GetFreshTrack(source);
        }

        private static void Normalize(StationConfig config, string folderName, string configPath, Action<string> warn) {
            config.Id = CreateStableId(string.IsNullOrWhiteSpace(config.Id) ? folderName ?? config.Name : config.Id);
            config.Tracks = config.Tracks ?? new List<MediaSourceConfig> { new MediaSourceConfig("*") };
            config.Commercials = config.Commercials ?? new List<MediaSourceConfig> { new MediaSourceConfig("*") };
            config.Playback = config.Playback ?? new PlaybackConfig();
            config.CommercialBreaks = config.CommercialBreaks ?? new CommercialBreakConfig();

            NormalizeMediaEntries(config.Tracks, "tracks", configPath);
            NormalizeMediaEntries(config.Commercials, "commercials", configPath);

            string mode = (config.Playback.Mode ?? "broadcast").Trim().ToLowerInvariant();
            if (mode != "broadcast" && mode != "playlist") {
                warn("WARNING: Invalid playback.mode '" + config.Playback.Mode + "' in '" +
                    configPath + "'; using 'broadcast'.");
                mode = "broadcast";
            }
            config.Playback.Mode = mode;

            float volume = config.Playback.Volume;
            if (float.IsNaN(volume) || float.IsInfinity(volume) || volume < 0f || volume > 1f) {
                float normalized = float.IsNaN(volume) || float.IsInfinity(volume)
                    ? 1f
                    : Math.Max(0f, Math.Min(1f, volume));
                warn("WARNING: playback.volume in '" + configPath + "' was clamped to " +
                    normalized.ToString(CultureInfo.InvariantCulture) + ".");
                config.Playback.Volume = normalized;
            }

            int minimum = config.CommercialBreaks.MinTracksBetween;
            int maximum = config.CommercialBreaks.MaxTracksBetween;
            NormalizeRange(ref minimum, ref maximum, "commercialBreaks tracks-between", configPath, warn);
            config.CommercialBreaks.MinTracksBetween = minimum;
            config.CommercialBreaks.MaxTracksBetween = maximum;

            minimum = config.CommercialBreaks.MinCommercials;
            maximum = config.CommercialBreaks.MaxCommercials;
            NormalizeRange(ref minimum, ref maximum, "commercialBreaks commercials", configPath, warn);
            config.CommercialBreaks.MinCommercials = minimum;
            config.CommercialBreaks.MaxCommercials = maximum;
        }

        private static void NormalizeMediaEntries(IEnumerable<MediaSourceConfig> entries, string propertyName, string configPath) {
            int index = 0;
            foreach (MediaSourceConfig source in entries) {
                if (source == null) {
                    index++;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(source.Cue) && !string.IsNullOrWhiteSpace(source.File))
                    throw new JsonException(propertyName + "[" + index + "] cannot specify both file and cue in '" +
                        configPath + "'. The CUE sheet FILE directive defines the audio file.");

                if (string.IsNullOrWhiteSpace(source.Cue) && !string.IsNullOrWhiteSpace(source.File) &&
                    string.Equals(Path.GetExtension(source.File.Trim()), ".cue", StringComparison.OrdinalIgnoreCase)) {
                    source.Cue = source.File;
                    source.File = null;
                }

                if (!string.IsNullOrWhiteSpace(source.Cue)) {
                    string cueMode = (source.CueMode ?? "split").Trim().ToLowerInvariant();
                    if (cueMode == "single" || cueMode == "subtracks")
                        cueMode = "continuous";
                    if (cueMode == "individual" || cueMode == "tracks")
                        cueMode = "split";
                    if (cueMode != "split" && cueMode != "continuous")
                        throw new JsonException("Invalid " + propertyName + "[" + index + "].cueMode value '" + source.CueMode +
                            "' in '" + configPath + "'. Use 'split' or 'continuous'.");
                    source.CueMode = cueMode;
                } else if (string.IsNullOrWhiteSpace(source.File)) {
                    throw new JsonException(propertyName + "[" + index + "] must specify file or cue in '" + configPath + "'.");
                }

                source.StartMs = ParseOptionalTime(source.Start, propertyName, index, "start", configPath);
                source.EndMs = ParseOptionalTime(source.End, propertyName, index, "end", configPath);
                if (source.StartMs.HasValue && source.EndMs.HasValue && source.EndMs.Value <= source.StartMs.Value)
                    throw new JsonException(propertyName + "[" + index + "].end must be after start in '" + configPath + "'.");
                if (!string.IsNullOrWhiteSpace(source.Cue) && source.CueMode == "split" &&
                    (source.StartMs.HasValue || source.EndMs.HasValue))
                    throw new JsonException(propertyName + "[" + index + "] cannot combine start/end with cueMode 'split' in '" +
                        configPath + "'; CUE INDEX boundaries define the individual tracks.");
                index++;
            }
        }

        private static uint? ParseOptionalTime(string value, string propertyName, int index, string field, string configPath) {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            uint parsed;
            if (!MediaTimeParser.TryParse(value, out parsed))
                throw new JsonException("Invalid " + propertyName + "[" + index + "]." + field + " value '" + value +
                    "' in '" + configPath + "'. Use MM:SS[.fff] or HH:MM:SS[.fff].");
            return parsed;
        }

        private static void NormalizeRange(ref int minimum, ref int maximum, string name, string configPath, Action<string> warn) {
            int originalMinimum = minimum;
            int originalMaximum = maximum;
            minimum = Math.Max(0, minimum);
            maximum = Math.Max(minimum, maximum);
            if (minimum != originalMinimum || maximum != originalMaximum) {
                warn("WARNING: Invalid " + name + " range in '" + configPath +
                    "'; normalized to " + minimum + "-" + maximum + ".");
            }
        }

        private static string ResolveIcon(string configuredIcon, string stationDirectory, string stationName, Action<string> warn) {
            if (string.IsNullOrWhiteSpace(configuredIcon))
                return null;

            try {
                string normalized = configuredIcon.Trim().Replace('/', Path.DirectorySeparatorChar);
                string path = Path.GetFullPath(Path.IsPathRooted(normalized)
                    ? normalized
                    : Path.Combine(stationDirectory, normalized));
                if (StationIconVariantResolver.HasAnyVariant(path))
                    return path;
                warn("WARNING: Icon for station '" + stationName + "' was not found: " + path);
            } catch (Exception ex) {
                warn("WARNING: Invalid icon path for station '" + stationName + "': " + ex.Message);
            }
            return null;
        }

        private static uint StableHash(string value) {
            const uint offset = 2166136261;
            const uint prime = 16777619;
            uint hash = offset;
            foreach (byte valueByte in Encoding.UTF8.GetBytes(value)) {
                hash ^= valueByte;
                hash *= prime;
            }
            return hash;
        }
    }
}
