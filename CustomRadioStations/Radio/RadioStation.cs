using GTA;
using GTA.Native;

using SelectorWheel;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CustomRadioStations {
    internal sealed class RadioStation : IDisposable {
        internal static readonly Random random = new Random();

        private readonly WheelCategory wheelCategory;
        private readonly List<ResolvedMediaSource> trackSources;
        private readonly List<ResolvedMediaSource> commercialSources;
        private readonly List<StationMediaItem> programme = new List<StationMediaItem>();
        private readonly PlaybackConfig playback;
        private readonly CommercialBreakConfig commercialBreaks;

        private SoundFile currentSound;
        private bool hasPlayedOnce;
        private bool playbackFinished;
        private int lastPlayedSoundIndex;
        private int lastCommercialIndex = -1;
        private uint stoppedPositionSound;
        private DateTime lastPlayedTimeUtc;
        private DateTime trackUpdateTimer = DateTime.Now;
        private readonly int initialProgrammeIndex;
        private readonly uint initialProgrammePosition;
        private readonly DateTime broadcastEpochUtc;
        private readonly uint[] programmeLengths;

        internal RadioStation(WheelCategory correspondingWheelCategory, StationDefinition definition) {
            wheelCategory = correspondingWheelCategory;
            Id = definition.Id;
            Name = definition.Name;
            Description = definition.Description ?? string.Empty;
            playback = definition.Playback ?? new PlaybackConfig();
            commercialBreaks = definition.CommercialBreaks ?? new CommercialBreakConfig();
            trackSources = new List<ResolvedMediaSource>(definition.Tracks ?? new ResolvedMediaSource[0]);
            commercialSources = new List<ResolvedMediaSource>(definition.Commercials ?? new ResolvedMediaSource[0]);

            BuildProgramme();
            programmeLengths = programme.Select(item => item.SoundFile.Length).ToArray();
            initialProgrammeIndex = SelectInitialProgrammeIndex();
            initialProgrammePosition = SelectInitialProgrammePosition();
            broadcastEpochUtc = DateTime.UtcNow;
            lastPlayedSoundIndex = IsBroadcastMode ? initialProgrammeIndex : 0;
            stoppedPositionSound = IsBroadcastMode ? initialProgrammePosition : 0u;
            lastPlayedTimeUtc = broadcastEpochUtc;
            RefreshWheelInfo();
        }

        internal string Id { get; }
        internal string Name { get; }
        internal string Description { get; }
        internal uint TotalLength { get; private set; }
        internal bool HasPlayableSounds => programme.Any(item => !item.IsCommercial);
        internal string PlaybackMode => IsBroadcastMode ? "Broadcast" : "Playlist";
        internal int ProgrammeCount => programme.Count;

        private bool IsBroadcastMode => string.Equals(playback.Mode, "broadcast", StringComparison.OrdinalIgnoreCase);

        private void BuildProgramme() {
            var tracks = new List<StationMediaItem>();
            foreach (ResolvedMediaSource source in trackSources) {
                SoundFile sound = TryCreateSound(source, "track");
                if (sound != null)
                    tracks.Add(new StationMediaItem(sound, false));
                Config.LoadTick();
            }

            if (playback.Shuffle)
                Shuffle(tracks);

            int tracksUntilBreak = NextInclusive(commercialBreaks.MinTracksBetween, commercialBreaks.MaxTracksBetween);
            int tracksSinceBreak = 0;
            foreach (StationMediaItem track in tracks) {
                programme.Add(track);
                tracksSinceBreak++;

                if (!commercialBreaks.Enabled || commercialSources.Count == 0 ||
                    commercialBreaks.MaxCommercials == 0 || tracksSinceBreak < tracksUntilBreak)
                    continue;

                int commercialCount = NextInclusive(commercialBreaks.MinCommercials, commercialBreaks.MaxCommercials);
                for (int i = 0; i < commercialCount; i++) {
                    ResolvedMediaSource source = GetNextCommercialSource();
                    SoundFile commercial = TryCreateSound(source, "commercial");
                    if (commercial != null)
                        programme.Add(new StationMediaItem(commercial, true));
                }

                tracksSinceBreak = 0;
                tracksUntilBreak = NextInclusive(commercialBreaks.MinTracksBetween, commercialBreaks.MaxTracksBetween);
            }
        }

        private ResolvedMediaSource GetNextCommercialSource() {
            if (commercialSources.Count == 1) {
                lastCommercialIndex = 0;
                return commercialSources[0];
            }

            int next;
            do {
                next = random.Next(commercialSources.Count);
            }
            while (next == lastCommercialIndex);
            lastCommercialIndex = next;
            return commercialSources[next];
        }

        private SoundFile TryCreateSound(ResolvedMediaSource source, string kind) {
            try {
                string path = source.FilePath;
                if (string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase)) {
                    string target = GeneralHelper.GetShortcutTargetFile(path);
                    if (string.IsNullOrEmpty(target))
                        throw new FileNotFoundException("Shortcut target does not exist.", path);
                    return new SoundFile(target, path, source);
                }
                return new SoundFile(source);
            } catch (Exception ex) {
                Logger.Log("WARNING: Could not load " + kind + " '" + source.FilePath + "' for station '" + Name + "': " + ex.Message);
                return null;
            }
        }

        private static void Shuffle<T>(IList<T> items) {
            for (int i = items.Count - 1; i > 0; i--) {
                int selected = random.Next(i + 1);
                T value = items[selected];
                items[selected] = items[i];
                items[i] = value;
            }
        }

        private static int NextInclusive(int minimum, int maximum) {
            return minimum >= maximum ? minimum : random.Next(minimum, maximum + 1);
        }

        private int SelectInitialProgrammeIndex() {
            if (!IsBroadcastMode)
                return 0;

            int[] trackIndexes = programme
                .Select((item, index) => new { item, index })
                .Where(candidate => !candidate.item.IsCommercial)
                .Select(candidate => candidate.index)
                .ToArray();
            return trackIndexes.Length == 0 ? 0 : trackIndexes[random.Next(trackIndexes.Length)];
        }

        private uint SelectInitialProgrammePosition() {
            if (!IsBroadcastMode || programme.Count == 0)
                return 0u;

            uint length = programme[initialProgrammeIndex].SoundFile.Length;
            if (length <= 1u)
                return 0u;

            double fraction = 0.05d + (random.NextDouble() * 0.80d);
            return (uint)(fraction * (length - 1u));
        }

        private bool HasCompleteProgrammeTimeline() {
            if (programmeLengths == null || programmeLengths.Length != programme.Count || programmeLengths.Length == 0)
                return false;
            for (int index = 0; index < programmeLengths.Length; index++) {
                if (programmeLengths[index] == 0u)
                    return false;
            }
            return true;
        }

        private void RefreshProgrammeLength(int index) {
            if (programmeLengths == null || index < 0 || index >= programmeLengths.Length)
                return;
            programmeLengths[index] = programme[index].SoundFile.Length;
        }

        private BroadcastPosition GetInactivePlaybackPosition(DateTime utcNow) {
            if (programme.Count == 0 || playbackFinished)
                return new BroadcastPosition(-1, 0u, true);

            int anchorIndex = hasPlayedOnce ? lastPlayedSoundIndex : (IsBroadcastMode ? initialProgrammeIndex : 0);
            uint anchorPosition = hasPlayedOnce ? stoppedPositionSound : (IsBroadcastMode ? initialProgrammePosition : 0u);
            if (!IsBroadcastMode)
                return new BroadcastPosition(Math.Max(0, Math.Min(anchorIndex, programme.Count - 1)), anchorPosition, false);

            DateTime anchorTime = hasPlayedOnce ? lastPlayedTimeUtc : broadcastEpochUtc;
            ulong elapsed = (ulong)Math.Max(0d, (utcNow - anchorTime).TotalMilliseconds);
            if (HasCompleteProgrammeTimeline())
                return BroadcastTimeline.Advance(programmeLengths, anchorIndex, anchorPosition, elapsed, playback.Loop);

            return BroadcastTimeline.AdvanceKnownPrefix(
                programmeLengths, anchorIndex, anchorPosition, elapsed, playback.Loop);
        }

        private BroadcastPosition GetCurrentPlaybackPosition() {
            if (currentSound != null && currentSound.Sound != null) {
                int index = programme.FindIndex(item => item.SoundFile == currentSound);
                if (index >= 0)
                    return new BroadcastPosition(index, currentSound.PlayPosition(), false);
            }
            return GetInactivePlaybackPosition(DateTime.UtcNow);
        }

        internal void Update() {
            if (currentSound == null || currentSound.Sound == null)
                return;
            if (currentSound.HasTrackList && trackUpdateTimer < DateTime.Now) {
                UpdateWheelInfo();
                UpdateTrackUpdateTimer();
            }
            if (currentSound.IsFinishedPlaying())
                PlayNextSound();
        }

        internal void Play() {
            if (!HasPlayableSounds) {
                Logger.Log("WARNING: Station '" + Name + "' has no playable tracks; ignoring play request.");
                return;
            }

            if (!hasPlayedOnce) {
                if (IsBroadcastMode && !HasCompleteProgrammeTimeline()) {
                    ulong elapsed = (ulong)Math.Max(0d, (DateTime.UtcNow - broadcastEpochUtc).TotalMilliseconds);
                    if (!ResumeBroadcastWithUnknownLengths(initialProgrammeIndex, initialProgrammePosition, elapsed))
                        return;
                    currentSound.Sound.Paused = false;
                } else {
                    BroadcastPosition target = GetInactivePlaybackPosition(DateTime.UtcNow);
                    if (target.Finished) {
                        FinishPlayback();
                        return;
                    }

                    int requestedIndex = Math.Max(0, Math.Min(target.Index, programme.Count - 1));
                    int startedIndex;
                    if (!TryStartProgrammeItem(requestedIndex, false, IsBroadcastMode && playback.Loop, out startedIndex))
                        return;
                    lastPlayedSoundIndex = startedIndex;
                    currentSound.Seek(startedIndex == requestedIndex ? target.Position : 0u);
                    currentSound.Sound.Paused = false;
                    UpdateProgrammeLength();
                }
                hasPlayedOnce = true;
                playbackFinished = false;
                UpdateWheelInfo();
            } else if (IsBroadcastMode) {
                ResumeBroadcastContinuity();
            } else {
                ResumePlaylist();
            }

            if (currentSound == null || currentSound.Sound == null)
                return;

            Function.Call(Hash.SET_AUDIO_FLAG, "DisableFlightMusic", true);
            Function.Call(Hash.SET_AUDIO_FLAG, "DisableWantedMusic", true);
            AudioPauseCoordinator.NotifyStationStarted(this);
        }

        private bool StartCurrentSound(bool resume = false) {
            currentSound.PlaySound(resume, false, true);
            if (currentSound.Sound == null) {
                Logger.Log("WARNING: MiniAudioEx failed to start station '" + Name + "'.");
                currentSound = null;
                return false;
            }
            currentSound.Sound.Volume = playback.Volume * currentSound.NormalizationGain;
            return true;
        }

        private void ResumePlaylist() {
            int index = Math.Max(0, Math.Min(lastPlayedSoundIndex, programme.Count - 1));
            int startedIndex;
            if (!TryStartProgrammeItem(index, true, false, out startedIndex))
                return;
            lastPlayedSoundIndex = startedIndex;
            uint maximum = currentSound.Length > 0 ? currentSound.Length - 1 : 0;
            currentSound.Seek(startedIndex == index ? Math.Min(maximum, stoppedPositionSound) : 0u);
            playbackFinished = false;
            CurrentSoundIsPaused = false;
            UpdateProgrammeLength();
            UpdateWheelInfo();
        }

        private void ResumeBroadcastContinuity() {
            int safeLastIndex = Math.Max(0, Math.Min(lastPlayedSoundIndex, programme.Count - 1));
            ulong elapsed = lastPlayedTimeUtc == default(DateTime)
                ? 0UL
                : (ulong)Math.Max(0d, (DateTime.UtcNow - lastPlayedTimeUtc).TotalMilliseconds);

            if (HasCompleteProgrammeTimeline()) {
                BroadcastPosition target = GetInactivePlaybackPosition(DateTime.UtcNow);
                if (target.Finished) {
                    FinishPlayback();
                    return;
                }

                if (target.Index != safeLastIndex)
                    programme[safeLastIndex].SoundFile.ReleaseSound();

                int startedIndex;
                if (!TryStartProgrammeItem(target.Index, true, playback.Loop, out startedIndex)) {
                    FinishPlayback();
                    return;
                }
                lastPlayedSoundIndex = startedIndex;
                currentSound.Seek(startedIndex == target.Index
                    ? Math.Min(currentSound.Length > 0 ? currentSound.Length - 1 : 0, target.Position)
                    : 0u);
                UpdateProgrammeLength();
            } else if (!ResumeBroadcastWithUnknownLengths(safeLastIndex, stoppedPositionSound, elapsed)) {
                FinishPlayback();
                return;
            }

            playbackFinished = false;
            CurrentSoundIsPaused = false;
            UpdateWheelInfo();
            if (currentSound != null && currentSound.HasTrackList)
                UpdateTrackUpdateTimer();
        }

        private bool ResumeBroadcastWithUnknownLengths(int startIndex, uint startPosition, ulong elapsed) {
            int index = startIndex;
            uint position = startPosition;
            int consecutiveFailures = 0;

            while (true) {
                currentSound = programme[index].SoundFile;
                if (!StartCurrentSound(true)) {
                    consecutiveFailures++;
                    if (consecutiveFailures >= programme.Count)
                        return false;
                    if (!TryGetNextProgrammeIndex(index, playback.Loop, out index))
                        return false;
                    position = 0u;
                    continue;
                }

                consecutiveFailures = 0;
                RefreshProgrammeLength(index);
                UpdateProgrammeLength();
                uint length = currentSound.Length;
                ulong remaining = length > position ? (ulong)(length - position) : 0UL;
                if (length > 0 && elapsed < remaining) {
                    lastPlayedSoundIndex = index;
                    currentSound.Seek(position + (uint)elapsed);
                    return true;
                }

                if (remaining > 0)
                    elapsed -= remaining;
                currentSound.StopSound();
                currentSound.ReleaseSound();

                if (!TryGetNextProgrammeIndex(index, playback.Loop, out index))
                    return false;
                position = 0u;

                if (playback.Loop && programme.All(item => item.SoundFile.Length > 0)) {
                    ulong totalLength = (ulong)programme.Sum(item => (long)item.SoundFile.Length);
                    if (totalLength > 0)
                        elapsed %= totalLength;
                }
            }
        }

        private bool TryStartProgrammeItem(int startIndex, bool resume, bool allowWrap, out int startedIndex) {
            startedIndex = -1;
            if (programme.Count == 0)
                return false;

            int index = Math.Max(0, Math.Min(startIndex, programme.Count - 1));
            for (int attempts = 0; attempts < programme.Count; attempts++) {
                currentSound = programme[index].SoundFile;
                if (StartCurrentSound(resume)) {
                    RefreshProgrammeLength(index);
                    startedIndex = index;
                    return true;
                }

                if (!TryGetNextProgrammeIndex(index, allowWrap, out index))
                    break;
            }

            currentSound = null;
            return false;
        }

        private bool TryGetNextProgrammeIndex(int currentIndex, bool allowWrap, out int nextIndex) {
            if (currentIndex < programme.Count - 1) {
                nextIndex = currentIndex + 1;
                return true;
            }
            if (allowWrap && programme.Count > 0) {
                nextIndex = 0;
                return true;
            }
            nextIndex = -1;
            return false;
        }

        private bool TryGetPreviousProgrammeIndex(int currentIndex, bool allowWrap, out int previousIndex) {
            if (currentIndex > 0) {
                previousIndex = currentIndex - 1;
                return true;
            }
            if (allowWrap && programme.Count > 0) {
                previousIndex = programme.Count - 1;
                return true;
            }
            previousIndex = -1;
            return false;
        }

        internal void Stop() {
            if (currentSound == null || currentSound.Sound == null)
                return;
            StationMediaItem item = programme.Find(candidate => candidate.SoundFile == currentSound);
            if (item == null)
                return;

            stoppedPositionSound = currentSound.PlayPosition();
            lastPlayedSoundIndex = programme.IndexOf(item);
            lastPlayedTimeUtc = DateTime.UtcNow;
            playbackFinished = false;
            CurrentSoundIsPaused = true;
            AudioPauseCoordinator.NotifyStationStopped(this);
            currentSound = null;
            RefreshWheelInfo();
            ResetAudioFlags();
        }

        private void PlayNextSound() {
            if (!HasPlayableSounds)
                return;
            int currentIndex = currentSound == null ? -1 : programme.FindIndex(item => item.SoundFile == currentSound);
            SoundFile previousSound = currentSound;
            if (previousSound != null) {
                previousSound.StopSound();
                previousSound.ReleaseSound();
            }

            if (currentIndex >= programme.Count - 1 && !playback.Loop) {
                FinishPlayback();
                return;
            }

            int nextIndex = currentIndex >= 0 && currentIndex < programme.Count - 1 ? currentIndex + 1 : 0;
            int startedIndex;
            if (!TryStartProgrammeItem(nextIndex, true, playback.Loop, out startedIndex)) {
                FinishPlayback();
                return;
            }
            lastPlayedSoundIndex = startedIndex;
            stoppedPositionSound = 0u;
            playbackFinished = false;
            UpdateProgrammeLength();
            CurrentSoundIsPaused = false;
            UpdateWheelInfo();
            UpdateTrackUpdateTimer();
        }

        internal void PlayNextSong() {
            if (currentSound == null)
                return;
            int nextTrackIndex = currentSound.GetNextPlayableTrackIndex(currentSound.GetCurrentTrackIndex());
            if (nextTrackIndex >= 0 && currentSound.SeekToTrackIndex(nextTrackIndex)) {
                UpdateWheelInfo();
                UpdateTrackUpdateTimer();
            } else {
                PlayNextSound();
            }
        }

        internal bool PlayPreviousSong() {
            if (currentSound == null || currentSound.Sound == null)
                return false;

            int trackIndex = currentSound.GetCurrentTrackIndex();
            int previousTrackIndex = currentSound.GetPreviousPlayableTrackIndex(trackIndex);
            if (previousTrackIndex >= 0) {
                if (!currentSound.SeekToTrackIndex(previousTrackIndex))
                    return false;
                UpdateWheelInfo();
                UpdateTrackUpdateTimer();
                return true;
            }

            int currentIndex = programme.FindIndex(item => item.SoundFile == currentSound);
            int previousIndex;
            if (currentIndex < 0 || !TryGetPreviousProgrammeIndex(currentIndex, playback.Loop, out previousIndex)) {
                currentSound.RestartCurrentTrack();
                UpdateWheelInfo();
                UpdateTrackUpdateTimer();
                return true;
            }

            SoundFile previousSound = currentSound;
            previousSound.StopSound();
            previousSound.ReleaseSound();

            int startedIndex;
            if (!TryStartProgrammeItem(previousIndex, true, playback.Loop, out startedIndex))
                return false;

            lastPlayedSoundIndex = startedIndex;
            stoppedPositionSound = 0u;
            playbackFinished = false;
            UpdateProgrammeLength();
            CurrentSoundIsPaused = false;
            int lastPlayableTrack = currentSound.GetLastPlayableTrackIndex();
            if (lastPlayableTrack >= 0)
                currentSound.SeekToTrackIndex(lastPlayableTrack);
            else
                currentSound.Seek(0u);
            UpdateWheelInfo();
            UpdateTrackUpdateTimer();
            return true;
        }

        internal bool RestartCurrentSong() {
            if (currentSound == null || currentSound.Sound == null || !currentSound.RestartCurrentTrack())
                return false;
            UpdateWheelInfo();
            UpdateTrackUpdateTimer();
            return true;
        }

        internal bool SeekCurrentSong(double seconds) {
            if (currentSound == null || currentSound.Sound == null || double.IsNaN(seconds) || double.IsInfinity(seconds))
                return false;

            uint logicalPosition = currentSound.PlayPosition();
            uint start;
            uint end;
            currentSound.GetLogicalTrackBounds(logicalPosition, out start, out end);
            if (end <= start)
                return false;

            double milliseconds = Math.Max(0d, seconds * 1000d);
            ulong target = (ulong)start + (ulong)Math.Min(milliseconds, uint.MaxValue);
            uint maximum = end - 1u;
            currentSound.Seek((uint)Math.Min((ulong)maximum, target));
            UpdateWheelInfo();
            UpdateTrackUpdateTimer();
            return true;
        }

        internal bool SeekCurrentSongRelative(double seconds) {
            if (currentSound == null || currentSound.Sound == null || double.IsNaN(seconds) || double.IsInfinity(seconds))
                return false;

            uint logicalPosition = currentSound.PlayPosition();
            uint start;
            uint end;
            currentSound.GetLogicalTrackBounds(logicalPosition, out start, out end);
            if (end <= start)
                return false;

            double currentSongPosition = logicalPosition >= start ? logicalPosition - start : 0u;
            double targetSeconds = (currentSongPosition / 1000d) + seconds;
            return SeekCurrentSong(Math.Max(0d, targetSeconds));
        }

        internal bool SeekCurrentSongPercent(double percent) {
            if (currentSound == null || currentSound.Sound == null || double.IsNaN(percent) || double.IsInfinity(percent))
                return false;

            uint logicalPosition = currentSound.PlayPosition();
            uint start;
            uint end;
            currentSound.GetLogicalTrackBounds(logicalPosition, out start, out end);
            if (end <= start)
                return false;
            double clamped = Math.Max(0d, Math.Min(100d, percent));
            double durationSeconds = (end - start) / 1000d;
            return SeekCurrentSong(durationSeconds * (clamped / 100d));
        }

        internal bool SetPaused(bool paused) {
            if (currentSound == null || currentSound.Sound == null)
                return false;
            if (!paused && AudioPauseCoordinator.IsSuspended)
                return false;
            currentSound.IsPaused = paused;
            return true;
        }

        internal RadioApiSnapshot GetApiSnapshot() {
            BroadcastPosition position = GetCurrentPlaybackPosition();
            if (position.Finished || position.Index < 0 || position.Index >= programme.Count)
                return RadioApiSnapshot.Empty(Name, Id, PlaybackMode, programme.Count, IsPlaying, CurrentSoundIsPaused);

            StationMediaItem item = programme[position.Index];
            SoundFile sound = item.SoundFile;
            uint songStart;
            uint songEnd;
            sound.GetLogicalTrackBounds(position.Position, out songStart, out songEnd);
            uint songPosition = position.Position > songStart ? position.Position - songStart : 0u;
            uint songDuration = songEnd > songStart ? songEnd - songStart : sound.Length;
            TrackAnalysis analysis = sound.Analysis;
            return new RadioApiSnapshot(
                Name, Id, PlaybackMode, position.Index, programme.Count, item.IsCommercial,
                sound.GetDisplayNameAtLogicalPosition(position.Position), sound.FilePath,
                songPosition, songDuration, position.Position, sound.Length, sound.PhysicalLength,
                sound.PlaybackStartMs, sound.PlaybackEndMs, sound.ConfiguredStartMs, sound.ConfiguredEndMs,
                analysis == null ? (uint?)null : analysis.AudioStartMs,
                analysis == null ? (uint?)null : analysis.AudioEndMs,
                analysis == null ? (uint?)null : analysis.DurationMs,
                sound.AllowAnalysisTrimWithinBounds, IsPlaying, CurrentSoundIsPaused);
        }

        internal string GetApiTimelineInfo() {
            RadioApiSnapshot snapshot = GetApiSnapshot();
            var lines = new List<string>();
            lines.Add(Name + " [" + Id + "]");
            lines.Add("Mode: " + PlaybackMode);
            lines.Add("Live source: " + (currentSound != null && currentSound.Sound != null));
            lines.Add("Has played: " + hasPlayedOnce);
            lines.Add("Finished: " + playbackFinished);
            lines.Add("Programme: " + (snapshot.ProgrammeIndex >= 0 ? (snapshot.ProgrammeIndex + 1).ToString() : "-") + " / " + programme.Count);
            lines.Add("Projected media position: " + FormatApiDuration(snapshot.MediaPositionMs) + " / " + FormatApiDuration(snapshot.MediaDurationMs));
            int anchorIndex = hasPlayedOnce ? lastPlayedSoundIndex : (IsBroadcastMode ? initialProgrammeIndex : 0);
            uint anchorPosition = hasPlayedOnce ? stoppedPositionSound : (IsBroadcastMode ? initialProgrammePosition : 0u);
            DateTime anchorTime = hasPlayedOnce ? lastPlayedTimeUtc : broadcastEpochUtc;
            lines.Add("Anchor index: " + anchorIndex);
            lines.Add("Anchor position: " + FormatApiDuration(anchorPosition));
            lines.Add("Anchor UTC: " + anchorTime.ToString("O"));
            if (IsBroadcastMode)
                lines.Add("Elapsed since anchor: " + TimeSpan.FromMilliseconds(Math.Max(0d, (DateTime.UtcNow - anchorTime).TotalMilliseconds)).ToString());
            lines.Add("Complete duration map: " + HasCompleteProgrammeTimeline());
            return string.Join(Environment.NewLine, lines);
        }

        internal string DumpProgrammeForApi() {
            var lines = new List<string>();
            RadioApiSnapshot current = GetApiSnapshot();
            lines.Add(Name + " — " + programme.Count + " programme items");
            for (int index = 0; index < programme.Count; index++) {
                SoundFile sound = programme[index].SoundFile;
                string marker = index == current.ProgrammeIndex ? "  < CURRENT" : string.Empty;
                string kind = programme[index].IsCommercial ? " [AD]" : string.Empty;
                lines.Add("[" + index.ToString("D2") + "] " + FormatApiDuration(sound.Length) + "  " +
                    sound.PreviewDisplayName.Replace("\r", " ").Replace("\n", " / ").Trim() + kind + marker);
            }
            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatApiDuration(uint milliseconds) {
            TimeSpan value = TimeSpan.FromMilliseconds(milliseconds);
            return value.TotalHours >= 1d
                ? value.ToString(@"hh\:mm\:ss\.fff")
                : value.ToString(@"mm\:ss\.fff");
        }

        private void FinishPlayback() {
            AudioPauseCoordinator.NotifyStationStopped(this);
            if (currentSound != null) {
                currentSound.StopSound();
                currentSound.ReleaseSound();
            }
            currentSound = null;
            playbackFinished = true;
            RenderWheelInfo(null);
            ResetAudioFlags();
            if (ReferenceEquals(CurrentPlaying, this))
                CurrentPlaying = null;
        }

        private void UpdateProgrammeLength() {
            if (currentSound == null)
                return;
            StationMediaItem item = programme.Find(candidate => candidate.SoundFile == currentSound);
            if (item == null || currentSound.LengthAdded)
                return;
            item.StartTime = TotalLength;
            TotalLength += currentSound.Length;
            currentSound.LengthAdded = true;
        }

        internal void RescanSoundsTracklists() {
            foreach (StationMediaItem item in programme)
                item.SoundFile.HasTrackList = item.SoundFile.TracklistExists(item.SoundFile.FilePath);
            RefreshWheelInfo();
            if (currentSound != null && currentSound.Sound != null)
                UpdateTrackUpdateTimer();
        }

        private void UpdateWheelInfo() {
            RefreshWheelInfo();
        }

        internal void RefreshWheelInfo() {
            BroadcastPosition position = GetCurrentPlaybackPosition();
            if (position.Finished || position.Index < 0 || position.Index >= programme.Count) {
                RenderWheelInfo(null);
                return;
            }

            SoundFile sound = programme[position.Index].SoundFile;
            RenderWheelInfo(sound.GetDisplayNameAtLogicalPosition(position.Position));
        }

        internal void UpdateDashboardInfo() {
            if (currentSound == null || currentSound.Sound == null)
                return;
            Ped player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsInVehicle())
                return;
            string[] info = (currentSound.DisplayName ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            RadioNativeFunctions.UpdateRadioScaleform(Name,
                info.Length > 0 ? info[0] : string.Empty,
                info.Length > 1 ? info[1] : string.Empty);
        }

        private void UpdateTrackUpdateTimer() {
            trackUpdateTimer = DateTime.Now.AddMilliseconds(
                currentSound == null || currentSound.Sound == null ? 5000 : currentSound.TimeUntilNextTrack());
        }

        private void RenderWheelInfo(string displayName) {
            if (wheelCategory == null || wheelCategory.ItemList == null || wheelCategory.ItemList.Count == 0)
                return;

            wheelCategory.ItemList[0].Name = string.IsNullOrWhiteSpace(displayName)
                ? Name
                : Name + "\n" + displayName;
        }

        private static void ResetAudioFlags() {
            Function.Call(Hash.SET_AUDIO_FLAG, "DisableFlightMusic", false);
            Function.Call(Hash.SET_AUDIO_FLAG, "DisableWantedMusic", false);
        }


        internal bool IsPlaying => currentSound != null && currentSound.IsPlaying();

        internal bool CurrentSoundIsPaused {
            get {
                return currentSound != null && currentSound.IsPaused;
            }
            set {
                if (currentSound != null)
                    currentSound.IsPaused = value;
            }
        }

        public void Dispose() {
            AudioPauseCoordinator.NotifyStationStopped(this);
            foreach (StationMediaItem item in programme)
                item.SoundFile.Dispose();
            programme.Clear();
        }

        internal static volatile RadioStation CurrentPlaying;
        internal static RadioStation NextQueuedStation;

        internal static void ManageStations() {
            if (CurrentPlaying != null)
                CurrentPlaying.Update();
        }
    }

    internal sealed class StationMediaItem {
        internal StationMediaItem(SoundFile soundFile, bool isCommercial) {
            SoundFile = soundFile;
            IsCommercial = isCommercial;
        }

        internal SoundFile SoundFile { get; }
        internal bool IsCommercial { get; }
        internal uint StartTime { get; set; }
    }

    /// <summary>
    /// Keeps independent game-menu and application-focus pause reasons from fighting
    /// over the same MiniAudio source. Only a source paused here is resumed here.
    /// </summary>
    internal static class AudioPauseCoordinator {
        private static readonly object SyncRoot = new object();
        private static bool gamePaused;
        private static bool focusPaused;
        private static bool suspended;
        private static RadioStation pausedStation;
        private static DateTime pauseInputLatchUntil;
        private static bool observedPauseMenu;

        internal static bool IsSuspended {
            get {
                lock (SyncRoot)
                    return suspended;
            }
        }

        internal static void ReportGamePauseState(bool pauseMenuActive, bool pauseInputPressed) {
            lock (SyncRoot) {
                if (pauseInputPressed)
                    LatchPauseInput();

                if (pauseMenuActive) {
                    observedPauseMenu = true;
                    gamePaused = true;
                } else if (observedPauseMenu) {
                    observedPauseMenu = false;
                    pauseInputLatchUntil = DateTime.MinValue;
                    gamePaused = false;
                } else {
                    gamePaused = DateTime.UtcNow < pauseInputLatchUntil;
                }
                Apply();
            }
        }

        internal static void NotifyPauseInput() {
            lock (SyncRoot) {
                LatchPauseInput();
                gamePaused = true;
                Apply();
            }
        }

        internal static void SetFocusPaused(bool value) {
            lock (SyncRoot) {
                focusPaused = value;
                Apply();
            }
        }

        internal static void NotifyStationStarted(RadioStation station) {
            lock (SyncRoot) {
                if (!suspended || station == null || station.CurrentSoundIsPaused)
                    return;
                pausedStation = station;
                pausedStation.CurrentSoundIsPaused = true;
            }
        }

        internal static void NotifyStationStopped(RadioStation station) {
            lock (SyncRoot) {
                if (ReferenceEquals(pausedStation, station))
                    pausedStation = null;
            }
        }

        internal static void Reset() {
            lock (SyncRoot) {
                gamePaused = false;
                focusPaused = false;
                observedPauseMenu = false;
                pauseInputLatchUntil = DateTime.MinValue;
                ResumeOwnedStation();
                suspended = false;
            }
        }

        private static void Apply() {
            bool shouldSuspend = gamePaused || focusPaused;
            if (shouldSuspend == suspended)
                return;
            suspended = shouldSuspend;

            if (suspended) {
                RadioStation station = RadioStation.CurrentPlaying;
                if (station != null && !station.CurrentSoundIsPaused) {
                    pausedStation = station;
                    pausedStation.CurrentSoundIsPaused = true;
                }
            } else {
                ResumeOwnedStation();
            }
        }

        private static void ResumeOwnedStation() {
            if (pausedStation != null && ReferenceEquals(RadioStation.CurrentPlaying, pausedStation))
                pausedStation.CurrentSoundIsPaused = false;
            pausedStation = null;
        }

        private static void LatchPauseInput() {
            pauseInputLatchUntil = DateTime.UtcNow.AddMilliseconds(750);
        }
    }
}
