using CustomRadioStations;

using System;
using System.Globalization;

namespace CustomRadioStations.Analyzer {
    internal sealed class AnalyzerOptions {
        internal string InputPath { get; private set; }
        internal string FfmpegPath { get; private set; }
        internal int Jobs { get; private set; }
        internal bool Force { get; private set; }
        internal StationAnalysisSettings Settings { get; private set; }

        internal static AnalyzerOptions Parse(string[] args) {
            if (args == null || args.Length == 0)
                throw new ArgumentException("A station folder, station.json, or Custom Radio Stations root folder is required.");

            var options = new AnalyzerOptions {
                Jobs = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2)),
                Settings = new StationAnalysisSettings()
            };

            for (int i = 0; i < args.Length; i++) {
                string arg = args[i];
                if (string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase))
                    throw new HelpRequestedException();
                if (!arg.StartsWith("--", StringComparison.Ordinal)) {
                    if (options.InputPath != null)
                        throw new ArgumentException("Only one input path can be specified.");
                    options.InputPath = arg;
                    continue;
                }

                switch (arg.ToLowerInvariant()) {
                    case "--jobs":
                        options.Jobs = ParseInt(Next(args, ref i, arg), arg, 1, 64);
                        break;
                    case "--ffmpeg":
                        options.FfmpegPath = Next(args, ref i, arg);
                        break;
                    case "--target-lufs":
                        options.Settings.TargetLufs = ParseDouble(Next(args, ref i, arg), arg);
                        break;
                    case "--max-gain-db":
                        options.Settings.MaxGainDb = ParseDouble(Next(args, ref i, arg), arg);
                        break;
                    case "--peak-headroom-db":
                        options.Settings.PeakHeadroomDb = ParseDouble(Next(args, ref i, arg), arg);
                        break;
                    case "--silence-threshold":
                        options.Settings.SilenceThresholdDb = ParseDouble(Next(args, ref i, arg), arg);
                        break;
                    case "--minimum-silence":
                        options.Settings.MinimumSilenceMs = ParseInt(Next(args, ref i, arg), arg, 0, 600000);
                        break;
                    case "--padding":
                        options.Settings.PaddingMs = ParseInt(Next(args, ref i, arg), arg, 0, 60000);
                        break;
                    case "--force":
                        options.Force = true;
                        break;
                    case "--no-trim":
                        options.Settings.TrimSilence = false;
                        break;
                    case "--no-normalize":
                        options.Settings.NormalizeLoudness = false;
                        break;
                    case "--help":
                        throw new HelpRequestedException();
                    default:
                        throw new ArgumentException("Unknown option: " + arg);
                }
            }

            if (string.IsNullOrWhiteSpace(options.InputPath))
                throw new ArgumentException("An input path is required.");
            return options;
        }

        private static string Next(string[] args, ref int index, string option) {
            index++;
            if (index >= args.Length)
                throw new ArgumentException(option + " requires a value.");
            return args[index];
        }

        private static int ParseInt(string value, string option, int min, int max) {
            int parsed;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) || parsed < min || parsed > max)
                throw new ArgumentException(option + " must be between " + min + " and " + max + ".");
            return parsed;
        }

        private static double ParseDouble(string value, string option) {
            double parsed;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) || double.IsNaN(parsed) || double.IsInfinity(parsed))
                throw new ArgumentException(option + " requires a finite numeric value.");
            return parsed;
        }
    }

    internal sealed class HelpRequestedException : Exception {
    }
}
