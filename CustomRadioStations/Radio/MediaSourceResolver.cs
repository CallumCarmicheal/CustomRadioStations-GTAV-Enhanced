using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CustomRadioStations
{
    /// <summary>
    /// Resolves exact paths, directories and glob expressions into media files.
    /// The warning callback and filesystem-only API keep this class independently testable.
    /// </summary>
    internal sealed class MediaSourceResolver
    {
        private static readonly HashSet<string> SupportedExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp3", ".flac", ".ogg", ".wav"
            };

        private readonly Action<string> warn;

        internal MediaSourceResolver(Action<string> warningSink)
        {
            warn = warningSink ?? (_ => { });
        }

        internal IReadOnlyList<string> Resolve(IEnumerable<string> entries, string defaultRoot, string sourceKind)
        {
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (entries == null) return results.ToArray();

            foreach (string entry in entries)
            {
                try
                {
                    ResolveEntry(entry, defaultRoot, sourceKind, results);
                }
                catch (Exception ex)
                {
                    warn("Could not resolve " + sourceKind + " source '" + (entry ?? "<null>") + "': " + ex.Message);
                }
            }

            return results.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private void ResolveEntry(string entry, string defaultRoot, string sourceKind, HashSet<string> results)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                warn("Skipping blank " + sourceKind + " source entry.");
                return;
            }

            string normalizedEntry = NormalizeSeparators(entry.Trim());
            if (normalizedEntry == "*")
            {
                AddDirectory(defaultRoot, sourceKind, results);
                return;
            }

            string combined = Path.IsPathRooted(normalizedEntry)
                ? normalizedEntry
                : Path.Combine(defaultRoot, normalizedEntry);

            // .NET Framework rejects wildcard characters in Path.GetFullPath. The
            // non-wildcard base is already rooted above, so expand globs first.
            if (ContainsWildcard(combined))
            {
                AddGlob(NormalizeSeparators(combined), sourceKind, results);
                return;
            }

            string fullPath = Path.GetFullPath(combined);

            if (File.Exists(fullPath))
            {
                AddFile(fullPath, sourceKind, results);
                return;
            }

            if (Directory.Exists(fullPath))
            {
                AddDirectory(fullPath, sourceKind, results);
                return;
            }

            warn("Skipping missing " + sourceKind + " path: " + fullPath);
        }

        private void AddDirectory(string directory, string sourceKind, HashSet<string> results)
        {
            string fullDirectory = Path.GetFullPath(directory);
            if (!Directory.Exists(fullDirectory))
            {
                // Default tracks/commercials directories are optional until content is added.
                warn("Skipping missing " + sourceKind + " directory: " + fullDirectory);
                return;
            }

            var pending = new Stack<string>();
            pending.Push(fullDirectory);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                try
                {
                    foreach (string file in Directory.GetFiles(current))
                        AddFile(file, sourceKind, results, false);

                    foreach (string child in Directory.GetDirectories(current)
                        .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
                        pending.Push(child);
                }
                catch (Exception ex)
                {
                    warn("Could not scan " + sourceKind + " directory '" + current + "': " + ex.Message);
                }
            }
        }

        private void AddGlob(string fullPattern, string sourceKind, HashSet<string> results)
        {
            string searchRoot = GetSearchRoot(fullPattern);
            if (string.IsNullOrEmpty(searchRoot) || !Directory.Exists(searchRoot))
            {
                warn("Skipping " + sourceKind + " glob with missing base directory: " + fullPattern);
                return;
            }

            var matcher = new Regex(GlobToRegex(fullPattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddDirectory(searchRoot, sourceKind, matches);
            foreach (string file in matches)
            {
                if (matcher.IsMatch(NormalizeSeparators(file)))
                    results.Add(file);
            }

            if (matches.Count > 0 && !matches.Any(file => matcher.IsMatch(NormalizeSeparators(file))))
                warn("No supported files matched " + sourceKind + " glob: " + fullPattern);
        }

        private void AddFile(string path, string sourceKind, HashSet<string> results, bool warnIfUnsupported = true)
        {
            string fullPath = Path.GetFullPath(path);
            if (SupportedExtensions.Contains(Path.GetExtension(fullPath)))
            {
                results.Add(fullPath);
            }
            else if (warnIfUnsupported)
            {
                warn("Skipping unsupported " + sourceKind + " file: " + fullPath);
            }
        }

        private static string GetSearchRoot(string fullPattern)
        {
            string root = Path.GetPathRoot(fullPattern);
            string current = root;
            string remainder = fullPattern.Substring(root.Length);
            foreach (string segment in remainder.Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (ContainsWildcard(segment)) break;
                current = Path.Combine(current, segment);
            }

            return current;
        }

        internal static string GlobToRegex(string pattern)
        {
            string normalized = NormalizeSeparators(pattern);
            string expression = Regex.Escape(normalized)
                .Replace(@"\*\*\\", @"(?:.*\\)?")
                .Replace(@"\*\*", ".*")
                .Replace(@"\*", @"[^\\]*")
                .Replace(@"\?", @"[^\\]");
            return "^" + expression + "$";
        }

        private static bool ContainsWildcard(string value)
        {
            return value.IndexOf('*') >= 0 || value.IndexOf('?') >= 0;
        }

        private static string NormalizeSeparators(string value)
        {
            return value.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
        }
    }
}
