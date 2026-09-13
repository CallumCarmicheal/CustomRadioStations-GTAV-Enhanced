using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CustomRadioStations {
    public sealed class CueSheet {
        public CueSheet() {
            Tracks = new List<CueTrack>();
        }

        public string FilePath { get; internal set; }
        public string Title { get; internal set; }
        public string Performer { get; internal set; }
        public List<CueTrack> Tracks { get; private set; }
    }

    public sealed class CueTrack {
        public int Number { get; internal set; }
        public string FilePath { get; internal set; }
        public uint StartMs { get; internal set; }
        public uint? EndMs { get; internal set; }
        public string Artist { get; internal set; }
        public string Title { get; internal set; }
    }

    /// <summary>Minimal CUE parser for audio FILE/TRACK/TITLE/PERFORMER/INDEX 01 directives.</summary>
    public static class CueSheetParser {
        public static CueSheet Parse(string cuePath) {
            if (string.IsNullOrWhiteSpace(cuePath))
                throw new ArgumentException("CUE path is required.", nameof(cuePath));

            string fullCuePath = Path.GetFullPath(cuePath);
            if (!File.Exists(fullCuePath))
                throw new FileNotFoundException("CUE sheet was not found.", fullCuePath);

            string cueDirectory = Path.GetDirectoryName(fullCuePath) ?? string.Empty;
            var sheet = new CueSheet { FilePath = fullCuePath };
            string currentFile = null;
            PendingTrack currentTrack = null;

            foreach (string rawLine in File.ReadAllLines(fullCuePath)) {
                string line = (rawLine ?? string.Empty).Trim();
                if (line.Length == 0)
                    continue;

                string command;
                string value;
                SplitCommand(line, out command, out value);
                switch (command) {
                    case "FILE":
                        CommitTrack(sheet, currentTrack, cueDirectory);
                        currentTrack = null;
                        currentFile = ParseFileValue(value);
                        break;
                    case "TRACK":
                        CommitTrack(sheet, currentTrack, cueDirectory);
                        currentTrack = ParseTrack(value, currentFile);
                        break;
                    case "TITLE":
                        if (currentTrack != null)
                            currentTrack.Title = ParseTextValue(value);
                        else
                            sheet.Title = ParseTextValue(value);
                        break;
                    case "PERFORMER":
                        if (currentTrack != null)
                            currentTrack.Performer = ParseTextValue(value);
                        else
                            sheet.Performer = ParseTextValue(value);
                        break;
                    case "INDEX":
                        if (currentTrack != null)
                            ParseIndex(value, currentTrack);
                        break;
                }
            }

            CommitTrack(sheet, currentTrack, cueDirectory);
            if (sheet.Tracks.Count == 0)
                throw new InvalidDataException("CUE sheet contains no AUDIO tracks with INDEX 01 entries: " + fullCuePath);


            foreach (CueTrack track in sheet.Tracks) {
                if (string.IsNullOrWhiteSpace(track.FilePath))
                    throw new InvalidDataException("CUE track " + track.Number + " has no FILE target in '" + fullCuePath + "'.");
                if (!File.Exists(track.FilePath))
                    throw new FileNotFoundException("Audio file referenced by CUE sheet was not found.", track.FilePath);
                if (string.IsNullOrWhiteSpace(track.Artist))
                    track.Artist = sheet.Performer;
            }

            for (int i = 0; i < sheet.Tracks.Count - 1; i++) {
                CueTrack current = sheet.Tracks[i];
                CueTrack next = sheet.Tracks[i + 1];
                if (string.Equals(current.FilePath, next.FilePath, StringComparison.OrdinalIgnoreCase) && next.StartMs > current.StartMs)
                    current.EndMs = next.StartMs;
            }

            return sheet;
        }

        public static bool TryParseCueTime(string value, out uint milliseconds) {
            milliseconds = 0u;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string[] parts = value.Trim().Split(':');
            if (parts.Length != 3)
                return false;

            int minutes;
            int seconds;
            int frames;
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out minutes) || minutes < 0 ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out seconds) || seconds < 0 || seconds > 59 ||
                !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out frames) || frames < 0 || frames > 74)
                return false;

            ulong total = ((ulong)minutes * 60UL + (ulong)seconds) * 1000UL;
            total += ((ulong)frames * 1000UL + 37UL) / 75UL;
            if (total > uint.MaxValue)
                return false;
            milliseconds = (uint)total;
            return true;
        }

        private static void CommitTrack(CueSheet sheet, PendingTrack pending, string cueDirectory) {
            if (pending == null || !pending.IsAudio || !pending.StartMs.HasValue)
                return;

            string filePath = null;
            if (!string.IsNullOrWhiteSpace(pending.File)) {
                string normalized = pending.File.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                filePath = Path.GetFullPath(Path.IsPathRooted(normalized) ? normalized : Path.Combine(cueDirectory, normalized));
            }

            sheet.Tracks.Add(new CueTrack {
                Number = pending.Number,
                FilePath = filePath,
                StartMs = pending.StartMs.Value,
                Artist = pending.Performer,
                Title = pending.Title
            });
        }

        private static PendingTrack ParseTrack(string value, string currentFile) {
            string[] parts = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int number = 0;
            if (parts.Length > 0)
                int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out number);
            bool isAudio = parts.Length > 1 && string.Equals(parts[1], "AUDIO", StringComparison.OrdinalIgnoreCase);
            return new PendingTrack { Number = number, File = currentFile, IsAudio = isAudio };
        }

        private static void ParseIndex(string value, PendingTrack track) {
            string[] parts = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0] != "01")
                return;
            uint start;
            if (TryParseCueTime(parts[1], out start))
                track.StartMs = start;
        }

        private static string ParseFileValue(string value) {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            string trimmed = value.Trim();
            if (trimmed[0] == '"') {
                int end = trimmed.IndexOf('"', 1);
                return end > 0 ? trimmed.Substring(1, end - 1) : trimmed.Trim('"');
            }
            int separator = trimmed.IndexOfAny(new[] { ' ', '\t' });
            return separator < 0 ? trimmed : trimmed.Substring(0, separator);
        }

        private static string ParseTextValue(string value) {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            string trimmed = value.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
                return trimmed.Substring(1, trimmed.Length - 2).Replace("\"\"", "\"");
            return trimmed;
        }

        private static void SplitCommand(string line, out string command, out string value) {
            int separator = line.IndexOfAny(new[] { ' ', '\t' });
            if (separator < 0) {
                command = line.ToUpperInvariant();
                value = string.Empty;
                return;
            }
            command = line.Substring(0, separator).ToUpperInvariant();
            value = line.Substring(separator + 1).Trim();
        }

        private sealed class PendingTrack {
            internal int Number { get; set; }
            internal string File { get; set; }
            internal bool IsAudio { get; set; }
            internal uint? StartMs { get; set; }
            internal string Performer { get; set; }
            internal string Title { get; set; }
        }
    }
}
