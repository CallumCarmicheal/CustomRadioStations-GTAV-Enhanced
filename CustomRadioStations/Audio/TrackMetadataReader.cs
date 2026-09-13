using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomRadioStations {
    public sealed class TrackMetadataInfo {
        public string Artist { get; set; }
        public string Title { get; set; }
        public uint DurationMs { get; set; }
    }

    public static class TrackMetadataReader {
        public static TrackMetadataInfo ReadMetadata(string filePath, Action<string> warningSink = null) {
            try {
                using (TagLib.File taggedFile = TagLib.File.Create(filePath)) {
                    string artist = string.Join(", ", (taggedFile.Tag.Performers ?? new string[0])
                        .Select(NormalizeTagValue)
                        .Where(value => !string.IsNullOrWhiteSpace(value)));
                    double milliseconds = taggedFile.Properties.Duration.TotalMilliseconds;
                    return new TrackMetadataInfo {
                        Artist = artist,
                        Title = NormalizeTagValue(taggedFile.Tag.Title),
                        DurationMs = milliseconds <= 0d ? 0u : milliseconds >= uint.MaxValue ? uint.MaxValue : (uint)Math.Round(milliseconds)
                    };
                }
            } catch (Exception ex) {
                if (warningSink != null)
                    warningSink("Could not read audio tags from '" + filePath + "': " + ex.Message);
                return new TrackMetadataInfo();
            }
        }

        public static string ReadDisplayName(string filePath, string fallbackDisplayName) {
            return ReadDisplayName(filePath, fallbackDisplayName, null, null);
        }

        public static string ReadDisplayName(string filePath, string fallbackDisplayName, string overrideArtist, string overrideTitle) {
            TrackMetadataInfo metadata = ReadMetadata(filePath, message => Logger.Log("WARNING: " + message + ". Using the filename for display metadata."));
            string artist = FirstNonEmpty(overrideArtist, metadata.Artist);
            string title = FirstNonEmpty(overrideTitle, metadata.Title);
            return FormatDisplayName(artist, title, fallbackDisplayName);
        }

        public static string FormatDisplayName(IEnumerable<string> performers, string title, string fallbackDisplayName) {
            string artist = string.Join(", ", (performers ?? Enumerable.Empty<string>())
                .Select(NormalizeTagValue)
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            return FormatDisplayName(artist, title, fallbackDisplayName);
        }

        public static string FormatDisplayName(string artist, string title, string fallbackDisplayName) {
            string normalizedArtist = NormalizeTagValue(artist);
            string normalizedTitle = NormalizeTagValue(title);
            if (string.IsNullOrWhiteSpace(normalizedArtist) || string.IsNullOrWhiteSpace(normalizedTitle))
                return fallbackDisplayName;
            return normalizedArtist.ToUpperInvariant() + "\n" + normalizedTitle;
        }

        private static string FirstNonEmpty(string first, string second) {
            string normalized = NormalizeTagValue(first);
            return !string.IsNullOrWhiteSpace(normalized) ? normalized : NormalizeTagValue(second);
        }

        private static string NormalizeTagValue(string value) {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            return value.Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
