using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace GlitchStich;

public partial class MainWindow
{
    private int _exportCpuBudget = 2;
    private (string Quality, bool LowImpact)? ChooseExportSettings()
    {
        var window = new Window { Owner = this, Title = "Export quality", Width = 470, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, WindowStyle = WindowStyle.None, Background = Brush("#0B1520"), Foreground = Brush("#EAF5FF"), ShowInTaskbar = false };
        var frame = new Border { BorderBrush = Brush("#51D9E4"), BorderThickness = new Thickness(1), Padding = new Thickness(18) }; CyberShape.SetCut(frame, 16); window.Content = frame;
        var panel = new StackPanel(); frame.Child = panel;
        panel.Children.Add(new TextBlock { Text = "EXPORT // QUALITY & RESOURCES", Foreground = Brush("#F8D35C"), FontSize = 14, Margin = new Thickness(0, 0, 0, 14) });
        var quality = new ComboBox { ItemsSource = new[] { "High", "Balanced", "Maximum" }, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 10) }; panel.Children.Add(quality);
        panel.Children.Add(new TextBlock { Text = "High: source resolution and frame rate.\nBalanced: up to 1080p / 60 fps, smaller files.\nMaximum: source size, less compression, larger files.\nNo setting can restore detail absent from the source.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var low = new CheckBox { Content = "Low impact · fewer CPU workers", Margin = new Thickness(0, 0, 0, 10) }; panel.Children.Add(low);
        panel.Children.Add(new TextBlock { Text = "Automatic hardware encoding when supported; CPU fallback otherwise. Glitches process one at a time. Remaining time is estimated from actual export progress. Audible tracks become one stereo mix with your volume/mute settings.", TextWrapping = TextWrapping.Wrap, Foreground = Brush("#9FBBC9"), FontSize = 11 });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) }; cancel.Click += (_, _) => window.Close(); buttons.Children.Add(cancel);
        var start = new Button { Content = "Export" }; start.Click += (_, _) => window.DialogResult = true; buttons.Children.Add(start); panel.Children.Add(buttons);
        return window.ShowDialog() == true ? ((string)quality.SelectedItem, low.IsChecked == true) : null;
    }

    private async Task ExportEditedVideoAsync(string destination, IReadOnlyList<ExportSegment> segments)
    {
        if (string.Equals(Path.GetFullPath(destination), Path.GetFullPath(_videoPath!), StringComparison.OrdinalIgnoreCase) || _editingAssets.Any(asset => string.Equals(Path.GetFullPath(destination), Path.GetFullPath(asset.Path), StringComparison.OrdinalIgnoreCase)))
        { StatusText.Text = "Export to a different file; the original recording cannot be overwritten."; return; }
        var settings = ChooseExportSettings(); if (settings is null) return;
        PauseDraftPreview(); SetBusy(true, "Checking export hardware…");
        using var cancellation = new CancellationTokenSource();
        var progressWindow = new Window { Owner = this, Title = "Exporting GlitchStich", Width = 460, SizeToContent = SizeToContent.Height, WindowStyle = WindowStyle.None, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush("#0B1520"), Foreground = Brush("#EAF5FF"), ShowInTaskbar = false };
        var panel = new StackPanel { Margin = new Thickness(18) }; progressWindow.Content = panel;
        var detail = new TextBlock { Text = "Checking supported encoders…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) }; panel.Children.Add(detail);
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 10, Foreground = Brush("#51D9E4"), Margin = new Thickness(0, 0, 0, 12) }; panel.Children.Add(bar);
        var cancel = new Button { Content = "Cancel export" }; cancel.Click += (_, _) => { cancellation.Cancel(); cancel.IsEnabled = false; detail.Text = "Canceling export…"; }; panel.Children.Add(cancel);
        bool finished = false; bool exportSaved = false;
        progressWindow.Closing += (_, args) => { if (!finished) { cancellation.Cancel(); args.Cancel = true; } };
        progressWindow.Show();
        var folder = Path.Combine(_settingsDirectory, "ExportCache", Guid.NewGuid().ToString("N"));
        var partial = destination + $".{Guid.NewGuid():N}.partial.mp4";
        try
        {
            Directory.CreateDirectory(folder);
            await CheckDuplicateAudioAsync(cancellation.Token); BuildTimeline();
            var threads = ExportOptions.WorkerBudget(Environment.ProcessorCount, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, settings.Value.LowImpact);
            _exportCpuBudget = threads;
            var metadata = await ProbeEditingMediaAsync(_videoPath!, cancellation.Token);
            if (!metadata.Video || metadata.Width <= 0 || metadata.Height <= 0) throw new InvalidDataException("The source video dimensions could not be read.");
            var scale = settings.Value.Quality == "Balanced" ? Math.Min(1, Math.Min(1920d / metadata.Width, 1080d / metadata.Height)) : 1;
            var canvasWidth = Math.Max(2, (int)(metadata.Width * scale) / 2 * 2); var canvasHeight = Math.Max(2, (int)(metadata.Height * scale) / 2 * 2);
            var clips = segments.Select(segment => new EditingClip(segment.Start, segment.End, 0, segment.Effects ?? new())).ToArray();
            var placements = MediaRenderPlan.Resolve(clips, _editingAssets, _timedMedia, _durationSeconds);
            var encoder = "libx264";
            foreach (var candidate in new[] { "h264_nvenc", "h264_qsv", "h264_amf" })
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token); timeout.CancelAfter(TimeSpan.FromSeconds(8));
                try
                {
                    var probe = new List<string> { "-v", "error", "-f", "lavfi", "-i", "color=s=320x180:r=30:d=0.2", "-frames:v", "6" };
                    probe.AddRange(new ExportOptions(settings.Value.Quality, candidate, threads).VideoArguments); probe.AddRange(["-f", "null", "-"]);
                    await RunExportProcessAsync(probe, timeout.Token, _ => { }); encoder = candidate; break;
                }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { }
                catch (InvalidOperationException) { }
            }
            var tracks = GetExportAudio(); var total = segments.Sum(segment => segment.Duration);
            var timer = Stopwatch.StartNew();
            for (var attempt = 0; ; attempt++)
            {
                double complete = 0; timer.Restart();
                try
                {
                    for (var index = 0; index < segments.Count; index++)
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        var segment = segments[index]; var length = segment.Duration; var prior = complete;
                        var options = new ExportOptions(settings.Value.Quality, encoder, threads, HardwareDecode: encoder == "h264_nvenc", Intermediate: true) { CanvasWidth = canvasWidth, CanvasHeight = canvasHeight };
                        var args = ExportPlan.Build(_videoPath!, Path.Combine(folder, $"part_{index:000000}.mkv"), [segment], tracks, options);
                        await RunExportProcessAsync(args, cancellation.Token, seconds =>
                        {
                            var done = prior + Math.Clamp(seconds, 0, length); bar.Value = done / total * 100;
                            ReportTaskProgress("Video export (seconds)", done, total);
                            var eta = done > 2 && timer.Elapsed.TotalSeconds > 1 ? $" · about {FormatTime((total - done) * timer.Elapsed.TotalSeconds / done)} left" : " · estimating time";
                            detail.Text = $"{settings.Value.Quality} · {encoder} · {threads} CPU worker budget\nClip {index + 1}/{segments.Count} · {bar.Value:0}%{eta}";
                            StatusText.Text = detail.Text.Replace('\n', ' ');
                        });
                        complete += length;
                    }
                    break;
                }
                catch (InvalidOperationException) when (encoder != "libx264" && attempt == 0)
                { encoder = "libx264"; detail.Text = "Hardware encoder could not handle this source. Restarting with the bounded CPU fallback…"; }
            }
            var manifest = Path.Combine(folder, "join.txt");
            await File.WriteAllLinesAsync(manifest, Enumerable.Range(0, segments.Count).Select(index => $"file 'part_{index:000000}.mkv'"), cancellation.Token);
            detail.Text = "Finalizing the joined video…";
            var joined = placements.Count == 0 ? partial : Path.Combine(folder, "joined.mp4");
            await RunExportProcessAsync(["-y", "-v", "error", "-f", "concat", "-safe", "0", "-i", manifest, "-c:v", "copy", "-c:a", "aac", "-b:a", "320k", "-ar", "48000", "-ac", "2", "-threads", "2", "-movflags", "+faststart", joined], cancellation.Token, seconds => ReportTaskProgress("Finalizing (seconds)", seconds, total));
            if (placements.Count > 0)
            {
                detail.Text = "Rendering timed media and captions…"; var captions = await WriteCaptionFilesAsync(folder, placements, cancellation.Token);
                await RunExportProcessAsync(MediaRenderPlan.Build(joined, partial, placements, canvasWidth, canvasHeight, total, tracks.Any(track => !track.Muted && track.Volume > 0), captions, new(settings.Value.Quality, encoder, threads)), cancellation.Token, seconds => { bar.Value = Math.Clamp(seconds / total * 100, 0, 100); ReportTaskProgress("Media and captions (seconds)", seconds, total); });
            }
            cancellation.Token.ThrowIfCancellationRequested(); File.Move(partial, destination, true); exportSaved = true;
            StatusText.Text = $"Exported {settings.Value.Quality} video with one mixed audio stream: {destination}";
            if (YouTube.Settings.AutoUpload)
            {
                detail.Text = "Export saved · uploading to YouTube…";
                var uploadProgress = new Progress<(long Sent, long Total)>(value => { bar.Value = value.Total == 0 ? 0 : value.Sent * 100d / value.Total; detail.Text = $"Export saved · YouTube upload {bar.Value:0}%"; ReportTaskProgress("YouTube upload (bytes)", value.Sent, value.Total); });
                var url = await UploadAfterExportAsync(destination, uploadProgress, cancellation.Token); StatusText.Text = "Export saved · uploaded: " + url;
            }
        }
        catch (OperationCanceledException) { StatusText.Text = exportSaved ? "Export saved locally; YouTube upload canceled." : "Export canceled. Existing destination and source files were preserved."; }
        catch (Exception ex) { StatusText.Text = exportSaved ? $"Export saved locally; YouTube upload failed: {ex.Message}" : $"Export failed: {ex.Message}"; }
        finally
        {
            finished = true; progressWindow.Close();
            BuildTimeline();
            try { if (File.Exists(partial)) File.Delete(partial); } catch { }
            var root = Path.GetFullPath(Path.Combine(_settingsDirectory, "ExportCache")) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(folder).StartsWith(root, StringComparison.OrdinalIgnoreCase)) try { Directory.Delete(folder, true); } catch { }
            SetBusy(false);
        }
    }

    private async Task RunExportProcessAsync(IEnumerable<string> args, CancellationToken cancellation, Action<double> progress)
    {
        var start = new ProcessStartInfo(_ffmpegPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-progress", "pipe:1", "-nostats" }.Concat(args)) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
        if (OperatingSystem.IsWindows() && Environment.ProcessorCount <= 64)
        {
            try
            {
                var available = unchecked((ulong)process.ProcessorAffinity.ToInt64()); ulong mask = 0; var count = 0;
                for (var bit = 0; bit < 64 && count < _exportCpuBudget; bit++)
                    if ((available & (1UL << bit)) != 0) { mask |= 1UL << bit; count++; }
                if (mask != 0) process.ProcessorAffinity = new IntPtr(unchecked((long)mask));
            }
            catch { /* Priority and bounded encoder/filter workers remain the fallback. */ }
        }
        using var registration = cancellation.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        var errors = process.StandardError.ReadToEndAsync();
        while (await process.StandardOutput.ReadLineAsync() is { } line)
            if (line.StartsWith("out_time_us=", StringComparison.Ordinal) && TryProgressNumber(line[12..], out var micros)) progress(micros / 1_000_000);
        await process.WaitForExitAsync(); var error = await errors; cancellation.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new InvalidOperationException(error.Length > 1000 ? error[^1000..] : error);
    }
}
