using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ReelGood;

internal static class WindowWorkArea
{
    private static readonly DependencyProperty AttachedProperty = DependencyProperty.RegisterAttached("Attached", typeof(bool), typeof(WindowWorkArea), new PropertyMetadata(false));
    internal static void Attach(Window window)
    {
        if ((bool)window.GetValue(AttachedProperty)) return;
        window.SetValue(AttachedProperty, true);
        HwndSource? source = null;
        var minimumWidth = window.MinWidth; var minimumHeight = window.MinHeight;
        void FitMinimums()
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor)) return;
            var dpi = VisualDpi(window);
            window.MinWidth = Math.Min(minimumWidth, (monitor.Work.Right - monitor.Work.Left) / dpi.X);
            window.MinHeight = Math.Min(minimumHeight, (monitor.Work.Bottom - monitor.Work.Top) / dpi.Y);
            if (window.WindowState == WindowState.Normal)
            {
                if (double.IsFinite(window.Width)) window.Width = Math.Min(window.Width, (monitor.Work.Right - monitor.Work.Left) / dpi.X);
                if (double.IsFinite(window.Height)) window.Height = Math.Min(window.Height, (monitor.Work.Bottom - monitor.Work.Top) / dpi.Y);
            }
        }
        IntPtr Hook(IntPtr handle, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            if (message == 0x24)
            {
                var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor))
                {
                    var info = Marshal.PtrToStructure<MinMaxInfo>(lparam);
                    var bounds = Calculate(monitor.Monitor, monitor.Work);
                    info.MaxPosition = new PointI(bounds.Left, bounds.Top);
                    info.MaxSize = new PointI(bounds.Right, bounds.Bottom);
                    info.MaxTrackSize = info.MaxSize;
                    info.MinTrackSize.X = Math.Min(info.MinTrackSize.X, info.MaxSize.X);
                    info.MinTrackSize.Y = Math.Min(info.MinTrackSize.Y, info.MaxSize.Y);
                    Marshal.StructureToPtr(info, lparam, false); handled = true;
                }
            }
            if (message is 0x2E0 or 0x7E) window.Dispatcher.BeginInvoke(FitMinimums);
            return IntPtr.Zero;
        }
        window.SourceInitialized += (_, _) => { source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle); source?.AddHook(Hook); FitMinimums(); };
        window.StateChanged += (_, _) => FitMinimums();
        window.Closed += (_, _) => source?.RemoveHook(Hook);
    }
    private static (double X, double Y) VisualDpi(Window window) { var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window); return (dpi.DpiScaleX, dpi.DpiScaleY); }
    // Position is relative to the monitor, including monitors with negative origins.
    internal static RectI Calculate(RectI monitor, RectI work) => new(work.Left - monitor.Left, work.Top - monitor.Top, work.Right - work.Left, work.Bottom - work.Top);
    [StructLayout(LayoutKind.Sequential)] internal struct PointI(int x, int y) { public int X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)] internal struct RectI(int left, int top, int right, int bottom) { public int Left = left, Top = top, Right = right, Bottom = bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public PointI Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] private struct MonitorInfo { public int Size; public RectI Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}

