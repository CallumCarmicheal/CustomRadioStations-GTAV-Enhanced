using GTA;

using GTAVFunctions;

using SelectorWheel;

using System;
using System.Drawing;
using System.Globalization;

using Font = GTA.UI.Font;

namespace CustomRadioStations {
    internal static class TrackRatingWheelOverlay {
        private const float LabelFontSize = 0.31f;
        private const float StarFontSize = 0.34f;
        internal const Control DecreaseControl = Control.FrontendX;
        internal const Control IncreaseControl = Control.FrontendY;

        internal static void Draw(TrackRatingTarget target, float y) {
            float rating = TrackRatingStore.GetRating(target);
            float rowY = y + UIHelper.YPixelToPercentage(4);
            float centerX = 0.5f;

            DrawNativeCentered("RATE", centerX - UIHelper.XPixelToPercentage(128), rowY, LabelFontSize, Color.White);
            DrawNativeCentered(GTAFunction.InputString(DecreaseControl),
                centerX - UIHelper.XPixelToPercentage(82), rowY, LabelFontSize, Color.White);

            Color white = Color.FromArgb(255, 255, 255, 255);
            Color shadow = Color.FromArgb(255, 0, 0, 0);
            float starY = rowY - UIHelper.YPixelToPercentage(1);
            if (!UnicodeTextRenderer.TryDrawRatingStars(rating, StarFontSize, Font.ChaletComprimeCologne,
                white, shadow, centerX, starY, UnicodeTextAlignment.Center)) {
                // The vector renderer avoids relying on an obscure Unicode half-star glyph
                // which is missing from many Windows 7/Wine fonts. Keep the older text path
                // as a secondary fallback, then ASCII if texture rendering is unavailable.
                string stars = FormatStars(rating);
                if (!UnicodeTextRenderer.TryDraw(stars, StarFontSize, Font.ChaletComprimeCologne,
                    white, shadow, centerX, starY, UnicodeTextAlignment.Center))
                    DrawNativeCentered(FormatAsciiStars(rating), centerX, rowY, LabelFontSize, white);
            }

            DrawNativeCentered(GTAFunction.InputString(IncreaseControl),
                centerX + UIHelper.XPixelToPercentage(82), rowY, LabelFontSize, Color.White);
            string ratingText = FormatRating(rating) + (TrackRatingStore.LastSaveFailed ? " !" : string.Empty);
            DrawNativeCentered(ratingText,
                centerX + UIHelper.XPixelToPercentage(137), rowY, LabelFontSize,
                TrackRatingStore.LastSaveFailed ? Color.FromArgb(255, 255, 110, 110) : Color.White);
        }

        internal static string FormatStars(float rating) {
            int halfUnits = (int)Math.Round(TrackRatingStore.NormalizeRating(rating) * 2f, MidpointRounding.AwayFromZero);
            var characters = new char[5];
            for (int index = 0; index < characters.Length; index++) {
                if (halfUnits >= 2) {
                    characters[index] = '\u2605'; // BLACK STAR
                    halfUnits -= 2;
                } else if (halfUnits == 1) {
                    characters[index] = '\u2BEA'; // STAR WITH LEFT HALF BLACK
                    halfUnits = 0;
                } else {
                    characters[index] = '\u2606'; // WHITE STAR
                }
            }
            return new string(characters);
        }

        private static string FormatAsciiStars(float rating) {
            int halfUnits = (int)Math.Round(TrackRatingStore.NormalizeRating(rating) * 2f, MidpointRounding.AwayFromZero);
            var characters = new char[5];
            for (int index = 0; index < characters.Length; index++) {
                if (halfUnits >= 2) {
                    characters[index] = '*';
                    halfUnits -= 2;
                } else if (halfUnits == 1) {
                    characters[index] = '+';
                    halfUnits = 0;
                } else {
                    characters[index] = '-';
                }
            }
            return new string(characters);
        }

        private static string FormatRating(float rating) {
            float normalized = TrackRatingStore.NormalizeRating(rating);
            return normalized <= 0f
                ? "UNRATED"
                : normalized.ToString("0.0", CultureInfo.InvariantCulture) + " / 5";
        }

        private static void DrawNativeCentered(string text, float x, float y, float fontSize, Color color) {
            UIHelper.DrawNativeText(text, fontSize, Font.ChaletComprimeCologne,
                color.R, color.G, color.B, color.A, x, y,
                2, 0, 0, 0, 255, UIHelper.TextJustification.Center);
        }
    }
}
