using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using GTA;
using GTA.Native;
using GTA.Math;
using SelectorWheel;

namespace CustomRadioStations
{
    class RadioStation
    {

        public static Random random = new Random();

        public string Name { get; set; }

        /// <summary>
        /// In milliseconds
        /// </summary>
        public uint TotalLength { get; private set; } = 0;

        WheelCategory corrWheelCat;
        List<SoundFileTimePair> SoundFileTimePairs;
        SoundFile CurrentSound;

        bool hasPlayedOnce;
        uint stoppedPositionStation;
        DateTime lastPlayedTime;

        int lastPlayedSoundIndex;
        uint stoppedPositionSound;
        bool allSoundsPlayedOnce;

        public bool HasPlayableSounds => SoundFileTimePairs != null && SoundFileTimePairs.Count > 0;

        public RadioStation(WheelCategory correspondingWheelCategory, IEnumerable<string> songFilesPaths)
        {
            corrWheelCat = correspondingWheelCategory;
            Name = corrWheelCat.Name;
            SoundFileTimePairs = new List<SoundFileTimePair>();
            List<Tuple<string, string>> commercials = new List<Tuple<string, string>>();

            foreach (var path in songFilesPaths)
            {
                try
                {
                    //Logger.Log(path.Substring(path.LastIndexOf('\\') + 1));
                    // If file is a shortcut, get the real path first
                    if (string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        string str = GeneralHelper.GetShortcutTargetFile(path);

                        if (str == string.Empty) continue;

                        if (Path.GetFileNameWithoutExtension(str).Contains("[Commercial]")
                            || Path.GetFileNameWithoutExtension(path).Contains("[Commercial]"))
                        {
                            commercials.Add(Tuple.Create(str, path));
                        }
                        else
                        {
                            SoundFileTimePairs.Add(new SoundFileTimePair(new SoundFile(str, path), 0));
                        }
                    }
                    else
                    {
                        if (Path.GetFileNameWithoutExtension(path).Contains("[Commercial]"))
                        {
                            commercials.Add(Tuple.Create(path, string.Empty));
                        }
                        else
                        {
                            SoundFileTimePairs.Add(new SoundFileTimePair(new SoundFile(path), 0));
                        }
                    }

                    Config.LoadTick();
                }
                catch (Exception ex)
                {
                    Logger.Log("ERROR : " + path.Substring(path.LastIndexOf('\\') + 1) + " : " + ex.Message);
                    Script.Wait(500);
                }
            }

            ShuffleList(); // Do this based on an ini setting? Yes. TODO
            InsertCommercials(commercials);

            // Calculate lengths and stuff for the station
            // Replaced by UpdateRadioLengthWithCurrentSound()
            //foreach (var s in SoundFileTimePairs)
            //{
            //    s.StartTime = TotalLength;
            //    TotalLength += s.SoundFile.Length;
            //}
        }

        /// <summary>
        /// Must be called after CurrentSound.PlaySound();
        /// </summary>
        private void UpdateRadioLengthWithCurrentSound()
        {
            if (CurrentSound == null) return;
            var s = SoundFileTimePairs.Find(x => x.SoundFile == CurrentSound);
            if (s == null) return;
            if (!CurrentSound.LengthAdded)
            {
                s.StartTime = TotalLength;
                TotalLength += CurrentSound.Length;
                CurrentSound.LengthAdded = true;
            }
        }

        DateTime trackUpdateTimer = DateTime.Now;
        public void Update()
        {
            if (CurrentSound == null || CurrentSound.Sound == null) return;

            // Legacy debug subtitle((lastPlayedSoundIndex + 1) + " / " + SoundFileTimePairs.Count);
            
            if (CurrentSound.HasTrackList && trackUpdateTimer < DateTime.Now)
            {
                UpdateWheelInfo();
                UpdateTrackUpdateTimer();
            }
            
            if (CurrentSound.IsFinishedPlaying())
            {
                PlayNextSound();
            }
        }

        private void ShuffleList()
        {
            /*int n = SoundFileTimePairs.Count;

            for (int i = SoundFileTimePairs.Count - 1; i > 1; i--)
            {
                int rnd = random.Next(i + 1);

                SoundFileTimePair value = SoundFileTimePairs[rnd];
                SoundFileTimePairs[rnd] = SoundFileTimePairs[i];
                SoundFileTimePairs[i] = value;
            }*/

            var count = SoundFileTimePairs.Count;
            var last = count - 1;
            for (var i = 0; i < last; ++i)
            {
                var r = random.Next(i, count);
                var tmp = SoundFileTimePairs[i];
                SoundFileTimePairs[i] = SoundFileTimePairs[r];
                SoundFileTimePairs[r] = tmp;
            }
        }

        int lastCommIndex = 0;
        private void InsertCommercials(List<Tuple<string, string>> commercials)
        {
            if (commercials.Count > 0)
            {
                int numToInsert = SoundFileTimePairs.Count / 3;
                int lastIndex = -1;
                SoundFileTimePairs.Capacity += numToInsert;

                for (int i = 0; i < numToInsert; i++)
                {
                    lastIndex += 4;

                    lastCommIndex = GetNewRandom(lastCommIndex, commercials.Count);
                    var commercial = commercials[lastCommIndex];

                    try
                    {
                        var pair = new SoundFileTimePair(
                                string.IsNullOrEmpty(commercial.Item2) ? new SoundFile(commercial.Item1) :
                                new SoundFile(commercial.Item1, commercial.Item2), 0);

                        if (lastIndex >= SoundFileTimePairs.Count)
                        {
                            SoundFileTimePairs.Add(pair);
                        }
                        else
                        {
                            SoundFileTimePairs.Insert(lastIndex, pair);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("WARNING: Could not load commercial '" + commercial.Item1 + "': " + ex.Message);
                    }
                }
            }
        }

        private int GetNewRandom(int input, int max)
        {
            int rnd = random.Next(0, max);
            if (input == rnd && max != 1)
            {
                return GetNewRandom(input, max);
            }
            else
            {
                return rnd;
            }

        }

        internal void RescanSoundsTracklists()
        {
            foreach (var pair in SoundFileTimePairs)
            {
                var soundFile = pair.SoundFile;
                soundFile.HasTrackList = soundFile.TracklistExists(soundFile.FilePath);
            }
            UpdateWheelInfo();
            UpdateTrackUpdateTimer();
        }

        private void UpdateWheelInfo()
        {
            if (CurrentSound == null || CurrentSound.Sound == null) return;

            if (corrWheelCat == null || corrWheelCat.ItemList == null || corrWheelCat.ItemList.Count == 0) return;
            WheelCategoryItem radioWheelItem = corrWheelCat.ItemList[0];

            radioWheelItem.Name = Name + "\n" + CurrentSound.DisplayName;
        }

        public void UpdateDashboardInfo()
        {
            if (CurrentSound == null || CurrentSound.Sound == null) return;

            Ped player = Game.Player.Character;
            if (player != null && player.Exists() && player.IsInVehicle())
            {
                string[] info = (CurrentSound.DisplayName ?? string.Empty).Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
                string artist = info.Length > 0 ? info[0] : string.Empty;
                string track = info.Length > 1 ? info[1] : string.Empty;
                RadioNativeFunctions.UpdateRadioScaleform(Name, artist, track);
            }
        }

        private void UpdateTrackUpdateTimer()
        {
            trackUpdateTimer = DateTime.Now.AddMilliseconds(CurrentSound == null || CurrentSound.Sound == null ? 5000 : CurrentSound.TimeUntilNextTrack());
        }

        public void Play()
        {
            if (!HasPlayableSounds)
            {
                Logger.Log("WARNING: Station '" + Name + "' has no playable sounds; ignoring play request.");
                return;
            }

            if (!hasPlayedOnce)
            {
                CurrentSound = SoundFileTimePairs[0].SoundFile;
                CurrentSound.PlaySound(false, false, true);
                if (CurrentSound.Sound == null)
                {
                    Logger.Log("WARNING: irrKlang failed to start station '" + Name + "'.");
                    CurrentSound = null;
                    return;
                }
                CurrentSound.Sound.PlayPosition = CurrentSound.GetRandomPlayPosition();
                CurrentSound.Sound.Paused = false;
                UpdateRadioLengthWithCurrentSound();
                hasPlayedOnce = true;

                UpdateWheelInfo();
            }
            else
            {
                if (!allSoundsPlayedOnce &&
                    lastPlayedSoundIndex == SoundFileTimePairs.Count - 1)
                {
                    allSoundsPlayedOnce = true;
                }

                ResumeContinuity();
            }

            Function.Call(Hash.SET_AUDIO_FLAG, "DisableFlightMusic", true);
            Function.Call(Hash.SET_AUDIO_FLAG, "DisableWantedMusic", true);
        }

        private void ResumeContinuity()
        {
            if (!HasPlayableSounds) return;

            uint elapsed = lastPlayedTime == default(DateTime)
                ? 0u
                : (uint)Math.Min(uint.MaxValue, Math.Max(0d, (DateTime.Now - lastPlayedTime).TotalMilliseconds));

            int safeLastIndex = Math.Max(0, Math.Min(lastPlayedSoundIndex, SoundFileTimePairs.Count - 1));
            var lastPlayedSound = SoundFileTimePairs[safeLastIndex].SoundFile;

            if (allSoundsPlayedOnce && TotalLength > 0)
            {
                uint newPlayPos = GetTimeFromPrevious(stoppedPositionStation, TotalLength, elapsed);
                var stPair = SoundFileTimePairs.LastOrDefault(s => newPlayPos >= s.StartTime) ?? SoundFileTimePairs[0];
                CurrentSound = stPair.SoundFile;
                CurrentSound.PlaySound(true, false, true);
                if (CurrentSound.Sound == null) { CurrentSound = null; return; }
                UpdateRadioLengthWithCurrentSound();
                CurrentSound.Sound.PlayPosition = Math.Min(CurrentSound.Length > 0 ? CurrentSound.Length - 1 : 0u,
                    newPlayPos >= stPair.StartTime ? newPlayPos - stPair.StartTime : 0u);
                CurrentSoundIsPaused = false;
            }
            else
            {
                uint remaining = lastPlayedSound.Length > stoppedPositionSound
                    ? lastPlayedSound.Length - stoppedPositionSound
                    : 0u;

                if (elapsed < remaining)
                {
                    CurrentSound = lastPlayedSound;
                    CurrentSound.PlaySound(true, false, true);
                    if (CurrentSound.Sound == null) { CurrentSound = null; return; }
                    uint maxPos = CurrentSound.Length > 0 ? CurrentSound.Length - 1 : 0u;
                    CurrentSound.Sound.PlayPosition = Math.Min(maxPos, stoppedPositionSound + elapsed);
                    CurrentSoundIsPaused = false;
                    UpdateRadioLengthWithCurrentSound();
                }
                else
                {
                    CurrentSound = safeLastIndex != SoundFileTimePairs.Count - 1
                        ? SoundFileTimePairs[safeLastIndex + 1].SoundFile
                        : SoundFileTimePairs[0].SoundFile;
                    CurrentSound.PlaySound(true);
                    if (CurrentSound.Sound == null) { CurrentSound = null; return; }
                    UpdateRadioLengthWithCurrentSound();
                }
            }

            UpdateWheelInfo();
            if (CurrentSound != null && CurrentSound.HasTrackList)
                UpdateTrackUpdateTimer();
        }

        private uint GetTimeFromPrevious(uint previous, uint duration, uint elapsed)
        {
            if (duration == 0) return 0;
            // Use 64-bit arithmetic so long real-time gaps cannot wrap uint before modulo.
            ulong normalizedPrevious = previous % duration;
            ulong time = (normalizedPrevious + elapsed) % duration;
            return (uint)time;
        }

        public void Stop()
        {
            if (CurrentSound == null || CurrentSound.Sound == null) return;

            // Get stopped position
            var stPair = SoundFileTimePairs.Find(s => s.SoundFile == CurrentSound);
            if (stPair == null) return;
            stoppedPositionStation = stPair.StartTime + CurrentSound.PlayPosition();
            lastPlayedTime = DateTime.Now;
            lastPlayedSoundIndex = SoundFileTimePairs.IndexOf(stPair);
            stoppedPositionSound = CurrentSound.PlayPosition();

            // Set name in wheel to just the station name
            if (corrWheelCat != null && corrWheelCat.ItemList != null && corrWheelCat.ItemList.Count > 0)
            {
                WheelCategoryItem radioWheelItem = corrWheelCat.ItemList[0];
                radioWheelItem.Name = Name;
            }

            //CurrentSound.StopSound();
            CurrentSoundIsPaused = true;
            CurrentSound = null;
            
            Function.Call(Hash.SET_AUDIO_FLAG, "DisableFlightMusic", false);
            Function.Call(Hash.SET_AUDIO_FLAG, "DisableWantedMusic", false);
        }
        
        private void PlayNextSound()
        {
            if (!HasPlayableSounds) return;

            int currentSoundIndex = -1;
            if (CurrentSound != null)
            {
                currentSoundIndex = SoundFileTimePairs.FindIndex(s => s.SoundFile == CurrentSound);
                CurrentSound.StopSound();
            }

            // Set next in list; -1 naturally advances to index 0.
            currentSoundIndex = currentSoundIndex >= 0 && currentSoundIndex < SoundFileTimePairs.Count - 1
                ? currentSoundIndex + 1
                : 0;

            CurrentSound = SoundFileTimePairs[currentSoundIndex].SoundFile;
            CurrentSound.PlaySound(true);
            if (CurrentSound.Sound == null)
            {
                Logger.Log("WARNING: Failed to start next audio file on station '" + Name + "'.");
                CurrentSound = null;
                return;
            }
            UpdateRadioLengthWithCurrentSound();
            UpdateWheelInfo();
            UpdateTrackUpdateTimer();
        }

        public void PlayNextSong()
        {
            if (CurrentSound != null)
            {
                // If CurrentSound has a tracklist but isn't at the last song, skip to the next song in the tracklist.
                if (CurrentSound.HasTrackList && CurrentSound.GetCurrentTrackIndex() < CurrentSound.Tracklist.Count - 1)
                {
                    CurrentSound.SkipToNextTrack();
                    UpdateWheelInfo();
                    UpdateTrackUpdateTimer();
                }
                // Else, skip to the next SoundFile.
                else
                {
                    PlayNextSound();
                }
            }
        }

        public bool IsPlaying
        {
            get { return CurrentSound != null && CurrentSound.IsPlaying(); }
        }

        public bool CurrentSoundIsPaused
        {
            get
            {
                return CurrentSound != null && CurrentSound.IsPaused;
            }
            set
            {
                if (CurrentSound != null) CurrentSound.IsPaused = value;
            }
        }

        public static RadioStation CurrentPlaying;
        public static RadioStation NextQueuedStation;

        public static void ManageStations()
        {
            if (CurrentPlaying == null) return;

            CurrentPlaying.Update();
        }
    }

    class SoundFileTimePair
    {
        public SoundFile SoundFile;
        public uint StartTime;

        public SoundFileTimePair(SoundFile sFile, uint time)
        {
            SoundFile = sFile;
            StartTime = time;
        }
    }
}
