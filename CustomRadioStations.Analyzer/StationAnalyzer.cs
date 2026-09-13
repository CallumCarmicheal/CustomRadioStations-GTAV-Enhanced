using CustomRadioStations;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CustomRadioStations.Analyzer {
    internal sealed class WorkerProgress {
        internal int WorkerIndex { get; set; }
        internal string FileName { get; set; }
        internal double Progress { get; set; }
    }

    internal sealed class AnalyzerProgress {
        internal int Total { get; set; }
        internal int Completed { get; set; }
        internal int Cached { get; set; }
        internal int Failed { get; set; }
        internal double OverallProgress { get; set; }
        internal WorkerProgress Worker { get; set; }
    }

    internal sealed class StationAnalyzerResult {
        internal int Total { get; set; }
        internal int Cached { get; set; }
        internal int Analyzed { get; set; }
        internal int Failed { get; set; }
        internal bool Cancelled { get; set; }
        internal List<string> Failures { get; set; }
    }

    internal sealed class StationAnalyzer {
        private readonly AnalyzerOptions options;
        private readonly FfmpegAudioAnalyzer analyzer;
        private readonly object sync = new object();
        private readonly Dictionary<int, ActiveWork> active = new Dictionary<int, ActiveWork>();
        private long completedWeight;
        private long totalWeight;
        private int completed;
        private int cached;
        private int failed;
        private readonly List<string> failures = new List<string>();

        internal StationAnalyzer(AnalyzerOptions options, string ffmpegPath) {
            this.options = options;
            analyzer = new FfmpegAudioAnalyzer(ffmpegPath, options.Settings);
        }

        internal async Task<StationAnalyzerResult> AnalyzeAsync(StationDefinition station, Action<AnalyzerProgress> progress, CancellationToken cancellationToken) {
            string stationDirectory = Path.GetDirectoryName(station.ConfigPath);
            StationAnalysis sidecar = StationAnalysisLoader.LoadOrCreate(stationDirectory);
            bool settingsRequireRescan = sidecar.Settings == null || sidecar.Settings.RequiresAudioRescan(options.Settings);
            sidecar.Tracks = sidecar.Tracks == null
                ? new Dictionary<string, TrackAnalysis>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, TrackAnalysis>(sidecar.Tracks, StringComparer.OrdinalIgnoreCase);
            if (settingsRequireRescan)
                sidecar.Tracks.Clear();

            List<ResolvedMediaSource> sources = station.Tracks.Concat(station.Commercials ?? new ResolvedMediaSource[0])
                .GroupBy(source => source.AnalysisKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList();

            var physicalDurations = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            var jobs = new List<AnalysisJob>();
            foreach (ResolvedMediaSource source in sources) {
                string path = Path.GetFullPath(source.FilePath);
                uint physicalDuration = ReadPhysicalDuration(path, physicalDurations);
                uint segmentStart;
                uint segmentEnd;
                GetSegmentBounds(source, physicalDuration, out segmentStart, out segmentEnd);
                uint segmentDuration = segmentEnd > segmentStart ? segmentEnd - segmentStart : physicalDuration;

                TrackAnalysis existing;
                bool fresh = sidecar.Tracks.TryGetValue(source.AnalysisKey, out existing) && existing != null && existing.IsCurrentFor(path);
                if (fresh && !options.Force && !settingsRequireRescan) {
                    SetSourceBounds(existing, source, segmentStart, segmentEnd);
                    existing.GainDb = LoudnessNormalization.CalculateGainDb(existing.IntegratedLufs, existing.TruePeakDb, options.Settings);
                    if (!options.Settings.TrimSilence) {
                        existing.AudioStartMs = segmentStart;
                        existing.AudioEndMs = segmentEnd;
                    }
                    cached++;
                    completed++;
                    long weight = Weight(segmentDuration);
                    completedWeight += weight;
                    totalWeight += weight;
                    continue;
                }

                long jobWeight = Weight(segmentDuration);
                jobs.Add(new AnalysisJob(source, physicalDuration, segmentStart, segmentEnd, segmentDuration, jobWeight));
                totalWeight += jobWeight;
            }

            sidecar.Settings = CloneSettings(options.Settings);
            if (jobs.Count == 0) {
                StationAnalysisLoader.SaveAtomic(stationDirectory, sidecar);
                Report(progress, null, sources.Count);
                return new StationAnalyzerResult { Total = sources.Count, Cached = cached, Analyzed = 0, Failed = 0, Failures = new List<string>() };
            }

            var queue = new ConcurrentQueue<AnalysisJob>(jobs);
            int workerCount = Math.Min(options.Jobs, jobs.Count);
            var workers = new List<Task>();
            for (int workerIndex = 0; workerIndex < workerCount; workerIndex++) {
                int captured = workerIndex;
                workers.Add(Task.Run(() => WorkerAsync(captured, queue, sidecar, stationDirectory, sources.Count, progress, cancellationToken), cancellationToken));
            }

            try {
                await Task.WhenAll(workers).ConfigureAwait(false);
            } catch (OperationCanceledException) {
            }

            return new StationAnalyzerResult {
                Total = sources.Count,
                Cached = cached,
                Analyzed = Math.Max(0, completed - cached),
                Failed = failed,
                Cancelled = cancellationToken.IsCancellationRequested,
                Failures = new List<string>(failures)
            };
        }

        private async Task WorkerAsync(int workerIndex, ConcurrentQueue<AnalysisJob> queue, StationAnalysis sidecar,
            string stationDirectory, int total, Action<AnalyzerProgress> progress, CancellationToken cancellationToken) {
            AnalysisJob job;
            while (!cancellationToken.IsCancellationRequested && queue.TryDequeue(out job)) {
                SetActive(workerIndex, job, 0d, progress, total);
                try {
                    AudioAnalysisResult result = await analyzer.AnalyzeAsync(job.Source.FilePath, job.SegmentDurationMs,
                        job.SegmentStartMs, job.SegmentEndMs,
                        value => SetActive(workerIndex, job, value, progress, total), cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();

                    var info = new FileInfo(job.Source.FilePath);
                    uint detectedStart = options.Settings.TrimSilence
                        ? SafeAdd(job.SegmentStartMs, result.AudioStartMs)
                        : job.SegmentStartMs;
                    uint detectedEnd = options.Settings.TrimSilence
                        ? SafeAdd(job.SegmentStartMs, result.AudioEndMs)
                        : job.SegmentEndMs;
                    detectedStart = Math.Max(job.SegmentStartMs, Math.Min(detectedStart, job.SegmentEndMs));
                    detectedEnd = Math.Max(detectedStart, Math.Min(detectedEnd, job.SegmentEndMs));

                    var track = new TrackAnalysis {
                        FileSize = info.Length,
                        LastWriteUtc = info.LastWriteTimeUtc,
                        DurationMs = job.PhysicalDurationMs,
                        SourceStartMs = job.Source.IsSegment ? (uint?)job.SegmentStartMs : null,
                        SourceEndMs = job.Source.IsSegment ? (uint?)job.SegmentEndMs : null,
                        AudioStartMs = detectedStart,
                        AudioEndMs = detectedEnd,
                        IntegratedLufs = result.IntegratedLufs,
                        TruePeakDb = result.TruePeakDb,
                        GainDb = LoudnessNormalization.CalculateGainDb(result.IntegratedLufs, result.TruePeakDb, options.Settings),
                        AnalyzedUtc = DateTime.UtcNow
                    };

                    lock (sync) {
                        sidecar.Tracks[job.Source.AnalysisKey] = track;
                        sidecar.Settings = CloneSettings(options.Settings);
                        StationAnalysisLoader.SaveAtomic(stationDirectory, sidecar);
                        completed++;
                        completedWeight += job.Weight;
                        active.Remove(workerIndex);
                    }
                    Report(progress, new WorkerProgress { WorkerIndex = workerIndex, FileName = job.DisplayName, Progress = 1d }, total);
                } catch (OperationCanceledException) {
                    lock (sync)
                        active.Remove(workerIndex);
                    return;
                } catch (Exception ex) {
                    lock (sync) {
                        failed++;
                        failures.Add(job.DisplayName + ": " + ex.Message);
                        completed++;
                        completedWeight += job.Weight;
                        active.Remove(workerIndex);
                    }
                    Report(progress, new WorkerProgress { WorkerIndex = workerIndex, FileName = job.DisplayName, Progress = 1d }, total);
                }
            }
        }

        private void SetActive(int workerIndex, AnalysisJob job, double value, Action<AnalyzerProgress> progress, int total) {
            lock (sync)
                active[workerIndex] = new ActiveWork(job, Math.Max(0d, Math.Min(1d, value)));
            Report(progress, new WorkerProgress {
                WorkerIndex = workerIndex,
                FileName = job.DisplayName,
                Progress = value
            }, total);
        }

        private void Report(Action<AnalyzerProgress> progress, WorkerProgress worker, int total) {
            if (progress == null)
                return;
            lock (sync) {
                double activeWeight = active.Values.Sum(item => item.Job.Weight * item.Progress);
                double overall = totalWeight <= 0 ? 1d : Math.Min(1d, (completedWeight + activeWeight) / totalWeight);
                progress(new AnalyzerProgress {
                    Total = total,
                    Completed = completed,
                    Cached = cached,
                    Failed = failed,
                    OverallProgress = overall,
                    Worker = worker
                });
            }
        }

        private static uint ReadPhysicalDuration(string path, IDictionary<string, uint> cache) {
            uint duration;
            if (cache.TryGetValue(path, out duration))
                return duration;
            duration = TrackMetadataReader.ReadMetadata(path).DurationMs;
            cache[path] = duration;
            return duration;
        }

        private static void GetSegmentBounds(ResolvedMediaSource source, uint physicalDuration, out uint start, out uint end) {
            start = source.StartMs ?? 0u;
            end = source.EndMs ?? physicalDuration;
            if (physicalDuration > 0u) {
                start = Math.Min(start, physicalDuration - 1u);
                end = Math.Min(end, physicalDuration);
            }
            if (end <= start) {
                start = 0u;
                end = physicalDuration;
            }
        }

        private static void SetSourceBounds(TrackAnalysis analysis, ResolvedMediaSource source, uint segmentStartMs, uint segmentEndMs) {
            if (analysis == null)
                return;
            analysis.SourceStartMs = source != null && source.IsSegment ? (uint?)segmentStartMs : null;
            analysis.SourceEndMs = source != null && source.IsSegment ? (uint?)segmentEndMs : null;
        }

        private static uint SafeAdd(uint left, uint right) {
            ulong value = (ulong)left + right;
            return value >= uint.MaxValue ? uint.MaxValue : (uint)value;
        }

        private static long Weight(uint durationMs) {
            return durationMs > 0 ? durationMs : 1L;
        }

        private static StationAnalysisSettings CloneSettings(StationAnalysisSettings settings) {
            return new StationAnalysisSettings {
                SilenceThresholdDb = settings.SilenceThresholdDb,
                MinimumSilenceMs = settings.MinimumSilenceMs,
                PaddingMs = settings.PaddingMs,
                TargetLufs = settings.TargetLufs,
                PeakHeadroomDb = settings.PeakHeadroomDb,
                MaxGainDb = settings.MaxGainDb,
                TrimSilence = settings.TrimSilence,
                NormalizeLoudness = settings.NormalizeLoudness
            };
        }

        private sealed class AnalysisJob {
            internal AnalysisJob(ResolvedMediaSource source, uint physicalDurationMs, uint segmentStartMs,
                uint segmentEndMs, uint segmentDurationMs, long weight) {
                Source = source;
                PhysicalDurationMs = physicalDurationMs;
                SegmentStartMs = segmentStartMs;
                SegmentEndMs = segmentEndMs;
                SegmentDurationMs = segmentDurationMs;
                Weight = weight;
                string segment = source.IsSegment
                    ? " [" + FormatTime(segmentStartMs) + "-" + FormatTime(segmentEndMs) + "]"
                    : string.Empty;
                DisplayName = Path.GetFileName(source.FilePath) + segment;
            }

            internal ResolvedMediaSource Source { get; }
            internal uint PhysicalDurationMs { get; }
            internal uint SegmentStartMs { get; }
            internal uint SegmentEndMs { get; }
            internal uint SegmentDurationMs { get; }
            internal long Weight { get; }
            internal string DisplayName { get; }

            private static string FormatTime(uint milliseconds) {
                TimeSpan time = TimeSpan.FromMilliseconds(milliseconds);
                return time.TotalHours >= 1d
                    ? ((int)time.TotalHours) + ":" + time.Minutes.ToString("00") + ":" + time.Seconds.ToString("00")
                    : ((int)time.TotalMinutes) + ":" + time.Seconds.ToString("00");
            }
        }

        private sealed class ActiveWork {
            internal ActiveWork(AnalysisJob job, double progress) {
                Job = job;
                Progress = progress;
            }

            internal AnalysisJob Job { get; }
            internal double Progress { get; }
        }
    }
}
