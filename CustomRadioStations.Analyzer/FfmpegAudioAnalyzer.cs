using CustomRadioStations;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CustomRadioStations.Analyzer {
    internal sealed class AudioAnalysisResult {
        internal uint DurationMs { get; set; }
        internal uint AudioStartMs { get; set; }
        internal uint AudioEndMs { get; set; }
        internal double? IntegratedLufs { get; set; }
        internal double? TruePeakDb { get; set; }
    }

    internal sealed class FfmpegAudioAnalyzer {
        private static readonly Regex DurationRegex = new Regex(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex SilenceStartRegex = new Regex(@"silence_start:\s*([0-9]+(?:\.[0-9]+)?)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex SilenceEndRegex = new Regex(@"silence_end:\s*([0-9]+(?:\.[0-9]+)?)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex IntegratedRegex = new Regex(@"^\s*I:\s*(-?[0-9]+(?:\.[0-9]+)?)\s+LUFS", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex PeakRegex = new Regex(@"^\s*Peak:\s*(-?[0-9]+(?:\.[0-9]+)?)\s+dBFS", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly string ffmpegPath;
        private readonly StationAnalysisSettings settings;

        internal FfmpegAudioAnalyzer(string ffmpegPath, StationAnalysisSettings settings) {
            this.ffmpegPath = ffmpegPath;
            this.settings = settings;
        }

        internal Task<AudioAnalysisResult> AnalyzeAsync(string filePath, uint knownDurationMs, uint segmentStartMs, uint segmentEndMs,
            Action<double> progress, CancellationToken cancellationToken) {
            var tcs = new TaskCompletionSource<AudioAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stderr = new List<string>();
            var silenceIntervals = new List<SilenceInterval>();
            object sync = new object();
            double? activeSilenceStart = null;
            uint durationMs = knownDurationMs;
            double? integratedLufs = null;
            double? truePeakDb = null;

            string filter = string.Format(CultureInfo.InvariantCulture,
                "silencedetect=noise={0}dB:d={1},ebur128=peak=true",
                settings.SilenceThresholdDb,
                Math.Max(0, settings.MinimumSilenceMs) / 1000d);

            string seek = segmentStartMs > 0u
                ? "-ss " + Seconds(segmentStartMs) + " "
                : string.Empty;
            string limit = segmentEndMs > segmentStartMs
                ? "-t " + Seconds(segmentEndMs - segmentStartMs) + " "
                : string.Empty;

            var startInfo = new ProcessStartInfo {
                FileName = ffmpegPath,
                Arguments = "-hide_banner -nostdin -progress pipe:1 -nostats " + seek + "-i " + Quote(filePath) + " " + limit +
                    "-vn -af " + Quote(filter) + " -f null -",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            CancellationTokenRegistration registration = default(CancellationTokenRegistration);

            process.OutputDataReceived += (sender, eventArgs) => {
                if (eventArgs.Data == null)
                    return;
                if (eventArgs.Data.StartsWith("out_time_us=", StringComparison.Ordinal)) {
                    long microseconds;
                    if (long.TryParse(eventArgs.Data.Substring("out_time_us=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out microseconds) && durationMs > 0) {
                        double percent = Math.Max(0d, Math.Min(1d, (microseconds / 1000d) / durationMs));
                        if (progress != null)
                            progress(percent);
                    }
                }
            };

            process.ErrorDataReceived += (sender, eventArgs) => {
                string line = eventArgs.Data;
                if (line == null)
                    return;
                lock (sync) {
                    stderr.Add(line);
                    if (stderr.Count > 80)
                        stderr.RemoveAt(0);

                    if (durationMs == 0) {
                        Match durationMatch = DurationRegex.Match(line);
                        if (durationMatch.Success)
                            durationMs = ParseDuration(durationMatch);
                    }

                    Match startMatch = SilenceStartRegex.Match(line);
                    if (startMatch.Success) {
                        double seconds;
                        if (double.TryParse(startMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
                            activeSilenceStart = seconds;
                    }

                    Match endMatch = SilenceEndRegex.Match(line);
                    if (endMatch.Success && activeSilenceStart.HasValue) {
                        double seconds;
                        if (double.TryParse(endMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) {
                            silenceIntervals.Add(new SilenceInterval(activeSilenceStart.Value, seconds));
                            activeSilenceStart = null;
                        }
                    }

                    Match integratedMatch = IntegratedRegex.Match(line);
                    if (integratedMatch.Success) {
                        double value;
                        if (double.TryParse(integratedMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                            integratedLufs = value;
                    }

                    Match peakMatch = PeakRegex.Match(line);
                    if (peakMatch.Success) {
                        double value;
                        if (double.TryParse(peakMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                            truePeakDb = value;
                    }
                }
            };



            try {
                if (!process.Start())
                    throw new InvalidOperationException("FFmpeg did not start.");
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                registration = cancellationToken.Register(() => {
                    try {
                        if (!process.HasExited)
                            process.Kill();
                    } catch { }
                });

                Task.Run(() => {
                    try {
                        process.WaitForExit();
                        registration.Dispose();
                        if (cancellationToken.IsCancellationRequested) {
                            tcs.TrySetCanceled();
                            return;
                        }
                        if (process.ExitCode != 0) {
                            string message;
                            lock (sync)
                                message = string.Join(Environment.NewLine, stderr.ToArray());
                            tcs.TrySetException(new InvalidOperationException("FFmpeg failed for '" + filePath + "'." + Environment.NewLine + message));
                            return;
                        }

                        lock (sync) {
                            if (activeSilenceStart.HasValue && durationMs > 0)
                                silenceIntervals.Add(new SilenceInterval(activeSilenceStart.Value, durationMs / 1000d));
                        }

                        uint audioStart;
                        uint audioEnd;
                        CalculateBounds(durationMs, silenceIntervals, settings, out audioStart, out audioEnd);
                        if (progress != null)
                            progress(1d);
                        tcs.TrySetResult(new AudioAnalysisResult {
                            DurationMs = durationMs,
                            AudioStartMs = audioStart,
                            AudioEndMs = audioEnd,
                            IntegratedLufs = integratedLufs,
                            TruePeakDb = truePeakDb
                        });
                    } catch (Exception ex) {
                        if (cancellationToken.IsCancellationRequested)
                            tcs.TrySetCanceled();
                        else
                            tcs.TrySetException(ex);
                    } finally {
                        registration.Dispose();
                        process.Dispose();
                    }
                });
            } catch (Exception ex) {
                registration.Dispose();
                process.Dispose();
                tcs.TrySetException(ex);
            }

            return tcs.Task;
        }

        private static void CalculateBounds(uint durationMs, IList<SilenceInterval> intervals, StationAnalysisSettings settings,
            out uint audioStartMs, out uint audioEndMs) {
            audioStartMs = 0u;
            audioEndMs = durationMs;
            if (!settings.TrimSilence || durationMs == 0 || intervals == null || intervals.Count == 0)
                return;

            SilenceInterval first = intervals[0];
            if (first.StartSeconds <= 0.05d) {
                long end = (long)Math.Round(first.EndSeconds * 1000d) - settings.PaddingMs;
                audioStartMs = (uint)Math.Max(0L, Math.Min((long)durationMs, end));
            }

            SilenceInterval last = intervals[intervals.Count - 1];
            long lastEndMs = (long)Math.Round(last.EndSeconds * 1000d);
            if (lastEndMs >= (long)durationMs - 250L) {
                long start = (long)Math.Round(last.StartSeconds * 1000d) + settings.PaddingMs;
                audioEndMs = (uint)Math.Max(0L, Math.Min((long)durationMs, start));
            }

            if (audioEndMs <= audioStartMs) {
                audioStartMs = 0u;
                audioEndMs = durationMs;
            }
        }

        private static uint ParseDuration(Match match) {
            double hours = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            double minutes = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            double seconds = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            double milliseconds = ((hours * 60d + minutes) * 60d + seconds) * 1000d;
            return milliseconds >= uint.MaxValue ? uint.MaxValue : (uint)Math.Round(milliseconds);
        }

        private static string Seconds(uint milliseconds) {
            return (milliseconds / 1000d).ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Quote(string value) {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private sealed class SilenceInterval {
            internal SilenceInterval(double startSeconds, double endSeconds) {
                StartSeconds = startSeconds;
                EndSeconds = endSeconds;
            }

            internal double StartSeconds { get; }
            internal double EndSeconds { get; }
        }
    }
}
