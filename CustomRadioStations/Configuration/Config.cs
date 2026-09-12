using GTA;

using Newtonsoft.Json;

using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

using Control = GTA.Control;

namespace CustomRadioStations {
    public static class Config {
        private const int CurrentSettingsVersion = 3;
        private const int LegacyDefaultIconSize = 30;
        private const int CurrentDefaultIconSize = 64;

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        private static ApplicationSettings settings = new ApplicationSettings();

        public static bool CustomWheelAsDefault;
        public static int WheelActionDelay;
        public static int LoadMS;
        public static int LoadStartDelay;
        public static bool DisplayHelpText;
        public static bool EnableWheelSlowmotion;

        public static int IconX;
        public static int IconY;
        public static float WheelRadius;
        public static Color IconBG;
        public static Color IconHL;
        public static double IconBgSizeMultiple;
        public static double IconHlSizeMultiple;

        public static Keys KB_Toggle;
        public static Control KB_Skip_Track;
        public static Control KB_Volume_Up;
        public static Control KB_Volume_Down;

        public static Control GP_Toggle;
        public static Control GP_Skip_Track;
        public static Control GP_Volume_Up;
        public static Control GP_Volume_Down;
        public static float GP_RadialDeadzone;
        public static float GP_RadialHysteresisDegrees;

        public static void Load() {
            bool shouldCreate = !File.Exists(AppPaths.SettingsFile);
            if (!shouldCreate) {
                try {
                    settings = JsonConvert.DeserializeObject<ApplicationSettings>(
                        File.ReadAllText(AppPaths.SettingsFile), JsonSettings) ?? new ApplicationSettings();
                } catch (Exception ex) {
                    Logger.Log("ERROR: Failed to load global settings JSON '" + AppPaths.SettingsFile + "': " + ex.Message);
                    settings = new ApplicationSettings();
                }
            }

            bool shouldSaveUpgrade = UpgradeSettings();
            NormalizeSettings();
            ApplySettings();

            if (shouldCreate || shouldSaveUpgrade)
                Save();
        }

        private static bool UpgradeSettings() {
            if (settings.Version >= CurrentSettingsVersion)
                return false;

            settings.Graphics = settings.Graphics ?? new GraphicsSettings();
            if (settings.Graphics.IconWidth == LegacyDefaultIconSize &&
                settings.Graphics.IconHeight == LegacyDefaultIconSize) {
                settings.Graphics.IconWidth = CurrentDefaultIconSize;
                settings.Graphics.IconHeight = CurrentDefaultIconSize;
                Logger.Log("Upgraded the default station icon size from 30x30 to 64x64 virtual pixels.");
            }

            settings.Version = CurrentSettingsVersion;
            return true;
        }

        public static void Save() {
            try {
                settings.General.MasterVolume = SoundFile.SoundEngine.SoundVolume;
                settings.General.CustomWheelAsDefault = CustomWheelAsDefault;
                settings.General.WheelActionDelayMs = WheelActionDelay;
                settings.General.LoadYieldMs = LoadMS;
                settings.General.LoadStartDelayMs = LoadStartDelay;
                settings.General.DisplayHelpText = DisplayHelpText;
                settings.General.EnableWheelSlowMotion = EnableWheelSlowmotion;

                settings.Graphics.IconWidth = IconX;
                settings.Graphics.IconHeight = IconY;
                settings.Graphics.WheelRadius = WheelRadius;
                settings.Graphics.IconBackgroundColor = GeneralHelper.ColorToHex(IconBG);
                settings.Graphics.IconHighlightColor = GeneralHelper.ColorToHex(IconHL);
                settings.Graphics.BackgroundIconSizeMultiplier = IconBgSizeMultiple;
                settings.Graphics.HighlightIconSizeMultiplier = IconHlSizeMultiple;

                settings.KeyboardControls.ToggleModifier = KB_Toggle;
                settings.KeyboardControls.SkipTrack = KB_Skip_Track;
                settings.KeyboardControls.VolumeUp = KB_Volume_Up;
                settings.KeyboardControls.VolumeDown = KB_Volume_Down;

                settings.GamepadControls.ToggleModifier = GP_Toggle;
                settings.GamepadControls.SkipTrack = GP_Skip_Track;
                settings.GamepadControls.VolumeUp = GP_Volume_Up;
                settings.GamepadControls.VolumeDown = GP_Volume_Down;
                settings.GamepadControls.RadialDeadzone = GP_RadialDeadzone;
                settings.GamepadControls.RadialHysteresisDegrees = GP_RadialHysteresisDegrees;

                Directory.CreateDirectory(AppPaths.RootDirectory);
                File.WriteAllText(AppPaths.SettingsFile, JsonConvert.SerializeObject(settings, Formatting.Indented));
            } catch (Exception ex) {
                Logger.Log("ERROR: Failed to save global settings JSON '" + AppPaths.SettingsFile + "': " + ex.Message);
            }
        }

        public static (int iconX, int iconY, float wheelRadius) LoadWheelSettings(string directory) {
            string path = Path.Combine(directory, AppPaths.WheelSettingsFileName);
            if (!File.Exists(path))
                return (IconX, IconY, WheelRadius);

            try {
                WheelSettings wheel = JsonConvert.DeserializeObject<WheelSettings>(File.ReadAllText(path), JsonSettings)
                    ?? new WheelSettings();
                int iconX = wheel.IconWidth.GetValueOrDefault(IconX);
                int iconY = wheel.IconHeight.GetValueOrDefault(IconY);
                float radius = wheel.Radius.GetValueOrDefault(WheelRadius);

                if (iconX <= 0 || iconY <= 0 || radius <= 0f) {
                    Logger.Log("WARNING: Invalid wheel.json dimensions in '" + path + "'; using global defaults.");
                    return (IconX, IconY, WheelRadius);
                }

                return (iconX, iconY, radius);
            } catch (Exception ex) {
                Logger.Log("WARNING: Failed to load wheel JSON '" + path + "': " + ex.Message);
                return (IconX, IconY, WheelRadius);
            }
        }

        public static void RescanForTracklists() {
            StationWheelPair.List.ForEach(pair => pair.RescanStationTracklists());
        }

        public static CultureInfo culture;

        public static void SetupSystemCulture() {
            culture = new CultureInfo(System.Threading.Thread.CurrentThread.CurrentCulture.Name, true);
            culture.NumberFormat.NumberDecimalSeparator = ".";
            ForceDecimal();
        }

        public static void ForceDecimal() {
            if (culture != null)
                System.Threading.Thread.CurrentThread.CurrentCulture = culture;
        }

        public static int loadCounter;
        public static int loadInterval = 10;

        public static void LoadTick() {
            if (loadCounter % loadInterval == 0)
                Script.Wait(LoadMS);
            loadCounter++;
        }

        private static void NormalizeSettings() {
            settings.General = settings.General ?? new GeneralSettings();
            settings.Graphics = settings.Graphics ?? new GraphicsSettings();
            settings.KeyboardControls = settings.KeyboardControls ?? new KeyboardControlSettings();
            settings.GamepadControls = settings.GamepadControls ?? new GamepadControlSettings();

            settings.General.MasterVolume = Clamp(settings.General.MasterVolume, 0f, 1f, "general.masterVolume");
            settings.General.WheelActionDelayMs = Math.Max(0, settings.General.WheelActionDelayMs);
            settings.General.LoadYieldMs = Math.Max(0, settings.General.LoadYieldMs);
            settings.General.LoadStartDelayMs = Math.Max(0, settings.General.LoadStartDelayMs);
            settings.Graphics.IconWidth = Math.Max(1, settings.Graphics.IconWidth);
            settings.Graphics.IconHeight = Math.Max(1, settings.Graphics.IconHeight);
            settings.Graphics.WheelRadius = Math.Max(1f, settings.Graphics.WheelRadius);
            settings.Graphics.BackgroundIconSizeMultiplier = Math.Max(0.1, settings.Graphics.BackgroundIconSizeMultiplier);
            settings.Graphics.HighlightIconSizeMultiplier = Math.Max(0.1, settings.Graphics.HighlightIconSizeMultiplier);
            settings.GamepadControls.RadialDeadzone = Clamp(settings.GamepadControls.RadialDeadzone, 0f, 0.95f,
                "gamepadControls.radialDeadzone");
            settings.GamepadControls.RadialHysteresisDegrees = Clamp(settings.GamepadControls.RadialHysteresisDegrees, 0f, 30f,
                "gamepadControls.radialHysteresisDegrees");
        }

        private static void ApplySettings() {
            SoundFile.SoundEngine.SoundVolume = settings.General.MasterVolume;
            CustomWheelAsDefault = settings.General.CustomWheelAsDefault;
            WheelActionDelay = settings.General.WheelActionDelayMs;
            LoadMS = settings.General.LoadYieldMs;
            LoadStartDelay = settings.General.LoadStartDelayMs;
            DisplayHelpText = settings.General.DisplayHelpText;
            EnableWheelSlowmotion = settings.General.EnableWheelSlowMotion;

            IconX = settings.Graphics.IconWidth;
            IconY = settings.Graphics.IconHeight;
            WheelRadius = settings.Graphics.WheelRadius;
            IconBG = ParseColor(settings.Graphics.IconBackgroundColor, "#CC000000", "graphics.iconBackgroundColor");
            IconHL = ParseColor(settings.Graphics.IconHighlightColor, "#FF00CFEE", "graphics.iconHighlightColor");
            IconBgSizeMultiple = settings.Graphics.BackgroundIconSizeMultiplier;
            IconHlSizeMultiple = settings.Graphics.HighlightIconSizeMultiplier;

            KB_Toggle = settings.KeyboardControls.ToggleModifier;
            KB_Skip_Track = settings.KeyboardControls.SkipTrack;
            KB_Volume_Up = settings.KeyboardControls.VolumeUp;
            KB_Volume_Down = settings.KeyboardControls.VolumeDown;
            GP_Toggle = settings.GamepadControls.ToggleModifier;
            GP_Skip_Track = settings.GamepadControls.SkipTrack;
            GP_Volume_Up = settings.GamepadControls.VolumeUp;
            GP_Volume_Down = settings.GamepadControls.VolumeDown;
            GP_RadialDeadzone = settings.GamepadControls.RadialDeadzone;
            GP_RadialHysteresisDegrees = settings.GamepadControls.RadialHysteresisDegrees;
        }

        private static Color ParseColor(string value, string fallback, string settingName) {
            try {
                return GeneralHelper.HexToColor(value);
            } catch (Exception ex) {
                Logger.Log("WARNING: Invalid " + settingName + " value '" + value + "': " + ex.Message);
                return GeneralHelper.HexToColor(fallback);
            }
        }

        private static float Clamp(float value, float min, float max, string settingName) {
            if (float.IsNaN(value) || float.IsInfinity(value)) {
                Logger.Log("WARNING: Invalid " + settingName + "; using " + min + ".");
                return min;
            }
            return Math.Max(min, Math.Min(max, value));
        }
    }
}