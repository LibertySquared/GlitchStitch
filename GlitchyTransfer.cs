using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ReelGood;

internal static class GlitchyTransfer
{
    internal static async Task JumpAsync(GlitchyCompanion source, GlitchyCompanion target, Window flightOwner, CancellationToken cancellation)
    {
        target.Visibility = Visibility.Hidden;
        var destinationWindow = Window.GetWindow(target);
        destinationWindow?.UpdateLayout(); source.UpdateLayout(); target.UpdateLayout();
        var start = source.CharacterSurface.PointToScreen(new Point(0, 0));
        var end = target.CharacterSurface.PointToScreen(new Point(0, 0));
        var sourceDpi = VisualTreeHelper.GetDpi(source); var targetDpi = VisualTreeHelper.GetDpi(target);
        var flyer = new GlitchyCompanion { Width = 142, Height = 132, MinHeight = 0 };
        flyer.SetChatMode(); flyer.SetJumping(true);
        var overlay = new FlightWindow(start, end, new Size(142 * sourceDpi.DpiScaleX, 132 * sourceDpi.DpiScaleY), new Size(142 * targetDpi.DpiScaleX, 132 * targetDpi.DpiScaleY))
        {
            Owner = flightOwner, Content = new Viewbox { Child = flyer, Stretch = Stretch.Uniform }
        };
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(550)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        progress.Completed += (_, _) => done.TrySetResult();
        source.Bubble.Dismiss(); source.Visibility = Visibility.Hidden;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            overlay.Show(); overlay.Move(0);
            overlay.BeginAnimation(FlightWindow.ProgressProperty, progress);
            await done.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellation);
            await target.Dispatcher.InvokeAsync(() =>
            {
                if (destinationWindow?.IsVisible == true) { target.Arrive(); target.Visibility = Visibility.Visible; }
            });
        }
        catch (OperationCanceledException)
        {
            await source.Dispatcher.InvokeAsync(() =>
            {
                if (Window.GetWindow(source) is { IsVisible: true } sourceWindow && sourceWindow is not MainWindow { IsShuttingDown: true })
                { source.Visibility = Visibility.Visible; source.Arrive(); }
            });
        }
        finally
        {
            await overlay.Dispatcher.InvokeAsync(() =>
            {
                overlay.BeginAnimation(FlightWindow.ProgressProperty, null);
                flyer.Stop(); overlay.Close();
                if (destinationWindow?.IsVisible == true && !cancellation.IsCancellationRequested) target.Visibility = Visibility.Visible;
            });
        }
    }
    // Physical-pixel positions keep the flight aligned across DPI/monitor changes.
    private sealed class FlightWindow : Window
    {
        internal static readonly DependencyProperty ProgressProperty = DependencyProperty.Register("Progress", typeof(double), typeof(FlightWindow), new PropertyMetadata(0d, (d, e) => ((FlightWindow)d).Move((double)e.NewValue)));
        private readonly Point _start, _end; private readonly Size _fromSize, _toSize;
        internal FlightWindow(Point start, Point end, Size fromSize, Size toSize)
        {
            _start = start; _end = end; _fromSize = fromSize; _toSize = toSize;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            Width = 142; Height = 132; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
            ShowActivated = false; Focusable = false; IsHitTestVisible = false;
            SourceInitialized += (_, _) => { var handle = new WindowInteropHelper(this).Handle; SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x20 | 0x08000000); };
        }
        internal void Move(double progress)
        {
            var handle = new WindowInteropHelper(this).Handle; if (handle == IntPtr.Zero) return;
            var x = _start.X + (_end.X - _start.X) * progress;
            var y = _start.Y + (_end.Y - _start.Y) * progress - 130 * Math.Sin(Math.PI * progress);
            var width = _fromSize.Width + (_toSize.Width - _fromSize.Width) * progress;
            var height = _fromSize.Height + (_toSize.Height - _fromSize.Height) * progress;
            SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(width), (int)Math.Round(height), 0x0010 | 0x0004);
        }
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr handle, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}


