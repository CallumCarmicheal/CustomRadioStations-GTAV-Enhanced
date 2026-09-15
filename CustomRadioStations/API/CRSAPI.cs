using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

/// <summary>
/// Public console/debug facade for Custom Radio Stations.
/// Intended for ScriptHookVDotNet's F4 C# console, for example:
/// CRSAPI.NextSong(); or return CRSAPI.CurrentTrack;
/// </summary>
public static class CRSAPI {
    public static bool IsReady => CustomRadioStations.CRSApiRuntime.IsReady;
    public static string LastResult => CustomRadioStations.CRSApiRuntime.LastResult;
    public static int StationCount => CustomRadioStations.CRSApiRuntime.StationCount;

    public static string CurrentStation => CustomRadioStations.CRSApiRuntime.GetCurrentSnapshot().StationName;
    public static string CurrentTrack => CustomRadioStations.CRSApiRuntime.GetCurrentSnapshot().TrackDisplayName;
    public static TimeSpan Position => TimeSpan.FromMilliseconds(CustomRadioStations.CRSApiRuntime.GetCurrentSnapshot().SongPositionMs);
    public static TimeSpan Duration => TimeSpan.FromMilliseconds(CustomRadioStations.CRSApiRuntime.GetCurrentSnapshot().SongDurationMs);
    public static double PositionSeconds => CustomRadioStations.CRSApiRuntime.GetCurrentSnapshot().SongPositionMs / 1000d;
    public static double DurationSeconds => CustomRadioStations.CRSApiRuntime.GetCurrentSnapshot().SongDurationMs / 1000d;
    public static bool IsPlaying => CustomRadioStations.CRSApiRuntime.GetCurrentSnapshot().IsPlaying;
    public static bool IsPaused => CustomRadioStations.CRSApiRuntime.GetCurrentSnapshot().IsPaused;
    public static float CurrentRating => CustomRadioStations.CRSApiRuntime.GetCurrentSnapshot().Rating;
    public static string CurrentRatingKey => CustomRadioStations.CRSApiRuntime.GetCurrentSnapshot().RatingKey;

    public static CustomRadioStations.CRSTrackInfo Track => new CustomRadioStations.CRSTrackInfo(CustomRadioStations.CRSApiRuntime.GetCurrentSnapshot());
    public static CustomRadioStations.CRSStationInfo Station => new CustomRadioStations.CRSStationInfo(CustomRadioStations.CRSApiRuntime.GetCurrentSnapshot());

    public static string NextSong() => CustomRadioStations.CRSApiRuntime.Enqueue(CustomRadioStations.CRSApiCommandType.NextSong);
    public static string PreviousSong() => CustomRadioStations.CRSApiRuntime.Enqueue(CustomRadioStations.CRSApiCommandType.PreviousSong);
    public static string RestartSong() => CustomRadioStations.CRSApiRuntime.Enqueue(CustomRadioStations.CRSApiCommandType.RestartSong);
    public static string Play() => CustomRadioStations.CRSApiRuntime.Enqueue(CustomRadioStations.CRSApiCommandType.Play);
    public static string Pause() => CustomRadioStations.CRSApiRuntime.Enqueue(CustomRadioStations.CRSApiCommandType.Pause);
    public static string TogglePause() => CustomRadioStations.CRSApiRuntime.Enqueue(CustomRadioStations.CRSApiCommandType.TogglePause);
    public static string Stop() => CustomRadioStations.CRSApiRuntime.Enqueue(CustomRadioStations.CRSApiCommandType.Stop);
    public static string RateUp() => CustomRadioStations.CRSApiRuntime.Enqueue(CustomRadioStations.CRSApiCommandType.RateUp);
    public static string RateDown() => CustomRadioStations.CRSApiRuntime.Enqueue(CustomRadioStations.CRSApiCommandType.RateDown);
    public static string SetRating(double rating) => CustomRadioStations.CRSApiRuntime.Enqueue(CustomRadioStations.CRSApiCommandType.SetRating, rating);
    public static string ClearRating() => CustomRadioStations.CRSApiRuntime.Enqueue(CustomRadioStations.CRSApiCommandType.ClearRating);

    public static string Seek(double seconds) => CustomRadioStations.CRSApiRuntime.Enqueue(CustomRadioStations.CRSApiCommandType.Seek, seconds);
    public static string SeekRelative(double seconds) => CustomRadioStations.CRSApiRuntime.Enqueue(CustomRadioStations.CRSApiCommandType.SeekRelative, seconds);
    public static string SeekPercent(double percent) => CustomRadioStations.CRSApiRuntime.Enqueue(CustomRadioStations.CRSApiCommandType.SeekPercent, percent);

    public static string NextStation() => CustomRadioStations.CRSApiRuntime.Enqueue(CustomRadioStations.CRSApiCommandType.NextStation);
    public static string PreviousStation() => CustomRadioStations.CRSApiRuntime.Enqueue(CustomRadioStations.CRSApiCommandType.PreviousStation);
    public static string SetStation(int index) => CustomRadioStations.CRSApiRuntime.Enqueue(CustomRadioStations.CRSApiCommandType.SetStationIndex, index);
    public static string SetStation(string nameOrId) => CustomRadioStations.CRSApiRuntime.Enqueue(CustomRadioStations.CRSApiCommandType.SetStationName, nameOrId);

    public static string ReloadConfig() => CustomRadioStations.CRSApiRuntime.Enqueue(CustomRadioStations.CRSApiCommandType.ReloadConfig);
    public static string ReloadStations() => CustomRadioStations.CRSApiRuntime.Enqueue(CustomRadioStations.CRSApiCommandType.ReloadStations);
    public static string Reload() => CustomRadioStations.CRSApiRuntime.Enqueue(CustomRadioStations.CRSApiCommandType.ReloadAll);

    public static string Status() => CustomRadioStations.CRSApiRuntime.Status();
    public static string TrackInfo() => CustomRadioStations.CRSApiRuntime.TrackInfo();
    public static string StationInfo() => CustomRadioStations.CRSApiRuntime.StationInfo();
    public static string Timeline() => CustomRadioStations.CRSApiRuntime.Timeline();
    public static string BoundsInfo() => CustomRadioStations.CRSApiRuntime.BoundsInfo();
    public static string DumpProgramme() => CustomRadioStations.CRSApiRuntime.DumpProgramme();
    public static string Stations() => CustomRadioStations.CRSApiRuntime.Stations();
    public static string Help() => CustomRadioStations.CRSApiRuntime.Help();

}


namespace CustomRadioStations {

    /// <summary>Read-only snapshot of the currently selected logical song.</summary>
    public sealed class CRSTrackInfo {
        internal CRSTrackInfo(CustomRadioStations.RadioApiSnapshot snapshot) {
            DisplayName = snapshot.TrackDisplayName;
            FilePath = snapshot.FilePath;
            Position = TimeSpan.FromMilliseconds(snapshot.SongPositionMs);
            Duration = TimeSpan.FromMilliseconds(snapshot.SongDurationMs);
            MediaPosition = TimeSpan.FromMilliseconds(snapshot.MediaPositionMs);
            MediaDuration = TimeSpan.FromMilliseconds(snapshot.MediaDurationMs);
            SourceDuration = TimeSpan.FromMilliseconds(snapshot.PhysicalDurationMs);
            PlaybackStart = TimeSpan.FromMilliseconds(snapshot.PlaybackStartMs);
            PlaybackEnd = TimeSpan.FromMilliseconds(snapshot.PlaybackEndMs);
            Rating = snapshot.Rating;
            RatingKey = snapshot.RatingKey;
            IsCommercial = snapshot.IsCommercial;
        }

        public string DisplayName { get; }
        public string FilePath { get; }
        public TimeSpan Position { get; }
        public TimeSpan Duration { get; }
        public TimeSpan MediaPosition { get; }
        public TimeSpan MediaDuration { get; }
        public TimeSpan SourceDuration { get; }
        public TimeSpan PlaybackStart { get; }
        public TimeSpan PlaybackEnd { get; }
        public float Rating { get; }
        public string RatingKey { get; }
        public bool IsCommercial { get; }

        public override string ToString() {
            if (string.IsNullOrWhiteSpace(DisplayName))
                return "No custom-radio track is active.";
            return DisplayName.Replace("\r", string.Empty).Trim() + Environment.NewLine +
                Format(Position) + " / " + Format(Duration) + Environment.NewLine +
                "Rating: " + (Rating > 0f ? Rating.ToString("0.0", CultureInfo.InvariantCulture) + " / 5" : "Unrated");
        }

        private static string Format(TimeSpan value) {
            return value.TotalHours >= 1d ? value.ToString(@"hh\:mm\:ss\.fff") : value.ToString(@"mm\:ss\.fff");
        }
    }

    /// <summary>Read-only snapshot of the current custom station.</summary>
    public sealed class CRSStationInfo {
        internal CRSStationInfo(CustomRadioStations.RadioApiSnapshot snapshot) {
            Name = snapshot.StationName;
            Id = snapshot.StationId;
            Mode = snapshot.PlaybackMode;
            ProgrammeIndex = snapshot.ProgrammeIndex;
            ProgrammeCount = snapshot.ProgrammeCount;
            IsPlaying = snapshot.IsPlaying;
            IsPaused = snapshot.IsPaused;
        }

        public string Name { get; }
        public string Id { get; }
        public string Mode { get; }
        public int ProgrammeIndex { get; }
        public int ProgrammeCount { get; }
        public bool IsPlaying { get; }
        public bool IsPaused { get; }

        public override string ToString() {
            if (string.IsNullOrWhiteSpace(Name))
                return "No custom station is active.";
            return Name + " [" + Id + "]" + Environment.NewLine +
                Mode + " | item " + (ProgrammeIndex + 1).ToString(CultureInfo.InvariantCulture) +
                " / " + ProgrammeCount.ToString(CultureInfo.InvariantCulture) +
                " | " + (IsPaused ? "Paused" : IsPlaying ? "Playing" : "Stopped");
        }
    }


    internal enum CRSApiCommandType {
        NextSong,
        PreviousSong,
        RestartSong,
        Play,
        Pause,
        TogglePause,
        Stop,
        RateUp,
        RateDown,
        SetRating,
        ClearRating,
        Seek,
        SeekRelative,
        SeekPercent,
        NextStation,
        PreviousStation,
        SetStationIndex,
        SetStationName,
        ReloadConfig,
        ReloadStations,
        ReloadAll
    }

    internal sealed class CRSApiCommand {
        internal CRSApiCommand(CRSApiCommandType type, double number = 0d, string text = null) {
            Type = type;
            Number = number;
            Text = text;
        }

        internal CRSApiCommandType Type { get; }
        internal double Number { get; }
        internal string Text { get; }
    }

    internal struct RadioApiSnapshot {
        internal RadioApiSnapshot(string stationName, string stationId, string playbackMode,
            int programmeIndex, int programmeCount, bool isCommercial, string trackDisplayName, string filePath,
            uint songPositionMs, uint songDurationMs, uint mediaPositionMs, uint mediaDurationMs,
            uint physicalDurationMs, uint playbackStartMs, uint playbackEndMs,
            uint? configuredStartMs, uint? configuredEndMs, uint? analysisStartMs, uint? analysisEndMs,
            uint? analysisDurationMs, bool allowAnalysisTrimWithinBounds, float rating, string ratingKey,
            bool isPlaying, bool isPaused) {
            StationName = stationName ?? string.Empty;
            StationId = stationId ?? string.Empty;
            PlaybackMode = playbackMode ?? string.Empty;
            ProgrammeIndex = programmeIndex;
            ProgrammeCount = programmeCount;
            IsCommercial = isCommercial;
            TrackDisplayName = trackDisplayName ?? string.Empty;
            FilePath = filePath ?? string.Empty;
            SongPositionMs = songPositionMs;
            SongDurationMs = songDurationMs;
            MediaPositionMs = mediaPositionMs;
            MediaDurationMs = mediaDurationMs;
            PhysicalDurationMs = physicalDurationMs;
            PlaybackStartMs = playbackStartMs;
            PlaybackEndMs = playbackEndMs;
            ConfiguredStartMs = configuredStartMs;
            ConfiguredEndMs = configuredEndMs;
            AnalysisStartMs = analysisStartMs;
            AnalysisEndMs = analysisEndMs;
            AnalysisDurationMs = analysisDurationMs;
            AllowAnalysisTrimWithinBounds = allowAnalysisTrimWithinBounds;
            Rating = rating;
            RatingKey = ratingKey ?? string.Empty;
            IsPlaying = isPlaying;
            IsPaused = isPaused;
        }

        internal static RadioApiSnapshot Empty(string stationName = "", string stationId = "", string mode = "",
            int programmeCount = 0, bool isPlaying = false, bool isPaused = false) {
            return new RadioApiSnapshot(stationName, stationId, mode, -1, programmeCount, false, string.Empty, string.Empty,
                0u, 0u, 0u, 0u, 0u, 0u, 0u, null, null, null, null, null, false, 0f, string.Empty, isPlaying, isPaused);
        }

        internal string StationName;
        internal string StationId;
        internal string PlaybackMode;
        internal int ProgrammeIndex;
        internal int ProgrammeCount;
        internal bool IsCommercial;
        internal string TrackDisplayName;
        internal string FilePath;
        internal uint SongPositionMs;
        internal uint SongDurationMs;
        internal uint MediaPositionMs;
        internal uint MediaDurationMs;
        internal uint PhysicalDurationMs;
        internal uint PlaybackStartMs;
        internal uint PlaybackEndMs;
        internal uint? ConfiguredStartMs;
        internal uint? ConfiguredEndMs;
        internal uint? AnalysisStartMs;
        internal uint? AnalysisEndMs;
        internal uint? AnalysisDurationMs;
        internal bool AllowAnalysisTrimWithinBounds;
        internal float Rating;
        internal string RatingKey;
        internal bool IsPlaying;
        internal bool IsPaused;
    }

    internal static class CRSApiRuntime {
        private const int MaxCommandsPerTick = 32;
        private static readonly ConcurrentQueue<CRSApiCommand> Pending = new ConcurrentQueue<CRSApiCommand>();
        private static volatile MainScript host;
        private static volatile string lastResult = "CRSAPI is waiting for Custom Radio Stations to load.";

        internal static bool IsReady => host != null && host.ApiIsLoaded;
        internal static string LastResult => lastResult ?? string.Empty;
        internal static int StationCount {
            get {
                try { return StationWheelPair.List.Count; } catch { return 0; }
            }
        }

        internal static void Register(MainScript script) {
            CRSApiCommand ignored;
            while (Pending.TryDequeue(out ignored)) { }
            host = script;
            lastResult = "CRSAPI registered; waiting for station catalog.";
        }

        internal static void Unregister(MainScript script) {
            if (!ReferenceEquals(host, script))
                return;
            host = null;
            CRSApiCommand ignored;
            while (Pending.TryDequeue(out ignored)) { }
            lastResult = "CRSAPI is unavailable because the script was unloaded.";
        }

        internal static string Enqueue(CRSApiCommandType type) {
            return Enqueue(new CRSApiCommand(type));
        }

        internal static string Enqueue(CRSApiCommandType type, double number) {
            return Enqueue(new CRSApiCommand(type, number));
        }

        internal static string Enqueue(CRSApiCommandType type, int number) {
            return Enqueue(new CRSApiCommand(type, number));
        }

        internal static string Enqueue(CRSApiCommandType type, string text) {
            return Enqueue(new CRSApiCommand(type, 0d, text));
        }

        private static string Enqueue(CRSApiCommand command) {
            MainScript currentHost = host;
            if (currentHost == null)
                return "CRSAPI unavailable: Custom Radio Stations is not loaded.";
            if (!currentHost.ApiIsLoaded)
                return "CRSAPI unavailable: Custom Radio Stations is still loading or failed to initialize.";
            Pending.Enqueue(command);
            return "Queued: " + command.Type;
        }

        internal static void ProcessPending(MainScript script) {
            if (!ReferenceEquals(host, script))
                return;
            CRSApiCommand command;
            int processed = 0;
            while (processed < MaxCommandsPerTick && Pending.TryDequeue(out command)) {
                processed++;
                try {
                    lastResult = Execute(script, command);
                } catch (Exception ex) {
                    lastResult = "ERROR: " + ex.Message;
                    Logger.Log("WARNING: CRSAPI command '" + command.Type + "' failed: " + ex);
                }
            }
        }

        private static string Execute(MainScript script, CRSApiCommand command) {
            RadioStation station = RadioStation.CurrentPlaying;
            switch (command.Type) {
            case CRSApiCommandType.NextSong:
                if (station == null) return NoStation();
                station.PlayNextSong();
                return "OK: " + CurrentTrackLabel();
            case CRSApiCommandType.PreviousSong:
                if (station == null) return NoStation();
                return station.PlayPreviousSong() ? "OK: " + CurrentTrackLabel() : "Previous song unavailable.";
            case CRSApiCommandType.RestartSong:
                if (station == null) return NoStation();
                return station.RestartCurrentSong() ? "OK: restarted " + CurrentTrackLabel() : "Restart unavailable.";
            case CRSApiCommandType.Play:
                return script.ApiPlay();
            case CRSApiCommandType.Pause:
                if (station == null) return NoStation();
                return station.SetPaused(true) ? "OK: paused." : "Pause unavailable.";
            case CRSApiCommandType.TogglePause: {
                if (station == null) return NoStation();
                bool pause = !station.CurrentSoundIsPaused;
                return station.SetPaused(pause) ? "OK: " + (pause ? "paused." : "playing.") : "Pause toggle unavailable.";
            }
            case CRSApiCommandType.Stop:
                return script.ApiStop();
            case CRSApiCommandType.RateUp:
                return ChangeCurrentRating(station, 0.5f);
            case CRSApiCommandType.RateDown:
                return ChangeCurrentRating(station, -0.5f);
            case CRSApiCommandType.SetRating:
                return SetCurrentRating(station, command.Number);
            case CRSApiCommandType.ClearRating:
                return SetCurrentRating(station, 0d);
            case CRSApiCommandType.Seek:
                if (station == null) return NoStation();
                return station.SeekCurrentSong(command.Number) ? "OK: " + FormatPosition(station.GetApiSnapshot()) : "Seek unavailable.";
            case CRSApiCommandType.SeekRelative:
                if (station == null) return NoStation();
                return station.SeekCurrentSongRelative(command.Number) ? "OK: " + FormatPosition(station.GetApiSnapshot()) : "Seek unavailable.";
            case CRSApiCommandType.SeekPercent:
                if (station == null) return NoStation();
                return station.SeekCurrentSongPercent(command.Number) ? "OK: " + FormatPosition(station.GetApiSnapshot()) : "Seek unavailable.";
            case CRSApiCommandType.NextStation:
                return script.ApiChangeStation(1);
            case CRSApiCommandType.PreviousStation:
                return script.ApiChangeStation(-1);
            case CRSApiCommandType.SetStationIndex:
                return script.ApiSetStation((int)command.Number);
            case CRSApiCommandType.SetStationName:
                return script.ApiSetStation(command.Text);
            case CRSApiCommandType.ReloadConfig:
                Config.Load();
                return "OK: settings.json reloaded.";
            case CRSApiCommandType.ReloadStations:
                script.SetupRadio();
                return "OK: station catalog reloaded (" + StationWheelPair.List.Count + " stations).";
            case CRSApiCommandType.ReloadAll:
                Config.Load();
                script.SetupRadio();
                return "OK: configuration and station catalog reloaded (" + StationWheelPair.List.Count + " stations).";
            default:
                return "Unsupported CRSAPI command.";
            }
        }

        internal static RadioApiSnapshot GetCurrentSnapshot() {
            try {
                RadioStation station = RadioStation.CurrentPlaying;
                return station == null ? RadioApiSnapshot.Empty() : station.GetApiSnapshot();
            } catch {
                return RadioApiSnapshot.Empty();
            }
        }

        internal static string Status() {
            RadioApiSnapshot snapshot = GetCurrentSnapshot();
            if (string.IsNullOrWhiteSpace(snapshot.StationName))
                return IsReady ? "Custom Radio Stations loaded; no custom station is active." : LastResult;
            var builder = new StringBuilder();
            builder.AppendLine(snapshot.StationName + " [" + snapshot.StationId + "]");
            builder.AppendLine(NormalizeDisplay(snapshot.TrackDisplayName));
            builder.AppendLine();
            builder.AppendLine(snapshot.IsPaused ? "Paused" : snapshot.IsPlaying ? "Playing" : "Stopped");
            builder.AppendLine("Mode: " + snapshot.PlaybackMode);
            builder.AppendLine("Programme: " + (snapshot.ProgrammeIndex + 1) + " / " + snapshot.ProgrammeCount + (snapshot.IsCommercial ? " [commercial]" : string.Empty));
            builder.AppendLine("Position: " + FormatMs(snapshot.SongPositionMs) + " / " + FormatMs(snapshot.SongDurationMs));
            return builder.ToString().TrimEnd();
        }

        internal static string TrackInfo() {
            RadioApiSnapshot snapshot = GetCurrentSnapshot();
            if (string.IsNullOrWhiteSpace(snapshot.TrackDisplayName))
                return NoStation();
            var builder = new StringBuilder();
            builder.AppendLine(NormalizeDisplay(snapshot.TrackDisplayName));
            builder.AppendLine("File: " + snapshot.FilePath);
            builder.AppendLine("Song position: " + FormatMs(snapshot.SongPositionMs) + " / " + FormatMs(snapshot.SongDurationMs));
            builder.AppendLine("Media position: " + FormatMs(snapshot.MediaPositionMs) + " / " + FormatMs(snapshot.MediaDurationMs));
            builder.AppendLine("Source duration: " + FormatMs(snapshot.PhysicalDurationMs));
            builder.AppendLine("Rating: " + (snapshot.Rating > 0f ? snapshot.Rating.ToString("0.0", CultureInfo.InvariantCulture) + " / 5" : "Unrated"));
            if (!string.IsNullOrWhiteSpace(snapshot.RatingKey))
                builder.AppendLine("Rating key: " + snapshot.RatingKey);
            builder.AppendLine("Commercial: " + snapshot.IsCommercial);
            return builder.ToString().TrimEnd();
        }

        internal static string StationInfo() {
            RadioApiSnapshot snapshot = GetCurrentSnapshot();
            if (string.IsNullOrWhiteSpace(snapshot.StationName))
                return NoStation();
            return snapshot.StationName + " [" + snapshot.StationId + "]" + Environment.NewLine +
                "Mode: " + snapshot.PlaybackMode + Environment.NewLine +
                "Programme item: " + (snapshot.ProgrammeIndex + 1) + " / " + snapshot.ProgrammeCount + Environment.NewLine +
                "State: " + (snapshot.IsPaused ? "Paused" : snapshot.IsPlaying ? "Playing" : "Stopped");
        }

        internal static string Timeline() {
            RadioStation station = RadioStation.CurrentPlaying;
            return station == null ? NoStation() : station.GetApiTimelineInfo();
        }

        internal static string BoundsInfo() {
            RadioApiSnapshot snapshot = GetCurrentSnapshot();
            if (string.IsNullOrWhiteSpace(snapshot.FilePath))
                return NoStation();
            var builder = new StringBuilder();
            builder.AppendLine("File: " + snapshot.FilePath);
            builder.AppendLine("Physical: " + FormatMs(snapshot.PhysicalDurationMs));
            builder.AppendLine("Configured start: " + FormatNullableMs(snapshot.ConfiguredStartMs));
            builder.AppendLine("Configured end: " + FormatNullableMs(snapshot.ConfiguredEndMs));
            builder.AppendLine("Analysis start: " + FormatNullableMs(snapshot.AnalysisStartMs));
            builder.AppendLine("Analysis end: " + FormatNullableMs(snapshot.AnalysisEndMs));
            builder.AppendLine("Analysis duration: " + FormatNullableMs(snapshot.AnalysisDurationMs));
            builder.AppendLine("Allow analysis trim within manual bounds: " + snapshot.AllowAnalysisTrimWithinBounds);
            builder.AppendLine("Effective start: " + FormatMs(snapshot.PlaybackStartMs));
            builder.AppendLine("Effective end: " + FormatMs(snapshot.PlaybackEndMs));
            builder.AppendLine("Effective length: " + FormatMs(snapshot.MediaDurationMs));
            return builder.ToString().TrimEnd();
        }

        internal static string DumpProgramme() {
            RadioStation station = RadioStation.CurrentPlaying;
            return station == null ? NoStation() : station.DumpProgrammeForApi();
        }

        internal static string Help() {
            return "CRSAPI commands" + Environment.NewLine +
                "State: CurrentStation, CurrentTrack, Position, Duration, CurrentRating, CurrentRatingKey, Track, Station, IsPlaying, IsPaused, LastResult" + Environment.NewLine +
                "Playback: NextSong(), PreviousSong(), RestartSong(), Play(), Pause(), TogglePause(), Stop()" + Environment.NewLine +
                "Ratings: RateUp(), RateDown(), SetRating(value), ClearRating()" + Environment.NewLine +
                "Seek: Seek(seconds), SeekRelative(seconds), SeekPercent(percent)" + Environment.NewLine +
                "Stations: NextStation(), PreviousStation(), SetStation(index/name), Stations()" + Environment.NewLine +
                "Debug: Status(), TrackInfo(), StationInfo(), Timeline(), BoundsInfo(), DumpProgramme()" + Environment.NewLine +
                "Reload: ReloadConfig(), ReloadStations(), Reload()";
        }

        internal static string Stations() {
            try {
                StationWheelPair[] pairs = StationWheelPair.List.ToArray();
                if (pairs.Length == 0)
                    return "No custom stations are loaded.";
                var builder = new StringBuilder();
                for (int index = 0; index < pairs.Length; index++) {
                    builder.Append(index.ToString(CultureInfo.InvariantCulture));
                    builder.Append(": ");
                    builder.Append(pairs[index].Station.Name);
                    builder.Append(" [");
                    builder.Append(pairs[index].Station.Id);
                    builder.Append(']');
                    if (ReferenceEquals(pairs[index].Station, RadioStation.CurrentPlaying))
                        builder.Append("  < CURRENT");
                    if (index < pairs.Length - 1)
                        builder.AppendLine();
                }
                return builder.ToString();
            } catch (Exception ex) {
                return "Could not enumerate stations: " + ex.Message;
            }
        }

        private static string ChangeCurrentRating(RadioStation station, float delta) {
            if (station == null)
                return NoStation();
            TrackRatingTarget target;
            if (!station.TryGetCurrentRatingTarget(out target))
                return "Rating unavailable for the current programme item.";
            float value = TrackRatingStore.ChangeRating(target, delta);
            return "OK: rating " + FormatRating(value) + ".";
        }

        private static string SetCurrentRating(RadioStation station, double requested) {
            if (station == null)
                return NoStation();
            if (double.IsNaN(requested) || double.IsInfinity(requested) || requested < 0d || requested > 5d)
                return "Rating must be between 0 and 5.";
            TrackRatingTarget target;
            if (!station.TryGetCurrentRatingTarget(out target))
                return "Rating unavailable for the current programme item.";
            float value = TrackRatingStore.SetRating(target, (float)requested);
            return "OK: rating " + FormatRating(value) + ".";
        }

        private static string FormatRating(float rating) {
            return rating > 0f ? rating.ToString("0.0", CultureInfo.InvariantCulture) + " / 5" : "cleared";
        }

        private static string CurrentTrackLabel() {
            RadioApiSnapshot snapshot = GetCurrentSnapshot();
            return string.IsNullOrWhiteSpace(snapshot.TrackDisplayName) ? snapshot.StationName : NormalizeDisplay(snapshot.TrackDisplayName);
        }

        private static string FormatPosition(RadioApiSnapshot snapshot) {
            return FormatMs(snapshot.SongPositionMs) + " / " + FormatMs(snapshot.SongDurationMs);
        }

        private static string NormalizeDisplay(string value) {
            return (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", " - ").Trim();
        }

        private static string FormatNullableMs(uint? milliseconds) {
            return milliseconds.HasValue ? FormatMs(milliseconds.Value) : "<none>";
        }

        private static string FormatMs(uint milliseconds) {
            TimeSpan value = TimeSpan.FromMilliseconds(milliseconds);
            return value.TotalHours >= 1d ? value.ToString(@"hh\:mm\:ss\.fff") : value.ToString(@"mm\:ss\.fff");
        }

        private static string NoStation() {
            return "No custom station is active.";
        }
    }
}
