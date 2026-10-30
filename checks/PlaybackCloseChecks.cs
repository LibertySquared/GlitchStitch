using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ReelGood;

internal static class PlaybackCloseChecks
{
    public static void Run()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
            {
                try { await Check(); } catch (Exception ex) { error = ex; }
                finally { app.Shutdown(); }
            });
            app.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(70))) throw new Exception("Playback close check timed out");
        if (error is not null) throw new Exception("Playback close check failed", error);
    }

    private static async Task Check()
    {
        const string root = @"D:\Editor\ReelGood";
        var folder = Path.Combine(root, ".reelgood-publish-staging");
        if (!File.Exists(Path.Combine(folder, "ReelGood.dll"))) folder = Path.Combine(root, "portable-release");
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(folder, name.Name + ".dll")) ? Assembly.LoadFrom(Path.Combine(folder, name.Name + ".dll")) : null;
        var flyleaf = Assembly.LoadFrom(Path.Combine(folder, "FlyleafLib.dll"));
        var engineConfigType = flyleaf.GetType("FlyleafLib.EngineConfig", true)!;
        var engineConfig = Activator.CreateInstance(engineConfigType)!;
        engineConfigType.GetProperty("FFmpegPath")!.SetValue(engineConfig, Path.Combine(folder, "Resources", "Flyleaf", "FFmpeg"));
        engineConfigType.GetProperty("UIRefresh")!.SetValue(engineConfig, false);
        flyleaf.GetType("FlyleafLib.Engine", true)!.GetMethod("Start", [engineConfigType])!.Invoke(null, [engineConfig]);
        var playerType = flyleaf.GetType("FlyleafLib.MediaPlayer.Player", true)!;
        var hostType = flyleaf.GetType("FlyleafLib.Controls.WPF.FlyleafHost", true)!;
        object? Invoke(object target, string name, params object?[] supplied)
        {
            var method = target.GetType().GetMethods().First(method => method.Name == name &&
                method.GetParameters().Length >= supplied.Length && method.GetParameters().Skip(supplied.Length).All(parameter => parameter.HasDefaultValue) &&
                method.GetParameters().Take(supplied.Length).Select((parameter, index) => supplied[index] is null || parameter.ParameterType.IsInstanceOfType(supplied[index])).All(value => value));
            return method.Invoke(target, supplied.Concat(method.GetParameters().Skip(supplied.Length).Select(parameter => parameter.DefaultValue)).ToArray());
        }
        var results = new List<object>();
        foreach (var source in new[] {
            @"D:\Videos\NVIDIAFortnite\Fortnite 2026.10.11 - 12.40.24.06.Double Elimination.DVR.mp4",
            @"D:\Videos\Medal\Edits\MedalTVGearsofWarEDay20261025205351168-trim-1793289290085.mp4" })
        {
            var config = Activator.CreateInstance(flyleaf.GetType("FlyleafLib.Config", true)!)!;
            var constructor = playerType.GetConstructors().First(ctor => ctor.GetParameters().FirstOrDefault()?.ParameterType.IsInstanceOfType(config) == true && ctor.GetParameters().Skip(1).All(parameter => parameter.HasDefaultValue));
            var player = constructor.Invoke(new[] { config }.Concat(constructor.GetParameters().Skip(1).Select(parameter => parameter.DefaultValue)).ToArray());
            var audio = playerType.GetProperty("Audio")!.GetValue(player)!; audio.GetType().GetProperty("Mute")!.SetValue(audio, true);
            var host = (FrameworkElement)Activator.CreateInstance(hostType)!;
            hostType.GetProperty("Player")!.SetValue(host, player);
            var window = new Window { Content = host, Width = 320, Height = 180, Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
            window.Show();
            try
            {
                Invoke(player, "OpenAsync", source);
                var open = Stopwatch.StartNew();
                while (!(bool)playerType.GetProperty("CanPlay")!.GetValue(player)!)
                { if (open.Elapsed > TimeSpan.FromSeconds(20)) throw new Exception("Recorded test video did not open"); await Task.Delay(50); }
                Invoke(player, "Play"); await Task.Delay(500);
                var uiThread = Environment.CurrentManagedThreadId; var nativeThread = uiThread; var close = Stopwatch.StartNew();
                await PlaybackShutdown.ReleaseAsync(() => { hostType.GetProperty("Player")!.SetValue(host, null); ((IDisposable)host).Dispose(); },
                    () => { nativeThread = Environment.CurrentManagedThreadId; ((IDisposable)player).Dispose(); }).WaitAsync(TimeSpan.FromSeconds(20));
                if (nativeThread == uiThread) throw new Exception("Native cleanup ran on the WPF interface thread");
                results.Add(new { source = Path.GetFileName(source), milliseconds = close.Elapsed.TotalMilliseconds, nativeCleanupOffUiThread = true });
                Console.WriteLine($"PASS Native playback close: {Path.GetFileName(source)} · {close.Elapsed.TotalMilliseconds:0}ms · cleanup off UI thread");
            }
            finally { window.Close(); }
        }
        var output = Path.Combine(root, "checks", "results", "scan-close"); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "playback-close.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }
}
