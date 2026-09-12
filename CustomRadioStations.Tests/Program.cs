using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.IO;

namespace CustomRadioStations {
    internal static class Program {
        private static int passed;
        private static string root;
        private static string tracks;

        private static int Main() {
            root = Path.Combine(Path.GetTempPath(), "crs-json-tests-" + Guid.NewGuid().ToString("N"));
            tracks = Path.Combine(root, "tracks");
            try {
                CreateFixture();
                TestDefaultsAndEmptyArrays();
                TestResolverSources();
                TestInvalidConfigurations();
                TestLegacyIniParser();
                TestJsonTracklistModel();
                Console.WriteLine("Passed " + passed + " station configuration tests.");
                return 0;
            } catch (Exception ex) {
                Console.Error.WriteLine(ex);
                return 1;
            } finally {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static void CreateFixture() {
            Directory.CreateDirectory(Path.Combine(tracks, "Albums"));
            File.WriteAllBytes(Path.Combine(tracks, "song.mp3"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(tracks, "UPPER.MP3"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(tracks, "ignore.txt"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(tracks, "Albums", "album.mp3"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(tracks, "Albums", "rare.flac"), new byte[] { 1 });
        }

        private static void TestDefaultsAndEmptyArrays() {
            WriteStationJson("{\"name\":\"Vice City FM\"}");
            StationDefinition definition;
            Assert(StationConfigLoader.TryLoad(root, out definition), "tracks omitted uses default wildcard");
            Assert(definition.Tracks.Count == 4, "default wildcard recursively resolves supported files");
            Assert(definition.Id == "vice-city-fm" || definition.Id.StartsWith("crs-json-tests-"), "deterministic ID generated");

            WriteStationJson("{\"name\":\"Empty\",\"tracks\":[]}");
            Assert(!StationConfigLoader.TryLoad(root, out definition), "explicit empty tracks disables station");

            StationConfig config = JsonConvert.DeserializeObject<StationConfig>("{\"name\":\"Defaults\"}");
            Assert(config.Tracks.Count == 1 && config.Tracks[0] == "*", "typed model supplies missing tracks default");
            Assert(config.Commercials.Count == 1 && config.Commercials[0] == "*", "typed model supplies missing commercials default");
        }

        private static void TestResolverSources() {
            var warnings = new List<string>();
            var resolver = new MediaSourceResolver(warnings.Add);
            Assert(resolver.Resolve(new[] { "*" }, tracks, "track").Count == 4, "wildcard recursively scans root");
            Assert(resolver.Resolve(new[] { "song.mp3" }, tracks, "track").Count == 1, "relative exact file");
            Assert(resolver.Resolve(new[] { "Albums" }, tracks, "track").Count == 2, "relative directory recursion");
            IReadOnlyList<string> relativeGlob = resolver.Resolve(new[] { "Albums/*.mp3" }, tracks, "track");
            if (relativeGlob.Count != 1)
                Console.WriteLine("Glob diagnostic: " + MediaSourceResolver.GlobToRegex(Path.Combine(tracks, "Albums", "*.mp3")) + " | " + string.Join(" | ", warnings));
            Assert(relativeGlob.Count == 1, "relative explicit glob");
            Assert(resolver.Resolve(new[] { tracks }, tracks, "track").Count == 4, "absolute directory");
            Assert(resolver.Resolve(new[] { Path.Combine(tracks, "Albums", "*.flac") }, tracks, "track").Count == 1, "absolute glob");
            Assert(resolver.Resolve(new[] { "song.mp3", "*.mp3" }, tracks, "track").Count == 2, "duplicate matches are removed");
            Assert(resolver.Resolve(new[] { "missing.mp3" }, tracks, "track").Count == 0, "missing file skipped");
            Assert(resolver.Resolve(new[] { "missing-folder" }, tracks, "track").Count == 0, "missing directory skipped");
            Assert(resolver.Resolve(new[] { "ignore.txt" }, tracks, "track").Count == 0, "unsupported extension skipped");
            Assert(resolver.Resolve(new[] { "Albums/album.mp3" }, tracks, "track").Count == 1, "mixed slash path");
            Assert(resolver.Resolve(new[] { "UPPER.MP3" }, tracks, "track").Count == 1, "uppercase extension");
            Assert(resolver.Resolve(new[] { "*" }, Path.Combine(root, "commercials"), "commercial").Count == 0,
                "absent commercials directory is harmless");
            Assert(resolver.Resolve(new string[0], Path.Combine(root, "commercials"), "commercial").Count == 0,
                "empty commercials list remains empty");
        }

        private static void TestInvalidConfigurations() {
            StationDefinition definition;
            WriteStationJson("{not-json");
            Assert(!StationConfigLoader.TryLoad(root, out definition), "malformed JSON skipped");

            WriteStationJson("{\"name\":\"Mode\",\"playback\":{\"mode\":\"wrong\"}}");
            Assert(StationConfigLoader.TryLoad(root, out definition) && definition.Playback.Mode == "broadcast",
                "invalid playback mode falls back to broadcast");

            WriteStationJson("{\"name\":\"Ranges\",\"commercialBreaks\":{\"minTracksBetween\":8,\"maxTracksBetween\":2,\"minCommercials\":-1,\"maxCommercials\":-4}}");
            Assert(StationConfigLoader.TryLoad(root, out definition), "invalid commercial ranges do not kill station");
            Assert(definition.CommercialBreaks.MinTracksBetween == 8 && definition.CommercialBreaks.MaxTracksBetween == 8,
                "track interval normalized");
            Assert(definition.CommercialBreaks.MinCommercials == 0 && definition.CommercialBreaks.MaxCommercials == 0,
                "commercial count normalized");
        }

        private static void WriteStationJson(string json) {
            File.WriteAllText(Path.Combine(root, "station.json"), json);
        }

        private static void TestLegacyIniParser() {
            string path = Path.Combine(root, "station.ini");
            File.WriteAllText(path, "[GENERAL]" + Environment.NewLine + "DESCRIPTION = Existing station");
            Settings.ScriptSettings legacy = Settings.ScriptSettings.Load(path);
            Assert(legacy.GetValue("GENERAL", "DESCRIPTION", string.Empty) == "Existing station",
                "legacy station INI remains readable");
        }

        private static void TestJsonTracklistModel() {
            TracklistConfig config = JsonConvert.DeserializeObject<TracklistConfig>(
                "{\"tracks\":[{\"startTimeMs\":185000,\"artist\":\"Artist\",\"title\":\"Song\"}]}");
            Assert(config.Tracks.Count == 1 && config.Tracks[0].StartTime == 185000 &&
                config.Tracks[0].Artist == "Artist" && config.Tracks[0].Title == "Song",
                "JSON tracklist metadata is strongly typed");
        }

        private static void Assert(bool condition, string name) {
            if (!condition) throw new InvalidOperationException("FAILED: " + name);
            passed++;
            Console.WriteLine("PASS: " + name);
        }
    }
}
