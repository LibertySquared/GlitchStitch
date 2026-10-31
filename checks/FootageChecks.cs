using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using GlitchStich;

internal static class FootageChecks
{
    private static readonly string Project = @"D:\Editor\GlitchStich";
    private static string Ffmpeg => Path.Combine(Project, "tools", "ffmpeg.exe");
    private static string Probe => Path.Combine(Project, "tools", "ffprobe.exe");
    private static string N(double n) => n.ToString("0.######", CultureInfo.InvariantCulture);
    private static (string Output, double Seconds) Run(string tool, IEnumerable<string> args)
    {
        var start = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        var clock = Stopwatch.StartNew();
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(180_000)) { process.Kill(true); throw new TimeoutException("Processing exceeded three minutes."); }
        Task.WaitAll(stdout, stderr);
        if (process.ExitCode != 0) throw new InvalidOperationException(stderr.Result);
        if (!string.IsNullOrWhiteSpace(stderr.Result)) throw new InvalidOperationException("Media tool reported errors: " + stderr.Result);
        return (stdout.Result, clock.Elapsed.TotalSeconds);
    }

    public static void Run(string inventoryPath, bool useProvidedSelection = false)
    {
        using var inventory = JsonDocument.Parse(File.ReadAllText(inventoryPath));
        var all = inventory.RootElement.EnumerateArray().Where(e => !e.TryGetProperty("error", out _)).ToArray();
        var selected = new List<JsonElement>();
        if (useProvidedSelection) selected.AddRange(all);
        foreach (var pattern in useProvidedSelection ? Array.Empty<string>() : new[] { "Fortnite 2026.10.05 - 22.34", "Fortnite 2026.10.12 - 22.56", "MedalTVFortnite202610191152", "MedalTVGearsofWar", "12.40.24.06.Double" })
        {
            var entry = all.FirstOrDefault(e => e.GetProperty("path").GetString()!.Contains(pattern, StringComparison.OrdinalIgnoreCase));
            if (entry.ValueKind != JsonValueKind.Undefined) selected.Add(entry);
        }
        var outputRoot = Path.Combine(Project, "checks", "results", useProvidedSelection ? "new-footage" : "footage");
        Directory.CreateDirectory(outputRoot);
        var reports = new List<object>();
        var failed = false;
        for (var index = 0; index < selected.Count; index++)
        {
            var entry = selected[index]; var source = entry.GetProperty("path").GetString()!;
            var before = new FileInfo(source); var originalSize = before.Length; var originalTime = before.LastWriteTimeUtc;
            var folder = Path.Combine(outputRoot, $"sample-{index + 1}"); Directory.CreateDirectory(folder);
            Console.WriteLine($"TEST {index + 1}/{selected.Count}: {Path.GetFileName(source)}");
            try
            {
                var duration = entry.GetProperty("duration").GetDouble();
                var start = duration > 120 ? Math.Min(300, duration / 3) : 0;
                var audio = entry.GetProperty("audio").EnumerateArray().Select(a => a.GetProperty("index").GetInt32()).ToArray();
                var decode = Run(Ffmpeg, ["-v", "error", "-ss", N(start), "-i", source, "-t", "12", "-map", "0:v:0", "-an", "-f", "null", "-"]);
                var wavs = new List<string>();
                foreach (var stream in audio)
                {
                    var wav = Path.Combine(folder, $"audio-{stream}.wav");
                    Run(Ffmpeg, ["-y", "-v", "error", "-ss", N(start), "-i", source, "-t", "12", "-map", $"0:{stream}", "-vn", "-c:a", "pcm_s16le", wav]);
                    wavs.Add(wav);
                }
                var proxy = Path.Combine(folder, "preview.mp4");
                var conversion = Run(Ffmpeg, ["-y", "-v", "error", "-hwaccel", "cuda", "-ss", N(start), "-i", source, "-t", "12", "-map", "0:v:0", "-map", "0:a:0?", "-vf", "scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2,fps=30,setsar=1", "-c:v", "h264_nvenc", "-preset", "p2", "-rc", "vbr", "-cq", "28", "-b:v", "0", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "96k", "-ac", "2", proxy]);
                var metadata = entry.GetProperty("audio").EnumerateArray().ToArray();
                var names = metadata.Select((track, i) => AudioTrackPolicy.ReadLabel(track, i + 1)).ToArray();
                var tracks = metadata.Where((track, i) => !AudioTrackPolicy.IsRedundantCombined(names[i], names))
                    .Select(track => new ExportAudio(track.GetProperty("index").GetInt32(), null, .8, false)).ToArray();
                var clip = Path.Combine(folder, "clip.mp4");
                Run(Ffmpeg, ExportPlan.Build(source, clip, [new(start, start + 2)], tracks));
                var edited = Path.Combine(folder, "timeline.mp4");
                Run(Ffmpeg, ExportPlan.Build(source, edited, [new(start, start + 1), new(start + 3, start + 4)], tracks.TakeLast(1).ToArray()));
                foreach (var pair in new[] { (clip, Math.Min(tracks.Length, 1)), (edited, Math.Min(tracks.Length, 1)) })
                {
                    using var p = JsonDocument.Parse(Run(Probe, ["-v", "error", "-show_streams", "-show_format", "-of", "json", pair.Item1]).Output);
                    var count = p.RootElement.GetProperty("streams").EnumerateArray().Count(s => s.GetProperty("codec_type").GetString() == "audio");
                    var length = double.Parse(p.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
                    if (count != pair.Item2 || Math.Abs(length - 2) > .2) throw new Exception("Export track count or duration mismatch.");
                    Run(Ffmpeg, ["-v", "error", "-i", pair.Item1, "-map", "0", "-f", "null", "-"]);
                }
                var timestamps = Run(Probe, ["-v", "error", "-read_intervals", $"{N(start)}%+5", "-select_streams", "v:0", "-show_entries", "frame=best_effort_timestamp_time", "-of", "json", source]).Output;
                using var frames = JsonDocument.Parse(timestamps);
                var times = frames.RootElement.GetProperty("frames").EnumerateArray().Where(f => f.TryGetProperty("best_effort_timestamp_time", out _)).Select(f => double.Parse(f.GetProperty("best_effort_timestamp_time").GetString()!, CultureInfo.InvariantCulture)).ToArray();
                var gaps = times.Zip(times.Skip(1), (a,b) => b-a).ToArray();
                before.Refresh();
                if (before.Length != originalSize || before.LastWriteTimeUtc != originalTime) throw new Exception("Source metadata changed during test.");
                reports.Add(new { source, status = "PASS", audioTracks = audio.Length, activeAudioTracks = tracks.Length, trackNames = names, offset = start, decodeSeconds = decode.Seconds, proxySeconds = conversion.Seconds,
                    sourceFrameGapsOver50ms = gaps.Count(g => g > .05), maxSourceFrameGapSeconds = gaps.Length == 0 ? 0 : gaps.Max(), wavs, proxy, clip, timeline = edited });
                Console.WriteLine($"PASS {audio.Length} WAV tracks; clip and edited exports; CUDA preview {conversion.Seconds:0.0}s; decode {decode.Seconds:0.0}s; source gaps >50ms: {gaps.Count(g => g > .05)}");
            }
            catch (Exception ex) { failed = true; reports.Add(new { source, status = "FAIL", error = ex.Message }); Console.WriteLine("FAIL " + ex.Message); }
            File.WriteAllText(Path.Combine(outputRoot, "report.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
        }
        if (failed) throw new Exception("One or more real-footage processing tests failed; see report.json.");
    }
}
