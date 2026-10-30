using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

internal static class WindowCloseChecks
{
    public static void Run(bool media = false, bool sync = false)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
            {
                try { await Check(app, media, sync); } catch (Exception ex) { error = ex; }
                finally { app.Shutdown(); }
            });
            app.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(media ? 420 : sync ? 180 : 90))) throw new Exception("Full editor shutdown check timed out");
        if (error is not null) throw new Exception("Full editor shutdown check failed", error);
    }

    private static async Task Check(Application app, bool media, bool sync)
    {
        const string root = @"D:\Editor\ReelGood";
        var folder = Environment.GetEnvironmentVariable("GLITCHSTICH_CHECK_PACKAGE") ?? new[] { Path.Combine(root, "bin", "Release", "net9.0-windows", "win-x64"), Path.Combine(root, ".reelgood-publish-staging"), Path.Combine(root, "portable-release") }
            .First(path => File.Exists(Path.Combine(path, "ReelGood.dll")));
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(folder, name.Name + ".dll")) ? Assembly.LoadFrom(Path.Combine(folder, name.Name + ".dll")) : null;
        var assembly = Assembly.LoadFrom(Path.Combine(folder, "ReelGood.dll"));
        app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(File.ReadAllText(Path.Combine(root, "CyberTheme.xaml")).Replace("clr-namespace:ReelGood", "clr-namespace:ReelGood;assembly=ReelGood")));
        var flyleaf = Assembly.LoadFrom(Path.Combine(folder, "FlyleafLib.dll"));
        var configType = flyleaf.GetType("FlyleafLib.EngineConfig", true)!; var config = Activator.CreateInstance(configType)!;
        configType.GetProperty("FFmpegPath")!.SetValue(config, Path.Combine(folder, "Resources", "Flyleaf", "FFmpeg"));
        configType.GetProperty("UIRefresh")!.SetValue(config, false);
        flyleaf.GetType("FlyleafLib.Engine", true)!.GetMethod("Start", [configType])!.Invoke(null, [config]);
        var type = assembly.GetType("ReelGood.MainWindow", true)!;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var data = Path.Combine(root, "checks", "results", media ? "media-placement" : sync ? "preview-sync" : "window-close"); Directory.CreateDirectory(Path.Combine(data, "tools"));
        // Isolate tool setup from real app data; remove these fixture copies after checking.
        foreach (var name in new[] { "ffmpeg.exe", "ffprobe.exe" })
        {
            var target = Path.Combine(data, "tools", name);
            if (!File.Exists(target)) File.Copy(Path.Combine(root, "tools", name), target);
        }
        Window Make() => (Window)Activator.CreateInstance(type, flags, null, [data], null)!;
        void Set(object instance, string field, object value) => type.GetField(field, flags)!.SetValue(instance, value);
        async Task WaitClosed(Window window)
        {
            var watch = Stopwatch.StartNew();
            while (!(bool)type.GetField("_allowCloseAfterCleanup", flags)!.GetValue(window)!)
            { if (watch.Elapsed > TimeSpan.FromSeconds(25)) throw new Exception("Editor did not complete background cleanup"); await Task.Delay(25); }
        }
        void Show(Window window)
        { window.Left = -32000; window.Top = -32000; window.ShowActivated = false; window.ShowInTaskbar = false; window.Opacity = 0; window.Show(); }
        void ChooseOnDialog(Window owner, string answer)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
            timer.Tick += (_, _) =>
            {
                var dialog = app.Windows.Cast<Window>().FirstOrDefault(w => w.Owner == owner && w.Title == "Save before closing?");
                if (dialog is null) return;
                var button = Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, answer));
                timer.Stop(); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            timer.Start();
        }
        if (sync)
        {
            var editor = Make(); Show(editor);
            try { await PreviewSyncChecks.Check(assembly, type, editor, root, data); }
            finally
            {
                Set(editor, "_projectHasChanges", false); editor.Close(); await WaitClosed(editor);
                foreach (var name in new[] { "ffmpeg.exe", "ffprobe.exe" }) File.Delete(Path.Combine(data, "tools", name));
            }
            return;
        }
        if (media)
        {
            var editor = Make(); Show(editor);
            try { await CheckMedia(assembly, type, editor, root, data); }
            finally { Set(editor, "_projectHasChanges", false); editor.Close(); await WaitClosed(editor); }
            foreach (var name in new[] { "ffmpeg.exe", "ffprobe.exe" }) File.Delete(Path.Combine(data, "tools", name));
            return;
        }
        var canceled = Make(); Show(canceled); Set(canceled, "_projectHasChanges", true); ChooseOnDialog(canceled, "CANCEL"); canceled.Close();
        if (!canceled.IsVisible || (bool)type.GetField("_isClosing", flags)!.GetValue(canceled)!) throw new Exception("Cancel dismissed the editor or started cleanup");
        Set(canceled, "_projectHasChanges", false); canceled.Close();
        if (canceled.IsVisible || canceled.ShowInTaskbar) throw new Exception("Already-saved editor stayed visible during cleanup");
        await WaitClosed(canceled);
        Console.WriteLine("PASS Real editor: Cancel keeps the window; already-saved close hides immediately and completes native cleanup");
        var declined = Make(); Show(declined); Set(declined, "_projectHasChanges", true); Set(declined, "_busyOperations", 1);
        ChooseOnDialog(declined, "NO"); declined.Close();
        if (declined.IsVisible || declined.ShowInTaskbar) throw new Exception("No left the editor visible during background work");
        if ((bool)type.GetField("_allowCloseAfterCleanup", flags)!.GetValue(declined)!) throw new Exception("Native objects were disposed while work still used them");
        declined.Close(); // Repeated close must be harmless.
        Set(declined, "_busyOperations", 0); await WaitClosed(declined);
        var log = File.ReadAllText(Path.Combine(data, "shutdown.log"));
        if (!log.Contains("Waiting in background") || !log.Contains("Native video player disposed")) throw new Exception("Shutdown stages missing from audit");
        Console.WriteLine("PASS Real editor: No hides immediately; pending work drains in background; repeated X is harmless; native cleanup completes");
        foreach (var name in new[] { "ffmpeg.exe", "ffprobe.exe" }) File.Delete(Path.Combine(data, "tools", name));
    }
    private static async Task CheckMedia(Assembly assembly, Type type, Window editor, string root, string data)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        const string source = @"D:\Videos\NVIDIAFortnite\Fortnite 2026.10.11 - 12.40.24.06.Double Elimination.DVR.mp4";
        await (Task)type.GetMethod("LoadVideoAsync", flags)!.Invoke(editor, [source])!;
        ((ComboBox)type.GetField("EditModeBox", flags)!.GetValue(editor)!).SelectedIndex = 1;
        ((TextBox)type.GetField("GameTitleBox", flags)!.GetValue(editor)!).Text = "Fortnite";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        var reviewTask = (Task)type.GetMethod("ReviewFootageWindowAsync", flags)!.Invoke(editor, ["Review highlight context for a short celebration overlay", "Actual recording test window", 18d, 12d, Path.Combine(data, "media-review"), cancellation.Token])!;
        await reviewTask; var review = reviewTask.GetType().GetProperty("Result")!.GetValue(reviewTask)!;
        File.WriteAllText(Path.Combine(data, "media-source-review.json"), System.Text.Json.JsonSerializer.Serialize(review, review.GetType(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        type.GetField("_lastFootageReview", flags)!.SetValue(editor, "Source 18–30 seconds: " + review.GetType().GetProperty("Display")!.GetValue(review));
        var ranges = (System.Collections.IList)type.GetField("_reviewedSourceRanges", flags)!.GetValue(editor)!;
        ranges.Add(Activator.CreateInstance(assembly.GetType("ReelGood.EditorialRange", true)!, [18d, 30d, "Actual sampled production review"]));
        var cues = (System.Collections.IList)type.GetField("_cues", flags)!.GetValue(editor)!;
        var cueType = assembly.GetType("ReelGood.MainWindow+CueItem", true)!;
        var cue = Activator.CreateInstance(cueType, [21d, "Manual highlight test candidate", "Manual", 24d, 0d, 0d, 28d])!;
        cueType.GetProperty("Decision")!.SetValue(cue, "Keep"); cues.Add(cue);
        var assetPath = Path.Combine(data, "test-celebration.png");
        var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 400, 120));
            drawing.DrawText(new FormattedText("CLUTCH!", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Consolas"), 64, Brushes.Cyan, 1), new Point(25, 20));
        }
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(400, 120, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap)); using (var stream = File.Create(assetPath)) encoder.Save(stream);
        var json = System.Text.Json.JsonSerializer.Serialize(new { Clips = new[] { new { Start = 18, End = 30, Layer = 0, Effects = new { Speed = 1 } } }, Assets = new[] { new { Id = "celebration", Path = assetPath, Kind = "Image", Description = "Short CLUTCH celebration after the user's kept highlight; place in a corner without blocking the action", AllowAi = true, HasAudio = false, Duration = 0 } }, Media = Array.Empty<object>() });
        var edit = System.Text.Json.JsonSerializer.Deserialize(json, assembly.GetType("ReelGood.EditingSnapshot", true)!)!;
        var task = (Task)type.GetMethod("SuggestMediaPlacementsAsync", flags)!.Invoke(editor, [edit, "Suggest a brief 1–2 second small celebration for my kept highlight at source 24s. Use only reviewed source 18–30s; avoid masking gameplay.", cancellation.Token])!;
        try { await task; }
        finally { File.WriteAllText(Path.Combine(data, "media-placement-response.json"), (string?)type.GetField("_lastMediaPlacementResponse", flags)!.GetValue(editor) ?? "No response"); }
        var suggestions = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var count = ((System.Collections.IEnumerable)suggestions).Cast<object>().Count(); if (count == 0) throw new Exception("Actual model returned no usable placement proposal");
        File.WriteAllText(Path.Combine(data, "media-live.json"), System.Text.Json.JsonSerializer.Serialize(new { source, review, suggestions }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS Local Gemma reviewed actual Fortnite frames/audio and a test caption asset, returned {count} validated placement proposal(s); nothing applied or uploaded");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}

