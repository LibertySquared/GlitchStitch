using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace GlitchStich;

public partial class MainWindow
{
    private async void Record_Click(object sender, RoutedEventArgs e)
    {
        if (_captureProcess is not null) { await StopCaptureAsync(); return; }
        if (_busyOperations > 0 || !await EnsureEngineAsync()) return;
        if (SystemAudioCheck.IsChecked != true && MicCheck.IsChecked != true)
        { StatusText.Text = "Choose at least one audio source."; return; }
        if (!ResolveUnsavedProjectChanges("Save before recording a new video?")) return;
        var dialog = new SaveFileDialog { Title = "Save screen recording", FileName = $"GlitchStich-{DateTime.Now:yyyyMMdd-HHmmss}.mp4", Filter = "MP4 video|*.mp4" };
        if (dialog.ShowDialog(this) != true) return;
        _captureOutputPath = dialog.FileName;
        var folder = Path.Combine(_settingsDirectory, "Recordings", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        _captureVideoPath = Path.Combine(folder, "video.mp4");
        SetBusy(true, "Starting Windows screen and audio capture…");
        try
        {
            _recordingAudio = new RecordingAudioSession();
            _recordingAudio.Start(folder, SystemAudioCheck.IsChecked == true, MicCheck.IsChecked == true);
            var start = new ProcessStartInfo(_ffmpegPath!)
            { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var arg in new[] { "-y", "-f", "gdigrab", "-framerate", "60", "-i", "desktop", "-an", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", _captureVideoPath })
                start.ArgumentList.Add(arg);
            _captureProcess = new Process { StartInfo = start };
            _captureProcess.Start();
            _captureErrors = _captureProcess.StandardError.ReadToEndAsync();
            await Task.Delay(250);
            if (_captureProcess.HasExited) throw new InvalidOperationException(await _captureErrors);
            RecordButton.Content = "■  Stop recording";
            StatusText.Text = "Recording desktop with separate Windows system and microphone audio tracks.";
        }
        catch (Exception ex)
        {
            if (_recordingAudio is not null) try { await _recordingAudio.StopAsync(); } catch { }
            _recordingAudio = null;
            _captureProcess?.Dispose(); _captureProcess = null;
            StatusText.Text = $"Could not start recording: {ex.Message}";
        }
        finally { SetBusy(false); }
    }

    private async Task StopCaptureAsync()
    {
        var process = _captureProcess;
        if (process is null) return;
        _captureProcess = null;
        var audio = _recordingAudio; _recordingAudio = null;
        SetBusy(true, "Finishing recording and saving separate audio tracks…");
        var completed = false;
        try
        {
            var audioStop = audio?.StopAsync() ?? Task.CompletedTask;
            if (!process.HasExited)
            {
                await process.StandardInput.WriteLineAsync("q");
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            }
            await audioStop;
            var errors = _captureErrors is null ? "" : await _captureErrors;
            if (process.ExitCode != 0) throw new InvalidOperationException(errors);
            await RunFfmpegAsync(RecordingMux.Build(_captureVideoPath!, audio?.Files ?? [], _captureOutputPath!));
            completed = true;
            foreach (var file in audio?.Files ?? []) File.Delete(file.Path);
            File.Delete(_captureVideoPath!);
            var folder = Path.GetDirectoryName(_captureVideoPath!);
            if (folder is not null && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
            StatusText.Text = "Recording saved with separate audio tracks.";
        }
        catch (Exception ex)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            StatusText.Text = $"Recording could not finish: {ex.Message}. Recovery files: {Path.GetDirectoryName(_captureVideoPath)}";
        }
        finally
        {
            RecordButton.Content = "●  Record"; process.Dispose(); SetBusy(false);
        }
        if (completed && !_isClosing && _captureOutputPath is not null && ResolveUnsavedProjectChanges("Save before opening the recording?"))
            await LoadVideoAsync(_captureOutputPath);
    }
}
