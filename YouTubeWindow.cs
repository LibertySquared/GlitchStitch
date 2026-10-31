using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Win32;

namespace GlitchStich;

internal sealed class YouTubeWindow : Window
{
    private readonly YouTubeConnection _connection;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Ink("#9FBBC9") };
    private readonly ComboBox _privacy = new() { ItemsSource = new[] { "private", "unlisted", "public" }, SelectedIndex = 0 };
    private readonly TextBox _description = new() { AcceptsReturn = true, Height = 80, TextWrapping = TextWrapping.Wrap }, _tags = new(), _title = new();
    private readonly CheckBox _auto = new() { Content = "Automatically upload completed exports", IsChecked = false }, _kids = new() { Content = "This content is made for kids" };
    private readonly StackPanel _body = new(); private readonly ProgressBar _progress = new() { Height = 10, Minimum = 0, Maximum = 100 };
    private readonly CancellationTokenSource _life = new(); private CancellationTokenSource? _work;
    public YouTubeWindow(Window owner, YouTubeConnection connection)
    {
        Owner = owner; _connection = connection; Title = "GlitchStich / YouTube"; Width = 660; Height = 780; MinHeight = 600; MinWidth = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize; ShowInTaskbar = false; Background = Ink("#080E17"); Foreground = Ink("#EAF5FF");
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6) });
        var frame = new Border { Padding = new Thickness(18), Margin = new Thickness(4), Background = Background, BorderBrush = Ink("#51D9E4"), BorderThickness = new Thickness(1) }; CyberShape.SetCut(frame, 18); Content = frame;
        var root = new DockPanel(); frame.Child = root; var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var close = ActionButton("×", () => Close()); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var title = new TextBlock { Text = "YOUTUBE // CONNECT & UPLOAD", FontSize = 16, Foreground = Ink("#F8D35C"), VerticalAlignment = VerticalAlignment.Center }; title.MouseLeftButtonDown += (_, _) => DragMove(); header.Children.Add(title);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); footer.Children.Add(_progress); footer.Children.Add(_status);
        footer.Children.Add(ActionButton("Cancel current task", () => _work?.Cancel())); root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _body.Children.Add(new TextBlock { Text = "1. Enable YouTube Data API v3 in Google Cloud. Create a Desktop OAuth client and configure the consent screen/test users.\n2. Import its JSON file below.\n3. Connect in your browser and approve upload/channel access.\nCredentials and refresh tokens are encrypted for your Windows user and stored with GlitchStich data on D:.", TextWrapping = TextWrapping.Wrap, Foreground = Ink("#9FBBC9"), Margin = new Thickness(0, 0, 0, 10) });
        _body.Children.Add(ActionButton("Open Google Cloud setup", () => Process.Start(new ProcessStartInfo("https://console.cloud.google.com/apis/credentials") { UseShellExecute = true })));
        _body.Children.Add(ActionButton("Import Desktop OAuth client JSON", async () =>
        {
            var dialog = new OpenFileDialog { Filter = "Google OAuth client|*.json", Title = "Import Google Desktop OAuth client" };
            if (dialog.ShowDialog(this) != true) return;
            await Run(async token => { await _connection.ImportClientAsync(await File.ReadAllTextAsync(dialog.FileName, token)); _auto.IsChecked = false; _status.Text = "Client imported. Connect to authorize your channel."; });
        }));
        _body.Children.Add(ActionButton("Connect YouTube account", async () => await Run(async token => { _status.Text = "Finish Google authorization in your browser…"; var channel = await _connection.ConnectAsync(token); _status.Text = "Connected channel: " + channel; })));
        _body.Children.Add(ActionButton("Disconnect / revoke access", async () => await Run(async token => { await _connection.DisconnectAsync(token); _auto.IsChecked = false; _status.Text = "Disconnected. Automatic uploads are off."; })));
        Label("Upload privacy", _privacy); Label("Description", _description); Label("Tags (comma separated)", _tags); _body.Children.Add(_kids);
        _auto.Margin = new Thickness(0, 10, 0, 7); _body.Children.Add(_auto);
        _body.Children.Add(new TextBlock { Text = "Automatic uploads are OFF until you enable and save this option. They use the exported filename as the title. Previews and source recordings are not uploaded automatically. Google may restrict unaudited/testing apps to private uploads; the server's rules still apply.", TextWrapping = TextWrapping.Wrap, Foreground = Ink("#9FBBC9"), FontSize = 10 });
        _body.Children.Add(ActionButton("Save upload preferences", async () => await Run(async _ => { if (_auto.IsChecked == true && !await _connection.IsConnectedAsync()) throw new InvalidOperationException("Connect a channel before enabling automatic uploads."); _connection.SaveSettings(ReadSettings()); _status.Text = "Upload preferences saved."; })));
        Label("Manual upload title", _title);
        _body.Children.Add(ActionButton("Choose exported MP4 and upload now", async () =>
        {
            var dialog = new OpenFileDialog { Filter = "Exported MP4 video|*.mp4", Title = "Choose a video to upload" }; if (dialog.ShowDialog(this) != true) return;
            if (string.IsNullOrWhiteSpace(_title.Text)) _title.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            var confirm = CyberDialog.Show(this, $"Upload {Path.GetFileName(dialog.FileName)} to {_connection.Settings.Channel} as {_privacy.SelectedItem}?\n\nTitle: {_title.Text}", "Upload video?", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            await Run(async token =>
            {
                var progress = new Progress<(long Sent, long Total)>(value => { _progress.Value = value.Total == 0 ? 0 : value.Sent * 100d / value.Total; _status.Text = $"Uploading · {_progress.Value:0}%"; });
                var url = await _connection.UploadAsync(dialog.FileName, _title.Text, ReadSettings(), progress, token); _progress.Value = 100; _status.Text = "Uploaded: " + url + " · YouTube may still be processing the video.";
            });
        }));
        var settings = connection.Settings; _privacy.SelectedItem = settings.Privacy; _description.Text = settings.Description; _tags.Text = settings.Tags; _kids.IsChecked = settings.MadeForKids; _auto.IsChecked = settings.AutoUpload;
        Loaded += async (_, _) => { try { _status.Text = await connection.IsConnectedAsync() ? "Connected channel: " + connection.Settings.Channel : "Not connected. Import your Desktop OAuth client file to get started."; } catch (Exception ex) { _status.Text = ex.Message; } };
        Closed += (_, _) => _life.Cancel();
    }
    private YouTubeSettings ReadSettings() => _connection.Settings with { AutoUpload = _auto.IsChecked == true, Privacy = _privacy.SelectedItem as string ?? "private", Description = _description.Text, Tags = _tags.Text, MadeForKids = _kids.IsChecked == true };
    private async Task Run(Func<CancellationToken, Task> task)
    {
        if (_work is not null) return; using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_life.Token); _work = cancel; _body.IsEnabled = false;
        try { await task(cancel.Token); } catch (OperationCanceledException) { _status.Text = "Task canceled; local video files are unchanged."; } catch (Exception ex) { _status.Text = ex.Message; }
        finally { _work = null; _body.IsEnabled = true; }
    }
    private void Label(string text, FrameworkElement input) { _body.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0, 8, 0, 3) }); _body.Children.Add(input); }
    private static Button ActionButton(string text, Action action) { var button = new Button { Content = text, Margin = new Thickness(0, 3, 0, 3), Padding = new Thickness(9, 6, 9, 6) }; button.Click += (_, _) => action(); return button; }
    private static SolidColorBrush Ink(string color) => new((Color)ColorConverter.ConvertFromString(color));
}
