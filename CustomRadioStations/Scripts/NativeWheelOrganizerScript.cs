using GTA;
using GTA.Native;
using System;
using System.Windows.Forms;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using GTAVFunctions;

namespace CustomRadioStations
{
    public class NativeWheelOrganizerScript : Script
    {
        NativeWheel currentWheel;

        List<string> validStationNames;

        int maxStationCount;

        bool Event_JUST_OPENED_OnNextOpen = true;

        bool loaded;

        bool nativeWheelWasApplied;

        public NativeWheelOrganizerScript()
        {
            Tick += OnTick;
            KeyDown += OnKeyDown;
            KeyUp += OnKeyUp;
            Aborted += OnAbort;

            Interval = 10;
        }

        private void OnAbort(object sender, EventArgs e)
        {
            if (nativeWheelWasApplied) UnhideAllStations();
        }

        void UnhideAllStations()
        {
            if (maxStationCount <= 0) return;
            for (int i = 0; i < maxStationCount; i++)
            {
                string station = RadioNativeFunctions.GET_RADIO_STATION_NAME(i);
                if (!string.IsNullOrWhiteSpace(station))
                    RadioNativeFunctions._LOCK_RADIO_STATION(station, false);
            }
        }

        void LogAllStations()
        {
            Logger.Init(AppPaths.NativeStationsLogFile);

            Logger.Log("Game version: " + Game.FileVersion, AppPaths.NativeStationsLogFile);
            Logger.Log("Checking all native and add-on radios...", AppPaths.NativeStationsLogFile);

            maxStationCount = RadioNativeFunctions._MAX_RADIO_STATION_INDEX();

            validStationNames = new List<string>();
            if (maxStationCount <= 0)
            {
                Logger.Log("Native radio wheel organization disabled: GTA did not report a valid station count.", AppPaths.NativeStationsLogFile);
                return;
            }

            for (int i = 0; i < maxStationCount; i++)
            {
                string stationName = RadioNativeFunctions.GET_RADIO_STATION_NAME(i);
                if (string.IsNullOrWhiteSpace(stationName)) continue;
                validStationNames.Add(stationName);
                string s = "Name: " + stationName + " || Proper name: " + RadioNativeFunctions.GetRadioStationProperName(i);
                Logger.Log(s, AppPaths.NativeStationsLogFile);
            }

            Logger.Log("Please use the 'Name' name for your wheel organization lists (NativeWheels.cfg)! 'Proper name' is only for display purposes.", AppPaths.NativeStationsLogFile);
        }

        void GetOrganizationLists()
        {
            if (!File.Exists(AppPaths.NativeWheelsFile) || validStationNames == null || validStationNames.Count == 0) return;
            
            string[] lines = File.ReadAllLines(AppPaths.NativeWheelsFile);

            bool lastLineWasWheelName = false;

            foreach (string line in lines)
            {
                if (String.IsNullOrWhiteSpace(line)) continue;

                string l = line.Trim();

                if (l.Contains("[") && l.Contains("]"))
                {
                    if (lastLineWasWheelName)
                    {
                        NativeWheel.WheelList.Remove(NativeWheel.WheelList.Last());
                    }

                    var wheel = new NativeWheel(l.Substring(1, l.Length - 2));
                    NativeWheel.WheelList.Add(wheel);
                    lastLineWasWheelName = true;
                    continue;
                }

                if (WheelListIsPopulated() && validStationNames.Any(s => string.Equals(s, l, StringComparison.OrdinalIgnoreCase)))
                {
                    NativeWheel.WheelList.Last().stationList.Add(l);
                    lastLineWasWheelName = false;
                }
            }

            // Drop headers that ended up with no valid GTA stations. An empty wheel
            // must never activate control interception or hide the whole stock wheel.
            NativeWheel.WheelList.RemoveAll(w => w == null || w.stationList == null || w.stationList.Count == 0);

            if (WheelListIsPopulated())
            {
                currentWheel = NativeWheel.WheelList[0];
            }
        }

        bool WheelListIsPopulated()
        {
            return NativeWheel.WheelList != null && NativeWheel.WheelList.Count > 0;
        }

        void OnTick(object sender, EventArgs e)
        {
            if (GTAFunction.HasCheatStringJustBeenEntered("radio_reload"))
            {
                if (nativeWheelWasApplied) UnhideAllStations();
                nativeWheelWasApplied = false;
                NativeWheel.WheelList = new List<NativeWheel>();
                currentWheel = null;
                LogAllStations();
                if (maxStationCount > 0) GetOrganizationLists();
                loaded = true;
                Wait(150);
            }

            if (RadioNativeFunctions.IsRadioHudComponentVisible())
            {
                if (!loaded && Game.Player.CanControlCharacter)
                {
                    LogAllStations();
                    if (maxStationCount > 0) GetOrganizationLists();
                    loaded = true;
                }

                ShowHelpTexts();

                // Native wheel organization is optional. Only intercept GTA's normal
                // radio controls when a valid NativeWheels.cfg produced at least one
                // usable wheel. Otherwise fail open and leave the stock radio untouched.
                if (WheelListIsPopulated() && currentWheel != null)
                {
                    ControlWheelChange();

                    if (Event_JUST_OPENED_OnNextOpen)
                    {
                        OnJustOpened();
                    }

                    if (RadioNativeFunctions.NativeWheelLockAvailable)
                        DisableNativeScrollRadioControls();
                }

                Event_JUST_OPENED_OnNextOpen = false;
            }
            else
            {
                if (!loaded) return;

                if (!Event_JUST_OPENED_OnNextOpen)
                {
                    OnJustClosed();
                    Event_JUST_OPENED_OnNextOpen = true;
                }
            }
        }

        GTA.Control ControlNextWheel;
        GTA.Control ControlPrevWheel;
        void ShowHelpTexts()
        {
            ControlNextWheel = GTAFunction.UsingGamepad() ? GTA.Control.VehicleAccelerate : GTA.Control.WeaponWheelPrev;
            ControlPrevWheel = GTAFunction.UsingGamepad() ? GTA.Control.VehicleBrake : GTA.Control.WeaponWheelNext;

            if (!Config.DisplayHelpText) return;

            string nativeWheelText = WheelListIsPopulated() && currentWheel != null ?
                "\n" +
                GTAFunction.InputString(ControlNextWheel) + " " +
                GTAFunction.InputString(ControlPrevWheel) +
                " : Next / Prev Wheel\n" +
                "Wheel: " + currentWheel.Name
                : "";

            GTAFunction.DisplayHelpTextThisFrame(
                GTAFunction.InputString(Config.KB_Toggle, Config.GP_Toggle) +
                " : Switch to Custom Wheels" +
                nativeWheelText
                , false, false
                );
        }

        void DisableNativeScrollRadioControls()
        {
            ControlInput.DisableThisFrame(GTA.Control.VehicleNextRadio);
            ControlInput.DisableThisFrame(GTA.Control.VehiclePrevRadio);
        }

        void ControlWheelChange()
        {
            if (!WheelListIsPopulated() || currentWheel == null) return;

            if (ControlInput.IsJustPressed(ControlNextWheel))
            {
                currentWheel = NativeWheel.WheelList.GetNext(currentWheel);
                UpdateWheelThisFrame();
            }
            else if (ControlInput.IsJustPressed(ControlPrevWheel))
            {
                currentWheel = NativeWheel.WheelList.GetPrevious(currentWheel);
                UpdateWheelThisFrame();
            }
        }

        void UpdateWheelThisFrame()
        {
            if (!WheelListIsPopulated() || currentWheel == null || validStationNames == null) return;

            // Unhide all listed radios
            foreach (var station in currentWheel.stationList)
            {
                RadioNativeFunctions._LOCK_RADIO_STATION(station, false);
            }

            // Hide any valid station name that isn't in the current wheel station list
            foreach (var station in validStationNames)
            {
                if (!currentWheel.stationList.Any(s => string.Equals(s, station, StringComparison.OrdinalIgnoreCase)))
                {
                    RadioNativeFunctions._LOCK_RADIO_STATION(station, true);
                }
            }

            if (!RadioNativeFunctions.NativeWheelLockAvailable)
            {
                Logger.Log("Native radio wheel organization disabled for this session because station locking is unavailable.", AppPaths.NativeStationsLogFile);
                NativeWheel.WheelList.Clear();
                currentWheel = null;
                nativeWheelWasApplied = false;
            }
            else
            {
                nativeWheelWasApplied = true;
            }
        }

        void OnJustOpened()
        {
            // Legacy debug subtitle("Just Opened");
            UpdateWheelThisFrame();
        }

        void OnJustClosed()
        {
            // Legacy debug subtitle("Just Closed");
        }

        void OnKeyDown(object sender, KeyEventArgs e)
        {
        }

        void OnKeyUp(object sender, KeyEventArgs e)
        {
        }
    }

    class NativeWheel
    {
        public string Name;
        public List<string> stationList = new List<string>();

        public NativeWheel(string name)
        {
            Name = name;
        }
        
        public static List<NativeWheel> WheelList = new List<NativeWheel>();
    }
}
