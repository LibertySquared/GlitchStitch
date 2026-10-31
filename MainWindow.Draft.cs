using System.IO;
using System.Windows;

namespace GlitchStich;

public partial class MainWindow
{
    private SavedDraftBackup? _draftBackup;

    private void GenerateDraft_Click(object sender, RoutedEventArgs e)
    {
        if (_busyOperations > 0 || _loadingSource || _captureProcess is not null || _isClosing)
        { StatusText.Text = "Finish the current operation before generating a draft."; return; }
        if (_videoPath is null || !File.Exists(_videoPath) || !double.IsFinite(_durationSeconds) || _durationSeconds <= 0)
        { StatusText.Text = "Import a video before generating a draft."; return; }
        if (VideoPreviewHost.Visibility != Visibility.Visible)
        { StatusText.Text = "Select the video in your media library before generating its draft."; return; }
        if (_cues.Count == 0 && _quietDowntimeRanges.Count == 0 && _protectedSceneRanges.Count == 0)
        { StatusText.Text = "Scan for moments or add bookmarks before generating a draft."; return; }

        var bookmarks = _cues.Select((cue, id) => new DraftBookmark(id, cue.TimeSeconds, cue.ClipEndSeconds,
            cue.EventLabel, cue.Decision, (cue.EventLabel.StartsWith("Closest video candidate:", StringComparison.OrdinalIgnoreCase) || cue.AiSuggestion?.StartsWith("AI suggests SKIP:", StringComparison.OrdinalIgnoreCase) == true),
            FootageVerdict.IsVerifiedOutcome(cue.TrackLabel, cue.EventLabel))).ToArray();
        var previousCue = _selectedCue; var previousSegment = _selectedVideoSegment;
        var previousStart = _clipStartSeconds; var previousEnd = _clipEndSeconds;
        var previousPosition = PreviewSeconds;
        PauseDraftPreview();
        var review = new DraftEditWindow(this, bookmarks, _durationSeconds, clip =>
        {
            if (!_previewPlayer.CanPlay) { StatusText.Text = "Video preview is not ready yet."; return; }
            PlayCue(new CueItem(clip.StartSeconds, "Draft preview", "Source video", clip.StartSeconds, 0, 0, clip.EndSeconds));
            // This callback is only invoked by the user's explicit Play clip button.
            SetPreviewPlaybackRequested(true); StartPreviewPlayback();
        }, PauseDraftPreview);
        review.ConfigureEditorialContext(new(CurrentEditorialMode, _protectedSceneRanges.ToArray(), _quietDowntimeRanges.ToArray()) { SecondaryMode = CurrentSecondaryEditorialMode });
        review.DraftReady += () =>
        {
            if (!_isClosing) _ = NotificationSound.PlayDraftReadyAsync(_notificationLifetime.Token);
        };
        bool approved = false;
        try { approved = review.ShowDialog() == true; }
        finally
        {
            PauseDraftPreview();
            _selectedCue = previousCue; _selectedVideoSegment = previousSegment;
            _clipStartSeconds = previousStart; _clipEndSeconds = previousEnd;
            SeekTo(previousPosition); BuildTimeline();
            ClipSelectedCueButton.Visibility = previousCue is null ? Visibility.Collapsed : Visibility.Visible;
            AddCueLayerButton.Visibility = ClipSelectedCueButton.Visibility;
        }
        if (!approved || review.ApprovedClips is not { Count: > 0 } clips)
        { StatusText.Text = "Draft dismissed · timeline unchanged."; return; }

        _draftBackup = new SavedDraftBackup(_videoLayerCount,
            _videoEditSegments.Select(segment => new SavedVideoSegment(segment.StartSeconds, segment.EndSeconds, segment.TrackIndex, segment.Effects)).ToList(),
            previousStart, previousEnd);
        var priorEffects = _videoEditSegments.ToArray(); _videoEditSegments.Clear();
        foreach (var clip in clips) _videoEditSegments.Add(new VideoEditSegment(clip.StartSeconds, clip.EndSeconds) { Effects = priorEffects.FirstOrDefault(previous => clip.StartSeconds >= previous.StartSeconds && clip.StartSeconds < previous.EndSeconds)?.Effects ?? new() });
        _videoLayerCount = 1;
        EditModeBox.SelectedIndex = (int)review.SelectedEditingMode;
        SecondaryEditModeBox.SelectedIndex = review.SelectedSecondaryEditingMode is { } secondary ? (int)secondary + 1 : 0;
        FinishDraftTimelineChange();
        var total = clips.Sum(clip => clip.Duration);
        StatusText.Text = $"Draft applied · {clips.Count} glitches · {FormatTime(total)} total · review, trim, then export.";
        TimelineStatus.Text = "Draft uses source timestamps; playback skips gaps and export joins glitches. Restore pre-draft brings back the previous video edit.";
    }

    private void RestoreDraft_Click(object sender, RoutedEventArgs e)
    {
        if (_draftBackup is null || _busyOperations > 0 || _captureProcess is not null) return;
        PauseDraftPreview();
        var backup = _draftBackup;
        _videoEditSegments.Clear();
        foreach (var segment in backup.Segments ?? [])
            if (double.IsFinite(segment.StartSeconds) && double.IsFinite(segment.EndSeconds) &&
                segment.StartSeconds >= 0 && segment.EndSeconds <= _durationSeconds && segment.EndSeconds > segment.StartSeconds)
                _videoEditSegments.Add(new VideoEditSegment(segment.StartSeconds, segment.EndSeconds) { TrackIndex = Math.Max(0, segment.TrackIndex), Effects = segment.Effects ?? new() });
        _videoLayerCount = Math.Max(1, backup.LayerCount); _draftBackup = null;
        FinishDraftTimelineChange();
        if (double.IsFinite(backup.ClipStart) && double.IsFinite(backup.ClipEnd))
        {
            _clipStartSeconds = Math.Clamp(backup.ClipStart, 0, _durationSeconds);
            _clipEndSeconds = Math.Clamp(backup.ClipEnd, _clipStartSeconds, _durationSeconds);
            UpdateVideoClipVisuals();
        }
        StatusText.Text = "Pre-draft video timeline restored · bookmarks and audio settings retained.";
    }

    private void FinishDraftTimelineChange()
    {
        _selectedCue = null; _selectedTimelineItem = null; _selectedVideoSegment = _videoEditSegments.FirstOrDefault();
        ClipSelectedCueButton.Visibility = Visibility.Collapsed; AddCueLayerButton.Visibility = Visibility.Collapsed;
        RemoveTimelineItemButton.IsEnabled = false;
        _clipStartSeconds = _selectedVideoSegment?.StartSeconds ?? 0;
        _clipEndSeconds = _selectedVideoSegment?.EndSeconds ?? _durationSeconds;
        BuildTimeline(); SeekTo(_clipStartSeconds);
        TimelineScroll.ScrollToHorizontalOffset(Math.Max(0, _clipStartSeconds * _pixelsPerSecond - 100));
        MarkProjectChanged();
    }

    private void PauseDraftPreview()
    {
        SetPreviewPlaybackRequested(false);
        _previewPlayer.Pause();
        foreach (var player in _stemPlayers) try { player.Pause(); } catch { }
    }

    private sealed record SavedDraftBackup(int LayerCount, List<SavedVideoSegment> Segments, double ClipStart, double ClipEnd);
}
