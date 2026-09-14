using System.Collections.Generic;

namespace CustomRadioStations.UI.Settings {
    internal sealed class SettingsPage {
        internal SettingsPage(string title, params SettingsItem[] items)
            : this(title, string.Empty, items) { }

        internal SettingsPage(string title, string subtitle, params SettingsItem[] items) {
            Title = title ?? string.Empty;
            Subtitle = subtitle ?? string.Empty;
            Items = new List<SettingsItem>(items ?? new SettingsItem[0]);
        }

        internal string Title { get; }
        internal string Subtitle { get; }
        internal List<SettingsItem> Items { get; }
        internal int SelectedIndex { get; set; }
        internal int ScrollOffset { get; set; }

        internal SettingsItem SelectedItem {
            get {
                if (Items.Count == 0)
                    return null;
                SelectedIndex = System.Math.Max(0, System.Math.Min(Items.Count - 1, SelectedIndex));
                return Items[SelectedIndex];
            }
        }
    }
}
