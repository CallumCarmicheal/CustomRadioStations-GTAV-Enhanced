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
        private bool allSoundsPlayedOnce;
        private int lastPlayedSoundIndex;
        private int lastCommercialIndex = -1;
        private uint stoppedPositionStation;
        private uint stoppedPositionSound;
        private DateTime lastPlayedTime;
        private DateTime trackUpdateTimer = DateTime.Now;

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
                if (sound != null) tracks.Add(new StationMediaItem(sound, false));
                Config.LoadTick();
            }

            if (playback.Shuffle) Shuffle(tracks);

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
                    if (commercial != null) programme.Add(new StationMediaItem(commercial, true));
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
            do { next = random.Next(commercialSources.Count); }
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

        internal void Update() {
            if (currentSound == null || currentSound.Sound == null) return;
            if (currentSound.HasTrackList && trackUpdateTimer < DateTime.Now) {
                UpdateWheelInfo();
                UpdateTrackUpdateTimer();
            }
            if (currentSound.IsFinishedPlaying()) PlayNextSound();
        }

        internal void Play() {
            if (!HasPlayableSounds) {
                Logger.Log("WARNING: Station '" + Name + "' has no playable tracks; ignoring play request.");
                return;
            }

            if (!hasPlayedOnce) {
                currentSound = programme[0].SoundFile;
                if (!StartCurrentSound()) return;
                currentSound.Sound.PlayPosition = IsBroadcastMode ? currentSound.GetRandomPlayPosition() : 0u;
                currentSound.Sound.Paused = false;
                UpdateProgrammeLength();
                hasPlayedOnce = true;
                UpdateWheelInfo();
            } else if (IsBroadcastMode) {
                if (!allSoundsPlayedOnce && lastPlayedSoundIndex == programme.Count - 1)
                    allSoundsPlayedOnce = true;
                ResumeBroadcastContinuity();
            } else {
                ResumePlaylist();
            }

            Function.Call(Hash.SET_AUDIO_FLAG, "DisableFlightMusic", true);
            Function.Call(Hash.SET_AUDIO_FLAG, "DisableWantedMusic", true);
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
            currentSound = programme[index].SoundFile;
            if (!StartCurrentSound(true)) return;
            uint maximum = currentSound.Length > 0 ? currentSound.Length - 1 : 0;
            currentSound.Sound.PlayPosition = Math.Min(maximum, stoppedPositionSound);
            CurrentSoundIsPaused = false;
            UpdateProgrammeLength();
            UpdateWheelInfo();
        }

        private void ResumeBroadcastContinuity() {
            uint elapsed = lastPlayedTime == default(DateTime)
                ? 0u
                : (uint)Math.Min(uint.MaxValue, Math.Max(0d, (DateTime.Now - lastPlayedTime).TotalMilliseconds));
            int safeLastIndex = Math.Max(0, Math.Min(lastPlayedSoundIndex, programme.Count - 1));
            SoundFile lastSound = programme[safeLastIndex].SoundFile;

            if (allSoundsPlayedOnce && TotalLength > 0) {
                uint newPosition = GetTimeFromPrevious(stoppedPositionStation, TotalLength, elapsed);
                StationMediaItem item = programme.LastOrDefault(candidate => newPosition >= candidate.StartTime) ?? programme[0];
                currentSound = item.SoundFile;
                if (!StartCurrentSound(true)) return;
                UpdateProgrammeLength();
                currentSound.Sound.PlayPosition = Math.Min(currentSound.Length > 0 ? currentSound.Length - 1 : 0,
                    newPosition >= item.StartTime ? newPosition - item.StartTime : 0);
            } else {
                uint remaining = lastSound.Length > stoppedPositionSound ? lastSound.Length - stoppedPositionSound : 0;
                if (elapsed < remaining) {
                    currentSound = lastSound;
                    if (!StartCurrentSound(true)) return;
                    currentSound.Sound.PlayPosition = Math.Min(currentSound.Length > 0 ? currentSound.Length - 1 : 0,
                        stoppedPositionSound + elapsed);
                    UpdateProgrammeLength();
                } else {
                    int nextIndex = safeLastIndex < programme.Count - 1 ? safeLastIndex + 1 : 0;
                    currentSound = programme[nextIndex].SoundFile;
                    if (!StartCurrentSound(true)) return;
                    UpdateProgrammeLength();
                }
            }

            CurrentSoundIsPaused = false;
            UpdateWheelInfo();
            if (currentSound != null && currentSound.HasTrackList) UpdateTrackUpdateTimer();
        }

        internal void Stop() {
            if (currentSound == null || currentSound.Sound == null) return;
            StationMediaItem item = programme.Find(candidate => candidate.SoundFile == currentSound);
            if (item == null) return;

            stoppedPositionStation = item.StartTime + currentSound.PlayPosition();
            stoppedPositionSound = currentSound.PlayPosition();
            lastPlayedSoundIndex = programme.IndexOf(item);
            lastPlayedTime = DateTime.Now;
            ResetWheelInfo();
            CurrentSoundIsPaused = true;
            currentSound = null;
            ResetAudioFlags();
        }

        private void PlayNextSound() {
            if (!HasPlayableSounds) return;
            int currentIndex = currentSound == null ? -1 : programme.FindIndex(item => item.SoundFile == currentSound);
            if (currentSound != null) currentSound.StopSound();

            if (currentIndex >= programme.Count - 1 && !playback.Loop) {
                FinishPlayback();
                return;
            }

            int nextIndex = currentIndex >= 0 && currentIndex < programme.Count - 1 ? currentIndex + 1 : 0;
            currentSound = programme[nextIndex].SoundFile;
            if (!StartCurrentSound(true)) return;
            UpdateProgrammeLength();
            UpdateWheelInfo();
            UpdateTrackUpdateTimer();
        }

        internal void PlayNextSong() {
            if (currentSound == null) return;
            if (currentSound.HasTrackList && currentSound.GetCurrentTrackIndex() < currentSound.Tracklist.Count - 1) {
                currentSound.SkipToNextTrack();
                UpdateWheelInfo();
                UpdateTrackUpdateTimer();
            } else {
                PlayNextSound();
            }
        }

        private void FinishPlayback() {
            currentSound = null;
            ResetWheelInfo();
            ResetAudioFlags();
            if (ReferenceEquals(CurrentPlaying, this)) CurrentPlaying = null;
            RadioNativeFunctions.VanillaRadioFadedOut(false);
        }

        private void UpdateProgrammeLength() {
            if (currentSound == null) return;
            StationMediaItem item = programme.Find(candidate => candidate.SoundFile == currentSound);
            if (item == null || currentSound.LengthAdded) return;
            item.StartTime = TotalLength;
            TotalLength += currentSound.Length;
            currentSound.LengthAdded = true;
        }

        internal void RescanSoundsTracklists() {
            foreach (StationMediaItem item in programme)
                item.SoundFile.HasTrackList = item.SoundFile.TracklistExists(item.SoundFile.FilePath);
            UpdateWheelInfo();
            UpdateTrackUpdateTimer();
        }

        private void UpdateWheelInfo() {
            if (currentSound == null || currentSound.Sound == null || wheelCategory == null ||
                wheelCategory.ItemList == null || wheelCategory.ItemList.Count == 0) return;
            wheelCategory.ItemList[0].Name = Name + "\n" + currentSound.DisplayName;
        }

        internal void UpdateDashboardInfo() {
            if (currentSound == null || currentSound.Sound == null) return;
            Ped player = Game.Player.Character;
            if (player == null || !player.Exists() || !player.IsInVehicle()) return;
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

        private void ResetWheelInfo() {
            if (wheelCategory != null && wheelCategory.ItemList != null && wheelCategory.ItemList.Count > 0)
                wheelCategory.ItemList[0].Name = Name;
        }

        private static void ResetAudioFlags() {
            Function.Call(Hash.SET_AUDIO_FLAG, "DisableFlightMusic", false);
            Function.Call(Hash.SET_AUDIO_FLAG, "DisableWantedMusic", false);
        }

        private static uint GetTimeFromPrevious(uint previous, uint duration, uint elapsed) {
            if (duration == 0) return 0;
            return (uint)(((ulong)(previous % duration) + elapsed) % duration);
        }

        internal bool IsPlaying => currentSound != null && currentSound.IsPlaying();

        internal bool CurrentSoundIsPaused {
            get { return currentSound != null && currentSound.IsPaused; }
            set { if (currentSound != null) currentSound.IsPaused = value; }
        }

        public void Dispose() {
            foreach (StationMediaItem item in programme)
                item.SoundFile.Dispose();
            programme.Clear();
        }

        internal static RadioStation CurrentPlaying;
        internal static RadioStation NextQueuedStation;

        internal static void ManageStations() {
            if (CurrentPlaying != null) CurrentPlaying.Update();
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
}
