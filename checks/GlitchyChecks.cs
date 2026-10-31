using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class GlitchyChecks
{
    internal static void Run(Assembly assembly, string output)
    {
        var type = assembly.GetType("GlitchStich.GlitchyCompanion", true)!;
        FrameworkElement Create() => (FrameworkElement)Activator.CreateInstance(type)!;
        void Pose(FrameworkElement control, double time) => type.GetMethod("RenderPose", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, [time]);
        var source = Create();
        Pose(source, 8); Save(source, 620, 160, Path.Combine(output, "glitchy-gaming.png"));
        Pose(source, 22); Save(source, 620, 160, Path.Combine(output, "glitchy-cards.png"));
        type.GetMethod("Say")!.Invoke(source, ["I can review the scanned glitches and suggest a tighter stitch. You decide which changes to Apply.", true]);
        var bubble = type.GetProperty("Bubble")!.GetValue(source)!;
        bubble.GetType().GetMethod("ShowMessage")!.Invoke(bubble, ["I can review the scanned glitches and suggest a tighter stitch. You decide which changes to Apply.", false]);
        Pose(source, 23); Save(source, 620, 180, Path.Combine(output, "glitchy-speech.png"));
        var target = Create(); type.GetMethod("SetChatMode")!.Invoke(target, null);
        var owner = new Window { Width = 900, Height = 620, WindowStyle = WindowStyle.None, WindowStartupLocation = WindowStartupLocation.CenterScreen, Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Content = source };
        System.Windows.Shell.WindowChrome.SetWindowChrome(owner, new System.Windows.Shell.WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0) });
        assembly.GetType("GlitchStich.WindowWorkArea", true)!.GetMethod("Attach", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [owner]);
        var chat = new Window { Width = 780, Height = 580, WindowStartupLocation = WindowStartupLocation.CenterOwner, Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Content = target };
        try
        {
            owner.Show(); chat.Owner = owner; chat.Show(); Pump(Task.Delay(120));
            bool Running(FrameworkElement control) => (bool)type.GetProperty("AnimationRunning")!.GetValue(control)!;
            if (!Running(source)) throw new Exception("Glitchy's idle animation did not start when loaded");
            owner.WindowState = WindowState.Minimized; Pump(Task.Delay(80));
            if (Running(source)) throw new Exception("Glitchy's animation keeps running while minimized");
            owner.WindowState = WindowState.Normal; Pump(Task.Delay(80));
            if (!Running(source)) throw new Exception("Glitchy's animation did not resume after restore");
            owner.WindowState = WindowState.Maximized; Pump(Task.Delay(100));
            var handle = new WindowInteropHelper(owner).Handle;
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref info)) throw new Exception("Could not inspect the active monitor's work area");
            GetClientRect(handle, out var client); var origin = new PointI(); ClientToScreen(handle, ref origin);
            if (origin.Y + client.Bottom > info.Work.Bottom + 1 || origin.X + client.Right > info.Work.Right + 1 || origin.Y < info.Work.Top - 1 || origin.X < info.Work.Left - 1)
                throw new Exception($"Maximized client overlaps reserved screen space: {origin.X},{origin.Y} + {client.Right}x{client.Bottom}; work {info.Work.Left},{info.Work.Top}..{info.Work.Right},{info.Work.Bottom}");
            Console.WriteLine("PASS Native borderless maximized client stays inside the monitor work area; taskbar space is reserved");
            owner.WindowState = WindowState.Normal; chat.Show(); Pump(Task.Delay(70));
            var clicked = 0;
            type.GetEvent("ChatRequested")!.AddEventHandler(source, (EventHandler)((_, _) => clicked++));
            var surface = (FrameworkElement)type.GetProperty("CharacterSurface")!.GetValue(source)!;
            surface.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            if (clicked != 1) throw new Exception("The character is not the chat launcher");
            var transfer = assembly.GetType("GlitchStich.GlitchyTransfer", true)!.GetMethod("JumpAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
            Pump((Task)transfer.Invoke(null, [source, target, chat, CancellationToken.None])!);
            if (source.Visibility != Visibility.Hidden || target.Visibility != Visibility.Visible) throw new Exception("Glitchy did not transfer into chat");
            if (Application.Current.Windows.Cast<Window>().Any(w => w.GetType().Name == "FlightWindow")) throw new Exception("A flight overlay remains after arrival");
            Pump((Task)transfer.Invoke(null, [target, source, chat, CancellationToken.None])!);
            if (source.Visibility != Visibility.Visible || target.Visibility != Visibility.Hidden) throw new Exception("Glitchy did not return from chat");
            bubble.GetType().GetMethod("Dismiss")!.Invoke(bubble, null); Pump(Task.Delay(240));
            if (((FrameworkElement)bubble).Visibility != Visibility.Collapsed) throw new Exception("The speech bubble cannot be dismissed");
            var nextTip = (double)type.GetField("_nextTip", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
            if (nextTip < 590) throw new Exception("Dismissing a reminder did not postpone further reminders");
        }
        finally { chat.Close(); owner.Close(); }
        if ((bool)type.GetProperty("AnimationRunning")!.GetValue(source)! || (bool)type.GetProperty("AnimationRunning")!.GetValue(target)!) throw new Exception("A closed companion keeps animating");
        Console.WriteLine("PASS Glitchy gaming/cards/speech render; clickable chat, outward/return jumps, dismissible reminders, minimized/closed animation cleanup");
    }
    private static void Pump(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(4);
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) => { if (task.IsCompleted || DateTime.UtcNow > deadline) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
        if (!task.IsCompleted) throw new Exception("Glitchy UI operation timed out");
        task.GetAwaiter().GetResult();
    }
    private static void Save(FrameworkElement content, int width, int height, string file)
    {
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(content);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); using var output = File.Create(file); png.Save(output);
    }
    [StructLayout(LayoutKind.Sequential)] private struct PointI { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RectI { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public RectI Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr handle, out RectI rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr handle, ref PointI point);
}

