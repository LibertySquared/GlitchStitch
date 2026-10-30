using System.Windows.Controls;
using System.Windows.Threading;

namespace ReelGood;

public partial class MainWindow
{
    private double _sourcePlaybackSpeed = 1;
    private bool _playbackRequested;
    private bool _previewSeekPending;
    private int _pendingSeekMilliseconds;
    private DateTime _previewSeekStarted;
    private readonly HashSet<MediaElement> _readyStemPlayers = [];
    private readonly Dictionary<MediaElement, DateTime> _lastStemResync = [];

    private void SetPreviewPlaybackRequested(bool playing)
    {
        _playbackRequested = playing;
        PlayButton.Content = playing ? "Ⅱ" : "▶";
    }

    private void PreviewSeekCompleted(object? sender, int milliseconds)
    {
        // Flyleaf raises this from its decoder thread. Never synchronously wait
        // for WPF here: the UI may be initiating a seek or disposing the decoder.
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            if (_isClosing || !_previewSeekPending) return;
            if (milliseconds >= 0 && Math.Abs(milliseconds - _pendingSeekMilliseconds) > 50) return;
            _previewSeekPending = false;
            _sequenceSeekUntil = DateTime.UtcNow.AddMilliseconds(150);
            if (milliseconds < 0) { PauseDraftPreview(); StatusText.Text = "Preview could not seek. Try that position again."; return; }
            // CurTime now represents the completed video seek, rather than the
            // previous frame that was still being decoded when the user clicked.
            SetStemPositions(PreviewPosition);
            StartPreviewPlayback();
        }));
    }

    private void StartPreviewPlayback()
    {
        if (_isClosing || _previewSeekPending || !_playbackRequested) return;
        _previewPlayer.Play(); ResumeStemPlayback();
    }

    private void ResumeStemPlayback()
    {
        if (_isClosing || _previewSeekPending) return;
        if (!_playbackRequested) return;
        foreach (var player in _stemPlayers)
            try { player.SpeedRatio = _sourcePlaybackSpeed; player.Play(); } catch { }
    }

    private void SynchronizeStemPlayback()
    {
        if (_isClosing) return;
        if (_previewSeekPending)
        {
            if (DateTime.UtcNow - _previewSeekStarted > TimeSpan.FromSeconds(8))
            { _previewSeekPending = false; PauseDraftPreview(); StatusText.Text = "Preview seek timed out. Try that position again."; }
            return;
        }
        if (!_playbackRequested)
        {
            if (_previewPlayer.IsPlaying) { _previewPlayer.Pause(); foreach (var player in _stemPlayers) try { player.Pause(); } catch { } }
            return;
        }
        var videoSeconds = PreviewSeconds;
        foreach (var player in _stemPlayers)
        {
            if (!_readyStemPlayers.Contains(player)) continue;
            try
            {
                var error = videoSeconds - player.Position.TotalSeconds;
                if (Math.Abs(error) > .08 && (!_lastStemResync.TryGetValue(player, out var last) || DateTime.UtcNow - last > TimeSpan.FromMilliseconds(750)))
                {
                    player.Position = PreviewPosition; player.SpeedRatio = _sourcePlaybackSpeed;
                    _lastStemResync[player] = DateTime.UtcNow;
                }
            }
            catch { /* A loading/ending audio track is resynchronized when ready. */ }
        }
    }
}
