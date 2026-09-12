using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CustomRadioStations
{
    internal static class StationConfigLoader
    {
        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        internal static bool TryLoad(string stationDirectory, out StationDefinition definition)
        {
            definition = null;
            string configPath = Path.Combine(stationDirectory, AppPaths.StationJsonFileName);
            string folderName = Path.GetFileName(stationDirectory);

            try
            {
                StationConfig config = JsonConvert.DeserializeObject<StationConfig>(
                    File.ReadAllText(configPath), SerializerSettings);
                if (config == null)
                    throw new JsonException("The document did not contain a JSON object.");
                if (string.IsNullOrWhiteSpace(config.Name))
                    throw new JsonException("Required property 'name' is missing or blank.");

                Normalize(config, folderName, configPath);

                string tracksRoot = Path.Combine(stationDirectory, "tracks");
                string commercialsRoot = Path.Combine(stationDirectory, "commercials");
                var resolver = new MediaSourceResolver(message =>
                    Logger.Log("WARNING: Station '" + config.Name + "': " + message));

                IReadOnlyList<string> tracks = resolver.Resolve(config.Tracks, tracksRoot, "track");
                IReadOnlyList<string> commercials = resolver.Resolve(config.Commercials, commercialsRoot, "commercial");
                if (tracks.Count == 0)
                {
                    Logger.Log("WARNING: Skipping JSON station '" + config.Name + "' because it resolved no playable tracks.");
                    return false;
                }

                definition = new StationDefinition
                {
                    Id = config.Id,
                    Name = config.Name.Trim(),
                    Description = config.Description ?? string.Empty,
                    IconPath = ResolveIcon(config.Icon, stationDirectory, config.Name),
                    ConfigPath = configPath,
                    IsLegacyIni = false,
                    Tracks = tracks,
                    Commercials = commercials,
                    Playback = config.Playback,
                    CommercialBreaks = config.CommercialBreaks
                };
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("ERROR: Failed to load station.json for '" + folderName + "' in '" +
                    stationDirectory + "' (" + configPath + "): " + ex.Message + " Skipping station.");
                return false;
            }
        }

        internal static string CreateStableId(string value)
        {
            string source = string.IsNullOrWhiteSpace(value) ? "station" : value.Trim();
            string decomposed = source.Normalize(NormalizationForm.FormD);
            var slug = new StringBuilder();
            bool pendingSeparator = false;

            foreach (char character in decomposed)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
                if (category == UnicodeCategory.NonSpacingMark) continue;

                char lower = char.ToLowerInvariant(character);
                if ((lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9'))
                {
                    if (pendingSeparator && slug.Length > 0) slug.Append('-');
                    slug.Append(lower);
                    pendingSeparator = false;
                }
                else
                {
                    pendingSeparator = true;
                }
            }

            if (slug.Length > 0) return slug.ToString();
            return "station-" + StableHash(source).ToString("x8", CultureInfo.InvariantCulture);
        }

        private static void Normalize(StationConfig config, string folderName, string configPath)
        {
            config.Id = CreateStableId(string.IsNullOrWhiteSpace(config.Id) ? folderName ?? config.Name : config.Id);
            config.Tracks = config.Tracks ?? new List<string> { "*" };
            config.Commercials = config.Commercials ?? new List<string> { "*" };
            config.Playback = config.Playback ?? new PlaybackConfig();
            config.CommercialBreaks = config.CommercialBreaks ?? new CommercialBreakConfig();

            string mode = (config.Playback.Mode ?? "broadcast").Trim().ToLowerInvariant();
            if (mode != "broadcast" && mode != "playlist")
            {
                Logger.Log("WARNING: Invalid playback.mode '" + config.Playback.Mode + "' in '" +
                    configPath + "'; using 'broadcast'.");
                mode = "broadcast";
            }
            config.Playback.Mode = mode;

            float volume = config.Playback.Volume;
            if (float.IsNaN(volume) || float.IsInfinity(volume) || volume < 0f || volume > 1f)
            {
                float normalized = float.IsNaN(volume) || float.IsInfinity(volume)
                    ? 1f
                    : Math.Max(0f, Math.Min(1f, volume));
                Logger.Log("WARNING: playback.volume in '" + configPath + "' was clamped to " +
                    normalized.ToString(CultureInfo.InvariantCulture) + ".");
                config.Playback.Volume = normalized;
            }

            int minimum = config.CommercialBreaks.MinTracksBetween;
            int maximum = config.CommercialBreaks.MaxTracksBetween;
            NormalizeRange(ref minimum, ref maximum, "commercialBreaks tracks-between", configPath);
            config.CommercialBreaks.MinTracksBetween = minimum;
            config.CommercialBreaks.MaxTracksBetween = maximum;

            minimum = config.CommercialBreaks.MinCommercials;
            maximum = config.CommercialBreaks.MaxCommercials;
            NormalizeRange(ref minimum, ref maximum, "commercialBreaks commercials", configPath);
            config.CommercialBreaks.MinCommercials = minimum;
            config.CommercialBreaks.MaxCommercials = maximum;
        }

        private static void NormalizeRange(ref int minimum, ref int maximum, string name, string configPath)
        {
            int originalMinimum = minimum;
            int originalMaximum = maximum;
            minimum = Math.Max(0, minimum);
            maximum = Math.Max(minimum, maximum);
            if (minimum != originalMinimum || maximum != originalMaximum)
            {
                Logger.Log("WARNING: Invalid " + name + " range in '" + configPath +
                    "'; normalized to " + minimum + "-" + maximum + ".");
            }
        }

        private static string ResolveIcon(string configuredIcon, string stationDirectory, string stationName)
        {
            if (string.IsNullOrWhiteSpace(configuredIcon)) return null;

            try
            {
                string normalized = configuredIcon.Trim().Replace('/', Path.DirectorySeparatorChar);
                string path = Path.GetFullPath(Path.IsPathRooted(normalized)
                    ? normalized
                    : Path.Combine(stationDirectory, normalized));
                if (File.Exists(path)) return path;
                Logger.Log("WARNING: Icon for station '" + stationName + "' was not found: " + path);
            }
            catch (Exception ex)
            {
                Logger.Log("WARNING: Invalid icon path for station '" + stationName + "': " + ex.Message);
            }
            return null;
        }

        private static uint StableHash(string value)
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;
            uint hash = offset;
            foreach (byte valueByte in Encoding.UTF8.GetBytes(value))
            {
                hash ^= valueByte;
                hash *= prime;
            }
            return hash;
        }
    }
}
