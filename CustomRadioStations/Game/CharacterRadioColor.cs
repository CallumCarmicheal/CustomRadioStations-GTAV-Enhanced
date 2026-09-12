using GTA;
using GTA.Native;

using System;
using System.Drawing;

namespace CustomRadioStations {
    internal static class CharacterRadioColor {
        private const uint MichaelModel = 0x0D7114C9;
        private const uint FranklinModel = 0x9B22DBAF;
        private const uint TrevorModel = 0x9B810FA2;

        private const int MichaelHudColor = 143;
        private const int FranklinHudColor = 144;
        private const int TrevorHudColor = 145;

        internal static Color GetCurrent(Color configuredFallback) {
            try {
                Ped player = Game.Player.Character;
                if (player == null || !player.Exists())
                    return configuredFallback;

                uint model = unchecked((uint)player.Model.Hash);
                switch (model) {
                case MichaelModel:
                    return GetHudColor(MichaelHudColor, Color.FromArgb(255, 93, 182, 229));
                case FranklinModel:
                    return GetHudColor(FranklinHudColor, Color.FromArgb(255, 114, 204, 114));
                case TrevorModel:
                    return GetHudColor(TrevorHudColor, Color.FromArgb(255, 255, 163, 87));
                default:
                    return configuredFallback;
                }
            } catch {
                return configuredFallback;
            }
        }

        private static Color GetHudColor(int hudColorIndex, Color fallback) {
            try {
                var red = new OutputArgument();
                var green = new OutputArgument();
                var blue = new OutputArgument();
                var alpha = new OutputArgument();
                Function.Call((Hash)0x7C9C91AB74A0360F, hudColorIndex, red, green, blue, alpha);
                return Color.FromArgb(
                    ClampByte(alpha.GetResult<int>()),
                    ClampByte(red.GetResult<int>()),
                    ClampByte(green.GetResult<int>()),
                    ClampByte(blue.GetResult<int>()));
            } catch {
                return fallback;
            }
        }

        private static int ClampByte(int value) {
            return Math.Max(0, Math.Min(255, value));
        }
    }
}