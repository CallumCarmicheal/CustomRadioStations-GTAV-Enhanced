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
        private readonly List<string> trackSources;
        private readonly List<string> commercialSources;
        private readonly List<StationMediaItem> programme = new List<StationMediaItem>();
        private readonly PlaybackConfig playback;
        private readonly CommercialBreakConfig commercialBreaks;

        private SoundFile currentSound;
        private bool hasPlayedOnce;
        private int lastPlayedSoundIndex;
        private int lastCommercialIndex = -1;
        private uint stoppedPositionSound;
        private DateTime lastPlayedTime;
        private DateTime trackUpdateTimer = DateTime.Now;
        private string cachedTrackDisplayName;
        private readonly int initialProgrammeIndex;
        private readonly double initialPositionFraction;
        private readonly DateTime broadcastEpoch;

        internal RadioStation(WheelCategory correspondingWheelCategory, StationDefinition definition) {
            wheelCategory = correspondingWheelCategory;
            Id = definition.Id;
            Name = definition.Name;
            Description = definition.Description ?? string.Empty;
            playback = definition.Playback ?? new PlaybackConfig();
            commercialBreaks = definition.CommercialBreaks ?? new CommercialBreakConfig();
            trackSources = new List<string>(definition.Tracks ?? new string[0]);
            commercialSources = new List<string>(definition.Commercials ?? new string[0]);

            BuildProgramme();
            initialProgrammeIndex = SelectInitialProgrammeIndex();
            initialPositionFraction = 0.05d + (random.NextDouble() * 0.80d);
            broadcastEpoch = DateTime.UtcNow;
            StationMediaItem initialTrack = programme.Count > 0
                ? programme[initialProgrammeIndex]
                : null;
            if (initialTrack != null)
                cachedTrackDisplayName = initialTrack.SoundFile.PreviewDisplayName;
            RenderCachedWheelInfo();
        }

        internal string Id { get; }
        internal string Name { get; }
        internal string Description { get; }
        internal uint TotalLength { get; private set; }
        internal bool HasPlayableSounds => programme.Any(item => !item.IsCommercial);

        private bool IsBroadcastMode => string.Equals(playback.Mode, "broadcast", StringComparison.OrdinalIgnoreCase);

        private void BuildProgramme() {
            var tracks = new List<StationMediaItem>();
            foreach (string source in trackSources) {
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
                    string source = GetNextCommercialSource();
                    SoundFile commercial = TryCreateSound(source, "commercial");
                    if (commercial != null)
                        programme.Add(new StationMediaItem(commercial, true));
                }

                tracksSinceBreak = 0;
                tracksUntilBreak = NextInclusive(commercialBreaks.MinTracksBetween, commercialBreaks.MaxTracksBetween);
            }
        }

        private string GetNextCommercialSource() {
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

        private SoundFile TryCreateSound(string path, string kind) {
            try {
                if (string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase)) {
                    string target = GeneralHelper.GetShortcutTargetFile(path);
                    if (string.IsNullOrEmpty(target))
                        throw new FileNotFoundException("Shortcut target does not exist.", path);
                    return new SoundFile(target, path);
                }
                return new SoundFile(path);
            } catch (Exception ex) {
                Logger.Log("WARNING: Could not load " + kind + " '" + path + "' for station '" + Name + "': " + ex.Message);
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

        private uint GetInitialBroadcastPosition() {
            if (currentSound == null || currentSound.Length <= 1)
                return 0u;

            ulong length = currentSound.Length;
            ulong initialPosition = (ulong)(initialPositionFraction * (length - 1));
            ulong elapsed = (ulong)Math.Max(0d, (DateTime.UtcNow - broadcastEpoch).TotalMilliseconds);
            return (uint)((initialPosition + elapsed) % length);
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
                lastPlayedSoundIndex = IsBroadcastMode ? initialProgrammeIndex : 0;
                int startedIndex;
                if (!TryStartProgrammeItem(lastPlayedSoundIndex, false, IsBroadcastMode && playback.Loop, out startedIndex))
                    return;
                lastPlayedSoundIndex = startedIndex;
                currentSound.Sound.PlayPosition = IsBroadcastMode ? GetInitialBroadcastPosition() : 0u;
                currentSound.Sound.Paused = false;
                UpdateProgrammeLength();
                hasPlayedOnce = true;
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
            currentSound.Sound.Volume = playback.Volume;
            return true;
        }

        private void ResumePlaylist() {
            int index = Math.Max(0, Math.Min(lastPlayedSoundIndex, programme.Count - 1));
            int startedIndex;
            if (!TryStartProgrammeItem(index, true, false, out startedIndex))
                return;
            lastPlayedSoundIndex = startedIndex;
            uint maximum = currentSound.Length > 0 ? currentSound.Length - 1 : 0;
            currentSound.Sound.PlayPosition = startedIndex == index ? Math.Min(maximum, stoppedPositionSound) : 0u;
            CurrentSoundIsPaused = false;
            UpdateProgrammeLength();
            UpdateWheelInfo();
        }

        private void ResumeBroadcastContinuity() {
            ulong elapsed = lastPlayedTime == default(DateTime)
                ? 0UL
                : (ulong)Math.Max(0d, (DateTime.Now - lastPlayedTime).TotalMilliseconds);
            int safeLastIndex = Math.Max(0, Math.Min(lastPlayedSoundIndex, programme.Count - 1));

            uint[] knownLengths = programme.Select(item => item.SoundFile.Length).ToArray();
            if (knownLengths.All(length => length > 0)) {
                BroadcastPosition target = BroadcastTimeline.Advance(knownLengths, safeLastIndex, stoppedPositionSound, elapsed, playback.Loop);
                if (target.Finished) {
                    FinishPlayback();
                    return;
                }

                int startedIndex;
                if (!TryStartProgrammeItem(target.Index, true, playback.Loop, out startedIndex)) {
                    FinishPlayback();
                    return;
                }
                lastPlayedSoundIndex = startedIndex;
                currentSound.Sound.PlayPosition = startedIndex == target.Index
                    ? Math.Min(currentSound.Length > 0 ? currentSound.Length - 1 : 0, target.Position)
                    : 0u;
                UpdateProgrammeLength();
            } else if (!ResumeBroadcastWithUnknownLengths(safeLastIndex, stoppedPositionSound, elapsed)) {
                FinishPlayback();
                return;
            }

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
                UpdateProgrammeLength();
                uint length = currentSound.Length;
                ulong remaining = length > position ? (ulong)(length - position) : 0UL;
                if (length > 0 && elapsed < remaining) {
                    lastPlayedSoundIndex = index;
                    currentSound.Sound.PlayPosition = position + (uint)elapsed;
                    return true;
                }

                if (remaining > 0)
                    elapsed -= remaining;
                currentSound.StopSound();

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

        internal void Stop() {
            if (currentSound == null || currentSound.Sound == null)
                return;
            StationMediaItem item = programme.Find(candidate => candidate.SoundFile == currentSound);
            if (item == null)
                return;

            stoppedPositionSound = currentSound.PlayPosition();
            lastPlayedSoundIndex = programme.IndexOf(item);
            lastPlayedTime = DateTime.Now;
            CacheCurrentTrackDisplayName();
            CurrentSoundIsPaused = true;
            AudioPauseCoordinator.NotifyStationStopped(this);
            currentSound = null;
            ResetAudioFlags();
        }

        private void PlayNextSound() {
            if (!HasPlayableSounds)
                return;
            int currentIndex = currentSound == null ? -1 : programme.FindIndex(item => item.SoundFile == currentSound);
            if (currentSound != null)
                currentSound.StopSound();

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
            UpdateProgrammeLength();
            CurrentSoundIsPaused = false;
            UpdateWheelInfo();
            UpdateTrackUpdateTimer();
        }

        internal void PlayNextSong() {
            if (currentSound == null)
                return;
            if (currentSound.HasTrackList && currentSound.GetCurrentTrackIndex() < currentSound.Tracklist.Count - 1) {
                currentSound.SkipToNextTrack();
                UpdateWheelInfo();
                UpdateTrackUpdateTimer();
            } else {
                PlayNextSound();
            }
        }

        private void FinishPlayback() {
            AudioPauseCoordinator.NotifyStationStopped(this);
            currentSound = null;
            RenderCachedWheelInfo();
            ResetAudioFlags();
            if (ReferenceEquals(CurrentPlaying, this))
                CurrentPlaying = null;
            RadioNativeFunctions.VanillaRadioFadedOut(false);
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
            if (currentSound == null && programme.Count > 0) {
                int previewIndex = Math.Max(0, Math.Min(lastPlayedSoundIndex, programme.Count - 1));
                cachedTrackDisplayName = programme[previewIndex].SoundFile.PreviewDisplayName;
            }
            UpdateWheelInfo();
            UpdateTrackUpdateTimer();
        }

        private void UpdateWheelInfo() {
            CacheCurrentTrackDisplayName();
            RenderCachedWheelInfo();
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

        private void CacheCurrentTrackDisplayName() {
            if (currentSound == null)
                return;
            string displayName = currentSound.DisplayName;
            if (!string.IsNullOrWhiteSpace(displayName))
                cachedTrackDisplayName = displayName;
        }

        private void RenderCachedWheelInfo() {
            if (wheelCategory == null || wheelCategory.ItemList == null || wheelCategory.ItemList.Count == 0)
                return;

            wheelCategory.ItemList[0].Name = string.IsNullOrWhiteSpace(cachedTrackDisplayName)
                ? Name
                : Name + "\n" + cachedTrackDisplayName;
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
