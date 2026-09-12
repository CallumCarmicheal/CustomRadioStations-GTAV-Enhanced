using System;

namespace CustomRadioStations {
    internal static class RadialSelectionHysteresis {
        internal static bool ShouldSwitch(float currentCenterDegrees, float inputDegrees,
            int itemCount, float hysteresisDegrees) {
            if (itemCount <= 1) return false;

            float halfSector = 180f / itemCount;
            float safeHysteresis = Math.Max(0f, Math.Min(hysteresisDegrees, halfSector * 0.45f));
            return AngularDistance(currentCenterDegrees, inputDegrees) > halfSector + safeHysteresis;
        }

        internal static float AngularDistance(float firstDegrees, float secondDegrees) {
            float difference = Math.Abs(Normalize(firstDegrees) - Normalize(secondDegrees));
            return difference > 180f ? 360f - difference : difference;
        }

        private static float Normalize(float degrees) {
            degrees %= 360f;
            return degrees < 0f ? degrees + 360f : degrees;
        }
    }
}
