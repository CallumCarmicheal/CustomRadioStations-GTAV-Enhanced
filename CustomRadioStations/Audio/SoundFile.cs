using MiniAudioEx.Core.StandardAPI;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace CustomRadioStations {
    internal class SoundFile {
        public volatile MiniAudioSound Sound;
        private AudioClip Clip;
        private MiniAudioEngine owningEngine;
        private readonly ResolvedMediaSource mediaSource;
        private uint physicalLength;
        private uint playbackStartMs;
        private uint playbackEndMs;
        private readonly object logicalEndTimerSync = new object();
        private Timer logicalEndTimer;
        private int logicalEndGeneration;
        private int playbackCompletionSignaled;

        internal event Action<SoundFile> PlaybackEnded;

        public string FileName;
        public string FilePath;

        private string _displayName;
        private string ratingArtist;
        private string ratingTitle;
        private string ratingFilePath;
        private string ratingAnalysisKey;
        private bool ratingTargetsBuilt;
        private TrackRatingTarget baseRatingTarget;
        private TrackRatingTarget[] subTrackRatingTargets = new TrackRatingTarget[0];
        public string DisplayName {
            get {
                if (HasTrackList) {
                    Track t = GetCurrentTrack();
                    if (t == null)
                        return PreviewDisplayName;
                    return TrackMetadataReader.FormatDisplayName(t.Artist, t.Title, _displayName);
                }
                return _displayName;
            }
        }

        public string PreviewDisplayName { get { return GetDisplayNameAtLogicalPosition(0u); } }

        /// <summary>Logical playable length in milliseconds after configured/detected trimming.</summary>
        public uint Length { get; private set; }
        public uint PhysicalLength { get { return physicalLength; } }
        public uint PlaybackStartMs { get { return playbackStartMs; } }
        public uint PlaybackEndMs { get { return playbackEndMs; } }
        internal uint? ConfiguredStartMs { get { return mediaSource.StartMs; } }
        internal uint? ConfiguredEndMs { get { return mediaSource.EndMs; } }
        internal TrackAnalysis Analysis { get { return mediaSource.Analysis; } }
        internal bool AllowAnalysisTrimWithinBounds { get { return mediaSource.AllowAnalysisTrimWithinBounds; } }
        public float NormalizationGain { get; private set; } = 1f;
        public bool LengthAdded { get; set; }

        public bool HasTrackList;
        public List<Track> Tracklist { get; private set; }

        public SoundFile(string filepath) : this(new ResolvedMediaSource(filepath)) { }

        public SoundFile(ResolvedMediaSource source) {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            mediaSource = source;
            Initialize(source.FilePath, source.FilePath);
        }

        public SoundFile(string filepath, string shortcutPath) : this(filepath, shortcutPath, new ResolvedMediaSource(shortcutPath)) { }

        public SoundFile(string filepath, string shortcutPath, ResolvedMediaSource source) {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            mediaSource = source;
            Initialize(filepath, shortcutPath);
        }

        private void Initialize(string audioPath, string publicPath) {
            FilePath = publicPath;
            try {
                owningEngine = SoundEngine;
                Clip = owningEngine.CreateClip(audioPath, true);
                FileName = Path.GetFileNameWithoutExtension(audioPath);

                TrackMetadataInfo metadata = TrackMetadataReader.ReadMetadata(audioPath,
                    message => Logger.Log("WARNING: " + message + ". Using the filename for display metadata."));
                string artist = !string.IsNullOrWhiteSpace(mediaSource.Artist) ? mediaSource.Artist : metadata.Artist;
                string title = !string.IsNullOrWhiteSpace(mediaSource.Title) ? mediaSource.Title : metadata.Title;
                ratingArtist = artist ?? string.Empty;
                ratingTitle = title ?? string.Empty;
                ratingFilePath = Path.GetFullPath(audioPath);
                ratingAnalysisKey = mediaSource.StableAnalysisKey ?? string.Empty;
                _displayName = TrackMetadataReader.FormatDisplayName(artist, title, DisplayNameFromFilename());
                HasTrackList = TracklistExists(publicPath);
                InvalidateRatingTargets();

                physicalLength = mediaSource.Analysis != null && mediaSource.Analysis.DurationMs > 0u
                    ? mediaSource.Analysis.DurationMs
                    : metadata.DurationMs;
                if (mediaSource.Analysis != null)
                    NormalizationGain = LoudnessNormalization.DbToLinear(mediaSource.Analysis.GainDb);
                RecalculatePlaybackBounds();
            } catch {
                // Constructors that fail after CreateClip() do not return an object to the
                // caller, so release the partially owned MiniAudio clip here.
                AudioClip clip = Clip;
                MiniAudioEngine engine = owningEngine;
                Clip = null;
                owningEngine = null;
                if (clip != null && engine != null) {
                    try { engine.DisposeClip(clip); } catch { }
                }
                throw;
            }
        }

        private string DisplayNameFromFilename() {
            string[] sections = FileName.Split(new string[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
            string str = "";
            foreach (string section in sections)
                str += section.Trim() + "\n";
            return str;
        }

        internal bool TracklistExists(string filepath) {
            if (mediaSource.SubTracks != null && mediaSource.SubTracks.Count > 0) {
                Tracklist = mediaSource.SubTracks
                    .Where(track => track != null)
                    .OrderBy(track => track.StartTime)
                    .Select(track => new Track(track.StartTime, track.Artist, track.Title))
                    .ToList();
                return Tracklist.Count > 0;
            }

            string tracklistPath = Path.ChangeExtension(filepath, ".tracklist.json");
            if (!File.Exists(tracklistPath)) {
                Tracklist = null;
                return false;
            }

            try {
                var serializerSettings = new JsonSerializerSettings {
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                    ObjectCreationHandling = ObjectCreationHandling.Replace
                };
                TracklistConfig config = JsonConvert.DeserializeObject<TracklistConfig>(File.ReadAllText(tracklistPath), serializerSettings);
                Tracklist = (config?.Tracks ?? new List<Track>())
                    .Where(track => track != null)
                    .OrderBy(track => track.StartTime)
                    .ToList();
                return Tracklist.Count > 0;
            } catch (Exception ex) {
                Logger.Log("WARNING: Failed to load tracklist JSON '" + tracklistPath + "': " + ex.Message);
                Tracklist = null;
                return false;
            }
        }

        internal void RefreshTracklist() {
            HasTrackList = TracklistExists(FilePath);
            InvalidateRatingTargets();
        }

        public Track GetCurrentTrack() {
            return GetTrackAtRawPosition(RawPlayPosition());
        }

        internal string GetDisplayNameAtLogicalPosition(uint logicalPositionMs) {
            if (!HasTrackList || Tracklist == null || Tracklist.Count == 0)
                return _displayName;

            Track track = GetTrackAtRawPosition(LogicalToRawPosition(logicalPositionMs));
            return track == null
                ? _displayName
                : TrackMetadataReader.FormatDisplayName(track.Artist, track.Title, _displayName);
        }

        private Track GetTrackAtRawPosition(uint rawPositionMs) {
            if (!HasTrackList || Tracklist == null || Tracklist.Count == 0)
                return null;
            Track track = Tracklist.LastOrDefault(candidate => rawPositionMs >= candidate.StartTime);
            return track == default(Track) ? null : track;
        }

        private uint LogicalToRawPosition(uint logicalPositionMs) {
            uint logical = Length > 0u ? Math.Min(logicalPositionMs, Length - 1u) : 0u;
            ulong raw = (ulong)playbackStartMs + logical;
            uint upper = playbackEndMs > playbackStartMs ? playbackEndMs - 1u : playbackStartMs;
            return (uint)Math.Min((ulong)upper, raw);
        }

        public int GetCurrentTrackIndex() {
            if (!HasTrackList || Tracklist == null || Tracklist.Count == 0)
                return -1;
            return Tracklist.IndexOf(GetCurrentTrack());
        }

        internal int GetTrackIndexAtLogicalPosition(uint logicalPositionMs) {
            if (!HasTrackList || Tracklist == null || Tracklist.Count == 0)
                return -1;
            Track track = GetTrackAtRawPosition(LogicalToRawPosition(logicalPositionMs));
            return track == null ? -1 : Tracklist.IndexOf(track);
        }

        internal bool TryGetRatingTargetAtLogicalPosition(uint logicalPositionMs, out TrackRatingTarget target) {
            if (!EnsureRatingTargets()) {
                target = default(TrackRatingTarget);
                return false;
            }

            if (!HasTrackList || Tracklist == null || Tracklist.Count == 0) {
                target = baseRatingTarget;
                return !string.IsNullOrEmpty(target.Key);
            }

            int index = GetTrackIndexAtLogicalPosition(logicalPositionMs);
            if (index < 0 || index >= subTrackRatingTargets.Length) {
                target = default(TrackRatingTarget);
                return false;
            }

            target = subTrackRatingTargets[index];
            return !string.IsNullOrEmpty(target.Key);
        }

        private bool EnsureRatingTargets() {
            if (ratingTargetsBuilt)
                return !string.IsNullOrWhiteSpace(ratingAnalysisKey);

            if (string.IsNullOrWhiteSpace(ratingAnalysisKey)) {
                try {
                    ratingAnalysisKey = TrackRatingStore.CreateAnalysisKey(ratingFilePath, mediaSource.StartMs, mediaSource.EndMs);
                } catch (Exception ex) {
                    ratingTargetsBuilt = true;
                    Logger.Log("WARNING: Could not create stable rating identity for '" + ratingFilePath + "': " + ex.Message);
                    return false;
                }
            }

            baseRatingTarget = new TrackRatingTarget(ratingAnalysisKey, ratingFilePath, null, ratingArtist, ratingTitle);
            subTrackRatingTargets = !HasTrackList || Tracklist == null || Tracklist.Count == 0
                ? new TrackRatingTarget[0]
                : Tracklist.Select(track => new TrackRatingTarget(ratingAnalysisKey, ratingFilePath,
                    track.StartTime, track.Artist, track.Title)).ToArray();
            ratingTargetsBuilt = true;
            return true;
        }

        private void InvalidateRatingTargets() {
            ratingTargetsBuilt = false;
            baseRatingTarget = default(TrackRatingTarget);
            subTrackRatingTargets = new TrackRatingTarget[0];
        }

        internal void GetLogicalTrackBounds(uint logicalPositionMs, out uint startMs, out uint endMs) {
            startMs = 0u;
            endMs = Length;
            if (!HasTrackList || Tracklist == null || Tracklist.Count == 0 || Length == 0u)
                return;

            uint rawPosition = LogicalToRawPosition(logicalPositionMs);
            Track current = GetTrackAtRawPosition(rawPosition);
            if (current == null)
                return;

            int index = Tracklist.IndexOf(current);
            uint rawStart = Math.Max(playbackStartMs, current.StartTime);
            uint rawEnd = playbackEndMs;
            if (index >= 0 && index < Tracklist.Count - 1)
                rawEnd = Math.Min(rawEnd, Tracklist[index + 1].StartTime);

            startMs = rawStart > playbackStartMs ? rawStart - playbackStartMs : 0u;
            endMs = rawEnd > playbackStartMs ? rawEnd - playbackStartMs : 0u;
            startMs = Math.Min(startMs, Length);
            endMs = Math.Min(Math.Max(endMs, startMs), Length);
        }

        internal bool SeekToTrackIndex(int index) {
            MiniAudioSound sound = Sound;
            if (!HasTrackList || Tracklist == null || sound == null || index < 0 || index >= Tracklist.Count)
                return false;
            uint raw = Math.Max(playbackStartMs, Tracklist[index].StartTime);
            uint upper = playbackEndMs > playbackStartMs ? playbackEndMs - 1u : playbackStartMs;
            sound.PlayPosition = Math.Min(raw, upper);
            Interlocked.Exchange(ref playbackCompletionSignaled, 0);
            ScheduleLogicalEnd();
            return true;
        }

        internal int GetNextPlayableTrackIndex(int currentIndex) {
            if (!HasTrackList || Tracklist == null)
                return -1;
            for (int index = Math.Max(-1, currentIndex) + 1; index < Tracklist.Count; index++) {
                if (IsTrackPlayable(index))
                    return index;
            }
            return -1;
        }

        internal int GetPreviousPlayableTrackIndex(int currentIndex) {
            if (!HasTrackList || Tracklist == null)
                return -1;
            for (int index = Math.Min(currentIndex - 1, Tracklist.Count - 1); index >= 0; index--) {
                if (IsTrackPlayable(index))
                    return index;
            }
            return -1;
        }

        internal int GetLastPlayableTrackIndex() {
            if (!HasTrackList || Tracklist == null)
                return -1;
            for (int index = Tracklist.Count - 1; index >= 0; index--) {
                if (IsTrackPlayable(index))
                    return index;
            }
            return -1;
        }

        private bool IsTrackPlayable(int index) {
            if (Tracklist == null || index < 0 || index >= Tracklist.Count || playbackEndMs <= playbackStartMs)
                return false;
            uint trackStart = Tracklist[index].StartTime;
            uint trackEnd = index < Tracklist.Count - 1 ? Tracklist[index + 1].StartTime : playbackEndMs;
            return trackStart < playbackEndMs && trackEnd > playbackStartMs;
        }

        internal bool RestartCurrentTrack() {
            if (Sound == null)
                return false;
            int index = GetCurrentTrackIndex();
            if (index >= 0)
                return SeekToTrackIndex(index);
            Seek(0u);
            return true;
        }

        public Track GetNextTrack() {
            if (!HasTrackList || Tracklist == null || Tracklist.Count == 0)
                return null;
            Track track = GetCurrentTrack();
            if (track == null)
                return Tracklist[0];
            int index = Tracklist.IndexOf(track);
            return index < 0 || index >= Tracklist.Count - 1 ? Tracklist[0] : Tracklist[index + 1];
        }

        public void SkipToNextTrack() {
            MiniAudioSound sound = Sound;
            if (!HasTrackList || sound == null)
                return;
            Track next = GetNextTrack();
            if (next == null)
                return;
            uint upper = playbackEndMs > 0 ? playbackEndMs - 1 : 0u;
            sound.PlayPosition = Math.Max(playbackStartMs, Math.Min(upper, next.StartTime));
            Interlocked.Exchange(ref playbackCompletionSignaled, 0);
            ScheduleLogicalEnd();
        }

        public uint TimeUntilNextTrack() {
            if (Sound == null)
                return 0;
            uint rawPosition = RawPlayPosition();
            uint remaining = playbackEndMs > rawPosition ? playbackEndMs - rawPosition : 0u;
            if (!HasTrackList)
                return remaining;
            Track next = GetNextTrack();
            if (next == null || next.StartTime <= rawPosition || next.StartTime >= playbackEndMs)
                return remaining;
            return next.StartTime - rawPosition + 1;
        }

        public void PlaySound(bool resume, bool playLooped = false, bool playPaused = false, bool allowMultipleInstances = false, bool allowSoundEffects = false) {
            MiniAudioEngine engine = owningEngine;
            if (Clip == null || engine == null)
                return;

            if (!allowMultipleInstances && Sound != null && !Sound.Finished && !IsPaused)
                return;

            if (resume && IsPaused) {
                // Station resume paths reposition retained sources before playback continues.
                // Respect playPaused so an old cursor cannot become briefly audible before
                // RadioStation applies the projected/stored seek and explicitly unpauses it.
                if (!playPaused)
                    IsPaused = false;
                return;
            }

            if (Sound != null) {
                Sound.Dispose();
                Sound = null;
            }

            try {
                Interlocked.Exchange(ref playbackCompletionSignaled, 0);
                Sound = engine.Play2D(Clip, playLooped, playPaused, OnPhysicalPlaybackEnded);
                if (Sound == null)
                    return;
                physicalLength = Sound.PlayLength;
                RecalculatePlaybackBounds();
                Seek(0u);
            } catch (Exception ex) {
                Logger.Log("ERROR: MiniAudioEx failed to start '" + FileName + "': " + ex.Message);
                if (Sound != null) {
                    Sound.Dispose();
                    Sound = null;
                }
            }
        }

        private void OnPhysicalPlaybackEnded(MiniAudioSound sound) {
            if (!ReferenceEquals(Sound, sound))
                return;
            SignalPlaybackEnded(sound);
        }

        private void ScheduleLogicalEnd() {
            lock (logicalEndTimerSync) {
                DisposeLogicalEndTimerLocked();

                MiniAudioSound sound = Sound;
                if (sound == null || sound.Paused || sound.Finished || playbackEndMs <= playbackStartMs)
                    return;

                // Physical EOF already produces AudioSource.End. A timer is only needed when
                // CRS intentionally ends playback before the file's real end (analysis/manual trim).
                if (physicalLength == 0u || playbackEndMs >= physicalLength)
                    return;

                uint rawPosition = sound.PlayPosition;
                uint remaining = playbackEndMs > rawPosition ? playbackEndMs - rawPosition : 0u;
                int dueTime = remaining > int.MaxValue ? int.MaxValue : (int)Math.Max(1u, remaining);
                int generation = ++logicalEndGeneration;
                logicalEndTimer = new Timer(OnLogicalEndTimer, new LogicalEndTimerState(sound, generation), dueTime, Timeout.Infinite);
            }
        }

        private void OnLogicalEndTimer(object state) {
            var timerState = state as LogicalEndTimerState;
            if (timerState == null)
                return;

            lock (logicalEndTimerSync) {
                if (timerState.Generation != logicalEndGeneration)
                    return;
                logicalEndTimer = null;
            }

            MiniAudioSound sound = timerState.Sound;
            if (sound == null || !ReferenceEquals(Sound, sound))
                return;

            try {
                if (sound.Paused || sound.Finished)
                    return;

                uint rawPosition = sound.PlayPosition;
                if (rawPosition >= playbackEndMs) {
                    // Keep automatic programme advancement on the same serialized audio
                    // callback path as MiniAudioEx physical EOF events. Carry the timer
                    // generation as well so a seek/pause that happens before the queued
                    // callback runs cannot complete an obsolete logical boundary.
                    int generation = timerState.Generation;
                    sound.EnqueueCallback(() => SignalLogicalPlaybackEnded(sound, generation));
                    return;
                }

                // Timer granularity/scheduling can wake slightly early. Re-arm for the
                // measured remainder rather than treating an early wake as completion.
                ScheduleLogicalEnd();
            } catch (ObjectDisposedException) {
                // The source was replaced while this one-shot callback was running.
            } catch (Exception ex) {
                Logger.Log("WARNING: Logical playback boundary check failed for '" + FileName + "': " + ex.Message);
            }
        }

        private void SignalLogicalPlaybackEnded(MiniAudioSound sound, int generation) {
            lock (logicalEndTimerSync) {
                if (generation != logicalEndGeneration)
                    return;
            }

            if (!ReferenceEquals(Sound, sound) || sound.Paused || sound.Finished)
                return;

            // A same-source seek can race the timer after it has queued this callback.
            // Re-check the actual cursor before treating the old boundary as completion.
            if (sound.PlayPosition < playbackEndMs) {
                ScheduleLogicalEnd();
                return;
            }

            SignalPlaybackEnded(sound);
        }

        private void SignalPlaybackEnded(MiniAudioSound sound) {
            if (!ReferenceEquals(Sound, sound) || Interlocked.Exchange(ref playbackCompletionSignaled, 1) != 0)
                return;

            CancelLogicalEndTimer();
            Action<SoundFile> handler = PlaybackEnded;
            if (handler != null)
                handler(this);
        }

        private void CancelLogicalEndTimer() {
            lock (logicalEndTimerSync) {
                logicalEndGeneration++;
                DisposeLogicalEndTimerLocked();
            }
        }

        private void DisposeLogicalEndTimerLocked() {
            Timer timer = logicalEndTimer;
            logicalEndTimer = null;
            if (timer != null) {
                try { timer.Dispose(); } catch { }
            }
        }

        private sealed class LogicalEndTimerState {
            internal LogicalEndTimerState(MiniAudioSound sound, int generation) {
                Sound = sound;
                Generation = generation;
            }

            internal MiniAudioSound Sound { get; }
            internal int Generation { get; }
        }

        private void RecalculatePlaybackBounds() {
            MediaPlaybackBounds bounds = MediaPlaybackBounds.Calculate(physicalLength, mediaSource.StartMs, mediaSource.EndMs,
                mediaSource.Analysis, mediaSource.AllowAnalysisTrimWithinBounds);
            playbackStartMs = bounds.StartMs;
            playbackEndMs = bounds.EndMs;
            Length = bounds.LengthMs;
        }

        public void Seek(uint logicalPositionMs) {
            MiniAudioSound sound = Sound;
            if (sound == null)
                return;
            uint position = Length > 0u ? Math.Min(logicalPositionMs, Length - 1u) : 0u;
            ulong raw = (ulong)playbackStartMs + position;
            uint upper = playbackEndMs > 0u ? playbackEndMs - 1u : 0u;
            sound.PlayPosition = (uint)Math.Min((ulong)upper, raw);
            Interlocked.Exchange(ref playbackCompletionSignaled, 0);
            ScheduleLogicalEnd();
        }

        public void StopSound() {
            CancelLogicalEndTimer();
            MiniAudioSound sound = Sound;
            if (sound == null)
                return;
            if (!sound.Finished)
                sound.Stop();
        }

        public void ReleaseSound() {
            CancelLogicalEndTimer();
            MiniAudioSound sound = Sound;
            if (sound == null)
                return;
            Sound = null;
            sound.Dispose();
        }

        public bool IsPaused {
            get {
                MiniAudioSound sound = Sound;
                return sound != null && sound.Paused;
            }
            set {
                MiniAudioSound sound = Sound;
                if (sound == null || sound.Finished)
                    return;
                sound.Paused = value;
                if (value)
                    CancelLogicalEndTimer();
                else
                    ScheduleLogicalEnd();
            }
        }

        public bool IsPlaying() {
            return Sound != null && !IsFinishedPlaying();
        }

        public bool IsFinishedPlaying() {
            MiniAudioSound sound = Sound;
            if (sound == null)
                return true;
            uint rawPosition = sound.PlayPosition;
            if (playbackEndMs > playbackStartMs && rawPosition >= playbackEndMs)
                return true;
            return sound.Finished;
        }

        /// <summary>Returns the current logical playback position in milliseconds.</summary>
        public uint PlayPosition() {
            uint raw = RawPlayPosition();
            if (raw <= playbackStartMs)
                return 0u;
            uint logical = raw - playbackStartMs;
            return Length > 0u ? Math.Min(logical, Length) : logical;
        }

        private uint RawPlayPosition() {
            MiniAudioSound sound = Sound;
            return sound == null ? 0u : sound.PlayPosition;
        }

        public void Dispose() {
            CancelLogicalEndTimer();
            MiniAudioSound sound = Sound;
            Sound = null;
            if (sound != null)
                sound.Dispose();
            AudioClip clip = Clip;
            MiniAudioEngine engine = owningEngine;
            Clip = null;
            owningEngine = null;
            if (clip != null && engine != null)
                engine.DisposeClip(clip);
            PlaybackEnded = null;
        }

        private static readonly object SoundEngineSync = new object();
        private static MiniAudioEngine soundEngine;

        public static MiniAudioEngine SoundEngine {
            get {
                lock (SoundEngineSync) {
                    if (soundEngine == null)
                        soundEngine = new MiniAudioEngine();
                    return soundEngine;
                }
            }
        }

        public static void StepVolume(float step, int decimals) {
            MiniAudioEngine engine = SoundEngine;
            float temp = (float)Math.Round(engine.SoundVolume + step, decimals, MidpointRounding.ToEven);
            engine.SoundVolume = temp.LimitToRange(0f, 1f);
        }

        public static void DisposeSoundEngine() {
            lock (SoundEngineSync) {
                MiniAudioEngine engine = soundEngine;
                if (engine == null)
                    return;

                // Keep the singleton reference stable while teardown is in progress so no
                // concurrent caller can construct a second AudioContext before the first
                // one has fully stopped and deinitialized.
                engine.Dispose();
                if (ReferenceEquals(soundEngine, engine))
                    soundEngine = null;
            }
        }
    }

    /// <summary>
    /// Small compatibility handle that keeps the old SoundFile/RadioStation timing API
    /// expressed in milliseconds while MiniAudioEx exposes its cursor in PCM frames.
    /// </summary>
    internal sealed class MiniAudioSound : IDisposable {
        private readonly MiniAudioEngine engine;
        private readonly AudioClip clip;
        private readonly AudioSource source;
        private readonly bool looped;
        private bool paused;
        private bool finished;
        private bool disposed;
        private int playbackRevision;

        internal event Action<MiniAudioSound> PlaybackEnded;

        internal MiniAudioSound(MiniAudioEngine engine, AudioClip clip, bool looped, bool startPaused,
            Action<MiniAudioSound> playbackEnded) {
            this.engine = engine;
            this.clip = clip;
            this.looped = looped;
            if (playbackEnded != null)
                PlaybackEnded += playbackEnded;

            source = new AudioSource(1);
            try {
                source.End += OnPlaybackEnded;
                source.Volume = 0f;
                source.Play(clip);
                source.Loop = looped;

                // A zero-length, non-playing source means the decoder/open operation failed.
                if (source.Length == 0 && !source.IsPlaying)
                    throw new InvalidDataException("MiniAudioEx could not decode or open: " + clip.FilePath);

                if (startPaused) {
                    source.Stop();
                    source.Cursor = 0;
                    paused = true;
                }

                source.Volume = 1f;
            } catch {
                try { source.End -= OnPlaybackEnded; } catch { }
                try { source.Dispose(); } catch { }
                throw;
            }
        }

        public uint PlayPosition {
            get {
                return engine.WithContextLock(() => disposed ? 0u : FramesToMilliseconds(source.Cursor));
            }
            set {
                bool restarted = false;
                engine.WithContextLock(() => {
                    if (disposed)
                        return;

                    ulong frame = MillisecondsToFrames(value);
                    ulong length = source.Length;
                    ulong cursor = length > 0 && frame >= length ? length - 1 : frame;

                    // A seek can race physical EOF before AudioContext.Update has had a
                    // chance to set our managed finished flag. If the native source is
                    // already non-playing and this is not an intentional pause, restart it
                    // before applying the requested cursor. The revision increment below
                    // also invalidates any End callback that was already queued.
                    if (!paused && (finished || !source.IsPlaying)) {
                        source.Play(clip);
                        source.Loop = looped;
                        restarted = true;
                    }

                    source.Cursor = cursor;
                    finished = false;
                    playbackRevision++;
                });
                if (restarted)
                    engine.WakeEventPump();
            }
        }

        public uint PlayLength {
            get {
                return engine.WithContextLock(() => disposed ? 0u : FramesToMilliseconds(source.Length));
            }
        }

        public bool Paused {
            get { return engine.WithContextLock(() => disposed || paused); }
            set {
                bool resumed = false;
                engine.WithContextLock(() => {
                    if (disposed || finished || paused == value)
                        return;

                    if (value) {
                        source.Stop();
                        paused = true;
                        playbackRevision++;
                        return;
                    }

                    // AudioSource.Stop() preserves the cursor. Re-issuing Play() is the
                    // MiniAudioEx continue path; restore the cursor explicitly as well so
                    // this remains correct even if the backend reopens the streamed file.
                    ulong cursor = source.Cursor;
                    source.Play(clip);
                    source.Loop = looped;
                    if (cursor > 0)
                        source.Cursor = cursor;
                    paused = false;
                    finished = false;
                    playbackRevision++;
                    resumed = true;
                });
                if (resumed)
                    engine.WakeEventPump();
            }
        }

        internal bool RequiresEventPump => !disposed && !paused && !finished;

        internal void EnqueueCallback(Action callback) {
            if (!disposed)
                engine.EnqueueCallback(callback);
        }

        public bool Finished {
            get { return engine.WithContextLock(() => disposed || finished); }
        }

        public float Volume {
            get { return engine.WithContextLock(() => disposed ? 0f : source.Volume); }
            set {
                engine.WithContextLock(() => {
                    if (!disposed)
                        source.Volume = Math.Max(0f, value);
                });
            }
        }

        public void Stop() {
            engine.WithContextLock(() => {
                if (disposed)
                    return;
                source.Stop();
                paused = false;
                finished = true;
                playbackRevision++;
            });
        }

        private void OnPlaybackEnded() {
            if (looped || disposed)
                return;

            paused = false;
            finished = true;
            int revisionAtEnd = playbackRevision;

            // AudioContext.Update dispatches this callback while the engine context lock is
            // held. Queue CRS work so track disposal/start happens after Update returns.
            // A seek/stop performed before the queue drains changes playbackRevision and
            // invalidates this exact EOF instead of letting a stale End skip the new cursor.
            engine.EnqueueCallback(() => {
                Action<MiniAudioSound> handler = null;
                engine.WithContextLock(() => {
                    if (!disposed && finished && playbackRevision == revisionAtEnd)
                        handler = PlaybackEnded;
                });
                if (handler != null)
                    handler(this);
            });
        }

        public void Dispose() {
            engine.WithContextLock(() => {
                if (disposed)
                    return;
                disposed = true;
                playbackRevision++;
                PlaybackEnded = null;
                source.End -= OnPlaybackEnded;
                source.Dispose();
                engine.Unregister(this);
            });
        }

        private static uint FramesToMilliseconds(ulong frames) {
            int sampleRate = AudioContext.SampleRate;
            if (sampleRate <= 0)
                return 0;
            ulong milliseconds = (frames * 1000UL) / (ulong)sampleRate;
            return milliseconds > uint.MaxValue ? uint.MaxValue : (uint)milliseconds;
        }

        private static ulong MillisecondsToFrames(uint milliseconds) {
            int sampleRate = AudioContext.SampleRate;
            if (sampleRate <= 0)
                return 0;
            return ((ulong)milliseconds * (ulong)sampleRate) / 1000UL;
        }
    }

    /// <summary>
    /// Process-local MiniAudioEx owner. AudioContext.Update is pumped independently of
    /// GTA's script tick so MiniAudioEx End events continue to dispatch while GTA is
    /// paused or unfocused. All Standard API access is serialized through contextSyncRoot.
    /// </summary>
    internal sealed class MiniAudioEngine : IDisposable {
        private const uint SampleRate = 48000;
        private const uint Channels = 2;
        private const int UpdateIntervalMs = 10;

        private readonly List<MiniAudioSound> sounds = new List<MiniAudioSound>();
        private readonly Queue<Action> callbackQueue = new Queue<Action>();
        private readonly object contextSyncRoot = new object();
        private readonly object callbackSyncRoot = new object();
        private readonly AutoResetEvent updateWake = new AutoResetEvent(false);
        private readonly Thread updateThread;
        private volatile bool stopping;
        private bool disposed;
        private DateTime lastUpdateFailureLogUtc = DateTime.MinValue;
        private int suppressedUpdateFailures;

        internal MiniAudioEngine() {
            MiniAudioNativeLoader.LoadFromScriptDirectory();
            MiniAudioFrameworkCompatibility.Initialize(SampleRate, Channels);

            try {
                updateThread = new Thread(UpdateLoop) {
                    IsBackground = true,
                    Name = "CRS MiniAudio Event Pump"
                };
                updateThread.Start();
            } catch {
                // SoundEngine is assigned only after this constructor succeeds. If the
                // worker cannot start, roll the process-global AudioContext back now or a
                // later retry would fail with "MiniAudioEx is already initialized".
                try { AudioContext.Deinitialize(); } catch { }
                try { updateWake.Dispose(); } catch { }
                disposed = true;
                stopping = true;
                throw;
            }
        }

        public float SoundVolume {
            get { return WithContextLock(() => disposed ? 0f : AudioContext.MasterVolume); }
            set {
                WithContextLock(() => {
                    if (!disposed)
                        AudioContext.MasterVolume = value.LimitToRange(0f, 1f);
                });
            }
        }

        internal AudioClip CreateClip(string filePath, bool streamFromDisk) {
            return WithContextLock(() => {
                if (disposed || stopping)
                    throw new ObjectDisposedException(nameof(MiniAudioEngine));
                return new AudioClip(filePath, streamFromDisk);
            });
        }

        internal void DisposeClip(AudioClip clip) {
            if (clip == null)
                return;
            WithContextLock(() => {
                // AudioContext.Deinitialize() releases every registered clip handle. A
                // late owner cleanup after engine teardown must not ask AudioClip to touch
                // that already-deinitialized global context again.
                if (!disposed)
                    clip.Dispose();
            });
        }

        internal MiniAudioSound Play2D(AudioClip clip, bool looped, bool startPaused,
            Action<MiniAudioSound> playbackEnded) {
            MiniAudioSound sound = WithContextLock(() => {
                if (disposed || stopping)
                    return null;
                var created = new MiniAudioSound(this, clip, looped, startPaused, playbackEnded);
                sounds.Add(created);
                return created;
            });
            if (sound != null)
                updateWake.Set();
            return sound;
        }

        internal T WithContextLock<T>(Func<T> action) {
            lock (contextSyncRoot)
                return action();
        }

        internal void WithContextLock(Action action) {
            lock (contextSyncRoot)
                action();
        }

        internal void EnqueueCallback(Action callback) {
            if (callback == null || stopping)
                return;
            lock (callbackSyncRoot)
                callbackQueue.Enqueue(callback);
            updateWake.Set();
        }

        internal void WakeEventPump() {
            if (!stopping)
                updateWake.Set();
        }

        internal void Unregister(MiniAudioSound sound) {
            lock (contextSyncRoot)
                sounds.Remove(sound);
        }

        private void UpdateLoop() {
            while (!stopping) {
                bool hasActiveSounds = false;
                try {
                    lock (contextSyncRoot) {
                        if (!disposed) {
                            hasActiveSounds = sounds.Any(sound => sound.RequiresEventPump);
                            if (hasActiveSounds)
                                AudioContext.Update();
                        }
                    }
                } catch (Exception ex) {
                    LogUpdateFailure(ex);
                }

                // Do not let an AudioContext.Update failure starve callbacks that were
                // already queued by a valid EOF/logical-boundary event. Callback failures
                // are isolated individually inside DrainCallbacks().
                DrainCallbacks();

                if (stopping)
                    break;

                // Paused/finished sources cannot produce a new End event. Sleep until
                // Play2D, resume, or a queued callback wakes us instead of burning a 100 Hz
                // idle poll while a station is stopped or background playback is suspended.
                updateWake.WaitOne(hasActiveSounds ? UpdateIntervalMs : Timeout.Infinite);
            }
        }

        private void LogUpdateFailure(Exception ex) {
            DateTime now = DateTime.UtcNow;
            if ((now - lastUpdateFailureLogUtc).TotalSeconds < 5d) {
                suppressedUpdateFailures++;
                return;
            }

            string suppressed = suppressedUpdateFailures > 0
                ? " (" + suppressedUpdateFailures + " repeated failures suppressed)"
                : string.Empty;
            suppressedUpdateFailures = 0;
            lastUpdateFailureLogUtc = now;
            try { Logger.Log("WARNING: MiniAudioEx event pump failed: " + ex.Message + suppressed); } catch { }
        }

        private void DrainCallbacks() {
            while (true) {
                Action callback;
                lock (callbackSyncRoot) {
                    if (callbackQueue.Count == 0)
                        return;
                    callback = callbackQueue.Dequeue();
                }

                try { callback(); }
                catch (Exception ex) {
                    try { Logger.Log("WARNING: MiniAudioEx playback callback failed: " + ex.Message); } catch { }
                }
            }
        }

        public void Dispose() {
            if (disposed)
                return;

            stopping = true;
            updateWake.Set();
            if (Thread.CurrentThread != updateThread)
                updateThread.Join();

            lock (contextSyncRoot) {
                if (disposed)
                    return;

                // Dispose a copy because MiniAudioSound.Dispose unregisters itself.
                foreach (MiniAudioSound sound in sounds.ToArray()) {
                    try { sound.Dispose(); } catch { }
                }
                sounds.Clear();

                AudioContext.Deinitialize();
                disposed = true;
            }

            updateWake.Dispose();
            lock (callbackSyncRoot)
                callbackQueue.Clear();
        }
    }

    internal static class MiniAudioNativeLoader {
        private const string NativeLibraryName = "miniaudioex.dll";
        private static IntPtr nativeModule;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        internal static void LoadFromScriptDirectory() {
            if (nativeModule != IntPtr.Zero)
                return;

            string assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string appBase = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = new string[]
            {
                string.IsNullOrEmpty(assemblyDirectory) ? null : Path.Combine(assemblyDirectory, NativeLibraryName),
                string.IsNullOrEmpty(appBase) ? null : Path.Combine(appBase, "scripts", NativeLibraryName),
                string.IsNullOrEmpty(appBase) ? null : Path.Combine(appBase, NativeLibraryName),
            };

            foreach (string candidate in candidates) {
                if (string.IsNullOrEmpty(candidate) || !File.Exists(candidate))
                    continue;

                nativeModule = LoadLibrary(candidate);
                if (nativeModule != IntPtr.Zero)
                    return;

                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, "Failed to load MiniAudioEx native runtime: " + candidate);
            }

            // Let normal DllImport probing have one chance before surfacing a clearer error.
            // AudioContext.Initialize() will throw DllNotFoundException if the runtime truly
            // is unavailable.
        }
    }
}
