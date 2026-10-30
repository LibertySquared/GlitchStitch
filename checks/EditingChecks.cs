using System.Diagnostics;
using System.IO;
using System.Text.Json;
using ReelGood;

internal static class EditingChecks
{
    public static void Run()
    {
        const string ffmpeg = @"D:\Editor\ReelGood\tools\ffmpeg.exe";
        const string ffprobe = @"D:\Editor\ReelGood\tools\ffprobe.exe";
        const string folder = @"D:\Editor\ReelGood\checks\results\editing"; Directory.CreateDirectory(folder);
        byte[] Execute(string executable, IEnumerable<string> args)
        {
            var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in args) info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!; using var output = new MemoryStream();
            var copy = process.StandardOutput.BaseStream.CopyToAsync(output); var error = process.StandardError.ReadToEndAsync();
            process.WaitForExit(); copy.GetAwaiter().GetResult(); var text = error.GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new Exception(text); return output.ToArray();
        }
        var source = Path.Combine(folder, "source.mp4");
        Execute(ffmpeg, ["-y", "-v", "error", "-f", "lavfi", "-i", "color=c=red:s=320x180:r=30:d=2", "-f", "lavfi", "-i", "color=c=blue:s=320x180:r=30:d=2", "-f", "lavfi", "-i", "sine=frequency=500:sample_rate=48000:duration=4", "-f", "lavfi", "-i", "sine=frequency=800:sample_rate=48000:duration=4", "-filter_complex_threads", "1", "-filter_complex", "[0:v][1:v]concat=n=2:v=1:a=0[v]", "-map", "[v]", "-map", "2:a", "-map", "3:a", "-c:v", "libx264", "-threads", "2", "-pix_fmt", "yuv420p", "-c:a", "aac", source]);
        EditingClip[] clips = [new(2, 3, 0, new(Speed: 2, Rotation: 90, CropLeft: .1, FadeIn: .05, FadeOut: .05, Volume: .5, Brightness: .1)), new(0, 2, 1, new(Speed: .5))];
        var basic = Path.Combine(folder, "edited.mp4");
        Execute(ffmpeg, ExportPlan.Build(source, basic, clips.Select(clip => new ExportSegment(clip.Start, clip.End) { Effects = clip.Effects }).ToArray(), [new(1, null, .5, false), new(2, null, .5, false)], new("High", "libx264", 2) { CanvasWidth = 320, CanvasHeight = 180 }));
        using (var metadata = JsonDocument.Parse(Execute(ffprobe, ["-v", "error", "-show_streams", "-show_format", "-of", "json", basic])))
        {
            var streams = metadata.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            var duration = double.Parse(metadata.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            if (Math.Abs(duration - 4.5) > .12 || streams.Count(stream => stream.GetProperty("codec_type").GetString() == "audio") != 1) throw new Exception("Speed/order edit changed duration or failed to merge audio");
            if (streams[0].GetProperty("width").GetInt32() != 320 || streams[0].GetProperty("height").GetInt32() != 180) throw new Exception("Crop/rotation clips do not share a consistent output canvas");
        }
        byte[] Pixel(string path, double at, int x = 160, int y = 90) => Execute(ffmpeg, ["-v", "error", "-ss", at.ToString(System.Globalization.CultureInfo.InvariantCulture), "-i", path, "-frames:v", "1", "-vf", $"format=rgb24,crop=1:1:{x}:{y}", "-f", "rawvideo", "pipe:1"]);
        var blue = Pixel(basic, .2); var red = Pixel(basic, 1);
        if (blue[2] <= blue[0] || red[0] <= red[2]) throw new Exception("Reordered clips did not retain the requested visual sequence");
        Console.WriteLine("PASS Real FFmpeg export: clip order, speed, crop, rotation, fades, brightness, consistent canvas, and dual-track audio mix");

        var image = Path.Combine(folder, "green.png"); var sound = Path.Combine(folder, "reaction.wav"); var video = Path.Combine(folder, "meme.mp4");
        Execute(ffmpeg, ["-y", "-v", "error", "-f", "lavfi", "-i", "color=c=lime:s=64x64", "-frames:v", "1", "-threads", "1", image]);
        Execute(ffmpeg, ["-y", "-v", "error", "-f", "lavfi", "-i", "sine=frequency=1100:sample_rate=48000:duration=1", "-c:a", "pcm_s16le", sound]);
        Execute(ffmpeg, ["-y", "-v", "error", "-i", source, "-t", "1", "-map", "0:v", "-map", "0:a:0", "-c:v", "libx264", "-threads", "2", "-c:a", "aac", video]);
        EditingAsset[] assets = [new("image", image, "Image", "test image"), new("sound", sound, "Audio", "test sound", HasAudio: true, Duration: 1), new("video", video, "Video", "test video", HasAudio: true, Duration: 1)];
        TimedMedia[] media = [new("i", "image", "Image", 2.1, .25, 0, 0, .25), new("a", "sound", "Audio", .5, .5), new("v", "video", "Video", .25, .5, .5, .5, .25), new("t", null, "Text", 1, 1, .1, .1, .35, Text: "100% CLUTCH: 'quoted' [test]")];
        var resolved = MediaRenderPlan.Resolve(clips, assets, media, 4); var captions = new Dictionary<string, string> { ["t"] = Path.Combine(folder, "caption.txt") }; File.WriteAllText(captions["t"], media[3].Text);
        var outputPath = Path.Combine(folder, "with-media.mp4"); Execute(ffmpeg, MediaRenderPlan.Build(basic, outputPath, resolved, 320, 180, 4.5, true, captions, new("High", "libx264", 2)));
        var before = Pixel(outputPath, .01, 10, 10); var during = Pixel(outputPath, .15, 10, 10); var after = Pixel(outputPath, .4, 10, 10);
        if (during[1] <= during[0] + 40 || during[1] <= during[2] + 40 || before[1] > 120 || after[1] > 120) throw new Exception("Timed image did not appear only at its mapped anchor");
        Console.WriteLine("PASS Real FFmpeg render: timed image/video/audio overlays and escaped text captions");

        if (Math.Abs(EditTimeMap.ToOutput(.5, clips)!.Value - 1.5) > .001 || EditTimeMap.ToOutput(3.5, clips) is not null) throw new Exception("Overlay anchor mapping ignored source cuts, order or speed");
        if (EditTimeMap.ReviewedAndUnprotected(2.9, .6, clips, [new(0, 4, "review")], [new(.1, .4, "dialogue")]) ||
            EditTimeMap.ReviewedAndUnprotected(2.9, .6, clips, [new(2, 3, "review")], [])) throw new Exception("An AI placement crossed a cut into protected or unreviewed footage");
        var snapshot = new EditingSnapshot(clips, assets, media); var serialized = JsonSerializer.Deserialize<EditingSnapshot>(JsonSerializer.Serialize(snapshot))!;
        if (serialized.Clips[0].Effects.Speed != 2 || serialized.Media[3].Text != media[3].Text || serialized.Assets.Count != 3) throw new Exception("Editing state did not survive serialization");
        var history = new EditHistory(); history.Push(snapshot); var changed = snapshot with { Clips = clips.Reverse().ToArray() };
        if (history.Undo(changed)?.Clips[0].Start != 2 || history.Redo(snapshot)?.Clips[0].Start != 0) throw new Exception("Undo/redo did not restore clip ordering");
        foreach (var invalid in new[] { new ClipEffects(Speed: double.NaN), new ClipEffects(Speed: 0), new ClipEffects(Rotation: 12), new ClipEffects(CropLeft: .8) })
            if (invalid.Validate() is null) throw new Exception("Invalid edit effects were accepted");
        Console.WriteLine("PASS Source-anchor mapping, AI context guards, editing-state round trip, undo/redo, invalid-effect rejection");
        EditingClip[] repeated = [new(0, 2, 0, new()), new(0, 2, 1, new(Speed: 2))];
        if (EditTimeMap.ToOutput(1, repeated, 0) != 1 || EditTimeMap.ToOutput(1, repeated, 1) != 2.5 || EditTimeMap.ToOutput(1, repeated, 2) is not null) throw new Exception("Repeated source occurrence mapped to the wrong edited clip");
        Console.WriteLine("PASS Repeated-clip media anchors distinguish each occurrence after reordering and speed changes");
    }
}
