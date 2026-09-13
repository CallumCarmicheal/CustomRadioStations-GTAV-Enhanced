using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CustomRadioStations {
    public sealed class ResolvedMediaSource {
        public ResolvedMediaSource(string filePath) : this(filePath, null, null, null, null, null, null, false) {
        }

        public ResolvedMediaSource(string filePath, uint? startMs, uint? endMs, string artist, string title,
            IEnumerable<Track> subTracks = null, string cuePath = null, bool allowAnalysisTrimWithinBounds = false) {
            FilePath = Path.GetFullPath(filePath);
            StartMs = startMs;
            EndMs = endMs;
            Artist = artist;
            Title = title;
            SubTracks = subTracks == null ? new Track[0] : subTracks.OrderBy(track => track.StartTime).ToArray();
            CuePath = cuePath;
            AllowAnalysisTrimWithinBounds = allowAnalysisTrimWithinBounds;
            AnalysisKey = CreateAnalysisKey(FilePath, startMs, endMs);
        }

        public string FilePath { get; }
        public uint? StartMs { get; }
        public uint? EndMs { get; }
        public string Artist { get; }
        public string Title { get; }
        public IReadOnlyList<Track> SubTracks { get; }
        public string CuePath { get; }
        public bool AllowAnalysisTrimWithinBounds { get; }
        public string AnalysisKey { get; }
        public TrackAnalysis Analysis { get; internal set; }

        public bool IsSegment { get { return StartMs.HasValue || EndMs.HasValue; } }

        public static string CreateAnalysisKey(string filePath, uint? startMs, uint? endMs) {
            string fullPath = Path.GetFullPath(filePath);
            if (!startMs.HasValue && !endMs.HasValue)
                return fullPath;
            return fullPath + "|segment:" + (startMs.HasValue ? startMs.Value.ToString() : "0") + "-" +
                (endMs.HasValue ? endMs.Value.ToString() : "eof");
        }
    }

    /// <summary>
    /// Resolves exact paths, directories, glob expressions and CUE sheets into media sources.
    /// Later entries replace earlier entries for the same logical source identity.
    /// </summary>
    public sealed class MediaSourceResolver {
        private static readonly HashSet<string> SupportedExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp3", ".flac", ".ogg", ".wav"
            };

        private readonly Action<string> warn;

        public MediaSourceResolver(Action<string> warningSink = null) {
            warn = warningSink ?? (_ => { });
        }

        public IReadOnlyList<ResolvedMediaSource> Resolve(IEnumerable<MediaSourceConfig> entries, string defaultRoot, string sourceKind) {
            var results = new Dictionary<string, ResolvedMediaSource>(StringComparer.OrdinalIgnoreCase);
            if (entries == null)
                return new ResolvedMediaSource[0];

            foreach (MediaSourceConfig entry in entries) {
                try {
                    ResolveEntry(entry, defaultRoot, sourceKind, results);
                } catch (Exception ex) {
                    string label = entry == null ? "<null>" : !string.IsNullOrWhiteSpace(entry.Cue) ? entry.Cue : entry.File ?? "<null>";
                    warn("Could not resolve " + sourceKind + " source '" + label + "': " + ex.Message);
                }
            }

            return results.Values
                .OrderBy(source => source.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(source => source.StartMs ?? 0u)
                .ToArray();
        }

        public IReadOnlyList<string> Resolve(IEnumerable<string> entries, string defaultRoot, string sourceKind) {
            IEnumerable<MediaSourceConfig> typed = entries == null
                ? Enumerable.Empty<MediaSourceConfig>()
                : entries.Select(entry => new MediaSourceConfig(entry));
            return Resolve(typed, defaultRoot, sourceKind).Select(source => source.FilePath).ToArray();
        }

        private void ResolveEntry(MediaSourceConfig entry, string defaultRoot, string sourceKind,
            IDictionary<string, ResolvedMediaSource> results) {
            if (entry == null) {
                warn("Skipping blank " + sourceKind + " source entry.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(entry.Cue) && !string.IsNullOrWhiteSpace(entry.File))
                throw new InvalidOperationException("A media source cannot specify both file and cue. The CUE sheet FILE directive defines the audio file.");

            if (!string.IsNullOrWhiteSpace(entry.Cue)) {
                AddCue(entry, defaultRoot, sourceKind, results);
                return;
            }

            if (string.IsNullOrWhiteSpace(entry.File)) {
                warn("Skipping blank " + sourceKind + " source entry.");
                return;
            }

            string normalizedEntry = NormalizeSeparators(entry.File.Trim());
            if (string.Equals(Path.GetExtension(normalizedEntry), ".cue", StringComparison.OrdinalIgnoreCase)) {
                var cueEntry = CloneForCue(entry, normalizedEntry);
                AddCue(cueEntry, defaultRoot, sourceKind, results);
                return;
            }

            if (normalizedEntry == "*") {
                AddDirectory(defaultRoot, entry, sourceKind, results);
                return;
            }

            string combined = Path.IsPathRooted(normalizedEntry)
                ? normalizedEntry
                : Path.Combine(defaultRoot, normalizedEntry);

            if (ContainsWildcard(combined)) {
                AddGlob(NormalizeSeparators(combined), entry, sourceKind, results);
                return;
            }

            string fullPath = Path.GetFullPath(combined);
            if (File.Exists(fullPath)) {
                AddFile(fullPath, entry, sourceKind, results);
                return;
            }

            if (Directory.Exists(fullPath)) {
                AddDirectory(fullPath, entry, sourceKind, results);
                return;
            }

            warn("Skipping missing " + sourceKind + " path: " + fullPath);
        }

        private void AddCue(MediaSourceConfig entry, string defaultRoot, string sourceKind,
            IDictionary<string, ResolvedMediaSource> results) {
            string cueValue = NormalizeSeparators(entry.Cue.Trim());
            string cuePath = Path.GetFullPath(Path.IsPathRooted(cueValue) ? cueValue : Path.Combine(defaultRoot, cueValue));

            CueSheet sheet = CueSheetParser.Parse(cuePath);
            string mode = NormalizeCueMode(entry.CueMode);
            if (mode == "continuous")
                AddContinuousCue(sheet, entry, sourceKind, results);
            else
                AddSplitCue(sheet, entry, sourceKind, results);
        }

        private void AddSplitCue(CueSheet sheet, MediaSourceConfig entry, string sourceKind,
            IDictionary<string, ResolvedMediaSource> results) {
            var replacedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CueTrack cueTrack in sheet.Tracks) {
                if (!SupportedExtensions.Contains(Path.GetExtension(cueTrack.FilePath))) {
                    warn("Skipping unsupported " + sourceKind + " file referenced by CUE: " + cueTrack.FilePath);
                    continue;
                }
                if (replacedFiles.Add(cueTrack.FilePath))
                    RemoveSourcesForFile(results, cueTrack.FilePath, true);

                var source = new ResolvedMediaSource(
                    cueTrack.FilePath,
                    cueTrack.StartMs,
                    cueTrack.EndMs,
                    FirstNonEmpty(entry.Artist, cueTrack.Artist),
                    FirstNonEmpty(entry.Title, cueTrack.Title),
                    null,
                    sheet.FilePath,
                    true);
                results[source.AnalysisKey] = source;
            }
        }

        private void AddContinuousCue(CueSheet sheet, MediaSourceConfig entry, string sourceKind,
            IDictionary<string, ResolvedMediaSource> results) {
            foreach (IGrouping<string, CueTrack> group in sheet.Tracks.GroupBy(track => track.FilePath, StringComparer.OrdinalIgnoreCase)) {
                if (!SupportedExtensions.Contains(Path.GetExtension(group.Key))) {
                    warn("Skipping unsupported " + sourceKind + " file referenced by CUE: " + group.Key);
                    continue;
                }

                List<Track> subTracks = group.OrderBy(track => track.StartMs)
                    .Select(track => new Track(track.StartMs,
                        FirstNonEmpty(entry.Artist, track.Artist),
                        FirstNonEmpty(entry.Title, track.Title)))
                    .ToList();

                RemoveSourcesForFile(results, group.Key, true);
                var source = new ResolvedMediaSource(group.Key, entry.StartMs, entry.EndMs,
                    entry.Artist, entry.Title, subTracks, sheet.FilePath);
                results[source.AnalysisKey] = source;
            }
        }

        private void AddDirectory(string directory, MediaSourceConfig entry, string sourceKind,
            IDictionary<string, ResolvedMediaSource> results) {
            string fullDirectory = Path.GetFullPath(directory);
            if (!Directory.Exists(fullDirectory)) {
                warn("Skipping missing " + sourceKind + " directory: " + fullDirectory);
                return;
            }

            var pending = new Stack<string>();
            pending.Push(fullDirectory);
            while (pending.Count > 0) {
                string current = pending.Pop();
                try {
                    foreach (string file in Directory.GetFiles(current))
                        AddFile(file, entry, sourceKind, results, false);

                    foreach (string child in Directory.GetDirectories(current)
                        .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
                        pending.Push(child);
                } catch (Exception ex) {
                    warn("Could not scan " + sourceKind + " directory '" + current + "': " + ex.Message);
                }
            }
        }

        private void AddGlob(string fullPattern, MediaSourceConfig entry, string sourceKind,
            IDictionary<string, ResolvedMediaSource> results) {
            string searchRoot = GetSearchRoot(fullPattern);
            if (string.IsNullOrEmpty(searchRoot) || !Directory.Exists(searchRoot)) {
                warn("Skipping " + sourceKind + " glob with missing base directory: " + fullPattern);
                return;
            }

            var matcher = new Regex(GlobToRegex(fullPattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var matches = new Dictionary<string, ResolvedMediaSource>(StringComparer.OrdinalIgnoreCase);
            AddDirectory(searchRoot, entry, sourceKind, matches);
            int matched = 0;
            foreach (ResolvedMediaSource source in matches.Values) {
                if (!matcher.IsMatch(NormalizeSeparators(source.FilePath)))
                    continue;
                if (source.IsSegment)
                    results.Remove(ResolvedMediaSource.CreateAnalysisKey(source.FilePath, null, null));
                else
                    RemoveSourcesForFile(results, source.FilePath, true);
                results[source.AnalysisKey] = source;
                matched++;
            }

            if (matched == 0)
                warn("No supported files matched " + sourceKind + " glob: " + fullPattern);
        }

        private void AddFile(string path, MediaSourceConfig entry, string sourceKind,
            IDictionary<string, ResolvedMediaSource> results, bool warnIfUnsupported = true) {
            string fullPath = Path.GetFullPath(path);
            if (SupportedExtensions.Contains(Path.GetExtension(fullPath))) {
                var source = new ResolvedMediaSource(fullPath, entry.StartMs, entry.EndMs, entry.Artist, entry.Title);
                if (source.IsSegment)
                    results.Remove(ResolvedMediaSource.CreateAnalysisKey(fullPath, null, null));
                else
                    RemoveSourcesForFile(results, fullPath, true);
                results[source.AnalysisKey] = source;
            } else if (warnIfUnsupported) {
                warn("Skipping unsupported " + sourceKind + " file: " + fullPath);
            }
        }

        private static void RemoveSourcesForFile(IDictionary<string, ResolvedMediaSource> results, string filePath, bool includeSegments) {
            string fullPath = Path.GetFullPath(filePath);
            string[] keys = results
                .Where(pair => string.Equals(pair.Value.FilePath, fullPath, StringComparison.OrdinalIgnoreCase) &&
                    (includeSegments || !pair.Value.IsSegment))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (string key in keys)
                results.Remove(key);
        }

        private static MediaSourceConfig CloneForCue(MediaSourceConfig source, string cue) {
            return new MediaSourceConfig {
                Cue = cue,
                CueMode = source.CueMode,
                Start = source.Start,
                End = source.End,
                StartMs = source.StartMs,
                EndMs = source.EndMs,
                Artist = source.Artist,
                Title = source.Title
            };
        }

        private static string NormalizeCueMode(string value) {
            string mode = (value ?? "split").Trim().ToLowerInvariant();
            return mode == "continuous" || mode == "single" || mode == "subtracks" ? "continuous" : "split";
        }

        private static string FirstNonEmpty(string first, string second) {
            return !string.IsNullOrWhiteSpace(first) ? first : second;
        }

        private static string GetSearchRoot(string fullPattern) {
            string root = Path.GetPathRoot(fullPattern);
            string current = root;
            string remainder = fullPattern.Substring(root.Length);
            foreach (string segment in remainder.Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)) {
                if (ContainsWildcard(segment))
                    break;
                current = Path.Combine(current, segment);
            }
            return current;
        }

        public static string GlobToRegex(string pattern) {
            string normalized = NormalizeSeparators(pattern);
            string expression = Regex.Escape(normalized)
                .Replace(@"\*\*\\", @"(?:.*\\)?")
                .Replace(@"\*\*", ".*")
                .Replace(@"\*", @"[^\\]*")
                .Replace(@"\?", @"[^\\]");
            return "^" + expression + "$";
        }

        private static bool ContainsWildcard(string value) {
            return value.IndexOf('*') >= 0 || value.IndexOf('?') >= 0;
        }

        private static string NormalizeSeparators(string value) {
            return value.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
        }
    }
}
