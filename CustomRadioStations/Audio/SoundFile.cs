using MiniAudioEx.Core.StandardAPI;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CustomRadioStations {
    class SoundFile {
        public MiniAudioSound Sound;
        private AudioClip Clip;

        public string FileName;
        public string FilePath;

        private string _displayName;
        public string DisplayName {
            get {
                if (HasTrackList) {
                    Track t = GetCurrentTrack();
                    if (t == null)
                        return PreviewDisplayName;
                    return TrackMetadataReader.FormatDisplayName(t.Artist, t.Title, _displayName);
                } else {
                    return _displayName;
                }
            }
        }

        public string PreviewDisplayName {
            get {
                Track firstTrack = HasTrackList && Tracklist != null
                    ? Tracklist.FirstOrDefault()
                    : null;
                return firstTrack == null
                    ? _displayName
                    : TrackMetadataReader.FormatDisplayName(firstTrack.Artist, firstTrack.Title, _displayName);
            }
        }

        /// <summary>
        /// Returns sound length in milliseconds
        /// </summary>
        public uint Length { get; private set; } = 0;
        /// <summary>
        /// If Length has been added to this sound's corresponding station
        /// </summary>
        public bool LengthAdded {
            get; set;
        }

        public bool HasTrackList;
        public List<Track> Tracklist {
            get; private set;
        }

        //public float MaximumDistance = 20f;
        //public float MinimumDistance = 1f;

        public SoundFile(string filepath) {
            FilePath = filepath;
            Clip = new AudioClip(filepath, true);
            FileName = Path.GetFileNameWithoutExtension(filepath);
            _displayName = TrackMetadataReader.ReadDisplayName(filepath, DisplayNameFromFilename());
            HasTrackList = TracklistExists(filepath);
        }

        public SoundFile(string filepath, string shortcutPath) {
            FilePath = shortcutPath;
            Clip = new AudioClip(filepath, true);
            FileName = Path.GetFileNameWithoutExtension(filepath);
            _displayName = TrackMetadataReader.ReadDisplayName(filepath, DisplayNameFromFilename());
            HasTrackList = TracklistExists(shortcutPath);
        }

        private string DisplayNameFromFilename() {
            string[] sections = FileName.Split(new string[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
            string str = "";
            foreach (var s in sections) {
                str += s.Trim() + "\n";
            }
            return str;
        }

        internal bool TracklistExists(string filepath) {
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
                TracklistConfig config = JsonConvert.DeserializeObject<TracklistConfig>(
                    File.ReadAllText(tracklistPath), serializerSettings);
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

        public Track GetCurrentTrack() {
            if (!HasTrackList || Tracklist == null || Tracklist.Count == 0)
                return null;

            //Track trk = Tracklist.FirstOrDefault(t => t.StartTime <= PlayPosition());
            Track trk = Tracklist.LastOrDefault(t => PlayPosition() >= t.StartTime);

            if (trk == default(Track)) {
                return null;
            } else {
                return trk;
            }
        }

        public int GetCurrentTrackIndex() {
            if (!HasTrackList || Tracklist == null || Tracklist.Count == 0)
                return -1;

            return Tracklist.IndexOf(GetCurrentTrack());
        }

        public Track GetNextTrack() {
            if (!HasTrackList || Tracklist == null || Tracklist.Count == 0)
                return null;

            Track t = GetCurrentTrack();
            if (t == null)
                return Tracklist[0];

            int index = Tracklist.IndexOf(t);
            return index < 0 || index >= Tracklist.Count - 1 ? Tracklist[0] : Tracklist[index + 1];
        }

        public void SkipToNextTrack() {
            if (!HasTrackList || Sound == null)
                return;

            Track next = GetNextTrack();
            if (next != null)
                Sound.PlayPosition = next.StartTime;
        }

        public uint TimeUntilNextTrack() {
            if (Sound == null)
                return 0;
            uint pPos = PlayPosition();
            uint remaining = Length > pPos ? Length - pPos : 0u;
            if (!HasTrackList)
                return remaining;

            Track t = GetNextTrack();
            if (t == null)
                return remaining;

            return t.StartTime > pPos ? t.StartTime - pPos + 1 : remaining;
        }

        public void PlaySound(bool resume, bool playLooped = false, bool playPaused = false, bool allowMultipleInstances = false, bool allowSoundEffects = false) {
            if (Clip == null)
                return;

            if (!allowMultipleInstances && Sound != null && !Sound.Finished && !IsPaused)
                return;

            if (resume && IsPaused) {
                IsPaused = false;
                return;
            }

            if (Sound != null) {
                Sound.Dispose();
                Sound = null;
            }

            try {
                Sound = SoundEngine.Play2D(Clip, playLooped, playPaused);
                if (Sound == null)
                    return;

                if (Length == 0)
                    Length = Sound.PlayLength;
            } catch (Exception ex) {
                Logger.Log("ERROR: MiniAudioEx failed to start '" + FileName + "': " + ex.Message);
                if (Sound != null) {
                    Sound.Dispose();
                    Sound = null;
                }
            }
        }

        public void StopSound() {
            if (Sound == null || Sound.Finished)
                return;
            Sound.Stop();
        }

        public bool IsPaused {
            get {
                if (Sound == null)
                    return false;
                return Sound.Paused;
            }
            set {
                if (Sound == null || Sound.Finished)
                    return;
                Sound.Paused = value;
            }
        }

        public bool IsPlaying() {
            return Sound != null && !Sound.Finished;
        }

        public bool IsFinishedPlaying() {
            return Sound == null || Sound.Finished;
        }

        /// <summary>
        /// Returns the current playback position in milliseconds.
        /// </summary>
        public uint PlayPosition() {
            return Sound == null ? 0u : Sound.PlayPosition;
        }

        public void Dispose() {
            if (Sound != null) {
                Sound.Dispose();
                Sound = null;
            }

            if (Clip != null) {
                Clip.Dispose();
                Clip = null;
            }
        }

        public static MiniAudioEngine SoundEngine = new MiniAudioEngine();

        public static void ManageSoundEngine() {
            if (SoundEngine == null)
                return;
            SoundEngine.Update();
        }

        public static void StepVolume(float step, int decimals) {
            if (SoundEngine == null)
                return;
            float temp = (float)Math.Round(SoundEngine.SoundVolume + step, decimals, MidpointRounding.ToEven);
            SoundEngine.SoundVolume = temp.LimitToRange(0f, 1f);
        }

        public static void DisposeSoundEngine() {
            if (SoundEngine == null)
                return;
            SoundEngine.Dispose();
        }
    }

    /// <summary>
    /// Small compatibility handle that keeps the old SoundFile/RadioStation timing API
    /// expressed in milliseconds while MiniAudioEx exposes its cursor in PCM frames.
    /// </summary>
    sealed class MiniAudioSound : IDisposable {
        private readonly MiniAudioEngine engine;
        private readonly AudioClip clip;
        private readonly AudioSource source;
        private readonly bool looped;
        private bool paused;
        private bool finished;
        private bool disposed;

        internal MiniAudioSound(MiniAudioEngine engine, AudioClip clip, bool looped, bool startPaused) {
            this.engine = engine;
            this.clip = clip;
            this.looped = looped;

            source = new AudioSource(1);
            source.End += OnPlaybackEnded;
            source.Volume = 0f;
            source.Play(clip);
            source.Loop = looped;

            // A zero-length, non-playing source means the decoder/open operation failed.
            if (source.Length == 0 && !source.IsPlaying) {
                source.Dispose();
                throw new InvalidDataException("MiniAudioEx could not decode or open: " + clip.FilePath);
            }

            if (startPaused) {
                source.Stop();
                source.Cursor = 0;
                paused = true;
            }

            source.Volume = 1f;
        }

        public uint PlayPosition {
            get {
                return FramesToMilliseconds(source.Cursor);
            }
            set {
                ulong frame = MillisecondsToFrames(value);
                ulong length = source.Length;
                source.Cursor = length > 0 && frame >= length ? length - 1 : frame;
                if (finished && (length == 0 || source.Cursor < length))
                    finished = false;
            }
        }

        public uint PlayLength {
            get {
                return FramesToMilliseconds(source.Length);
            }
        }

        public bool Paused {
            get {
                return paused;
            }
            set {
                if (disposed || finished || paused == value)
                    return;

                if (value) {
                    source.Stop();
                    paused = true;
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
            }
        }

        public bool Finished {
            get {
                if (!finished && !paused && !looped && !source.IsPlaying) {
                    ulong length = source.Length;
                    if (length > 0 && source.Cursor >= length)
                        finished = true;
                }
                return finished;
            }
        }

        public float Volume {
            get {
                return source.Volume;
            }
            set {
                source.Volume = value.LimitToRange(0f, 1f);
            }
        }

        public void Stop() {
            if (disposed)
                return;
            source.Stop();
            paused = false;
            finished = true;
        }

        private void OnPlaybackEnded() {
            if (!looped) {
                paused = false;
                finished = true;
            }
        }

        public void Dispose() {
            if (disposed)
                return;
            disposed = true;
            source.End -= OnPlaybackEnded;
            source.Dispose();
            engine.Unregister(this);
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
    /// Process-local MiniAudioEx owner. The public surface intentionally mirrors the
    /// tiny engine surface that the rest of CRS historically used.
    /// </summary>
    sealed class MiniAudioEngine : IDisposable {
        private const uint SampleRate = 48000;
        private const uint Channels = 2;
        private readonly List<MiniAudioSound> sounds = new List<MiniAudioSound>();
        private bool disposed;

        internal MiniAudioEngine() {
            MiniAudioNativeLoader.LoadFromScriptDirectory();
            MiniAudioFrameworkCompatibility.Initialize(SampleRate, Channels);
        }

        public float SoundVolume {
            get {
                return disposed ? 0f : AudioContext.MasterVolume;
            }
            set {
                if (!disposed)
                    AudioContext.MasterVolume = value.LimitToRange(0f, 1f);
            }
        }

        internal MiniAudioSound Play2D(AudioClip clip, bool looped, bool startPaused) {
            if (disposed)
                return null;
            var sound = new MiniAudioSound(this, clip, looped, startPaused);
            sounds.Add(sound);
            return sound;
        }

        internal void Update() {
            if (!disposed)
                AudioContext.Update();
        }

        internal void Unregister(MiniAudioSound sound) {
            sounds.Remove(sound);
        }

        public void Dispose() {
            if (disposed)
                return;

            // Dispose a copy because MiniAudioSound.Dispose unregisters itself.
            foreach (MiniAudioSound sound in sounds.ToArray()) {
                try {
                    sound.Dispose();
                } catch { }
            }
            sounds.Clear();

            AudioContext.Deinitialize();
            disposed = true;
        }
    }

    static class MiniAudioNativeLoader {
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