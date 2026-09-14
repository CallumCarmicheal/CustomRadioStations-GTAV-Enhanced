using GTA;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using System.Collections.Generic;
using Keys = System.Windows.Forms.Keys;

using Control = GTA.Control;

namespace CustomRadioStations {

    internal static class ApplicationSettingsDefaults {
        internal const bool RememberSettingsPage = true;
        internal const string LastSettingsPage = "Playback";
        internal const int MenuHoldDelayMs = 400;
        internal const int MenuRepeatRateMs = 100;

        internal const float MasterVolume = 0.3f;
        internal const bool CustomWheelAsDefault = true;
        internal const int WheelActionDelayMs = 500;
        internal const int LoadYieldMs = 1;
        internal const int LoadStartDelayMs = 30000;
        internal const bool DisplayHelpText = true;
        internal const bool EnableWheelSlowMotion = true;
        internal const bool PlayInPauseMenu = false;
        internal const bool AllowSkippingTracks = true;
        internal const bool PlayWhileInBackground = false;

        internal const int IconWidth = 64;
        internal const int IconHeight = 64;
        internal const float WheelRadius = 300f;
        internal const string IconBackgroundColor = "#CC000000";
        internal const string IconHighlightColor = "#FF00CFEE";
        internal const double BackgroundIconSizeMultiplier = 1.35;
        internal const double HighlightIconSizeMultiplier = 1.45;
        internal const UnicodeTextMode UnicodeTextMode = CustomRadioStations.UnicodeTextMode.Auto;
        internal const string UnicodeFont = "";

        internal const Keys KeyboardToggleModifier = Keys.E;
        internal const Control KeyboardSkipTrack = Control.PhoneRight;
        internal const Control KeyboardVolumeUp = Control.PhoneUp;
        internal const Control KeyboardVolumeDown = Control.PhoneDown;
        internal const Keys KeyboardOpenSettings = Keys.F10;

        internal const float RadialDeadzone = 0.20f;
        internal const float RadialHysteresisDegrees = 4f;
        internal const Control GamepadToggleModifier = Control.VehicleDuck;
        internal const Control GamepadSkipTrack = Control.VehicleHandbrake;
        internal const Control GamepadVolumeUp = Control.MoveUpOnly;
        internal const Control GamepadVolumeDown = Control.MoveDownOnly;
        internal const Control GamepadOpenSettings = Control.ScriptSelect;
    }
    internal sealed class ApplicationSettings {
        public ApplicationSettings() {
            General = new GeneralSettings();
            Graphics = new GraphicsSettings();
            KeyboardControls = new KeyboardControlSettings();
            GamepadControls = new GamepadControlSettings();
            Ui = new UiSettings();
        }

        [JsonProperty("version")]
        public int Version { get; set; }

        [JsonProperty("general")]
        public GeneralSettings General { get; set; }

        [JsonProperty("graphics")]
        public GraphicsSettings Graphics { get; set; }

        [JsonProperty("keyboardControls")]
        public KeyboardControlSettings KeyboardControls { get; set; }

        [JsonProperty("gamepadControls")]
        public GamepadControlSettings GamepadControls { get; set; }

        [JsonProperty("ui")]
        public UiSettings Ui { get; set; }
    }

    internal sealed class UiSettings {
        [JsonProperty("rememberSettingsPage")]
        public bool RememberSettingsPage { get; set; } = ApplicationSettingsDefaults.RememberSettingsPage;

        [JsonProperty("lastSettingsPage")]
        public string LastSettingsPage { get; set; } = ApplicationSettingsDefaults.LastSettingsPage;

        [JsonProperty("menuHoldDelayMs")]
        public int MenuHoldDelayMs { get; set; } = ApplicationSettingsDefaults.MenuHoldDelayMs;

        [JsonProperty("menuRepeatRateMs")]
        public int MenuRepeatRateMs { get; set; } = ApplicationSettingsDefaults.MenuRepeatRateMs;
    }

    internal sealed class GeneralSettings {
        [JsonProperty("masterVolume")]
        public float MasterVolume { get; set; } = ApplicationSettingsDefaults.MasterVolume;

        [JsonProperty("customWheelAsDefault")]
        public bool CustomWheelAsDefault { get; set; } = ApplicationSettingsDefaults.CustomWheelAsDefault;

        [JsonProperty("wheelActionDelayMs")]
        public int WheelActionDelayMs { get; set; } = ApplicationSettingsDefaults.WheelActionDelayMs;

        [JsonProperty("loadYieldMs")]
        public int LoadYieldMs { get; set; } = ApplicationSettingsDefaults.LoadYieldMs;

        [JsonProperty("loadStartDelayMs")]
        public int LoadStartDelayMs { get; set; } = ApplicationSettingsDefaults.LoadStartDelayMs;

        [JsonProperty("displayHelpText")]
        public bool DisplayHelpText { get; set; } = ApplicationSettingsDefaults.DisplayHelpText;

        [JsonProperty("enableWheelSlowMotion")]
        public bool EnableWheelSlowMotion { get; set; } = ApplicationSettingsDefaults.EnableWheelSlowMotion;

        [JsonProperty("playInPauseMenu")]
        public bool PlayInPauseMenu { get; set; } = ApplicationSettingsDefaults.PlayInPauseMenu;
        [JsonProperty("allowSkippingTracks")]
        public bool AllowSkippingTracks { get; set; } = ApplicationSettingsDefaults.AllowSkippingTracks;

        [JsonProperty("playWhileInBackground")]
        public bool PlayWhileInBackground { get; set; } = ApplicationSettingsDefaults.PlayWhileInBackground;
    }

    internal sealed class GraphicsSettings {
        [JsonProperty("iconWidth")]
        public int IconWidth { get; set; } = ApplicationSettingsDefaults.IconWidth;

        [JsonProperty("iconHeight")]
        public int IconHeight { get; set; } = ApplicationSettingsDefaults.IconHeight;

        [JsonProperty("wheelRadius")]
        public float WheelRadius { get; set; } = ApplicationSettingsDefaults.WheelRadius;

        [JsonProperty("iconBackgroundColor")]
        public string IconBackgroundColor { get; set; } = ApplicationSettingsDefaults.IconBackgroundColor;

        [JsonProperty("iconHighlightColor")]
        public string IconHighlightColor { get; set; } = ApplicationSettingsDefaults.IconHighlightColor;

        [JsonProperty("backgroundIconSizeMultiplier")]
        public double BackgroundIconSizeMultiplier { get; set; } = ApplicationSettingsDefaults.BackgroundIconSizeMultiplier;

        [JsonProperty("highlightIconSizeMultiplier")]
        public double HighlightIconSizeMultiplier { get; set; } = ApplicationSettingsDefaults.HighlightIconSizeMultiplier;

        [JsonProperty("unicodeTextMode")]
        [JsonConverter(typeof(StringEnumConverter))]
        public UnicodeTextMode UnicodeTextMode { get; set; } = ApplicationSettingsDefaults.UnicodeTextMode;

        [JsonProperty("unicodeFont")]
        public string UnicodeFont { get; set; } = ApplicationSettingsDefaults.UnicodeFont;
    }

    internal sealed class KeyboardControlSettings {
        [JsonProperty("toggleModifier")]
        [JsonConverter(typeof(StringEnumConverter))]
        public Keys ToggleModifier { get; set; } = ApplicationSettingsDefaults.KeyboardToggleModifier;

        [JsonProperty("skipTrack")]
        [JsonConverter(typeof(StringEnumConverter))]
        public Control SkipTrack { get; set; } = ApplicationSettingsDefaults.KeyboardSkipTrack;

        [JsonProperty("volumeUp")]
        [JsonConverter(typeof(StringEnumConverter))]
        public Control VolumeUp { get; set; } = ApplicationSettingsDefaults.KeyboardVolumeUp;

        [JsonProperty("volumeDown")]
        [JsonConverter(typeof(StringEnumConverter))]
        public Control VolumeDown { get; set; } = ApplicationSettingsDefaults.KeyboardVolumeDown;

        [JsonProperty("openSettings")]
        [JsonConverter(typeof(StringEnumConverter))]
        public Keys OpenSettings { get; set; } = ApplicationSettingsDefaults.KeyboardOpenSettings;
    }

    internal sealed class GamepadControlSettings {
        [JsonProperty("radialDeadzone")]
        public float RadialDeadzone { get; set; } = ApplicationSettingsDefaults.RadialDeadzone;

        [JsonProperty("radialHysteresisDegrees")]
        public float RadialHysteresisDegrees { get; set; } = ApplicationSettingsDefaults.RadialHysteresisDegrees;

        [JsonProperty("toggleModifier")]
        [JsonConverter(typeof(StringEnumConverter))]
        public Control ToggleModifier { get; set; } = ApplicationSettingsDefaults.GamepadToggleModifier;

        [JsonProperty("skipTrack")]
        [JsonConverter(typeof(StringEnumConverter))]
        public Control SkipTrack { get; set; } = ApplicationSettingsDefaults.GamepadSkipTrack;

        [JsonProperty("volumeUp")]
        [JsonConverter(typeof(StringEnumConverter))]
        public Control VolumeUp { get; set; } = ApplicationSettingsDefaults.GamepadVolumeUp;

        [JsonProperty("volumeDown")]
        [JsonConverter(typeof(StringEnumConverter))]
        public Control VolumeDown { get; set; } = ApplicationSettingsDefaults.GamepadVolumeDown;

        [JsonProperty("openSettings")]
        [JsonConverter(typeof(StringEnumConverter))]
        public Control OpenSettings { get; set; } = ApplicationSettingsDefaults.GamepadOpenSettings;
    }

    internal sealed class WheelSettings {
        [JsonProperty("iconWidth")]
        public int? IconWidth { get; set; }

        [JsonProperty("iconHeight")]
        public int? IconHeight { get; set; }

        [JsonProperty("radius")]
        public float? Radius { get; set; }
    }

    internal sealed class NativeWheelConfiguration {
        public NativeWheelConfiguration() {
            Wheels = new List<NativeWheelSettings>();
        }

        [JsonProperty("wheels")]
        public List<NativeWheelSettings> Wheels { get; set; }
    }

    internal sealed class NativeWheelSettings {
        public NativeWheelSettings() {
            Stations = new List<string>();
        }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("stations")]
        public List<string> Stations { get; set; }
    }
}
