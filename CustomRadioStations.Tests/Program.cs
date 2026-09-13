using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
                TestMediaSourceObjects();
                TestCueSupport();
                TestAnalysisSidecar();
                TestLoudnessNormalization();
                TestMediaPlaybackBounds();
                TestInvalidConfigurations();
                TestLegacyIniParser();
                TestJsonTracklistModel();
                TestTrackDisplayMetadata();
                TestHoldRepeat();
                TestRadialSelectionHysteresis();
                TestRadioWheelAvailability();
                TestBroadcastTimeline();
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
            Assert(config.Tracks.Count == 1 && config.Tracks[0].File == "*", "typed model supplies missing tracks default");
            Assert(config.Commercials.Count == 1 && config.Commercials[0].File == "*", "typed model supplies missing commercials default");
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


        private static void TestMediaSourceObjects() {
            uint value;
            Assert(MediaTimeParser.TryParse("20:00", out value) && value == 1200000u, "human media time parses 20 minutes");
            Assert(MediaTimeParser.TryParse("1:02:03.500", out value) && value == 3723500u, "human media time parses hours and milliseconds");

            WriteStationJson("{\"name\":\"Objects\",\"tracks\":[\"song.mp3\",{\"file\":\"song.mp3\",\"start\":\"0:00.250\",\"end\":\"0:10\",\"artist\":\"Override Artist\",\"title\":\"Override Title\"}]}");
            StationDefinition definition;
            Assert(StationConfigLoader.TryLoad(root, out definition), "mixed string/object track entries load");
            Assert(definition.Tracks.Count == 1, "later object override replaces duplicate glob/path result");
            ResolvedMediaSource source = definition.Tracks[0];
            Assert(source.StartMs == 250u && source.EndMs == 10000u, "manual start/end survive resolution");
            Assert(source.Artist == "Override Artist" && source.Title == "Override Title", "manual artist/title survive resolution");

            WriteStationJson("{\"name\":\"Segments\",\"tracks\":[{\"file\":\"song.mp3\",\"start\":\"0:00\",\"end\":\"0:10\",\"title\":\"Part A\"},{\"file\":\"song.mp3\",\"start\":\"0:10\",\"end\":\"0:20\",\"title\":\"Part B\"}]}");
            Assert(StationConfigLoader.TryLoad(root, out definition) && definition.Tracks.Count == 2,
                "multiple manual segments of one physical file remain distinct logical tracks");
            Assert(definition.Tracks[0].AnalysisKey != definition.Tracks[1].AnalysisKey,
                "manual segments receive distinct analysis identities");

            WriteStationJson("{\"name\":\"Bad Time\",\"tracks\":[{\"file\":\"song.mp3\",\"start\":\"1:00\",\"end\":\"0:30\"}]}");
            Assert(!StationConfigLoader.TryLoad(root, out definition), "end before start rejects station config");
        }

        private static void TestCueSupport() {
            string mix = Path.Combine(tracks, "GTA Radio Mix.mp3");
            string cue = Path.Combine(tracks, "GTA Radio Mix.cue");
            File.WriteAllBytes(mix, new byte[] { 1 });
            File.WriteAllText(cue,
                "PERFORMER \"Various Artists\"\n" +
                "TITLE \"Example Radio\"\n" +
                "FILE \"GTA Radio Mix.mp3\" MP3\n" +
                "  TRACK 01 AUDIO\n" +
                "    TITLE \"First Song\"\n" +
                "    PERFORMER \"Artist One\"\n" +
                "    INDEX 01 00:00:00\n" +
                "  TRACK 02 AUDIO\n" +
                "    TITLE \"Second Song\"\n" +
                "    PERFORMER \"Artist Two\"\n" +
                "    INDEX 01 03:30:00\n" +
                "  TRACK 03 AUDIO\n" +
                "    TITLE \"Third Song\"\n" +
                "    INDEX 01 07:15:37\n");

            uint cueTime;
            Assert(CueSheetParser.TryParseCueTime("07:15:37", out cueTime) && cueTime == 435493u,
                "CUE MM:SS:FF timestamps convert from 75 fps frames");

            WriteStationJson("{\"name\":\"Cue Split\",\"tracks\":[{\"cue\":\"GTA Radio Mix.cue\",\"cueMode\":\"split\"}],\"commercials\":[]}");
            StationDefinition definition;
            Assert(StationConfigLoader.TryLoad(root, out definition), "split CUE station loads");
            Assert(definition.Tracks.Count == 3, "split CUE expands each TRACK into an independent radio item");
            Assert(definition.Tracks[0].StartMs == 0u && definition.Tracks[0].EndMs == 210000u,
                "split CUE first track uses next INDEX 01 as its end");
            Assert(definition.Tracks[1].StartMs == 210000u && definition.Tracks[1].EndMs == 435493u,
                "split CUE preserves exact section boundaries");
            Assert(definition.Tracks[2].StartMs == 435493u && !definition.Tracks[2].EndMs.HasValue,
                "split CUE final track runs to the physical file end");
            Assert(definition.Tracks[0].Artist == "Artist One" && definition.Tracks[0].Title == "First Song",
                "split CUE exposes per-track performer/title metadata");
            Assert(definition.Tracks[2].Artist == "Various Artists" && definition.Tracks[2].Title == "Third Song",
                "split CUE falls back to sheet performer when track performer is omitted");
            Assert(definition.Tracks[0].AnalysisKey != definition.Tracks[1].AnalysisKey,
                "split CUE sections sharing one physical file have distinct analysis identities");

            WriteStationJson("{\"name\":\"Cue Continuous\",\"tracks\":[{\"cue\":\"GTA Radio Mix.cue\",\"cueMode\":\"continuous\"}],\"commercials\":[]}");
            Assert(StationConfigLoader.TryLoad(root, out definition), "continuous CUE station loads");
            Assert(definition.Tracks.Count == 1, "continuous CUE keeps the physical recording as one radio item");
            Assert(definition.Tracks[0].SubTracks.Count == 3, "continuous CUE exposes CUE TRACKs as sub-tracks");
            Assert(definition.Tracks[0].SubTracks[1].StartTime == 210000u && definition.Tracks[0].SubTracks[1].Title == "Second Song",
                "continuous CUE sub-track timestamps and metadata are retained");

            WriteStationJson("{\"name\":\"Invalid Cue Override\",\"tracks\":[{\"file\":\"GTA Radio Mix.mp3\",\"cue\":\"GTA Radio Mix.cue\",\"cueMode\":\"continuous\"}],\"commercials\":[]}");
            Assert(!StationConfigLoader.TryLoad(root, out definition),
                "CUE source rejects a simultaneous file override because the CUE FILE directive owns the audio path");

            WriteStationJson("{\"name\":\"Cue Alias\",\"tracks\":[\"GTA Radio Mix.cue\"],\"commercials\":[]}");
            Assert(StationConfigLoader.TryLoad(root, out definition) && definition.Tracks.Count == 3,
                "plain .cue string defaults to split mode");

            WriteStationJson("{\"name\":\"Cue Override\",\"tracks\":[\"*\",{\"cue\":\"GTA Radio Mix.cue\",\"cueMode\":\"split\"}],\"commercials\":[]}");
            Assert(StationConfigLoader.TryLoad(root, out definition) &&
                definition.Tracks.Count(source => string.Equals(source.FilePath, Path.GetFullPath(mix), StringComparison.OrdinalIgnoreCase)) == 3 &&
                definition.Tracks.Where(source => string.Equals(source.FilePath, Path.GetFullPath(mix), StringComparison.OrdinalIgnoreCase)).All(source => source.IsSegment),
                "split CUE replaces a whole-file match from an earlier wildcard instead of duplicating it");
        }

        private static void TestAnalysisSidecar() {
            WriteStationJson("{\"name\":\"Analyzed\",\"tracks\":[\"song.mp3\"]}");
            string file = Path.Combine(tracks, "song.mp3");
            var info = new FileInfo(file);
            var analysis = new StationAnalysis();
            analysis.Tracks[Path.GetFullPath(file)] = new TrackAnalysis {
                FileSize = info.Length,
                LastWriteUtc = info.LastWriteTimeUtc,
                DurationMs = 10000u,
                AudioStartMs = 500u,
                AudioEndMs = 9500u,
                IntegratedLufs = -18d,
                TruePeakDb = -3d,
                GainDb = 2d,
                AnalyzedUtc = DateTime.UtcNow
            };
            StationAnalysisLoader.SaveAtomic(root, analysis);

            StationDefinition definition;
            Assert(StationConfigLoader.TryLoad(root, out definition) && definition.Tracks[0].Analysis != null,
                "fresh station analysis attaches to resolved media");
            Assert(definition.Tracks[0].Analysis.AudioStartMs == 500u && definition.Tracks[0].Analysis.AudioEndMs == 9500u,
                "analysis audio bounds are retained");
            Assert(!definition.Tracks[0].Analysis.SourceStartMs.HasValue && !definition.Tracks[0].Analysis.SourceEndMs.HasValue,
                "whole-file analysis does not emit logical segment bounds");

            var segmented = new TrackAnalysis {
                FileSize = info.Length,
                LastWriteUtc = info.LastWriteTimeUtc,
                DurationMs = 10000u,
                SourceStartMs = 1000u,
                SourceEndMs = 8000u,
                AudioStartMs = 1100u,
                AudioEndMs = 7900u,
                IntegratedLufs = -17d,
                TruePeakDb = -2d,
                GainDb = 1d,
                AnalyzedUtc = DateTime.UtcNow
            };
            string segmentedJson = JsonConvert.SerializeObject(segmented);
            Assert(segmentedJson.Contains("\"sourceStartMs\":1000") && segmentedJson.Contains("\"sourceEndMs\":8000"),
                "segmented analysis serializes the absolute source region separately from audible bounds");
            string wholeJson = JsonConvert.SerializeObject(new TrackAnalysis { AudioStartMs = 0u, AudioEndMs = 10000u });
            Assert(!wholeJson.Contains("sourceStartMs") && !wholeJson.Contains("sourceEndMs"),
                "whole-file analysis omits optional source segment fields");

            using (FileStream stream = new FileStream(file, FileMode.Append, FileAccess.Write))
                stream.WriteByte(2);
            Assert(StationConfigLoader.TryLoad(root, out definition) && definition.Tracks[0].Analysis == null,
                "stale station analysis is ignored after source file changes");
        }

        private static void TestMediaPlaybackBounds() {
            var analysis = new TrackAnalysis { AudioStartMs = 500u, AudioEndMs = 9500u };
            MediaPlaybackBounds bounds = MediaPlaybackBounds.Calculate(10000u, null, null, analysis);
            Assert(bounds.StartMs == 500u && bounds.EndMs == 9500u && bounds.LengthMs == 9000u,
                "analysis bounds define logical playback length");

            bounds = MediaPlaybackBounds.Calculate(10000u, 1000u, 8000u, analysis);
            Assert(bounds.StartMs == 1000u && bounds.EndMs == 8000u,
                "manual start/end override analysis bounds");

            bounds = MediaPlaybackBounds.Calculate(10000u, 1000u, null, analysis);
            Assert(bounds.StartMs == 1000u && bounds.EndMs == 9500u,
                "manual start combines with analyzed end");

            bounds = MediaPlaybackBounds.Calculate(10000u, 250u, 9750u,
                new TrackAnalysis { AudioStartMs = 500u, AudioEndMs = 9500u }, true);
            Assert(bounds.StartMs == 500u && bounds.EndMs == 9500u,
                "CUE structural bounds allow analysis to trim silence inside the segment");
        }

        private static void TestLoudnessNormalization() {
            var settings = new StationAnalysisSettings { TargetLufs = -16d, MaxGainDb = 12d, PeakHeadroomDb = 0.5d };
            double gain = LoudnessNormalization.CalculateGainDb(-20d, -6d, settings);
            Assert(Math.Abs(gain - 4d) < 0.001d, "loudness normalization calculates target gain");
            gain = LoudnessNormalization.CalculateGainDb(-24d, -1d, settings);
            Assert(Math.Abs(gain - 0.5d) < 0.001d, "true-peak headroom limits positive gain");
            Assert(Math.Abs(LoudnessNormalization.DbToLinear(6.020599913d) - 2f) < 0.01f, "dB gain converts to linear amplitude");
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
            File.WriteAllText(path,
                "[GENERAL]" + Environment.NewLine +
                "DESCRIPTION = Existing station" + Environment.NewLine +
                "TRACK = First.mp3" + Environment.NewLine +
                "TRACK = Second.mp3");
            Settings.ScriptSettings legacy = Settings.ScriptSettings.Load(path);

            Assert(legacy.GetValue("GENERAL", "DESCRIPTION", string.Empty) == "Existing station",
                "legacy station INI remains readable");
            string[] tracks = legacy.GetAllValues<string>("GENERAL", "TRACK");
            Assert(tracks.Length == 2 && tracks[0] == "First.mp3" && tracks[1] == "Second.mp3",
                "legacy duplicate INI keys remain ordered and finite");
            Assert(legacy.Save(), "legacy INI with duplicate keys saves successfully");
            legacy = Settings.ScriptSettings.Load(path);
            tracks = legacy.GetAllValues<string>("GENERAL", "TRACK");
            Assert(tracks.Length == 2 && tracks[1] == "Second.mp3",
                "legacy duplicate INI keys survive save/reload");
        }

        private static void TestJsonTracklistModel() {
            TracklistConfig config = JsonConvert.DeserializeObject<TracklistConfig>(
                "{\"tracks\":[{\"startTimeMs\":185000,\"artist\":\"Artist\",\"title\":\"Song\"}]}");

            Assert(config.Tracks.Count == 1 && config.Tracks[0].StartTime == 185000 &&
                config.Tracks[0].Artist == "Artist" && config.Tracks[0].Title == "Song",
                "JSON tracklist metadata is strongly typed");
        }


        private static void TestBroadcastTimeline() {
            uint[] lengths = { 180000u, 240000u, 300000u, 360000u };

            BroadcastPosition position = BroadcastTimeline.Advance(lengths, 2, 120000u, 60000u, true);
            Assert(!position.Finished && position.Index == 2 && position.Position == 180000u,
                "broadcast resume stays within current item when elapsed time fits");

            position = BroadcastTimeline.Advance(lengths, 2, 120000u, 600000u, true);
            Assert(!position.Finished && position.Index == 0 && position.Position == 60000u,
                "broadcast resume crosses multiple items and wraps from a rotated start");

            position = BroadcastTimeline.Advance(lengths, 3, 300000u, 660000u, true);
            Assert(!position.Finished && position.Index == 2 && position.Position == 180000u,
                "broadcast resume handles long gaps across several programme items");

            ulong cycle = 0;
            foreach (uint length in lengths)
                cycle += length;
            position = BroadcastTimeline.Advance(lengths, 1, 30000u, cycle + 90000u, true);
            Assert(!position.Finished && position.Index == 1 && position.Position == 120000u,
                "broadcast resume reduces full cycles without changing the target");

            position = BroadcastTimeline.Advance(lengths, 3, 350000u, 20000u, false);
            Assert(position.Finished, "non-looping broadcast finishes after the final programme item");

            uint[] withZeroLength = { 180000u, 0u, 300000u };
            position = BroadcastTimeline.Advance(withZeroLength, 0, 170000u, 20000u, true);
            Assert(!position.Finished && position.Index == 2 && position.Position == 10000u,
                "broadcast timeline safely skips zero-length entries");
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
