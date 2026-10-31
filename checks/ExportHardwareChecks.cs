using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using GlitchStich;

internal static class ExportHardwareChecks
{
    public static void Run()
    {
        const string root = @"D:\Editor\GlitchStich";
        var ffmpeg = Path.Combine(root, "tools", "ffmpeg.exe"); var probe = Path.Combine(root, "tools", "ffprobe.exe");
        string Execute(string tool, IEnumerable<string> args)
        {
            var start = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in args) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!; var text = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(180_000)) { process.Kill(true); throw new Exception("Export test timed out"); }
            Task.WaitAll(text, error); if (process.ExitCode != 0) throw new Exception(error.Result); return text.Result;
        }
        var folder = Path.Combine(root, "checks", "results", "export-quality"); Directory.CreateDirectory(folder);
        var source = @"D:\Videos\Medal\Edits\MedalTVGearsofWarEDay20261025205351168-trim-1793289290085.mp4";
        using var metadata = JsonDocument.Parse(Execute(probe, ["-v", "error", "-show_streams", "-of", "json", source]));
        var audio = metadata.RootElement.GetProperty("streams").EnumerateArray().Where(stream => stream.GetProperty("codec_type").GetString() == "audio").ToArray();
        var labels = audio.Select((stream, index) => AudioTrackPolicy.ReadLabel(stream, index + 1)).ToArray();
        var tracks = audio.Where((stream, index) => !AudioTrackPolicy.IsRedundantCombined(labels[index], labels)).Select(stream => new ExportAudio(stream.GetProperty("index").GetInt32(), null, .8, false)).ToArray();
        var sourceVideo = metadata.RootElement.GetProperty("streams").EnumerateArray().First(stream => stream.GetProperty("codec_type").GetString() == "video");
        var reports = new List<object>();
        foreach (var choice in new[] { ("High", "libx264"), ("Balanced", "libx264"), ("Maximum", "libx264"), ("High", "h264_nvenc") })
        {
            var watch = Stopwatch.StartNew(); var prefix = choice.Item1 + "-" + choice.Item2;
            var options = new ExportOptions(choice.Item1, choice.Item2, 2, HardwareDecode: choice.Item2 == "h264_nvenc", Intermediate: true);
            for (var index = 0; index < 2; index++)
            {
                var start = index == 0 ? 3d : 21d;
                Execute(ffmpeg, ExportPlan.Build(source, Path.Combine(folder, prefix + $"-{index}.mkv"), [new(start, start + 1)], tracks, options));
            }
            var manifest = Path.Combine(folder, prefix + ".txt");
            File.WriteAllLines(manifest, Enumerable.Range(0, 2).Select(index => $"file '{prefix}-{index}.mkv'"));
            var output = Path.Combine(folder, prefix + ".mp4");
            Execute(ffmpeg, ["-y", "-v", "error", "-f", "concat", "-safe", "0", "-i", manifest, "-c:v", "copy", "-c:a", "aac", "-b:a", "320k", "-movflags", "+faststart", output]);
            using var info = JsonDocument.Parse(Execute(probe, ["-v", "error", "-show_streams", "-show_format", "-of", "json", output]));
            var streams = info.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            var video = streams.First(stream => stream.GetProperty("codec_type").GetString() == "video");
            if (streams.Count(stream => stream.GetProperty("codec_type").GetString() == "audio") != 1) throw new Exception("Expected one mixed audio stream");
            var duration = double.Parse(info.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
            if (Math.Abs(duration - 2) > .2) throw new Exception("Sequential export duration changed");
            if (choice.Item1 != "Balanced" && (video.GetProperty("width").GetInt32() != sourceVideo.GetProperty("width").GetInt32() || video.GetProperty("height").GetInt32() != sourceVideo.GetProperty("height").GetInt32())) throw new Exception("Source resolution was lost");
            Execute(ffmpeg, ["-v", "error", "-xerror", "-i", output, "-map", "0", "-f", "null", "-"]);
            reports.Add(new { quality = choice.Item1, encoder = choice.Item2, duration, elapsed = watch.Elapsed.TotalSeconds, output });
            Console.WriteLine($"PASS Sequential {choice.Item1} / {choice.Item2}: mixed audio, joined duration, source dimensions, full decode");
        }
        File.WriteAllText(Path.Combine(folder, "report.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
    }
}
