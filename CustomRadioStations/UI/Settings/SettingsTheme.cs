using System.Drawing;

namespace CustomRadioStations.UI.Settings {
    internal static class SettingsTheme {
        internal static Color Accent {
            get {
                Color color = CharacterRadioColor.GetCurrent(Config.IconHL);
                return Color.FromArgb(255, color.R, color.G, color.B);
            }
        }

        internal static Color Backdrop => Color.FromArgb(94, 0, 0, 0);
        internal static Color Panel => Color.FromArgb(236, 13, 13, 16);
        internal static Color Header => Color.FromArgb(248, 22, 22, 27);
        internal static Color Rail => Color.FromArgb(235, 9, 9, 12);
        internal static Color Footer => Color.FromArgb(248, 18, 18, 22);
        internal static Color PageHeader => Color.FromArgb(235, 17, 17, 21);
        internal static Color SelectedRow => Color.FromArgb(220, 47, 47, 54);
        internal static Color HoverRow => Color.FromArgb(160, 35, 35, 41);
        internal static Color ValueSurface => Color.FromArgb(128, 42, 42, 49);
        internal static Color ValueSurfaceHover => Color.FromArgb(180, 54, 54, 62);
        internal static Color ScrollTrack => Color.FromArgb(54, 255, 255, 255);
        internal static Color PrimaryText => Color.FromArgb(255, 245, 245, 248);
        internal static Color SecondaryText => Color.FromArgb(215, 202, 202, 210);
        internal static Color MutedText => Color.FromArgb(150, 190, 190, 198);
        internal static Color Divider => Color.FromArgb(72, 255, 255, 255);
        internal static Color Danger => Color.FromArgb(255, 224, 78, 78);
        internal static Color DangerSurface => Color.FromArgb(145, 90, 31, 34);
        internal static Color DangerSurfaceHover => Color.FromArgb(215, 130, 43, 47);

        internal static Color Alpha(Color color, int alpha) {
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }
    }
}
