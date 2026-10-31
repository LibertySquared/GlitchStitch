using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Shell;
using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;

namespace GlitchStich;

internal sealed class EditedPreviewWindow : Window
{
    public EditedPreviewWindow(Window owner, string path)
    {
        Owner = owner; Title = "GlitchStich / Rendered preview · first 30 seconds"; Width = 960; Height = 640; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(8, 14, 23)); Foreground = Brushes.White;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize; MinWidth = 600; MinHeight = 400; ShowInTaskbar = false;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6) });
        var config = new Config(); config.Player.AutoPlay = false;
        var player = new Player(config); var host = new FlyleafHost { Player = player }; var root = new DockPanel();
        var frame = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(81, 217, 228)), BorderThickness = new Thickness(1), Padding = new Thickness(8), Child = root };
        CyberShape.SetCut(frame, 16); Content = frame;
        var header = new DockPanel { Margin = new Thickness(4, 0, 4, 8) }; DockPanel.SetDock(header, Dock.Top);
        var close = new Button { Content = "×", Width = 38, Height = 32 }; close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var title = new TextBlock { Text = "RENDERED PREVIEW // FIRST 30 SECONDS", Foreground = new SolidColorBrush(Color.FromRgb(248, 211, 92)), FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
        title.MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch { } }; header.Children.Add(title); root.Children.Add(header);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10) }; DockPanel.SetDock(controls, Dock.Bottom);
        var play = new Button { Content = "Play / pause", Margin = new Thickness(0, 0, 10, 0) }; play.Click += (_, _) => { if (player.Status.ToString() == "Playing") player.Pause(); else player.Play(); }; controls.Children.Add(play);
        var status = new TextBlock { Text = "Opening stitched result · mixed audio", VerticalAlignment = VerticalAlignment.Center }; controls.Children.Add(status); root.Children.Add(controls); root.Children.Add(host);
        var opening = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) }; timer.Tick += (_, _) =>
        {
            if (player.CanPlay) { timer.Stop(); status.Text = "Ready · click Play to review the stitched result"; }
            else if (opening.Elapsed > TimeSpan.FromSeconds(25)) { timer.Stop(); status.Text = "Preview could not open. Close this window and retry."; }
        };
        Loaded += (_, _) => { player.OpenAsync(path); timer.Start(); };
        var released = false; var closing = false;
        Closing += async (_, args) =>
        {
            if (released) return; args.Cancel = true; if (closing) return; closing = true; timer.Stop(); Hide();
            try { await PlaybackShutdown.ReleaseAsync(() => { host.Player = null; host.Dispose(); }, player.Dispose); }
            finally { released = true; Close(); }
        };
    }
}
