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
                TestTrackDisplayMetadata();
                TestHoldRepeat();
                TestRadialSelectionHysteresis();
                TestRadioWheelAvailability();
                TestWheelDisplayMetricsAndIconVariants();
                Console.WriteLine("Passed " + passed + " station configuration tests.");
                return 0;
            } catch (Exception ex) {
                Console.Error.WriteLine(ex);
                return 1;
            } finally {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
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
            Assert(resolver.Resolve(new[] { Path.Combine(tracks, "song.mp3") }, tracks, "track").Count == 1,
                "absolute exact file");
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

        private static void TestWheelDisplayMetricsAndIconVariants() {
            AssertClose(WheelDisplayMetrics.GetVirtualWidth(854, 480), 1281f, "480p 16:9 canvas width");
            AssertClose(WheelDisplayMetrics.GetVirtualWidth(640, 480), 960f, "480p 4:3 canvas width");
            AssertClose(WheelDisplayMetrics.GetVirtualWidth(1920, 1080), 1280f, "1080p 16:9 canvas width");
            AssertClose(WheelDisplayMetrics.GetVirtualWidth(2560, 1080), 1706.6666f, "1080p 21:9 canvas width");
            AssertClose(WheelDisplayMetrics.GetVirtualWidth(5120, 1440), 2560f, "1440p 32:9 canvas width");
            AssertClose(WheelDisplayMetrics.GetVirtualWidth(1024, 768), 960f, "4:3 canvas width");
            AssertClose(WheelDisplayMetrics.GetVirtualWidth(3840, 2160), 1280f, "4K 16:9 canvas width");

            Assert(WheelDisplayMetrics.GetRequiredIconPixels(64, 64, 480) == 43, "480p icon scale");
            Assert(WheelDisplayMetrics.GetRequiredIconPixels(64, 64, 1080) == 96, "1080p icon scale");
            Assert(WheelDisplayMetrics.GetRequiredIconPixels(64, 64, 1440) == 128, "1440p icon scale");
            Assert(WheelDisplayMetrics.GetRequiredIconPixels(64, 64, 2160) == 192, "4K icon scale");

            string icons = Path.Combine(root, "icons");
            Directory.CreateDirectory(icons);
            string icon = Path.Combine(icons, "icon.png");
            string icon256 = Path.Combine(icons, "icon.256.png");
            string icon512 = Path.Combine(icons, "icon.512.png");
            string icon1024 = Path.Combine(icons, "icon.1024.png");
            WritePngHeader(icon, 128);
            WritePngHeader(icon256, 256);
            WritePngHeader(icon512, 512);
            WritePngHeader(icon1024, 1024);

            Assert(StationIconVariantResolver.Resolve(icon, 43) == icon, "480p selects base 128px icon");
            Assert(StationIconVariantResolver.Resolve(icon, 96) == icon, "1080p selects base 128px icon");
            Assert(StationIconVariantResolver.Resolve(icon, 128) == icon, "1440p selects base 128px icon");
            Assert(StationIconVariantResolver.Resolve(icon, 192) == icon256, "4K selects 256px icon");
            Assert(StationIconVariantResolver.Resolve(icon, 700) == icon1024, "large display selects 1024px icon");

            File.Delete(icon);
            Assert(StationIconVariantResolver.HasAnyVariant(icon), "higher-resolution icon works without base icon");
            Assert(StationIconVariantResolver.Resolve(icon, 128) == icon256, "missing base selects next suitable variant");

            string validPng = Path.Combine(icons, "valid.png");
            WritePngHeader(validPng, 128);
            string validationError;
            Assert(TextureFileValidator.TryValidatePng(validPng, out validationError), "valid PNG header passes DirectX preflight");

            string invalidPng = Path.Combine(icons, "invalid.png");
            File.WriteAllBytes(invalidPng, new byte[] { 1, 2, 3 });
            Assert(!TextureFileValidator.TryValidatePng(invalidPng, out validationError), "malformed PNG is rejected before DirectX");
        }

        private static void TestTrackDisplayMetadata() {
            Assert(TrackMetadataReader.FormatDisplayName("Battle Tapes", "Feel The Same", "fallback") ==
                "BATTLE TAPES\nFeel The Same", "complete audio tags use GTA-style artist and title");
            Assert(TrackMetadataReader.FormatDisplayName("", "Feel The Same", "Artist - Track") ==
                "Artist - Track", "missing tag artist falls back to filename metadata");
            Assert(TrackMetadataReader.FormatDisplayName("Battle Tapes", "", "Track - Artist") ==
                "Track - Artist", "missing tag title falls back to filename metadata");
            Assert(TrackMetadataReader.FormatDisplayName(new[] { "Artist One", "Artist Two" }, "Collaboration", "fallback") ==
                "ARTIST ONE, ARTIST TWO\nCollaboration", "multiple tagged artists are retained");
        }

        private static void TestHoldRepeat() {
            DateTime start = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc);
            var repeat = new HoldRepeatState(TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(100));
            Assert(repeat.ShouldFire(true, true, start), "volume repeat fires immediately on press");
            Assert(!repeat.ShouldFire(false, true, start.AddMilliseconds(399)), "volume repeat waits through initial delay");
            Assert(repeat.ShouldFire(false, true, start.AddMilliseconds(400)), "volume repeat starts after hold delay");
            Assert(repeat.ShouldFire(false, true, start.AddMilliseconds(500)), "held volume control repeats at interval");
            Assert(!repeat.ShouldFire(false, false, start.AddMilliseconds(510)), "volume repeat resets on release");
            Assert(!repeat.ShouldFire(false, true, start.AddMilliseconds(1000)), "held input needs a fresh press after reset");
        }

        private static void TestRadialSelectionHysteresis() {
            Assert(!RadialSelectionHysteresis.ShouldSwitch(0f, 18f, 12, 4f),
                "radial selection sticks inside hysteresis boundary");
            Assert(RadialSelectionHysteresis.ShouldSwitch(0f, 20f, 12, 4f),
                "radial selection changes after hysteresis boundary");
            Assert(!RadialSelectionHysteresis.ShouldSwitch(350f, 8f, 12, 4f),
                "radial hysteresis handles zero-degree wrap");
            Assert(RadialSelectionHysteresis.ShouldSwitch(350f, 10f, 12, 4f),
                "radial selection changes across zero-degree wrap");
            AssertClose(RadialSelectionHysteresis.AngularDistance(359f, 1f), 2f,
                "radial angular distance uses shortest arc");
        }

        private static void TestRadioWheelAvailability() {
            Assert(RadioWheelAvailability.CanShow(true, true, true, true, false),
                "radio wheel opens when GTA accepts the input and shows its radio HUD");
            Assert(!RadioWheelAvailability.CanShow(true, false, true, false, false),
                "consumed phone or interaction-menu input cannot open custom wheel");
            Assert(!RadioWheelAvailability.CanShow(true, true, true, false, false),
                "custom wheel waits for GTA radio HUD authorization");
            Assert(!RadioWheelAvailability.CanShow(false, false, true, false, true),
                "visible custom wheel closes when radio input is released");
            Assert(RadioWheelAvailability.CanShow(true, false, true, false, true),
                "open custom wheel can retain its disabled input");
            Assert(!RadioWheelAvailability.CanShow(true, true, false, true, false),
                "player control restrictions suppress custom wheel");
        }

        private static void Assert(bool condition, string name) {
            if (!condition)
                throw new InvalidOperationException("FAILED: " + name);
            passed++;
            Console.WriteLine("PASS: " + name);
        }

        private static void AssertClose(float actual, float expected, string name) {
            Assert(Math.Abs(actual - expected) < 0.01f, name);
        }

        private static void WritePngHeader(string path, int size) {
            byte[] header = new byte[24];
            byte[] signature = { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };
            Array.Copy(signature, header, signature.Length);
            header[16] = (byte)(size >> 24);
            header[17] = (byte)(size >> 16);
            header[18] = (byte)(size >> 8);
            header[19] = (byte)size;
            header[20] = (byte)(size >> 24);
            header[21] = (byte)(size >> 16);
            header[22] = (byte)(size >> 8);
            header[23] = (byte)size;
            File.WriteAllBytes(path, header);
        }
    }
}