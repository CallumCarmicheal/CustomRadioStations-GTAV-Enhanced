using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomRadioStations {
    internal static class TrackMetadataReader {
        internal static string ReadDisplayName(string filePath, string fallbackDisplayName) {
            try {
                using (TagLib.File taggedFile = TagLib.File.Create(filePath)) {
                    return FormatDisplayName(taggedFile.Tag.Performers, taggedFile.Tag.Title, fallbackDisplayName);
                }
            } catch (Exception ex) {
                Logger.Log("WARNING: Could not read audio tags from '" + filePath + "': " + ex.Message +
                    ". Using the filename for display metadata.");
                return fallbackDisplayName;
            }
        }

        internal static string FormatDisplayName(IEnumerable<string> performers, string title,
            string fallbackDisplayName) {
            string artist = string.Join(", ", (performers ?? Enumerable.Empty<string>())
                .Select(NormalizeTagValue)
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            string normalizedTitle = NormalizeTagValue(title);

            // Treat the tag pair as one unit. A partial pair is less useful than the
            // existing filename convention because the wheel expects both GTA-style
            // artist and title lines.
            if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(normalizedTitle))
                return fallbackDisplayName;

            return artist.ToUpperInvariant() + "\n" + normalizedTitle;
        }

        internal static string FormatDisplayName(string artist, string title, string fallbackDisplayName) {
            return FormatDisplayName(new[] { artist }, title, fallbackDisplayName);
        }

        private static string NormalizeTagValue(string value) {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            return value.Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}