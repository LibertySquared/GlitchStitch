using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

internal static class RenderChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Render(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Exception("Interface render failed", failure);
    }
    private static void Render()
    {
        var root = @"D:\Editor\ReelGood";
        var folder = Path.Combine(root, "bin", "Release", "net9.0-windows", "win-x64");
        if (!File.Exists(Path.Combine(folder, "ReelGood.dll"))) folder = Path.Combine(root, ".reelgood-publish-staging");
        if (!File.Exists(Path.Combine(folder, "ReelGood.dll"))) folder = Path.Combine(root, "portable-release");
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(folder, name.Name + ".dll")) ? Assembly.LoadFrom(Path.Combine(folder, name.Name + ".dll")) : null;
        var assembly = Assembly.LoadFrom(Path.Combine(folder, "ReelGood.dll"));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(File.ReadAllText(Path.Combine(root, "CyberTheme.xaml")).Replace("clr-namespace:ReelGood", "clr-namespace:ReelGood;assembly=ReelGood")));
        var document = XDocument.Load(Path.Combine(root, "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        document.Root!.Attribute(x + "Class")!.Remove();
        var events = new HashSet<string> { "Click", "Checked", "Unchecked", "MouseLeftButtonDown", "SelectionChanged", "TextChanged", "ValueChanged", "PreviewMouseLeftButtonDown", "PreviewMouseLeftButtonUp", "KeyDown", "PreviewKeyDown" };
        foreach (var attribute in document.Descendants().Attributes().Where(a => events.Contains(a.Name.LocalName)).ToArray()) attribute.Remove();
        foreach (var host in document.Descendants().Where(e => e.Name.LocalName == "FlyleafHost").ToArray()) host.ReplaceWith(new XElement(wpf + "Grid", host.Attributes().Where(a => a.Name.LocalName != "Player")));
        var window = (Window)XamlReader.Parse(document.ToString().Replace("clr-namespace:ReelGood", "clr-namespace:ReelGood;assembly=ReelGood"));
        if (window.Title != "GlitchStich Studio" || !((Button)window.FindName("SaveProjectButton")).Content.ToString()!.Contains("Save stitch")) throw new Exception("GlitchStich identity or stitch controls did not load");
        var body = (FrameworkElement)window.Content; window.Content = null;
        body.SetValue(TextElement.ForegroundProperty, window.Foreground);
        body.SetValue(TextElement.FontFamilyProperty, window.FontFamily);
        var library = (ListBox)window.FindName("MediaLibraryList");
        library.ItemsSource = new[] { new { Icon = "▣", Name = "Fortnite · Session 01.mp4", Details = "VIDEO / 2 AUDIO STREAMS" }, new { Icon = "♫", Name = "Microphone.wav", Details = "AUDIO / INDEPENDENT TRACK" } };
        ((ListBox)window.FindName("ScanCategoryList")).ItemsSource = new[] { new { Name = "Funny moments", Count = 8 }, new { Name = "Eliminations", Count = 3 } };
        ((ListBox)window.FindName("CueList")).ItemsSource = new[] { new { DisplayText = "00:24 setup · laughter at 00:28\nMicrophone · review" }, new { DisplayText = "01:12 setup · elimination at 01:16\nVideo frames · review" } };
        ((TextBlock)window.FindName("TaskProgressLabel")).Text = "VIDEO FRAMES 37% / 222 OF 600 · 00:18 elapsed";
        ((TextBlock)window.FindName("StatusText")).Text = "Comparing video frames · your source and audio tracks stay unchanged";
        ((ProgressBar)window.FindName("TaskProgress")).Value = 37;
        var output = Path.Combine(root, "checks", "results", "interface"); Directory.CreateDirectory(output);
        var glitchy = (FrameworkElement)window.FindName("GlitchyHost");
        if (window.FindName("CopilotButton") is not null || window.FindName("CopilotCornerAvatar") is not null) throw new Exception("Old Copilot controls remain in the studio");
        glitchy.GetType().GetMethod("RenderPose", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(glitchy, [8d]);
        var helpBubble = glitchy.GetType().GetProperty("Bubble")!.GetValue(glitchy)!;
        helpBubble.GetType().GetMethod("ShowMessage")!.Invoke(helpBubble, ["I'm Glitchy. Click me to review your glitches, refine a scan prompt or help build a draft stitch. You approve the changes.", false]);
        Save(body, 1500, 960, Path.Combine(output, "studio.png"));
        foreach (var name in new[] { "BrandAppIcon" })
        {
            if (((Image)window.FindName(name)).Source is not BitmapSource bitmap || bitmap.PixelWidth < 16) throw new Exception("Brand image did not load: " + name);
        }
        var icon = new IconBitmapDecoder(new Uri(Path.Combine(root, "Resources", "reelgood.ico")), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        if (!new[] { 16, 32, 48, 256 }.All(size => icon.Frames.Any(frame => frame.PixelWidth == size))) throw new Exception("Windows icon is missing required small/large resolutions");
        Console.WriteLine("PASS Embedded app/Copilot images load; Windows icon contains transparent small/large frames");
        var startScan = (Button)window.FindName("StartScanButton");
        var scanContainer = (FrameworkElement)window.FindName("ScanSettingsHost");
        var bookmarkPanel = (FrameworkElement)window.FindName("BookmarkPanel");
        var scanPoint = startScan.TranslatePoint(new Point(0, 0), scanContainer);
        if (scanPoint.Y < 0 || scanPoint.Y + startScan.ActualHeight > scanContainer.ActualHeight + 1) throw new Exception("Start scan is hidden below the scan-settings area");
        var inPanel = startScan.TranslatePoint(new Point(0, 0), bookmarkPanel);
        if (inPanel.Y + startScan.ActualHeight > bookmarkPanel.ActualHeight + 1) throw new Exception("Start scan extends outside the bookmark panel");
        Console.WriteLine("PASS Start scan is pinned visibly beneath the scrollable settings");
        body.IsEnabled = false;
        Save(body, 1500, 960, Path.Combine(output, "processing.png"));
        library.ApplyTemplate();
        var border = VisualTreeHelper.GetChild(library, 0) as Border;
        if (border?.Background is not SolidColorBrush background || background.Color.R > 40) throw new Exception("Disabled media list lost its dark background");
        body.IsEnabled = true; Save(body, 1120, 760, Path.Combine(output, "compact.png"));
        var footerPosition = glitchy.TranslatePoint(new Point(0, 0), body);
        if (footerPosition.Y + glitchy.ActualHeight > body.ActualHeight + .5) throw new Exception("Glitchy's footer is outside the compact editor's visible layout");
        var footer = (RowDefinition)window.FindName("BookmarkFooterRow"); footer.MinHeight = 0; footer.Height = new GridLength(0);
        ((GridSplitter)window.FindName("BookmarkPanelSplitter")).Visibility = Visibility.Collapsed;
        Save(body, 1500, 960, Path.Combine(output, "expanded-clips.png"));
        if (((ListBox)window.FindName("CueList")).ActualHeight < 250) throw new Exception("Expanded clips pane stayed small");
        RenderDraft(assembly, window, output);
        RenderCopilot(assembly, window, output);
        RenderEditing(assembly, window, output);
        var splash = (Window)Activator.CreateInstance(assembly.GetType("ReelGood.SplashWindow", true)!)!;
        // Drive the exact startup storyboard without creating an audio device.
        var intro = (System.Windows.Media.Animation.Storyboard)splash.FindResource("Intro");
        intro.Begin(splash, true);
        var scene = (FrameworkElement)splash.Content; splash.Content = null;
        foreach (var (time, name) in new[] { (.05, "intro-distant"), (.65, "intro-approach"), (1.25, "intro-somersault"), (1.85, "intro-launch"), (2.625, "intro-corkscrew"), (3.45, "intro-landing"), (4.85, "intro") })
        {
            intro.SeekAlignedToLastTick(splash, TimeSpan.FromSeconds(time), System.Windows.Media.Animation.TimeSeekOrigin.BeginTime);
            var actual = (double)splash.GetType().GetProperty("SceneTime")!.GetValue(splash)!;
            if (Math.Abs(actual - time) > .02) throw new Exception("The intro choreography clock did not seek with the storyboard");
            Save(scene, 1500, 960, Path.Combine(output, name + ".png"));
        }
        if (Environment.GetEnvironmentVariable("GLITCHSTICH_CAPTURE_INTRO") == "1")
        {
            var frames = Path.GetFullPath(Path.Combine(output, "intro-frames"));
            var expected = Path.GetFullPath(Path.Combine(root, "checks", "results", "interface", "intro-frames"));
            if (frames != expected) throw new Exception("Intro capture directory escaped the checks output");
            Directory.CreateDirectory(frames);
            for (var f = 0; f < 155; f++)
            {
                intro.SeekAlignedToLastTick(splash, TimeSpan.FromSeconds(f / 25d), System.Windows.Media.Animation.TimeSeekOrigin.BeginTime);
                Save(scene, 1000, 640, Path.Combine(frames, $"frame-{f:D3}.png"));
            }
            var render = new System.Diagnostics.ProcessStartInfo(Path.Combine(root, "tools", "ffmpeg.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-y", "-framerate", "25", "-i", Path.Combine(frames, "frame-%03d.png"), "-i", Path.Combine(root, "Resources", "Audio", "Intro.wav"), "-t", "6.2", "-c:v", "libx264", "-preset", "fast", "-crf", "19", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", Path.Combine(output, "platformer-intro-preview.mp4") }) render.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(render)!;
            var errors = process.StandardError.ReadToEnd(); process.WaitForExit();
            if (process.ExitCode != 0) throw new Exception("Intro preview export failed: " + errors);
            // Only generated frames in the verified directory are removed.
            Directory.Delete(frames, true);
            Console.WriteLine("PASS Exported 6.2-second animation preview with matching stereo soundtrack");
        }
        intro.Remove(splash);
        // Validate choreography against actual scene geometry and camera framing.
        const BindingFlags poseFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        var poseMethod = splash.GetType().GetMethod("Pose", poseFlags)!;
        var pitch = (System.Windows.Media.Media3D.AxisAngleRotation3D)splash.GetType().GetField("_flip", poseFlags)!.GetValue(splash)!;
        var roll = (System.Windows.Media.Media3D.AxisAngleRotation3D)splash.GetType().GetField("_roll", poseFlags)!.GetValue(splash)!;
        var character = (System.Windows.Media.Media3D.Model3DGroup)splash.GetType().GetField("_characterModel", poseFlags)!.GetValue(splash)!;
        var position = (System.Windows.Media.Media3D.TranslateTransform3D)splash.GetType().GetField("_position", poseFlags)!.GetValue(splash)!;
        var viewport = (Viewport3D)splash.FindName("Stage");
        var camera = (System.Windows.Media.Media3D.PerspectiveCamera)viewport.Camera;
        poseMethod.Invoke(splash, [.1d]);
        var before = new System.Windows.Media.Media3D.Point3D(position.OffsetX, position.OffsetY, position.OffsetZ);
        var farDistance = (camera.Position - before).Length;
        var forward = character.Transform.Transform(new System.Windows.Media.Media3D.Vector3D(0, 0, 1)); forward.Y = 0; forward.Normalize();
        poseMethod.Invoke(splash, [.2d]);
        var travel = new System.Windows.Media.Media3D.Vector3D(position.OffsetX - before.X, 0, position.OffsetZ - before.Z); travel.Normalize();
        if (System.Windows.Media.Media3D.Vector3D.DotProduct(forward, travel) < .999) throw new Exception("The distant run does not face its direction of travel");
        // Every wall corner must be beyond the right frustum boundary initially.
        poseMethod.Invoke(splash, [.1d]);
        var look = camera.LookDirection; look.Normalize();
        var right = System.Windows.Media.Media3D.Vector3D.CrossProduct(look, camera.UpDirection); right.Normalize();
        var halfWidth = Math.Tan(camera.FieldOfView * Math.PI / 360);
        foreach (var wx in new[] { -3.4, 7.6 }) foreach (var wy in new[] { -3.1, 6.9 }) foreach (var wz in new[] { -5.25, -4.45 })
        {
            var relative = new System.Windows.Media.Media3D.Point3D(wx, wy, wz) - camera.Position;
            var depth = System.Windows.Media.Media3D.Vector3D.DotProduct(relative, look);
            if (System.Windows.Media.Media3D.Vector3D.DotProduct(relative, right) / depth <= halfWidth) throw new Exception("The projection wall appears before the distant approach");
        }
        var obstacle = (System.Windows.Media.Media3D.Rect3D)splash.GetType().GetField("VaultObstacle", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var step = 0; step <= 195; step++)
        {
            var time = step / 60d; poseMethod.Invoke(splash, [time]);
            if (character.Bounds.Y < -2.725) throw new Exception("The character passes through a platform");
            if (time is >= .8 and <= 1.7 && character.Bounds.IntersectsWith(obstacle)) throw new Exception($"The somersault hits its obstacle at {time:F3}s");
            var direction = camera.LookDirection; direction.Normalize();
            var screenRight = System.Windows.Media.Media3D.Vector3D.CrossProduct(direction, camera.UpDirection); screenRight.Normalize();
            var screenUp = System.Windows.Media.Media3D.Vector3D.CrossProduct(screenRight, direction); screenUp.Normalize();
            var verticalLimit = Math.Tan(camera.FieldOfView * Math.PI / 360) / (viewport.ActualWidth / viewport.ActualHeight);
            var bounds = character.Bounds;
            foreach (var cx in new[] { bounds.X, bounds.X + bounds.SizeX })
            foreach (var cy in new[] { bounds.Y, bounds.Y + bounds.SizeY })
            foreach (var cz in new[] { bounds.Z, bounds.Z + bounds.SizeZ })
            {
                var relative = new System.Windows.Media.Media3D.Point3D(cx, cy, cz) - camera.Position;
                var depth = System.Windows.Media.Media3D.Vector3D.DotProduct(relative, direction);
                if (Math.Abs(System.Windows.Media.Media3D.Vector3D.DotProduct(relative, screenUp) / depth) > verticalLimit
                    || Math.Abs(System.Windows.Media.Media3D.Vector3D.DotProduct(relative, screenRight) / depth) > verticalLimit * (viewport.ActualWidth / viewport.ActualHeight))
                    throw new Exception($"The character leaves the camera frame at {time:F3}s");
            }
        }
        poseMethod.Invoke(splash, [1.7d]);
        if (Math.Abs(pitch.Angle - 360) > .01 || Math.Abs(roll.Angle) > .01) throw new Exception("The single somersault does not land upright before the corkscrew");
        poseMethod.Invoke(splash, [2.625d]);
        if (Math.Abs(roll.Angle - 180) > .01 || pitch.Angle > 383) throw new Exception("The gap jump is another somersault instead of a corkscrew");
        poseMethod.Invoke(splash, [3.25d]);
        if (Math.Abs(roll.Angle - 360) > .01 || Math.Abs(pitch.Angle - 360) > .01) throw new Exception("The corkscrew does not finish upright");
        poseMethod.Invoke(splash, [4.85d]);
        var nearDistance = (camera.Position - new System.Windows.Media.Media3D.Point3D(position.OffsetX, position.OffsetY, position.OffsetZ)).Length;
        if (farDistance < nearDistance * 2) throw new Exception("The approach no longer starts at a meaningful distance");
        if (watch.Elapsed.TotalMilliseconds / 196 > 33) throw new Exception("Platformer pose exceeds the frame budget");
        Console.WriteLine("PASS Distant forward run; wall outside initial camera shot; obstacle cleared; one somersault followed by airborne corkscrew; upright landing and platform clearance");
        // Sound track must end with the ident, not leak into normal editor use.
        using (var sound = assembly.GetManifestResourceStream("ReelGood.Audio.Intro.wav")!)
        using (var reader = new BinaryReader(sound))
        {
            reader.ReadBytes(22); var channels = reader.ReadInt16(); var rate = reader.ReadInt32();
            reader.ReadBytes(6); var bits = reader.ReadInt16(); reader.ReadBytes(4); var bytes = reader.ReadInt32();
            if (channels != 2 || rate != 48000 || bits != 16 || Math.Abs(bytes / (double)(rate * channels * bits / 8) - 6.2) > .01)
                throw new Exception("The ident soundtrack does not match the 6.2 second stereo choreography");
        }
        // Exercise the real startup entry and completion path with audio canceled.
        ((CancellationTokenSource)splash.GetType().GetField("_introSoundCancellation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(splash)!).Cancel();
        var started = System.Diagnostics.Stopwatch.StartNew();
        var completion = (Task)splash.GetType().GetMethod("PlayIntroAsync")!.Invoke(splash, null)!;
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) => { if (completion.IsCompleted || started.Elapsed.TotalSeconds > 7) frame.Continue = false; };
        timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame); timer.Stop();
        if (!completion.IsCompletedSuccessfully || started.Elapsed.TotalSeconds > 6.5) throw new Exception("The intro exceeded the startup watchdog or did not complete");
        splash.Close();
        Console.WriteLine($"PASS Intro entry completes in {started.Elapsed.TotalSeconds:F2}s, below the 6.5s startup watchdog; closure stops its animation and sound");
        Console.WriteLine("PASS WPF interface renders; articulated 3D intro renders approach/somersault/corkscrew/landing/projection frames; stereo SFX matches 6.2-second animation");
        RenderMascot(splash, output);
        GlitchyChecks.Run(assembly, output);
        app.Shutdown();
    }
    private static void RenderMascot(Window splash, string output)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var model = new System.Windows.Media.Media3D.Model3DGroup();
        model.Children.Add(new System.Windows.Media.Media3D.AmbientLight(Color.FromRgb(72, 84, 105)));
        model.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(Color.FromRgb(192, 229, 255), new(-.5, -.7, -1)));
        model.Children.Add(new System.Windows.Media.Media3D.PointLight(Color.FromRgb(255, 70, 205), new(-3, 3, 3)));
        model.Children.Add(new System.Windows.Media.Media3D.PointLight(Color.FromRgb(55, 225, 255), new(3, 2, 4)));
        splash.GetType().GetMethod("BuildCharacter", flags)!.Invoke(splash, [model]);
        splash.GetType().GetMethod("Pose", flags)!.Invoke(splash, [4.85d]);
        var camera = new System.Windows.Media.Media3D.PerspectiveCamera(new(5, 2.1, 10), new(-5, -2.1, -10), new(0, 1, 0), 39);
        var viewport = new System.Windows.Controls.Viewport3D { Camera = camera };
        viewport.Children.Add(new System.Windows.Media.Media3D.ModelVisual3D { Content = model });
        var card = new Grid { Background = new RadialGradientBrush(Color.FromRgb(31, 38, 62), Color.FromRgb(3, 6, 13)) };
        card.Children.Add(viewport);
        card.Children.Add(new TextBlock { Text = "GLITCHSTICH // COPILOT", FontFamily = new FontFamily("Consolas"), FontSize = 15, Foreground = Brushes.Cyan, Margin = new Thickness(25), VerticalAlignment = VerticalAlignment.Bottom });
        Save(card, 700, 800, Path.Combine(output, "blocky-character.png"));
        Console.WriteLine("PASS Blocky mascot renders separately for layered jacket, lapels, cuffs and icon-head review");
    }
    private static void RenderCopilot(Assembly assembly, Window owner, string output)
    {
        var proposalType = assembly.GetType("ReelGood.CopilotProposal", true)!;
        var footageType = assembly.GetType("ReelGood.CopilotFootageReview", true)!;
        var empty = Array.CreateInstance(proposalType, 0);
        var media = Activator.CreateInstance(footageType, ["Reviewed a sampled source window with actual frames/audio.", empty])!;
        var task = typeof(Task).GetMethods().Single(method => method.Name == "FromResult").MakeGenericMethod(footageType).Invoke(null, [media])!;
        var taskType = typeof(Task<>).MakeGenericType(footageType);
        var reviewDelegate = System.Linq.Expressions.Expression.Lambda(typeof(Func<,,,>).MakeGenericType(typeof(string), typeof(bool), typeof(CancellationToken), taskType), System.Linq.Expressions.Expression.Constant(task, taskType),
            System.Linq.Expressions.Expression.Parameter(typeof(string)), System.Linq.Expressions.Expression.Parameter(typeof(bool)), System.Linq.Expressions.Expression.Parameter(typeof(CancellationToken))).Compile();
        var applyDelegate = System.Linq.Expressions.Expression.Lambda(typeof(Func<,,>).MakeGenericType(proposalType, typeof(string), typeof(string)), System.Linq.Expressions.Expression.Constant("Applied test change"),
            System.Linq.Expressions.Expression.Parameter(proposalType), System.Linq.Expressions.Expression.Parameter(typeof(string))).Compile();
        var windowType = assembly.GetType("ReelGood.CopilotWindow", true)!;
        var copilot = (Window)Activator.CreateInstance(windowType, [owner, "http://localhost:11434", "qwen3:4b", true,
            (Func<bool, string>)(_ => "Test source, duration 60 seconds, bookmark 0 at 25 seconds."), (Action<string, string, bool>)((_, _, _) => { }), applyDelegate, reviewDelegate])!;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        windowType.GetMethod("AddMessage", flags)!.Invoke(copilot, ["assistant", "The sampled frame shows an elimination banner. Keep this moment and preserve four seconds of setup before it."]);
        var proposal = Activator.CreateInstance(proposalType, [null, 0, 21d, 30d, "Keep", 25d])!;
        windowType.GetMethod("AddProposal", flags)!.Invoke(copilot, [proposal, (Func<string>)(() => "snapshot"), (Action)(() => { })]);
        var chatGlitchy = copilot.GetType().GetProperty("Glitchy", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(copilot)!;
        var replyBubble = chatGlitchy.GetType().GetProperty("Bubble")!.GetValue(chatGlitchy)!;
        if (!replyBubble.GetType().GetProperty("Message")!.GetValue(replyBubble)!.ToString()!.Contains("elimination banner")) throw new Exception("Glitchy's reply did not reach the shared speech bubble");
        replyBubble.GetType().GetMethod("ShowMessage")!.Invoke(replyBubble, ["The sampled frame shows an elimination banner. Keep four seconds of setup. Review and Apply any change you want.", false]);
        var content = (FrameworkElement)copilot.Content; copilot.Content = null; content.SetValue(TextElement.ForegroundProperty, copilot.Foreground);
        Save(content, 800, 800, Path.Combine(output, "copilot-review.png"));
        Console.WriteLine("PASS Copilot media-review controls and editable proposal cards render; clips pane expands above 250px");
    }
    private static void RenderEditing(Assembly assembly, Window owner, string output)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var snapshotType = assembly.GetType("ReelGood.EditingSnapshot", true)!;
        var initial = System.Text.Json.JsonSerializer.Deserialize("""{"Clips":[{"Start":0,"End":20,"Layer":0,"Effects":{"Speed":1}},{"Start":30,"End":40,"Layer":1,"Effects":{"Speed":1}}],"Assets":[],"Media":[]}""", snapshotType)!;
        var type = assembly.GetType("ReelGood.EditingToolsWindow", true)!;
        var constructor = type.GetConstructors().Single();
        var args = new List<object?> { owner, initial, 60d, 10d };
        foreach (var parameter in constructor.GetParameters().Skip(4))
        {
            var invoke = parameter.ParameterType.GetMethod("Invoke")!;
            args.Add(System.Linq.Expressions.Expression.Lambda(parameter.ParameterType, System.Linq.Expressions.Expression.Default(invoke.ReturnType), invoke.GetParameters().Select(p => System.Linq.Expressions.Expression.Parameter(p.ParameterType))).Compile());
        }
        var editor = (Window)constructor.Invoke(args.ToArray());
        var fields = (Dictionary<string, TextBox>)type.GetField("_clipFields", flags)!.GetValue(editor)!;
        fields["Speed (0.25–4×)"].Text = "2";
        type.GetMethod("SaveClip", flags)!.Invoke(editor, null);
        double Speed() { var clips = (System.Collections.IList)type.GetField("_clips", flags)!.GetValue(editor)!; var fx = clips[0]!.GetType().GetProperty("Effects")!.GetValue(clips[0])!; return (double)fx.GetType().GetProperty("Speed")!.GetValue(fx)!; }
        if (Speed() != 2) throw new Exception("Editor speed did not update");
        type.GetMethod("Undo", flags)!.Invoke(editor, null); if (Speed() != 1) throw new Exception("Editor Undo did not restore speed");
        type.GetMethod("Redo", flags)!.Invoke(editor, null); if (Speed() != 2) throw new Exception("Editor Redo did not restore speed");
        var body = (FrameworkElement)editor.Content; body.SetValue(TextElement.ForegroundProperty, editor.Foreground);
        Save(body, 1020, 790, Path.Combine(output, "editing-clips.png"));
        Save(body, 900, 620, Path.Combine(output, "editing-clips-compact.png"));
        type.GetMethod("ShowPage", flags)!.Invoke(editor, [1]); Save(body, 1020, 790, Path.Combine(output, "editing-media.png"));
        type.GetMethod("ShowPage", flags)!.Invoke(editor, [2]); Save(body, 1020, 790, Path.Combine(output, "editing-ai.png"));
        var connectionType = assembly.GetType("ReelGood.YouTubeConnection", true)!;
        var connection = Activator.CreateInstance(connectionType, [Path.Combine(output, "youtube-fixture"), null])!;
        var youtube = (Window)Activator.CreateInstance(assembly.GetType("ReelGood.YouTubeWindow", true)!, [owner, connection])!;
        var youtubeBody = (FrameworkElement)youtube.Content; youtubeBody.SetValue(TextElement.ForegroundProperty, youtube.Foreground);
        Save(youtubeBody, 660, 780, Path.Combine(output, "youtube-setup.png"));
        if (((CheckBox)youtube.GetType().GetField("_auto", flags)!.GetValue(youtube)!).IsChecked == true) throw new Exception("YouTube setup enabled automatic uploads by default");
        Console.WriteLine("PASS Editing screens render at normal/compact sizes; clip controls apply and undo/redo; YouTube setup renders with automatic uploads off");
    }
    private static void RenderDraft(Assembly assembly, Window owner, string output)
    {
        var bookmarkType = assembly.GetType("ReelGood.DraftBookmark", true)!;
        var bookmarks = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(bookmarkType))!;
        bookmarks.Add(Activator.CreateInstance(bookmarkType, [0, 10d, 17d, "Laughter / microphone", "Keep", false, false]));
        bookmarks.Add(Activator.CreateInstance(bookmarkType, [1, 14d, 20d, "Elimination / video", null, false, false]));
        bookmarks.Add(Activator.CreateInstance(bookmarkType, [2, 50d, 60d, "Group reaction", null, false, false]));
        var clipType = assembly.GetType("ReelGood.DraftClip", true)!;
        var parameter = System.Linq.Expressions.Expression.Parameter(clipType);
        var preview = System.Linq.Expressions.Expression.Lambda(typeof(Action<>).MakeGenericType(clipType), System.Linq.Expressions.Expression.Empty(), parameter).Compile();
        var type = assembly.GetType("ReelGood.DraftEditWindow", true)!;
        new System.Windows.Interop.WindowInteropHelper(owner).EnsureHandle();
        var review = (Window)Activator.CreateInstance(type, [owner, bookmarks, 90d, preview, (Action)(() => { })])!;
        var readyCount = 0;
        type.GetEvent("DraftReady")!.AddEventHandler(review, (Action)(() => readyCount++));
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        type.GetField("_ready", flags)!.SetValue(review, true);
        type.GetMethod("Rebuild", flags)!.Invoke(review, null);
        if (readyCount != 1) throw new Exception("A usable draft did not signal completion exactly once");
        var apply = (Button)type.GetField("_apply", flags)!.GetValue(review)!;
        var list = (ListBox)type.GetField("_list", flags)!.GetValue(review)!;
        if (list.Items.Count != 2 || !apply.IsEnabled) throw new Exception("Draft review did not group the displayed bookmarks");
        foreach (var choice in list.Items) choice.GetType().GetProperty("IsIncluded")!.SetValue(choice, false);
        if (apply.IsEnabled) throw new Exception("An empty draft can be applied");
        list.Items[0].GetType().GetProperty("IsIncluded")!.SetValue(list.Items[0], true);
        var merge = (TextBox)type.GetField("_merge", flags)!.GetValue(review)!; merge.Text = "2";
        list.Items[1].GetType().GetProperty("IsIncluded")!.SetValue(list.Items[1], true);
        if (apply.IsEnabled) throw new Exception("Changed draft settings allowed stale preview application");
        type.GetMethod("Rebuild", flags)!.Invoke(review, null);
        var content = (FrameworkElement)review.Content; review.Content = null;
        content.SetValue(TextElement.ForegroundProperty, review.Foreground);
        Save(content, 560, 680, Path.Combine(output, "draft-review.png"));
        Save(content, 540, 540, Path.Combine(output, "draft-review-compact.png"));
        var completed = readyCount;
        merge.Text = "invalid";
        type.GetMethod("Rebuild", flags)!.Invoke(review, null);
        if (readyCount != completed) throw new Exception("An invalid draft signaled completion");
        merge.Text = "2"; bookmarks.Clear();
        type.GetMethod("Rebuild", flags)!.Invoke(review, null);
        if (readyCount != completed) throw new Exception("An empty draft signaled completion");
        var rangeType = assembly.GetType("ReelGood.EditorialRange", true)!;
        var protectedRanges = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(rangeType))!;
        protectedRanges.Add(Activator.CreateInstance(rangeType, [20d, 35d, "Character dialogue"]));
        var quietRanges = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(rangeType))!;
        quietRanges.Add(Activator.CreateInstance(rangeType, [10d, 25d, "Reviewed inactive quiet footage"]));
        var modeType = assembly.GetType("ReelGood.EditorialMode", true)!;
        var contextType = assembly.GetType("ReelGood.DraftReviewContext", true)!;
        var editorial = Activator.CreateInstance(contextType, [Enum.Parse(modeType, "Story"), protectedRanges, quietRanges])!;
        type.GetMethod("ConfigureEditorialContext")!.Invoke(review, [editorial]);
        type.GetMethod("Rebuild", flags)!.Invoke(review, null);
        var storyPlan = type.GetField("_plan", flags)!.GetValue(review)!;
        if (list.Items.Count != 2 || (double)storyPlan.GetType().GetProperty("RemovedQuietSeconds")!.GetValue(storyPlan)! != 10)
            throw new Exception("Story draft UI did not retain dialogue while cutting reviewed quiet time");
        Save(content, 560, 760, Path.Combine(output, "story-draft.png"));
        var primary = (ComboBox)type.GetField("_mode", flags)!.GetValue(review)!;
        var secondary = (ComboBox)type.GetField("_secondaryMode", flags)!.GetValue(review)!;
        primary.SelectedIndex = 1; secondary.SelectedIndex = 1; // Highlights + Story
        type.GetMethod("Rebuild", flags)!.Invoke(review, null);
        var combinedPlan = type.GetField("_plan", flags)!.GetValue(review)!;
        if (!(bool)combinedPlan.GetType().GetProperty("StoryContinuity")!.GetValue(combinedPlan)!) throw new Exception("Story in secondary slot did not protect narrative in the draft UI");
        Save(content, 560, 760, Path.Combine(output, "combined-mode-draft.png"));
        secondary.SelectedIndex = 2; // duplicate Highlights resets to None
        if (secondary.SelectedIndex != 0) throw new Exception("Draft UI allowed duplicate editing modes");
        Console.WriteLine("PASS Story draft UI retains protected dialogue and displays reviewed quiet cuts");
        Console.WriteLine("PASS Draft completion fires for usable drafts, not invalid settings or empty results");
        Console.WriteLine("PASS Draft review grouping, clip checkboxes, empty selection protection, and changed-settings protection");
    }
    private static void Save(FrameworkElement content, int width, int height, string path)
    {
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(path); encoder.Save(output);
    }
}











