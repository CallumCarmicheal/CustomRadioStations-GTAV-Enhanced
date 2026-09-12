using GTA;
using GTA.Native;
using System;
using System.Windows.Forms;
using System.IO;
using UIScreen = GTA.UI.Screen;
using SelectorWheel;
using GTAVFunctions;
using EventHelper;

namespace CustomRadioStations {
    public class MainScript : Script {
        bool lastPlayedOnFoot;

        int lastVanillaStationPlayed = 0;

        bool lastRadioWasCustom;

        DateTime? inputTimer = null;

        DateTime? loadDelayTimer = null;

        ActionOptions ActionQueued;

        bool loaded;

        string initializationFailure;

        GameFocusPauseMonitor focusPauseMonitor;

        enum ActionOptions {
            DoNothing,
            PlayQueued,
            StopCurrent,
            StopAllRadio
        }

        public MainScript() {
            // Keep script construction resilient. Missing/broken audio dependencies or a
            // malformed settings file should produce a log instead of preventing SHVDN
            // from constructing the script at all.
            try {
                if (!Directory.Exists(AppPaths.RootDirectory)) 
                    Directory.CreateDirectory(AppPaths.RootDirectory);

                Logger.Init();
                Config.SetupSystemCulture();
                Config.Load();
            } catch (Exception ex) {
                initializationFailure = ex.ToString();
                try { Logger.Log("FATAL: Startup dependency/configuration failure: " + ex); } catch { }
            }

            if (initializationFailure == null)
                focusPauseMonitor = new GameFocusPauseMonitor();

            Tick += OnTick;
            KeyDown += OnKeyDown;
            KeyUp += OnKeyUp;
            Aborted += OnAbort;

            Interval = 10;
        }

        private void OnAbort(object sender, EventArgs e) {
            // Cleanup is intentionally best-effort: an unavailable Enhanced native or an
            // already-disposed audio engine must not turn script shutdown into a crash.
            try { focusPauseMonitor?.Dispose(); } catch { }
            try { AudioPauseCoordinator.Reset(); } catch { }
            try { Game.TimeScale = 1f; } catch { }
            try { SoundFile.DisposeSoundEngine(); } catch { }
            try { RadioNativeFunctions.DisposeDashboardScaleform(); } catch { }
            try { Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER); } catch { }
            try { Function.Call(Hash.SET_AUDIO_FLAG, "DisableFlightMusic", false); } catch { }
            try { Function.Call(Hash.SET_AUDIO_FLAG, "DisableWantedMusic", false); } catch { }
            try {
                if (Function.Call<bool>(Hash.IS_AUDIO_SCENE_ACTIVE, "DEATH_SCENE")) {
                    Function.Call(Hash.STOP_AUDIO_SCENE, "DEATH_SCENE");
                    Function.Call(Hash.STOP_AUDIO_SCENE, "FADE_OUT_WORLD_250MS_SCENE");
                }
            } catch { }
            try {
                if (Function.Call<bool>(Hash.IS_AUDIO_SCENE_ACTIVE, "MP_JOB_CHANGE_RADIO_MUTE"))
                    Function.Call(Hash.STOP_AUDIO_SCENE, "MP_JOB_CHANGE_RADIO_MUTE");
            } catch { }
        }

        public void SetupRadio() {
            RadioCatalogLoader.Reload();

            foreach (Wheel radioWheel in WheelVars.RadioWheels) {
                radioWheel.OnCategoryChange += (sender, selectedCategory, selectedItem, wheelJustOpened) => {
                    if (selectedCategory.IsRadioOff) {
                        ActionQueued = ActionOptions.StopAllRadio;
                        RadioStation.NextQueuedStation = null;
                        SetActionDelay(Config.WheelActionDelay);
                        return;
                    }

                    StationWheelPair pair = StationWheelPair.List.Find(candidate =>
                        candidate.Wheel == radioWheel && candidate.Category == selectedCategory);
                    if (pair == null) return;

                    ActionQueued = ActionOptions.PlayQueued;
                    RadioStation.NextQueuedStation = pair.Station;
                    SetActionDelay(Config.WheelActionDelay);
                    lastRadioWasCustom = true;
                };

                // Register navigation once per wheel. The legacy loader registered this
                // identical handler once per station, causing duplicate callbacks.
                radioWheel.OnItemChange += (sender, selectedCategory, selectedItem, wheelJustOpened, goTo) => {
                    if (wheelJustOpened || !radioWheel.Visible ||
                        radioWheel != WheelVars.CurrentRadioWheel || WheelVars.NextQueuedWheel != null)
                        return;

                    if (goTo == GoTo.Next) {
                        radioWheel.Visible = false;
                        WheelVars.NextQueuedWheel = WheelVars.RadioWheels.GetNext(radioWheel);
                    } else if (goTo == GoTo.Prev) {
                        radioWheel.Visible = false;
                        WheelVars.NextQueuedWheel = WheelVars.RadioWheels.GetPrevious(radioWheel);
                    }
                };
            }

            if (WheelVars.RadioWheels.Count > 0) {
                WheelVars.CurrentRadioWheel = WheelVars.RadioWheels[0];
            } else {
                if (Config.DisplayHelpText)
                    UIScreen.ShowSubtitle("No music found in Custom Radio Stations. Add music and reload the script.");
                Logger.Log("ERROR: No playable music found in any station directory. Custom radio is disabled for this session.");
            }
        }

        void SetupEvents() {
            GeneralEvents.OnPlayerEnteredVehicle += (veh) => {
                if (veh == null || !veh.Exists() || StationWheelPair.List.Count == 0) return;

                bool vehWasEngineRunning = veh.IsEngineRunning;

                // Make vanilla radio silent
                RadioNativeFunctions.VanillaRadioFadedOut(true);

                DateTime enteredTime = DateTime.Now;

                // Wait for the vehicle engine to start, but never block the script forever.
                // The original condition used OR + ">", which becomes permanently true
                // after the timeout and can hang the script.
                while (!veh.IsEngineRunning && DateTime.Now < enteredTime.AddSeconds(10)) {
                    vehWasEngineRunning = false;
                    Yield();
                }

                // In case the timeout above caused the loop to break,
                // We will not continue because the vehicle is dead.
                if (!veh.IsEngineRunning) return;

                if (UsedVehiclesManager.IsUsedVehicle(veh)) {
                    if (UsedVehiclesManager.GetVehicleStationInfo(veh) == null) {
                        // Make vanilla radio audible
                        RadioNativeFunctions.VanillaRadioFadedOut(false);

                        lastRadioWasCustom = false;
                        return;
                    }

                    ActionQueued = ActionOptions.PlayQueued;

                    UsedVehiclesManager.SetLastStationNow(veh);

                    SetActionDelay(Config.WheelActionDelay + 300);

                    lastRadioWasCustom = true;

                    //UIScreen.ShowSubtitle("Started playback");
                } else {
                    // If the engine was running, don't mess with it.
                    // Since I can't figure out how to see if a vehicle
                    // was emitting a station, I'll just not mess with it.
                    if (vehWasEngineRunning) {
                        //UIScreen.ShowSubtitle("RADIO IS ENABLED: " + RadioNativeFunctions.GET_PLAYER_RADIO_STATION_INDEX().ToString());

                        // Make vanilla radio audible
                        RadioNativeFunctions.VanillaRadioFadedOut(false);

                        lastRadioWasCustom = false;
                        return;
                    }

                    int chooseRandom = RadioStation.random.Next(10);
                    //UIScreen.ShowSubtitle("RANDOM: " + chooseRandom.ToString());
                    // 70% chance to play a custom station.
                    if (chooseRandom >= 3) {
                        ActionQueued = ActionOptions.PlayQueued;

                        // Set the queued radio station randomly, chosen from stationPairList.
                        chooseRandom = RadioStation.random.Next(StationWheelPair.List.Count);

                        UsedVehiclesManager.UpdateVehicleWithStationInfo(veh,
                            StationWheelPair.List[chooseRandom]);

                        UsedVehiclesManager.SetLastStationNow(veh);

                        SetActionDelay(Config.WheelActionDelay + 300);

                        lastRadioWasCustom = true;
                    } else {
                        UsedVehiclesManager.UpdateVehicleWithStationInfo(veh, null);

                        // Make vanilla radio audible
                        RadioNativeFunctions.VanillaRadioFadedOut(false);

                        lastRadioWasCustom = false;
                    }
                }
            };

            GeneralEvents.OnPlayerExitedVehicle += (veh) => {
                if (veh == null) return;

                /*if (!IsMobileRadioEnabled())
                {
                    lastRadioWasCustom = IsCurrentCustomStationPlaying() ? true : false;
                }*/

                // Make vanilla radio audible
                RadioNativeFunctions.VanillaRadioFadedOut(false);

                StationWheelPair selectedPair = null;
                if (lastRadioWasCustom && WheelVars.CurrentRadioWheel != null && WheelVars.CurrentRadioWheel.SelectedCategory != null) {
                    selectedPair = StationWheelPair.List.Find(x => x.Category == WheelVars.CurrentRadioWheel.SelectedCategory);
                }

                UsedVehiclesManager.UpdateVehicleWithStationInfo(veh, selectedPair);
            };

            /*GeneralEvents.OnPlayerVehicleEngineTurnedOn += (veh) =>
            {
                if (IsCurrentCustomStationPlaying()) return;

                if (lastRadioWasCustom && canResumeCustomStation)
                {
                    ActionQueued = ActionOptions.PlayQueued;

                    // Set the queued radio station based on the current category selected, using stationPairList.
                    RadioStation.NextQueuedStation = StationWheelPair.List.Find(x => x.Category == WheelVars.CurrentRadioWheel.SelectedCategory).Station;

                    SetActionDelay(Config.WheelActionDelay);

                    canResumeCustomStation = false;
                }
            };*/
        }

        void OnTick(object sender, EventArgs e) {
            if (initializationFailure != null) {
                if (!loaded && Game.Player != null && Game.Player.CanControlCharacter) {
                    loaded = true;
                    try { UIScreen.ShowSubtitle("Custom Radio Stations could not start. Check CustomRadioStations.log."); } catch { }
                }
                return;
            }

            // Run before loading gates and other radio work. GTA can suspend script
            // ticks immediately after opening the pause menu.
            HandleGamePause();

            if (!loaded) {
                if (Game.Player == null || !Game.Player.CanControlCharacter) return;

                if (loadDelayTimer == null) loadDelayTimer = DateTime.Now.AddMilliseconds(Config.LoadStartDelay);

                if (loadDelayTimer < DateTime.Now || RuntimeState.HasLoadedOnce) {
                    if (Config.DisplayHelpText)
                        UIScreen.ShowSubtitle("Loading Custom Radios...");

                    Logger.Log("Starting Custom Radio Stations Enhanced compatibility build");
                    Logger.Log("Reported game version: " + Game.FileVersion);

                    try {
                        SetupRadio();
                        if (WheelVars.RadioWheels.Count > 0 && StationWheelPair.List.Count > 0)
                            SetupEvents();
                    } catch (Exception ex) {
                        Logger.Log("FATAL: Failed to initialize custom radio: " + ex);
                        if (Config.DisplayHelpText) UIScreen.ShowSubtitle("Custom Radio Stations failed to initialize. Check CustomRadioStations.log.");
                        loaded = true;
                        return;
                    }

                    if (WheelVars.RadioWheels.Count == 0) {
                        loaded = true;
                        return;
                    }

                    // Allow playing MP audio sounds and scenes
                    Function.Call(Hash.SET_AUDIO_FLAG, "LoadMPData", true);

                    RadioNativeFunctions.DashboardScaleform = Scaleform.RequestMovie("dashboard");

                    RuntimeState.HasLoadedOnce = true;

                    loaded = true;

                    if (Config.DisplayHelpText)
                        UIScreen.ShowSubtitle("Custom Radios Loaded");

                    if (Config.CustomWheelAsDefault && WheelVars.RadioWheels.Count > 0) {
                        lastRadioWasCustom = true;
                    }
                }

                return; // Return if loaded is still not true
            }

            if (GTAFunction.HasCheatStringJustBeenEntered("radio_reload")) {
                Config.Load();
                SetupRadio();
                UIScreen.ShowSubtitle("Custom Radio configuration reloaded:\n- settings.json\n- wheel.json and native-wheels.json\n- station.json and legacy station.ini\n- tracklist JSON metadata");
                Wait(150);
            }

            if (WheelVars.RadioWheels.Count == 0) return;

            if (VanillaOrCustomRadioWheelIsVisible()) {
                if (GTAFunction.UsingGamepad()) {
                    // Read disabled input above, then suppress GTA's own A-button radio
                    // selection so the same press only changes between radio menus.
                    ControlInput.DisableThisFrame(Config.GP_Toggle);
                    if (ControlInput.IsJustPressed(Config.GP_Toggle))
                        HandleRadioWheelToggle();
                }

                if (lastRadioWasCustom && WheelVars.CurrentRadioWheel != null) {
                    WheelVars.CurrentRadioWheel.Visible = true;
                }

                Ped player = Game.Player.Character;
                lastPlayedOnFoot = player == null || !player.Exists() || !player.IsInVehicle();
            }

            if (ControlInput.IsJustReleased(GTA.Control.VehicleRadioWheel)) {
                if (WheelVars.CurrentRadioWheel != null && WheelVars.CurrentRadioWheel.Visible) {
                    WheelVars.CurrentRadioWheel.Visible = false;
                }
            }

            Wheel.ControlTransitions(Config.EnableWheelSlowmotion);
            WheelVars.RadioWheels.ForEach(w => w.ProcessSelectorWheel());
            HandleRadioWheelQueue();
            SoundFile.ManageSoundEngine();
            RadioStation.ManageStations();
            HandleRadioWheelExtraControls();
            HandleQueuedStationActions();
            HandleEnterExitVehicles();
            UpdateDashboardInfo();
            GeneralEvents.Update();
        }

        public void HandleRadioWheelQueue() {
            if (WheelVars.NextQueuedWheel != null) {
                WheelVars.CurrentRadioWheel = WheelVars.NextQueuedWheel;
                WheelVars.CurrentRadioWheel.Visible = true;
                WheelVars.NextQueuedWheel = null;

                /*ActionQueued = ActionOptions.PlayQueued;

                // Set the queued radio station based on the current category selected, using stationPairList.
                RadioStation.NextQueuedStation = StationWheelPair.List.Find(x => x.Category == currentRadioWheel.SelectedCategory).Station;

                SetActionDelay(Config.WheelActionDelay);*/
            }
        }

        GTA.Control ControlSkipTrack;
        GTA.Control ControlVolumeUp;
        GTA.Control ControlVolumeDown;
        GTA.Control ControlNextWheel;
        GTA.Control ControlPrevWheel;
        readonly HoldRepeatState volumeUpRepeat = new HoldRepeatState(
            TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(100));
        readonly HoldRepeatState volumeDownRepeat = new HoldRepeatState(
            TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(100));
        bool volumeSavePending;
        DateTime volumeSaveAt;

        public void HandleRadioWheelExtraControls() {
            bool volumeControlsActive = false;
            if (WheelVars.CurrentRadioWheel != null && WheelVars.CurrentRadioWheel.Visible) {
                if (RadioStation.CurrentPlaying != null) {
                    volumeControlsActive = true;
                    ControlSkipTrack = GTAFunction.UsingGamepad() ? Config.GP_Skip_Track : Config.KB_Skip_Track;
                    ControlVolumeUp = GTAFunction.UsingGamepad() ? Config.GP_Volume_Up : Config.KB_Volume_Up;
                    ControlVolumeDown = GTAFunction.UsingGamepad() ? Config.GP_Volume_Down : Config.KB_Volume_Down;
                    ControlNextWheel = GTAFunction.UsingGamepad() ? GTA.Control.VehicleAccelerate : GTA.Control.WeaponWheelPrev;
                    ControlPrevWheel = GTAFunction.UsingGamepad() ? GTA.Control.VehicleBrake : GTA.Control.WeaponWheelNext;

                    if (Config.DisplayHelpText) {
                        GTAFunction.DisplayHelpTextThisFrame(
                            GTAFunction.InputString(ControlSkipTrack) +
                            " : Skip Track\n" +
                            GTAFunction.InputString(ControlVolumeUp) + " " +
                            GTAFunction.InputString(ControlVolumeDown) +
                            " : Volume: " +
                            Math.Round(SoundFile.SoundEngine.SoundVolume * 100, 0) + "%\n" +
                            GTAFunction.InputString(ControlNextWheel) + " " +
                            GTAFunction.InputString(ControlPrevWheel) +
                            " : Next / Prev Wheel\n", false, false);
                    }

                    if (ControlInput.IsJustPressed(ControlSkipTrack)) {
                        RadioStation.CurrentPlaying.PlayNextSong();
                    } else {
                        DateTime now = DateTime.UtcNow;
                        bool increase = volumeUpRepeat.ShouldFire(
                            ControlInput.IsJustPressed(ControlVolumeUp),
                            ControlInput.IsPressed(ControlVolumeUp), now);
                        bool decrease = volumeDownRepeat.ShouldFire(
                            ControlInput.IsJustPressed(ControlVolumeDown),
                            ControlInput.IsPressed(ControlVolumeDown), now);

                        // Opposing controls cancel each other when pressed together.
                        if (increase != decrease)
                            ChangeVolume(increase ? 0.05f : -0.05f, now);
                    }
                }
            }

            if (!volumeControlsActive) {
                volumeUpRepeat.Reset();
                volumeDownRepeat.Reset();
            }

            if (volumeSavePending && DateTime.UtcNow >= volumeSaveAt) {
                Config.Save();
                volumeSavePending = false;
            }

            if (RadioStation.CurrentPlaying != null) {
                ControlInput.DisableThisFrame(GTA.Control.VehicleNextRadio);
                ControlInput.DisableThisFrame(GTA.Control.VehicleNextRadioTrack);
                ControlInput.DisableThisFrame(GTA.Control.VehiclePrevRadio);
                ControlInput.DisableThisFrame(GTA.Control.VehiclePrevRadioTrack);

                RadioNativeFunctions.SetVanillaRadioOff();
            }
        }

        private void ChangeVolume(float step, DateTime now) {
            SoundFile.StepVolume(step, 2);
            volumeSavePending = true;
            volumeSaveAt = now.AddMilliseconds(300);
        }

        public void HandleEnterExitVehicles() {
            if (!lastPlayedOnFoot && !RadioNativeFunctions._IS_PLAYER_VEHICLE_RADIO_ENABLED()) {
                if (RadioStation.CurrentPlaying != null) {
                    RadioStation.CurrentPlaying.Stop();
                    RadioStation.CurrentPlaying = null;

                    if (WheelVars.CurrentRadioWheel != null) {
                        WheelVars.CurrentRadioWheel.Visible = false;
                    }

                    // Make vanilla radio audible
                    RadioNativeFunctions.VanillaRadioFadedOut(false);

                }
            }

            /*if (Game.Player.Character.CurrentVehicle != null
                && Game.Player.Character.CurrentVehicle.IsEngineRunning
                && lastRadioWasCustom
                && canResumeCustomStation
                && !IsCurrentCustomStationPlaying())
            {
                ActionQueued = ActionOptions.PlayQueued;

                // Set the queued radio station based on the current category selected, using stationPairList.
                RadioStation.NextQueuedStation = StationWheelPair.List.Find(x => x.Category == WheelVars.CurrentRadioWheel.SelectedCategory).Station;

                SetActionDelay(Config.WheelActionDelay);

                canResumeCustomStation = false;
            }*/
        }

        public void HandleGamePause() {
            bool pauseRequested = IsPauseControlJustPressed(GTA.Control.FrontendPause) ||
                IsPauseControlJustPressed(GTA.Control.FrontendPauseAlternate);
            bool pauseMenuActive = false;
            try { pauseMenuActive = Game.IsPaused; } catch { }
            AudioPauseCoordinator.SetGamePaused(pauseRequested || pauseMenuActive);
        }

        private static bool IsPauseControlJustPressed(GTA.Control control) {
            try {
                return Function.Call<bool>(Hash.IS_CONTROL_JUST_PRESSED, 0, (int)control) ||
                    Function.Call<bool>(Hash.IS_CONTROL_JUST_PRESSED, ControlInput.WheelInputGroup, (int)control) ||
                    Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, ControlInput.WheelInputGroup, (int)control);
            } catch {
                return false;
            }
        }

        void HandleQueuedStationActions() {
            if (CanDoQueuedAction()) {
                if (ActionQueued == ActionOptions.StopAllRadio) {
                    if (RadioStation.CurrentPlaying != null)
                        RadioStation.CurrentPlaying.Stop();

                    RadioStation.CurrentPlaying = null;
                    RadioStation.NextQueuedStation = null;
                    RadioNativeFunctions.SetVanillaRadioOff();
                    RadioNativeFunctions.VanillaRadioFadedOut(false);
                    lastRadioWasCustom = false;
                } else if (ActionQueued == ActionOptions.StopCurrent) {
                    if (RadioStation.CurrentPlaying != null) {
                        // Turn off custom radio
                        RadioStation.CurrentPlaying.Stop();

                        // Enable last played vanilla radio
                        RadioNativeFunctions.SET_RADIO_TO_STATION_INDEX(lastVanillaStationPlayed);

                        // Make vanilla radio audible
                        RadioNativeFunctions.VanillaRadioFadedOut(false);


                        RadioStation.CurrentPlaying = null;
                        lastRadioWasCustom = false;
                    }
                } else if (ActionQueued == ActionOptions.PlayQueued) {
                    if (RadioStation.NextQueuedStation != null
                        && RadioStation.NextQueuedStation != RadioStation.CurrentPlaying) {
                        // Enable custom radio
                        if (RadioStation.CurrentPlaying != null)
                            RadioStation.CurrentPlaying.Stop();

                        RadioStation.CurrentPlaying = RadioStation.NextQueuedStation;
                        RadioStation.CurrentPlaying.Play();
                        RadioStation.NextQueuedStation = null;

                        // Do not mute GTA's radio if MiniAudioEx failed to start the selected
                        // source. This keeps the game usable even when one station/file is bad.
                        if (!RadioStation.CurrentPlaying.IsPlaying) {
                            Logger.Log("WARNING: Custom station failed to start; leaving vanilla radio unchanged.");
                            RadioStation.CurrentPlaying = null;
                            lastRadioWasCustom = false;
                        } else {
                            // Set vanilla radio to Off but save what station was playing beforehand
                            lastVanillaStationPlayed = RadioNativeFunctions.GET_PLAYER_RADIO_STATION_INDEX();
                            RadioNativeFunctions.SetVanillaRadioOff();

                            // Make vanilla radio audible (custom audio is independent).
                            RadioNativeFunctions.VanillaRadioFadedOut(false);

                            lastRadioWasCustom = true;
                        }
                    }
                }

                // Set to DoNothing since queued action is completed
                ActionQueued = ActionOptions.DoNothing;
            }
        }

        bool CanDoQueuedAction() {
            if (inputTimer == null) return false;

            if (inputTimer < DateTime.Now) {
                inputTimer = null;
                return true;
            }
            return false;
        }

        void SetActionDelay(int ms = 500) {
            inputTimer = DateTime.Now.AddMilliseconds(ms);
        }

        void HandleRadioWheelToggle() {
            if (!loaded || WheelVars.CurrentRadioWheel == null || WheelVars.RadioWheels.Count == 0)
                return;

            if (WheelVars.CurrentRadioWheel.Visible) {
                ActionQueued = ActionOptions.StopCurrent;

                RadioStation.NextQueuedStation = null;

                SetActionDelay(Config.WheelActionDelay);

                // Switch to vanilla radio
                WheelVars.CurrentRadioWheel.Visible = false;
                lastRadioWasCustom = false;
            } else {
                // Switch to custom radio
                WheelVars.CurrentRadioWheel.Visible = true;
                lastRadioWasCustom = true;
            }
        }

        void UpdateDashboardInfo() {
            if (RadioStation.CurrentPlaying != null) {
                RadioStation.CurrentPlaying.UpdateDashboardInfo();
            }
        }

        bool VanillaOrCustomRadioWheelIsVisible() {
            //return /*_IS_PLAYER_VEHICLE_RADIO_ENABLED() &&*/ ControlInput.IsPressed(GTA.Control.VehicleRadioWheel) && Game.Player.CanControlCharacter;
            Ped player = Game.Player.Character;
            if (player == null || !player.Exists()) return false;

            if (ControlInput.IsPressed(GTA.Control.VehicleRadioWheel) && Game.Player.CanControlCharacter) {
                if (player.IsInVehicle() && RadioNativeFunctions._IS_PLAYER_VEHICLE_RADIO_ENABLED()) {
                    return true;
                } else if (player.IsOnFoot) {
                    if (RadioNativeFunctions.IsRadioHudComponentVisible() ||
                        (WheelVars.CurrentRadioWheel != null && WheelVars.CurrentRadioWheel.Visible) ||
                        IsCurrentCustomStationPlaying()) {
                        return true;
                    }
                }
            }
            return false;
        }

        bool IsMobileRadioEnabled() {
            return RadioNativeFunctions.IS_MOBILE_PHONE_RADIO_ACTIVE() || IsCurrentCustomStationPlaying();
        }

        bool IsCurrentCustomStationPlaying() {
            return RadioStation.CurrentPlaying != null && RadioStation.CurrentPlaying.IsPlaying && !RadioStation.CurrentPlaying.CurrentSoundIsPaused;
        }

        void OnKeyDown(object sender, KeyEventArgs e) {
        }

        void OnKeyUp(object sender, KeyEventArgs e) {
            if (e.KeyCode != Config.KB_Toggle) return;

            if (!loaded) {
                if (Config.DisplayHelpText) UIScreen.ShowSubtitle("Custom Radio not loaded yet, please try again later!");
                return;
            }

            if (WheelVars.CurrentRadioWheel != null && VanillaOrCustomRadioWheelIsVisible())
                HandleRadioWheelToggle();
        }
    }
}
