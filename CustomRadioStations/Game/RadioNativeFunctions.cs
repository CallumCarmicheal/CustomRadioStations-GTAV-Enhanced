using GTA;
using GTA.Native;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomRadioStations {
    public static class RadioNativeFunctions {

        public static Scaleform DashboardScaleform;
        private static bool dashboardDisabled;
        private static bool lockStationFailureLogged;
        private static bool maxStationFailureLogged;
        private static bool stationQueryFailureLogged;
        private static bool radioHudFailureLogged;

        public static bool NativeWheelLockAvailable { get; private set; } = true;

        public static void UpdateRadioScaleform(string station, string artist, string track) {
            try {
                if (dashboardDisabled || DashboardScaleform == null || !DashboardScaleform.IsValid || !DashboardScaleform.IsLoaded)
                    return;

                // Dashboard scaleform still exists in Enhanced, but keep this best-effort:
                // a UI asset change should not take down radio playback.
                DashboardScaleform.CallFunction("SET_RADIO",
                        "", station,
                        artist, track);
            } catch (Exception exception) {
                dashboardDisabled = true;
                Logger.Log("WARNING: Dashboard SET_RADIO Scaleform is unavailable; disabling dashboard metadata. " + exception.Message);
            }
        }


        public static void DisposeDashboardScaleform() {
            try {
                DashboardScaleform?.Dispose();
            } finally { DashboardScaleform = null; }
        }

        // Only works for vehicle radio.
        public static bool _IS_PLAYER_VEHICLE_RADIO_ENABLED() {
            try {
                return Function.Call<bool>((Hash)0x5F43D83FD6738741);
            } catch { return Game.Player.Character != null && Game.Player.Character.IsInVehicle(); }
        }

        public static bool IsRadioHudComponentVisible() {
            try {
                return Function.Call<bool>(Hash.IS_HUD_COMPONENT_ACTIVE, 16);
            } catch (Exception ex) {
                if (!radioHudFailureLogged) {
                    radioHudFailureLogged = true;
                    Logger.Log("WARNING: Could not query radio HUD state: " + ex.Message);
                }
                return false;
            }
        }

        public static int GET_PLAYER_RADIO_STATION_INDEX() {
            try {
                return Function.Call<int>(Hash.GET_PLAYER_RADIO_STATION_INDEX);
            } catch { return 255; }
        }

        public static void SET_RADIO_TO_STATION_INDEX(int index) {
            if (index == 255) {
                SetVanillaRadioOff();
            } else {
                try { Function.Call(Hash.SET_RADIO_TO_STATION_INDEX, index); } catch { }
            }
        }

        public static string GET_RADIO_STATION_NAME(int index) {
            try {
                return Function.Call<string>(Hash.GET_RADIO_STATION_NAME, index);
            } catch (Exception ex) {
                if (!stationQueryFailureLogged) {
                    stationQueryFailureLogged = true;
                    Logger.Log("WARNING: Could not query GTA radio station names; native wheel organization is disabled: " + ex.Message);
                }
                return string.Empty;
            }
        }

        public static void SET_RADIO_TO_STATION_NAME(string name) {
            try { Function.Call(Hash.SET_RADIO_TO_STATION_NAME, name); } catch { }
        }

        public static string GetRadioStationProperName(string name) {
            return _GET_LABEL_TEXT(name);
        }

        public static string GetRadioStationProperName(int index) {
            return _GET_LABEL_TEXT(GET_RADIO_STATION_NAME(index));
        }

        public static string GetCurrentPlayingArtist() {
            int trackId = Function.Call<int>(Hash.GET_AUDIBLE_MUSIC_TRACK_TEXT_ID);
            return _GET_LABEL_TEXT(trackId.ToString() + "A");
        }

        public static string GetCurrentPlayingSongname() {
            int trackId = Function.Call<int>(Hash.GET_AUDIBLE_MUSIC_TRACK_TEXT_ID);
            return _GET_LABEL_TEXT(trackId.ToString() + "S");
        }

        /// <summary>
        /// Hides station from wheel and stops playing it if it was playing
        /// </summary>
        /// <param name="stationName">Name returned by GET_RADIO_STATION_NAME. Not the fancy name.</param>
        /// <param name="hide">true = hide or remove from wheel</param>
        public static void _LOCK_RADIO_STATION(string stationName, bool hide) {
            if (string.IsNullOrEmpty(stationName))
                return;
            try {
                Function.Call((Hash)0x477D9DB48F889591, stationName, hide); // _LOCK_RADIO_STATION
            } catch (Exception ex) {
                NativeWheelLockAvailable = false;
                if (!lockStationFailureLogged) {
                    lockStationFailureLogged = true;
                    Logger.Log("WARNING: _LOCK_RADIO_STATION failed. Native wheel organization will be unavailable: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Get the number of stations enabled in the radio wheel. 
        /// Call BEFORE using _LOCK_RADIO_STATION if you want to get the default number of stations in the wheel.
        /// </summary>
        /// <returns></returns>
        public static int _MAX_RADIO_STATION_INDEX() {
            try {
                return Function.Call<int>((Hash)0xF1620ECB50E01DE7); // _MAX_RADIO_STATION_INDEX
            } catch (Exception ex) {
                if (!maxStationFailureLogged) {
                    maxStationFailureLogged = true;
                    Logger.Log("WARNING: _MAX_RADIO_STATION_INDEX failed. Native wheel organization will be disabled: " + ex.Message);
                }
                return 0;
            }
        }

        public static void SetVanillaRadioOff() {
            Ped player = Game.Player.Character;
            if (player != null && player.Exists() && player.IsInVehicle() && player.CurrentVehicle != null && player.CurrentVehicle.IsEngineRunning) {
                SetVehicleRadioStationOff();
            }
            if (IS_MOBILE_PHONE_RADIO_ACTIVE()) // Doesn't return true if mobile radio is enabled but set to OFF station.
                SET_MOBILE_PHONE_RADIO_STATE(false);
        }

        public static void SetVehicleRadioStationOff() {
            Ped player = Game.Player.Character;
            if (player == null || !player.Exists())
                return;
            Vehicle vehicle = player.CurrentVehicle;
            if (vehicle == null || !vehicle.Exists())
                return;

            Function.Call(Hash.SET_VEH_RADIO_STATION, vehicle, "OFF");
        }

        public static void VanillaRadioFadedOut(bool fadeOut) {
            string scene = "MP_JOB_CHANGE_RADIO_MUTE";
            try {
                if (!fadeOut) {
                    Function.Call(Hash.SET_AUDIO_SCENE_VARIABLE, scene, "apply", 0f);
                    return;
                }

                Function.Call(Hash.START_AUDIO_SCENE, scene);
                Function.Call(Hash.SET_AUDIO_SCENE_VARIABLE, scene, "apply", 1f);
            } catch (Exception ex) {
                Logger.Log("WARNING: Could not control vanilla radio mute scene: " + ex.Message);
            }
        }

        public static bool IS_MOBILE_PHONE_RADIO_ACTIVE() {
            try {
                return Function.Call<bool>(Hash.IS_MOBILE_PHONE_RADIO_ACTIVE);
            } catch { return false; }
        }

        public static void SET_MOBILE_PHONE_RADIO_STATE(bool on) {
            try { Function.Call(Hash.SET_MOBILE_PHONE_RADIO_STATE, on); } catch { }
        }

        static string _GET_LABEL_TEXT(string text) {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            try {
                return Function.Call<string>((Hash)0x7B5280EBA9840C72, text) ?? string.Empty;
            } catch { return text; }
        }
    }
}