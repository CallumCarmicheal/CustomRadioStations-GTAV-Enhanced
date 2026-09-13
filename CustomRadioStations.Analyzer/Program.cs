using CustomRadioStations;

using Spectre.Console;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CustomRadioStations.Analyzer {
    internal static class Program {
        private static int Main(string[] args) {
            try {
                return RunAsync(args).GetAwaiter().GetResult();
            } catch (HelpRequestedException) {
                PrintHelp();
                return 0;
            } catch (Exception ex) {
                AnsiConsole.MarkupLine("[red]Error:[/] " + Markup.Escape(ex.Message));
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
                return 1;
            }
        }

        private static async Task<int> RunAsync(string[] args) {
            AnalyzerOptions options = AnalyzerOptions.Parse(args);
            string ffmpeg = FindFfmpeg(options.FfmpegPath);
            if (ffmpeg == null)
                throw new FileNotFoundException("FFmpeg was not found. Put ffmpeg.exe beside the analyzer, add it to PATH, or pass --ffmpeg <path>.");

            IReadOnlyList<string> stationDirectories = DiscoverStations(options.InputPath);
            if (stationDirectories.Count == 0)
                throw new InvalidOperationException("No station.json files were found under: " + options.InputPath);

            using (var cancellation = new CancellationTokenSource()) {
                int cancelCount = 0;
                ConsoleCancelEventHandler handler = (sender, eventArgs) => {
                    cancelCount++;
                    if (cancelCount == 1) {
                        eventArgs.Cancel = true;
                        cancellation.Cancel();
                        AnsiConsole.MarkupLine("\n[yellow]Cancellation requested. Completed track analysis is already saved.[/]");
                    }
                };
                Console.CancelKeyPress += handler;
                try {
                    PrintHeader(options, ffmpeg, stationDirectories.Count);
                    int failed = 0;
                    foreach (string stationDirectory in stationDirectories) {
                        if (cancellation.IsCancellationRequested)
                            break;

                        StationDefinition station;
                        var warnings = new List<string>();
                        if (!StationConfigLoader.TryLoad(stationDirectory, out station, warnings.Add)) {
                            AnsiConsole.MarkupLine("[red]Skipped[/] " + Markup.Escape(stationDirectory));
                            foreach (string warning in warnings)
                                AnsiConsole.MarkupLine("  [grey]" + Markup.Escape(warning) + "[/]");
                            failed++;
                            continue;
                        }

                        AnsiConsole.Write(new Rule("[bold cyan]" + Markup.Escape(station.Name) + "[/]").LeftJustified());
                        foreach (string warning in warnings.Where(value => value.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase)))
                            AnsiConsole.MarkupLine("[grey]" + Markup.Escape(warning) + "[/]");

                        var analyzer = new StationAnalyzer(options, ffmpeg);
                        StationAnalyzerResult result = null;
                        await AnsiConsole.Progress()
                            .AutoClear(false)
                            .HideCompleted(false)
                            .Columns(new ProgressColumn[] {
                                new TaskDescriptionColumn { Alignment = Justify.Left },
                                new ProgressBarColumn(),
                                new PercentageColumn(),
                                new RemainingTimeColumn(),
                                new SpinnerColumn()
                            })
                            .StartAsync(async context => {
                                ProgressTask overall = context.AddTask("[green]Overall[/]", maxValue: 100d);
                                var workerTasks = new Dictionary<int, ProgressTask>();
                                for (int i = 0; i < options.Jobs; i++)
                                    workerTasks[i] = context.AddTask("[grey][[" + (i + 1) + "]][/] idle", maxValue: 100d);

                                result = await analyzer.AnalyzeAsync(station, update => {
                                    overall.Value = update.OverallProgress * 100d;
                                    overall.Description = "[green]Overall[/] " + update.Completed + "/" + update.Total +
                                        "  cached " + update.Cached + "  failed " + update.Failed;
                                    if (update.Worker != null && workerTasks.ContainsKey(update.Worker.WorkerIndex)) {
                                        ProgressTask worker = workerTasks[update.Worker.WorkerIndex];
                                        worker.Value = Math.Max(0d, Math.Min(100d, update.Worker.Progress * 100d));
                                        worker.Description = "[grey][[" + (update.Worker.WorkerIndex + 1) + "]][/] " + Markup.Escape(TrimName(update.Worker.FileName, 58));
                                    }
                                }, cancellation.Token).ConfigureAwait(false);
                                if (result != null && !result.Cancelled)
                                    overall.Value = 100d;
                            });

                        if (result != null) {
                            AnsiConsole.MarkupLine("[green]Saved[/] " + Markup.Escape(Path.Combine(stationDirectory, StationAnalysisLoader.FileName)) +
                                "  [grey](cached " + result.Cached + ", analyzed " + result.Analyzed + ", failed " + result.Failed + ")[/]");
                            if (result.Failures != null) {
                                foreach (string failure in result.Failures)
                                    AnsiConsole.MarkupLine("  [red]Failed:[/] " + Markup.Escape(failure));
                            }
                            failed += result.Failed;
                        }
                    }

                    return cancellation.IsCancellationRequested ? 2 : failed == 0 ? 0 : 1;
                } finally {
                    Console.CancelKeyPress -= handler;
                }
            }
        }

        private static void PrintHeader(AnalyzerOptions options, string ffmpeg, int stationCount) {
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[bold]Custom Radio Stations Audio Analyzer[/]");
            table.AddColumn("");
            table.AddRow("Stations", stationCount.ToString());
            table.AddRow("Workers", options.Jobs.ToString());
            table.AddRow("Target", options.Settings.NormalizeLoudness ? options.Settings.TargetLufs + " LUFS" : "normalization off");
            table.AddRow("Silence trim", options.Settings.TrimSilence ? options.Settings.SilenceThresholdDb + " dB" : "off");
            table.AddRow("FFmpeg", Markup.Escape(ffmpeg));
            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine("[grey]Ctrl+C cancels safely; completed analysis items remain in the sidecar and are reused next run.[/]\n");
        }

        private static IReadOnlyList<string> DiscoverStations(string input) {
            string full = Path.GetFullPath(input);
            if (File.Exists(full)) {
                if (!string.Equals(Path.GetFileName(full), "station.json", StringComparison.OrdinalIgnoreCase))
                    return new string[0];
                return new[] { Path.GetDirectoryName(full) };
            }
            if (!Directory.Exists(full))
                throw new DirectoryNotFoundException(full);
            if (File.Exists(Path.Combine(full, "station.json")))
                return new[] { full };
            return Directory.GetFiles(full, "station.json", SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string FindFfmpeg(string configured) {
            if (!string.IsNullOrWhiteSpace(configured)) {
                string full = Path.GetFullPath(configured);
                return File.Exists(full) ? full : null;
            }

            string beside = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            if (File.Exists(beside))
                return beside;
            beside = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
            if (File.Exists(beside))
                return beside;

            string executable = Environment.OSVersion.Platform == PlatformID.Win32NT ? "ffmpeg.exe" : "ffmpeg";
            foreach (string part in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator)) {
                if (string.IsNullOrWhiteSpace(part))
                    continue;
                try {
                    string candidate = Path.Combine(part.Trim(), executable);
                    if (File.Exists(candidate))
                        return candidate;
                } catch { }
            }
            return null;
        }

        private static string TrimName(string value, int max) {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
                return value ?? string.Empty;
            return value.Substring(0, max - 1) + "…";
        }

        private static void PrintHelp() {
            AnsiConsole.MarkupLine("[bold]CustomRadioStations.Analyzer[/] <station folder | station.json | root folder> [[options]]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  --jobs <n>                 Parallel FFmpeg workers (default: up to 4)");
            AnsiConsole.MarkupLine("  --ffmpeg <path>            Explicit FFmpeg executable");
            AnsiConsole.MarkupLine("  --target-lufs <-16>        Loudness target");
            AnsiConsole.MarkupLine("  --max-gain-db <12>         Maximum positive normalization gain");
            AnsiConsole.MarkupLine("  --peak-headroom-db <0.5>   Peak headroom used when limiting positive gain");
            AnsiConsole.MarkupLine("  --silence-threshold <-50>  Silence threshold in dBFS");
            AnsiConsole.MarkupLine("  --minimum-silence <400>    Minimum edge silence in milliseconds");
            AnsiConsole.MarkupLine("  --padding <75>             Audio kept around detected boundaries, milliseconds");
            AnsiConsole.MarkupLine("  --no-trim                   Disable silence trimming");
            AnsiConsole.MarkupLine("  --no-normalize              Disable loudness gain");
            AnsiConsole.MarkupLine("  --force                     Re-analyze every resolved media file");
        }
    }
}
