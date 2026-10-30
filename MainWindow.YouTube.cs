using System.IO;
using System.Windows;

namespace ReelGood;

public partial class MainWindow
{
    private YouTubeConnection? _youtube;
    private YouTubeConnection YouTube => _youtube ??= new(_settingsDirectory);
    private void OpenYouTube_Click(object sender, RoutedEventArgs e)
    {
        if (_busyOperations > 0 || _isClosing) return;
        new YouTubeWindow(this, YouTube).ShowDialog();
    }
    private async Task<string?> UploadAfterExportAsync(string path, IProgress<(long Sent, long Total)> progress, CancellationToken cancellation)
    {
        if (!YouTube.Settings.AutoUpload) return null;
        if (!await YouTube.IsConnectedAsync()) throw new InvalidOperationException("The export is saved, but the YouTube connection needs authorization.");
        return await YouTube.UploadAsync(path, Path.GetFileNameWithoutExtension(path), YouTube.Settings, progress, cancellation);
    }
}
