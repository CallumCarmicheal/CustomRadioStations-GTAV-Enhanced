using GTA.UI;
using System;

namespace CustomRadioStations.UI.Settings {
    internal struct SettingsLayout {
        internal const float VirtualHeight = 720f;
        internal const float HeaderHeight = 58f;
        internal const float DefaultFooterHeight = 150f;
        internal const float MinimumFooterHeight = 136f;
        internal const float PageHeaderHeight = 56f;
        internal const float RowHeight = 44f;
        internal const float OuterMargin = 40f;
        internal const float MaximumWidth = 1080f;
        internal const int VisibleRows = 9;
        internal const float CompactFooterWidth = 1000f;

        internal static SettingsLayout Create() {
            float screenWidth = Math.Max(960f, Screen.ScaledWidth);
            float safeZoneScale = GetSafeZoneScale();
            float safeZoneMargin = screenWidth * 0.5f * (1f - safeZoneScale);
            float verticalSafeZoneMargin = VirtualHeight * 0.5f * (1f - safeZoneScale);
            float horizontalMargin = Math.Max(OuterMargin, safeZoneMargin);
            float width = Math.Min(MaximumWidth, screenWidth - (horizontalMargin * 2f));

            float fixedHeight = HeaderHeight + PageHeaderHeight + (VisibleRows * RowHeight);
            float safeHeight = VirtualHeight - (verticalSafeZoneMargin * 2f);
            float footerHeight = Math.Max(MinimumFooterHeight,
                Math.Min(DefaultFooterHeight, safeHeight - fixedHeight));
            float height = fixedHeight + footerHeight;
            float left = (screenWidth - width) / 2f;
            float top = Math.Max(verticalSafeZoneMargin, (VirtualHeight - height) / 2f);
            float categoryWidth = Math.Max(205f, width * 0.22f);
            return new SettingsLayout(left, top, width, height, categoryWidth, footerHeight);
        }

        private static float GetSafeZoneScale() {
            int profile = Math.Max(0, Math.Min(10, Screen.SafeZoneSizeProfile));
            return 1f - (profile * 0.01f);
        }

        private SettingsLayout(float left, float top, float width, float height, float categoryWidth, float footerHeight) {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
            CategoryWidth = categoryWidth;
            FooterHeight = footerHeight;
        }

        internal float Left { get; }
        internal float Top { get; }
        internal float Width { get; }
        internal float Height { get; }
        internal float CategoryWidth { get; }
        internal float FooterHeight { get; }
        internal float BodyTop => Top + HeaderHeight;
        internal float BodyHeight => Height - HeaderHeight - FooterHeight;
        internal float FooterTop => Top + Height - FooterHeight;
        internal float ContentLeft => Left + CategoryWidth;
        internal float CategoryTop => BodyTop + PageHeaderHeight;
        internal float ItemsTop => BodyTop + PageHeaderHeight;
        internal float ItemsHeight => VisibleRows * RowHeight;
        internal float ContentWidth => Width - CategoryWidth;
        internal bool UseCompactFooter => Width < CompactFooterWidth || FooterHeight < DefaultFooterHeight;

        internal float ToX(float virtualX) => virtualX / Screen.ScaledWidth;
        internal static float ToY(float virtualY) => virtualY / VirtualHeight;
        internal float ToWidth(float virtualWidth) => virtualWidth / Screen.ScaledWidth;
        internal static float ToHeight(float virtualHeight) => virtualHeight / VirtualHeight;
    }
}
