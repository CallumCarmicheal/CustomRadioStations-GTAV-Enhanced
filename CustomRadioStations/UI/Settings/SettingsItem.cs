using System;
using System.Globalization;

namespace CustomRadioStations.UI.Settings {
    internal abstract class SettingsItem {
        protected SettingsItem(string title, string description) {
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
        }

        internal string Title { get; }
        internal string Description { get; }
        internal virtual bool Enabled => true;
        internal virtual bool Selectable => true;
        internal abstract string ValueText { get; }
        internal virtual bool CanAdjust => false;
        internal virtual bool CanReset => false;
        internal virtual bool IsDefault => true;
        internal virtual string DefaultValueText => string.Empty;
        internal virtual bool IsAction => false;
        internal virtual void Adjust(int direction) { }
        internal virtual void Activate() { }
        internal virtual void Reset() { }
    }

    internal sealed class ToggleSettingsItem : SettingsItem {
        private readonly Func<bool> getter;
        private readonly Action<bool> setter;
        private readonly bool defaultValue;

        internal ToggleSettingsItem(string title, string description, Func<bool> getter, Action<bool> setter, bool defaultValue)
            : base(title, description) {
            this.getter = getter;
            this.setter = setter;
            this.defaultValue = defaultValue;
        }

        internal bool IsOn => getter();
        internal override string ValueText => IsOn ? "ON" : "OFF";
        internal override bool CanAdjust => true;
        internal override bool CanReset => true;
        internal override bool IsDefault => getter() == defaultValue;
        internal override string DefaultValueText => defaultValue ? "ON" : "OFF";
        internal override void Adjust(int direction) => setter(!getter());
        internal override void Activate() => setter(!getter());
        internal override void Reset() => setter(defaultValue);
    }

    internal sealed class ChoiceSettingsItem : SettingsItem {
        private readonly string[] labels;
        private readonly Func<int> getter;
        private readonly Action<int> setter;
        private readonly int defaultIndex;

        internal ChoiceSettingsItem(string title, string description, string[] labels, Func<int> getter,
            Action<int> setter, int defaultIndex) : base(title, description) {
            this.labels = labels ?? new string[0];
            this.getter = getter;
            this.setter = setter;
            this.defaultIndex = defaultIndex;
        }

        internal override string ValueText {
            get {
                if (labels.Length == 0)
                    return string.Empty;
                int index = ClampIndex(getter());
                return labels[index];
            }
        }

        internal override bool CanAdjust => labels.Length > 1;
        internal override bool CanReset => true;
        internal override bool IsDefault => ClampIndex(getter()) == ClampIndex(defaultIndex);
        internal override string DefaultValueText => labels.Length == 0 ? string.Empty : labels[ClampIndex(defaultIndex)];

        internal override void Adjust(int direction) {
            if (labels.Length == 0)
                return;
            int index = ClampIndex(getter());
            index = (index + (direction >= 0 ? 1 : -1) + labels.Length) % labels.Length;
            setter(index);
        }

        internal override void Activate() => Adjust(1);
        internal override void Reset() => setter(ClampIndex(defaultIndex));

        private int ClampIndex(int index) {
            if (labels.Length == 0)
                return 0;
            return Math.Max(0, Math.Min(labels.Length - 1, index));
        }
    }

    internal sealed class SliderSettingsItem : SettingsItem {
        private readonly Func<float> getter;
        private readonly Action<float> setter;
        private readonly float minimum;
        private readonly float maximum;
        private readonly float step;
        private readonly float defaultValue;
        private readonly Func<float, string> formatter;

        internal SliderSettingsItem(string title, string description, Func<float> getter, Action<float> setter,
            float minimum, float maximum, float step, float defaultValue, Func<float, string> formatter = null)
            : base(title, description) {
            this.getter = getter;
            this.setter = setter;
            this.minimum = minimum;
            this.maximum = maximum;
            this.step = Math.Max(0.0001f, step);
            this.defaultValue = Clamp(defaultValue);
            this.formatter = formatter;
        }

        internal override string ValueText {
            get {
                float value = Clamp(getter());
                return formatter != null ? formatter(value) : value.ToString("0.##", CultureInfo.InvariantCulture);
            }
        }

        internal override bool CanAdjust => true;
        internal override bool CanReset => true;
        internal override bool IsDefault => Math.Abs(Clamp(getter()) - defaultValue) <= 0.0001f;
        internal override string DefaultValueText => formatter != null
            ? formatter(defaultValue)
            : defaultValue.ToString("0.##", CultureInfo.InvariantCulture);
        internal float NormalizedValue => maximum <= minimum ? 0f : (Clamp(getter()) - minimum) / (maximum - minimum);

        internal override void Adjust(int direction) {
            float current = Clamp(getter());
            double stepPosition = (current - minimum) / step;
            double targetStep = direction >= 0
                ? Math.Floor(stepPosition + 0.000001d) + 1d
                : Math.Ceiling(stepPosition - 0.000001d) - 1d;
            setter(Clamp(minimum + ((float)targetStep * step)));
        }

        internal void SetNormalized(float normalized) {
            normalized = Math.Max(0f, Math.Min(1f, normalized));
            float raw = minimum + ((maximum - minimum) * normalized);
            float snapped = minimum + ((float)Math.Round((raw - minimum) / step, MidpointRounding.AwayFromZero) * step);
            setter(Clamp(snapped));
        }

        internal override void Reset() => setter(defaultValue);

        private float Clamp(float value) => Math.Max(minimum, Math.Min(maximum, value));
    }

    internal sealed class BindingSettingsItem : SettingsItem {
        private readonly Func<string> value;
        private readonly Action activate;
        private readonly Action reset;
        private readonly Func<bool> isDefault;
        private readonly string defaultValueText;

        internal BindingSettingsItem(string title, string description, Func<string> value, Action activate, Action reset,
            Func<bool> isDefault = null, string defaultValueText = null) : base(title, description) {
            this.value = value;
            this.activate = activate;
            this.reset = reset;
            this.isDefault = isDefault;
            this.defaultValueText = defaultValueText ?? string.Empty;
        }

        internal override string ValueText => value == null ? string.Empty : (value() ?? string.Empty);
        internal override bool CanReset => reset != null;
        internal override bool IsDefault => isDefault == null || isDefault();
        internal override string DefaultValueText => defaultValueText;
        internal override void Activate() => activate?.Invoke();
        internal override void Reset() => reset?.Invoke();
    }

    internal sealed class SectionSettingsItem : SettingsItem {
        internal SectionSettingsItem(string title, string description = "") : base(title, description) { }

        internal override bool Selectable => false;
        internal override string ValueText => string.Empty;
    }

    internal sealed class InfoSettingsItem : SettingsItem {
        private readonly Func<string> value;

        internal InfoSettingsItem(string title, string description, Func<string> value) : base(title, description) {
            this.value = value;
        }

        internal override bool Selectable => false;
        internal override string ValueText => value == null ? string.Empty : (value() ?? string.Empty);
    }

    internal sealed class ActionSettingsItem : SettingsItem {
        private readonly Action action;

        internal ActionSettingsItem(string title, string description, Action action, bool isDestructive = false)
            : base(title, description) {
            this.action = action;
            IsDestructive = isDestructive;
        }

        internal bool IsDestructive { get; }
        internal override string ValueText => ">";
        internal override bool IsAction => true;
        internal override void Activate() => action?.Invoke();
    }
}
