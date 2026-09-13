using System;

namespace CustomRadioStations {
    public static class LoudnessNormalization {
        public static double CalculateGainDb(double? integratedLufs, double? truePeakDb, StationAnalysisSettings settings) {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (!settings.NormalizeLoudness || !integratedLufs.HasValue || double.IsNaN(integratedLufs.Value) || double.IsInfinity(integratedLufs.Value))
                return 0d;

            double desired = settings.TargetLufs - integratedLufs.Value;
            if (desired > settings.MaxGainDb)
                desired = settings.MaxGainDb;

            if (truePeakDb.HasValue && !double.IsNaN(truePeakDb.Value) && !double.IsInfinity(truePeakDb.Value)) {
                double safePositiveGain = -settings.PeakHeadroomDb - truePeakDb.Value;
                desired = Math.Min(desired, safePositiveGain);
            }
            return desired;
        }

        public static float DbToLinear(double gainDb) {
            double value = Math.Pow(10d, gainDb / 20d);
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
                return 1f;
            return value > float.MaxValue ? float.MaxValue : (float)value;
        }
    }
}
