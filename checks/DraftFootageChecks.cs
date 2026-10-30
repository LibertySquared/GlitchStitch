using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using ReelGood;

internal static class DraftFootageChecks
{
    public static void Run(string? sourceOverride = null)
    {
        var source = sourceOverride ?? @"D:\Videos\NVIDIAFortnite\Fortnite 2026.10.11 - 12.40.24.06.Double Elimination.DVR.mp4";
        const string root = @"D:\Editor\ReelGood";
        var ffmpeg = Path.Combine(root, "tools", "ffmpeg.exe"); var probe = Path.Combine(root, "tools", "ffprobe.exe");
        string Run(string tool, IEnumerable<string> args)
        {
            var start = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!; var text = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(180_000)) { process.Kill(true); throw new Exception("Draft export timed out"); }
            Task.WaitAll(text, error); if (process.ExitCode != 0) throw new Exception(error.Result);
            return text.Result;
        }
        var original = new FileInfo(source); var length = original.Length; var modified = original.LastWriteTimeUtc;
        using var metadata = JsonDocument.Parse(Run(probe, ["-v", "error", "-show_format", "-show_streams", "-of", "json", source]));
        var duration = double.Parse(metadata.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        var streams = metadata.RootElement.GetProperty("streams").EnumerateArray().Where(item => item.GetProperty("codec_type").GetString() == "audio").ToArray();
        var labels = streams.Select((item, index) => AudioTrackPolicy.ReadLabel(item, index + 1)).ToArray();
        var tracks = streams.Where((item, index) => !AudioTrackPolicy.IsRedundantCombined(labels[index], labels))
            .Select(item => new ExportAudio(item.GetProperty("index").GetInt32(), null, .8, false)).ToArray();
        var plan = DraftEditPlanner.Build([new(0, 21.5, Math.Min(29.5, duration), "Test selection / video", null), new(1, 22, 27, "Overlapping microphone cue", "Keep"), new(2, 3, 5, "Additional test range", "Keep"), new(3, 8, 10, "Excluded test range", "Skip")], duration);
        var folder = Path.Combine(root, "checks", "results", "draft", sourceOverride is null ? "nvidia" : "medal"); Directory.CreateDirectory(folder);
        var output = Path.Combine(folder, "draft.mp4");
        Run(ffmpeg, ExportPlan.Build(source, output, plan.Clips.Select(clip => new ExportSegment(clip.StartSeconds, clip.EndSeconds)).ToArray(), tracks));
        using var result = JsonDocument.Parse(Run(probe, ["-v", "error", "-show_format", "-show_streams", "-of", "json", output]));
        var exportedDuration = double.Parse(result.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        var exportedAudio = result.RootElement.GetProperty("streams").EnumerateArray().Count(item => item.GetProperty("codec_type").GetString() == "audio");
        if (plan.Clips.Count != 2 || Math.Abs(exportedDuration - plan.Clips.Sum(clip => clip.Duration)) > .2 || exportedAudio != Math.Min(1, tracks.Length)) throw new Exception("Draft output duration or separate audio track count is incorrect");
        foreach (var stream in result.RootElement.GetProperty("streams").EnumerateArray())
        {
            var start = double.Parse(stream.GetProperty("start_time").GetString()!, CultureInfo.InvariantCulture);
            var seconds = double.Parse(stream.GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
            if (Math.Abs(start) > .05 || Math.Abs(seconds - exportedDuration) > .2) throw new Exception("Draft audio/video streams have mismatched output bounds");
        }
        Run(ffmpeg, ["-v", "error", "-xerror", "-i", output, "-map", "0", "-f", "null", "-"]);
        original.Refresh(); if (original.Length != length || original.LastWriteTimeUtc != modified) throw new Exception("Original recording changed");
        File.WriteAllText(Path.Combine(folder, "report.json"), JsonSerializer.Serialize(new { source, output, plan, expectedDuration = plan.Clips.Sum(clip => clip.Duration), exportedDuration, exportedAudio, sourceUnchanged = true, note = "Extra ranges are synthetic integration-test selections, not detector accuracy labels." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS Real-footage draft export: {plan.Clips.Count} joined clips, {exportedDuration:0.00}s, {exportedAudio} separate audio tracks, complete decode, unchanged source");
    }
}
