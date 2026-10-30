using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

internal static class PreviewSyncChecks
{
    public static async Task Check(Assembly assembly, Type type, Window editor, string root, string data)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => type.GetField(name, flags)!.SetValue(editor, value);
        object? Field(string name) => type.GetField(name, flags)!.GetValue(editor);
        object? Call(string name, params object?[] args) => type.GetMethod(name, flags)!.Invoke(editor, args);
        async Task Run(string exe, params string[] args)
        {
            var start = new ProcessStartInfo(Path.Combine(root, "tools", exe)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var p = Process.Start(start)!; var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync(); await output; if (p.ExitCode != 0) throw new Exception(await error);
        }
        const string source = @"D:\Videos\Medal\Edits\MedalTVGearsofWarEDay20261025205351168-trim-1793289290085.mp4";
        Set("_videoPath", source); Set("_isMuted", true);
        await (Task)Call("OpenPreviewAsync", source)!;
        var player = Field("_previewPlayer")!; var playerType = player.GetType();
        double VideoSeconds() => (long)playerType.GetProperty("CurTime")!.GetValue(player)! / (double)TimeSpan.TicksPerSecond;
        async Task AssertPaused(string phase)
        {
            var watch = Stopwatch.StartNew();
            while ((bool)Field("_previewSeekPending")!) { if (watch.Elapsed.TotalSeconds > 5) throw new Exception("Paused seek did not settle during " + phase); await Task.Delay(25); }
            var initial = VideoSeconds(); await Task.Delay(1000);
            if ((bool)playerType.GetProperty("IsPlaying")!.GetValue(player)! || (bool)Field("_playbackRequested")! || Math.Abs(VideoSeconds() - initial) > .02)
                throw new Exception("Video autoplayed during " + phase);
            var audio = ((IEnumerable<MediaElement>)Field("_stemPlayers")!).Select(p => p.Position.TotalSeconds).ToArray();
            await Task.Delay(250);
            if (((IEnumerable<MediaElement>)Field("_stemPlayers")!).Select((p, i) => Math.Abs(p.Position.TotalSeconds - audio[i])).Any(delta => delta > .02))
                throw new Exception("WAV audio autoplayed during " + phase);
            Console.WriteLine("PASS No autoplay: " + phase);
        }
        await AssertPaused("source import before Play");
        var duration = (double)Field("_durationSeconds")!; Set("_clipEndSeconds", duration);
        var tracks = (System.Collections.IList)Field("_audioTracks")!;
        var trackType = assembly.GetType("ReelGood.MainWindow+AudioTrack", true)!;
        for (var index = 0; index < 2; index++)
        {
            var wav = Path.Combine(data, $"independent-track-{index}.wav");
            if (!File.Exists(wav)) await Run("ffmpeg.exe", "-y", "-v", "error", "-i", source, "-map", $"0:a:{index + 1}", "-t", "35", "-vn", "-c:a", "pcm_s16le", wav);
            var track = Activator.CreateInstance(trackType, [index + 2, $"Recorded independent track {index + 1}", "pcm_s16le", 2])!;
            trackType.GetProperty("WavPath")!.SetValue(track, wav); tracks.Add(track);
        }
        var clips = (System.Collections.IList)Field("_videoEditSegments")!;
        clips.Add(Activator.CreateInstance(assembly.GetType("ReelGood.MainWindow+VideoEditSegment", true)!, [0d, duration]));
        Call("BuildTimeline");
        await AssertPaused("WAV tracks added before Play");
        Console.WriteLine("PLAYER SeekCompleted: " + playerType.GetEvent("SeekCompleted")!.EventHandlerType);
        var samples = new List<object>(); var max = 0d;
        async Task Measure(string phase, int count)
        {
            await Task.Delay(1200);
            var began = (long)playerType.GetProperty("CurTime")!.GetValue(player)!;
            for (var i = 0; i < count; i++)
            {
                var video = (long)playerType.GetProperty("CurTime")!.GetValue(player)! / (double)TimeSpan.TicksPerSecond;
                var positions = ((IEnumerable<MediaElement>)Field("_stemPlayers")!).Select(p => p.Position.TotalSeconds).ToArray();
                var drift = positions.Select(p => Math.Abs(p - video)).DefaultIfEmpty().Max(); max = Math.Max(max, drift);
                samples.Add(new { phase, video, positions, drift }); await Task.Delay(200);
            }
            if ((long)playerType.GetProperty("CurTime")!.GetValue(player)! - began < TimeSpan.TicksPerSecond / 3)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { phase, began, current = VideoSeconds(), requested = Field("_playbackRequested"), pending = Field("_previewSeekPending"), playing = playerType.GetProperty("IsPlaying")!.GetValue(player), canPlay = playerType.GetProperty("CanPlay")!.GetValue(player), status = playerType.GetProperty("Status")?.GetValue(player)?.ToString(), sourceDuration = Field("_durationSeconds"), clipStart = Field("_clipStartSeconds"), clipEnd = Field("_clipEndSeconds"), visible = editor.IsVisible, left = editor.Left, top = editor.Top }));
                throw new Exception("Preview stopped advancing during " + phase);
            }
            var last = samples.Last(); Console.WriteLine(JsonSerializer.Serialize(last));
        }
        Call("TogglePlayback"); await Measure("start", 15);
        Call("SeekTo", 12d); await Measure("forward seek", 12);
        Call("TogglePlayback"); await Task.Delay(250); Call("SeekTo", 3d); Call("TogglePlayback"); await Measure("paused backward seek and resume", 12);
        Call("SetSourcePlaybackSpeed", 2d); await Measure("2x speed", 12);
        Call("SetSourcePlaybackSpeed", .5d); await Measure("0.5x speed", 12);
        var cueType = assembly.GetType("ReelGood.MainWindow+CueItem", true)!;
        var cue = Activator.CreateInstance(cueType, [5d, "Preview sync test", "Manual", 7d, 0d, 0d, 12d])!;
        Call("PlayCue", cue); await AssertPaused("bookmark click during playback");
        Call("TogglePlayback"); await Measure("explicit Play after bookmark selection", 12);
        Set("_pendingPreviewSeek", 8d); await (Task)Call("OpenPreviewAsync", source)!;
        await AssertPaused("preview source switch during playback");
        Call("TogglePlayback"); await Measure("explicit Play after preview switch", 8);
        Call("PauseDraftPreview");
        var preview = (Window)Activator.CreateInstance(assembly.GetType("ReelGood.EditedPreviewWindow", true)!, [editor, source])!;
        var panel = (DockPanel)((Border)preview.Content).Child;
        var host = panel.Children.Cast<FrameworkElement>().Single(element => element.GetType().Name == "FlyleafHost");
        var renderedPlayer = host.GetType().GetProperty("Player")!.GetValue(host)!;
        var renderedType = renderedPlayer.GetType(); var audioOut = renderedType.GetProperty("Audio")!.GetValue(renderedPlayer)!;
        audioOut.GetType().GetProperty("Mute")!.SetValue(audioOut, true);
        preview.Left = -32000; preview.Top = -32000; preview.Opacity = 0; preview.ShowActivated = false; preview.Show();
        try
        {
            var wait = Stopwatch.StartNew();
            while (!(bool)renderedType.GetProperty("CanPlay")!.GetValue(renderedPlayer)!)
            { if (wait.Elapsed.TotalSeconds > 20) throw new Exception("Rendered preview failed to open"); await Task.Delay(50); }
            var initial = (long)renderedType.GetProperty("CurTime")!.GetValue(renderedPlayer)!; await Task.Delay(1500);
            if ((bool)renderedType.GetProperty("IsPlaying")!.GetValue(renderedPlayer)! || Math.Abs((long)renderedType.GetProperty("CurTime")!.GetValue(renderedPlayer)! - initial) > TimeSpan.TicksPerMillisecond * 20)
                throw new Exception("Rendered preview autoplayed before Play");
            Console.WriteLine("PASS No autoplay: rendered preview remains paused after loading");
        }
        finally
        {
            var closed = new TaskCompletionSource(); preview.Closed += (_, _) => closed.TrySetResult(); preview.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        File.WriteAllText(Path.Combine(data, "sync-measurements.json"), JsonSerializer.Serialize(new { maxSteadyDriftSeconds = max, samples }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"MEASURED maximum steady preview clock mismatch: {max * 1000:0} ms");
        if (max > .12) throw new Exception($"Preview audio/video clocks diverged by {max * 1000:0}ms");
        if ((bool)Field("_previewSeekPending")!) throw new Exception("Preview never completed its final seek");
        Console.WriteLine("PASS Real recorded footage with two WAV tracks: start, forward/backward seek, resume, 2x/0.5x and bookmark review stay within 120ms");
    }
}

