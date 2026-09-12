using GTA;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using System.Collections.Generic;
using System.Windows.Forms;

using Control = GTA.Control;

namespace CustomRadioStations {
    internal sealed class ApplicationSettings {
        public ApplicationSettings() {
            General = new GeneralSettings();
            Graphics = new GraphicsSettings();
            KeyboardControls = new KeyboardControlSettings();
            GamepadControls = new GamepadControlSettings();
        }

        [JsonProperty("general")]
        public GeneralSettings General { get; set; }

        [JsonProperty("graphics")]
        public GraphicsSettings Graphics { get; set; }

        [JsonProperty("keyboardControls")]
        public KeyboardControlSettings KeyboardControls { get; set; }

        [JsonProperty("gamepadControls")]
        public GamepadControlSettings GamepadControls { get; set; }
    }

    internal sealed class GeneralSettings {
        [JsonProperty("masterVolume")]
        public float MasterVolume { get; set; } = 0.3f;

        [JsonProperty("customWheelAsDefault")]
        public bool CustomWheelAsDefault { get; set; } = true;

        [JsonProperty("wheelActionDelayMs")]
        public int WheelActionDelayMs { get; set; } = 500;

        [JsonProperty("loadYieldMs")]
        public int LoadYieldMs { get; set; } = 1;

        [JsonProperty("loadStartDelayMs")]
        public int LoadStartDelayMs { get; set; } = 30000;

        [JsonProperty("displayHelpText")]
        public bool DisplayHelpText { get; set; } = true;

        [JsonProperty("enableWheelSlowMotion")]
        public bool EnableWheelSlowMotion { get; set; } = true;
    }

    internal sealed class GraphicsSettings {
        [JsonProperty("iconWidth")]
        public int IconWidth { get; set; } = 30;

        [JsonProperty("iconHeight")]
        public int IconHeight { get; set; } = 30;

        [JsonProperty("wheelRadius")]
        public float WheelRadius { get; set; } = 300f;

        [JsonProperty("iconBackgroundColor")]
        public string IconBackgroundColor { get; set; } = "#CC000000";

        [JsonProperty("iconHighlightColor")]
        public string IconHighlightColor { get; set; } = "#FF00CFEE";

        [JsonProperty("backgroundIconSizeMultiplier")]
        public double BackgroundIconSizeMultiplier { get; set; } = 1.35;

        [JsonProperty("highlightIconSizeMultiplier")]
        public double HighlightIconSizeMultiplier { get; set; } = 1.45;
    }

    internal sealed class KeyboardControlSettings {
        [JsonProperty("toggleModifier")]
        [JsonConverter(typeof(StringEnumConverter))]
        public Keys ToggleModifier { get; set; } = Keys.E;

        [JsonProperty("skipTrack")]
        [JsonConverter(typeof(StringEnumConverter))]
        public Control SkipTrack { get; set; } = Control.PhoneRight;

        [JsonProperty("volumeUp")]
        [JsonConverter(typeof(StringEnumConverter))]
        public Control VolumeUp { get; set; } = Control.PhoneUp;

        [JsonProperty("volumeDown")]
        [JsonConverter(typeof(StringEnumConverter))]
        public Control VolumeDown { get; set; } = Control.PhoneDown;
    }

    internal sealed class GamepadControlSettings {
        [JsonProperty("toggleModifier")]
        [JsonConverter(typeof(StringEnumConverter))]
        public Control ToggleModifier { get; set; } = Control.VehicleDuck;

        [JsonProperty("skipTrack")]
        [JsonConverter(typeof(StringEnumConverter))]
        public Control SkipTrack { get; set; } = Control.VehicleHandbrake;

        [JsonProperty("volumeUp")]
        [JsonConverter(typeof(StringEnumConverter))]
        public Control VolumeUp { get; set; } = Control.MoveUpOnly;

        [JsonProperty("volumeDown")]
        [JsonConverter(typeof(StringEnumConverter))]
        public Control VolumeDown { get; set; } = Control.MoveDownOnly;
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
