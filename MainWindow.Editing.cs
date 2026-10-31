using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace GlitchStich;

public partial class MainWindow
{
    private readonly List<EditingAsset> _editingAssets = [];
    private readonly List<TimedMedia> _timedMedia = [];
    private readonly EditHistory _editHistory = new();
    private EditingSnapshot? _historyCurrent;
    private bool _applyingEditHistory;
    private int _sequencePlaybackIndex = -1;
    private DateTime _sequenceSeekUntil;
    private readonly Dictionary<string, double[]> _editWaveformCache = new(StringComparer.OrdinalIgnoreCase);

    private void NewProject_Click(object sender, RoutedEventArgs e)
    {
        if (_busyOperations > 0 || _loadingSource || _captureProcess is not null || _isClosing) return;
        if (!ResolveUnsavedProjectChanges("Save before starting a new project?")) return;
        ++_previewGeneration; StopPreviewProxyProcess(); PauseDraftPreview(); StopStemPlayers(); _previewPlayer.Stop(); SetSourcePlaybackSpeed(1);
        _videoPath = null; _previewSourcePath = null; _projectFilePath = null; _lastFootageReview = null;
        _videoEditSegments.Clear(); _audioTracks.Clear(); _waveforms.Clear(); _mediaLibrary.Clear(); _cues.Clear(); _visibleCues.Clear(); _scanCategories.Clear();
        _editingAssets.Clear(); _timedMedia.Clear(); _protectedSceneRanges.Clear(); _quietDowntimeRanges.Clear(); _reviewedSourceRanges.Clear(); _removedAudioStreamIndices.Clear();
        _editHistory.Clear(); _historyCurrent = null; _editWaveformCache.Clear(); _sequencePlaybackIndex = -1;
        _selectedCue = null; _selectedVideoSegment = null; _selectedTimelineItem = null; _draftBackup = null; _videoLayerCount = 1;
        _durationSeconds = 1; _clipStartSeconds = 0; _clipEndSeconds = 1; _usingPreviewProxy = false; _pendingPreviewSeek = 0;
        ProjectName.Text = "Untitled glitch"; MediaName.Text = "No media loaded"; MediaDetails.Text = "Add video, audio or image media.";
        PreferenceBox.Text = ""; GameTitleBox.Text = ""; GameModeBox.Text = ""; IRLModeCheck.IsChecked = false; EditModeBox.SelectedIndex = 0; SecondaryEditModeBox.SelectedIndex = 0;
        VideoPreviewHost.Visibility = Visibility.Collapsed; ImagePreview.Source = null; ImagePreview.Visibility = Visibility.Collapsed; PreviewEmpty.Visibility = Visibility.Visible;
        SeekSlider.Maximum = 1; SeekSlider.Value = 0; DurationText.Text = "00:00"; CurrentTime.Text = "00:00"; RemoveTimelineItemButton.IsEnabled = false;
        BuildTimeline(); _projectHasChanges = false; StatusText.Text = "New empty project · saved projects and media remain on disk.";
    }

    private EditingSnapshot CaptureEdit() => new(_videoEditSegments.Select(clip => new EditingClip(clip.StartSeconds, clip.EndSeconds, clip.TrackIndex, clip.Effects)).ToArray(), _editingAssets.ToArray(), _timedMedia.ToArray())
    { Audio = _audioTracks.Select(track => new EditingTrack(track.StreamIndex, track.Label, track.Codec, track.Channels, track.WavPath, track.ExternalSourcePath, track.Volume, track.IsMuted, track.IsCombinedMix, track.DuplicateOf, track.DuplicateFingerprint)).ToArray(), RemovedAudio = _removedAudioStreamIndices.ToArray() };

    private void TrackEditHistory()
    {
        if (_applyingEditHistory || _editHistory is null || _videoPath is null) return;
        for (var i = 0; i < _audioTracks.Count; i++) if (_audioTracks[i].WavPath is { } path && _waveforms.TryGetValue(i, out var waveform)) _editWaveformCache[path] = waveform;
        var current = CaptureEdit();
        if (_historyCurrent is not null && JsonSerializer.Serialize(_historyCurrent) != JsonSerializer.Serialize(current)) _editHistory.Push(_historyCurrent);
        _historyCurrent = current;
    }

    private void ApplyEdit(EditingSnapshot snapshot)
    {
        if (_videoPath is null) return;
        foreach (var clip in snapshot.Clips) if (clip.Validate(_durationSeconds) is { } error) throw new InvalidDataException(error);
        foreach (var item in snapshot.Media) if (item.Validate(_durationSeconds, snapshot.Assets) is { } error) throw new InvalidDataException(error);
        PauseDraftPreview(); _sequencePlaybackIndex = -1;
        _videoEditSegments.Clear();
        foreach (var clip in snapshot.Clips) _videoEditSegments.Add(new(clip.Start, clip.End) { TrackIndex = Math.Max(0, clip.Layer), Effects = clip.Effects });
        _videoLayerCount = Math.Max(1, _videoEditSegments.Select(clip => clip.TrackIndex + 1).DefaultIfEmpty(1).Max());
        _editingAssets.Clear(); _editingAssets.AddRange(snapshot.Assets);
        _timedMedia.Clear(); _timedMedia.AddRange(snapshot.Media);
        StopStemPlayers(); _audioTracks.Clear();
        foreach (var track in snapshot.Audio) _audioTracks.Add(new(track.Stream, track.Label, track.Codec, track.Channels) { WavPath = track.Wav, ExternalSourcePath = track.External, Volume = track.Volume, IsMuted = track.Muted, IsCombinedMix = track.Combined, DuplicateOf = track.DuplicateOf, DuplicateFingerprint = track.Fingerprint });
        _removedAudioStreamIndices.Clear(); foreach (var index in snapshot.RemovedAudio) _removedAudioStreamIndices.Add(index);
        _waveforms.Clear(); for (var i = 0; i < _audioTracks.Count; i++) if (_audioTracks[i].WavPath is { } path && _editWaveformCache.TryGetValue(path, out var waveform)) _waveforms[i] = waveform;
        _selectedCue = null; _selectedVideoSegment = _videoEditSegments.FirstOrDefault();
        _selectedTimelineItem = null; BuildTimeline(); MarkProjectChanged();
    }

    private void StartSequenceClip(int index)
    {
        ApplySequenceSpeed(index); var clip = _videoEditSegments[index];
        SeekTo(clip.StartSeconds); _sequencePlaybackIndex = index; _sequenceSeekUntil = DateTime.UtcNow.AddMilliseconds(100);
    }
    private void ApplySequenceSpeed(int index)
    {
        _sequencePlaybackIndex = index; var clip = _videoEditSegments[index];
        SetSourcePlaybackSpeed(clip.Effects.Speed);
        ApplyTrackVolumes();
    }
    private void SetSourcePlaybackSpeed(double speed)
    {
        if (_sourcePlaybackSpeed == speed) return;
        var playing = _previewPlayer.IsPlaying;
        if (playing) { _previewPlayer.Pause(); foreach (var stem in _stemPlayers) try { stem.Pause(); } catch { } }
        _previewPlayer.Speed = speed;
        _sourcePlaybackSpeed = _previewPlayer.Speed;
        foreach (var stem in _stemPlayers) try { stem.SpeedRatio = _sourcePlaybackSpeed; } catch { }
        SetStemPositions(PreviewPosition);
        if (playing) StartPreviewPlayback();
    }

    private bool AdvanceSequencePlayback(double seconds)
    {
        if (DateTime.UtcNow < _sequenceSeekUntil) return false;
        if (_sequencePlaybackIndex < 0 || _sequencePlaybackIndex >= _videoEditSegments.Count)
            _sequencePlaybackIndex = Math.Max(0, _videoEditSegments.FindIndex(clip => seconds >= clip.StartSeconds && seconds < clip.EndSeconds));
        var active = _videoEditSegments[_sequencePlaybackIndex];
        if (seconds >= active.StartSeconds - .1 && seconds < active.EndSeconds - .04) return false;
        if (_sequencePlaybackIndex + 1 < _videoEditSegments.Count) StartSequenceClip(_sequencePlaybackIndex + 1);
        else { SetPreviewPlaybackRequested(false); _previewPlayer.Pause(); foreach (var stem in _stemPlayers) try { stem.Pause(); } catch { } SeekTo(active.EndSeconds); }
        return true;
    }

    private int AddTimedMediaLanes(double width, int firstRow)
    {
        var row = firstRow;
        foreach (var item in _timedMedia)
        {
            TimelineTracks.RowDefinitions.Add(new() { Height = new GridLength(44) }); AddLaneLabel(item.Kind.ToUpperInvariant(), row);
            var lane = new Canvas { Width = width, Height = 44, Background = Brush("#0C1220") };
            var block = new Border { Background = Brush("#30213E"), BorderBrush = Brush("#FF70DA"), BorderThickness = new Thickness(1), Height = 34, Width = Math.Max(24, item.Duration * _pixelsPerSecond), Child = new TextBlock { Text = item.Kind == "Text" ? item.Text : _editingAssets.FirstOrDefault(asset => asset.Id == item.AssetId)?.Description ?? item.Kind, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(6), Foreground = Brush("#EAF5FF") } };
            Canvas.SetLeft(block, item.SourceAnchor * _pixelsPerSecond); Canvas.SetTop(block, 5); lane.Children.Add(block);
            block.MouseLeftButtonDown += (_, args) => { _selectedTimelineItem = item; RemoveTimelineItemButton.IsEnabled = true; SeekTo(item.SourceAnchor); if (args.ClickCount == 2) OpenEditingTools_Click(this, new RoutedEventArgs()); args.Handled = true; };
            Grid.SetColumn(lane, 1); Grid.SetRow(lane, row++); TimelineTracks.Children.Add(lane);
        }
        return row - firstRow;
    }

    private void UndoEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_busyOperations > 0 || _isClosing || _videoPath is null) return;
        var state = _editHistory.Undo(CaptureEdit()); if (state is null) { StatusText.Text = "No earlier edit to restore."; return; }
        _applyingEditHistory = true;
        try { ApplyEdit(state); _historyCurrent = CaptureEdit(); StatusText.Text = "Edit undone · source files preserved."; }
        finally { _applyingEditHistory = false; }
    }

    private void RedoEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_busyOperations > 0 || _isClosing || _videoPath is null) return;
        var state = _editHistory.Redo(CaptureEdit()); if (state is null) { StatusText.Text = "No edit to redo."; return; }
        _applyingEditHistory = true;
        try { ApplyEdit(state); _historyCurrent = CaptureEdit(); StatusText.Text = "Edit redone."; }
        finally { _applyingEditHistory = false; }
    }

    private void OpenEditingTools_Click(object sender, RoutedEventArgs e)
    {
        if (_videoPath is null || _busyOperations > 0 || _loadingSource || _captureProcess is not null) { StatusText.Text = "Import a video and finish the current task before opening editing tools."; return; }
        PauseDraftPreview();
        var editor = new EditingToolsWindow(this, CaptureEdit(), _durationSeconds, PreviewSeconds, ImportEditingAssetsAsync, SuggestMediaPlacementsAsync, RenderEditingPreviewAsync);
        if (editor.ShowDialog() == true && editor.Result is { } result) ApplyEdit(result);
    }

    private async Task<IReadOnlyList<EditingAsset>> ImportEditingAssetsAsync(IReadOnlyList<string> paths, CancellationToken cancellation)
    {
        if (!await EnsureEngineAsync()) throw new InvalidOperationException("The media engine is not ready.");
        var result = new List<EditingAsset>();
        foreach (var path in paths)
        {
            cancellation.ThrowIfCancellationRequested(); var metadata = await ProbeEditingMediaAsync(path, cancellation);
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var kind = new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" }.Contains(extension) ? "Image" : metadata.Video ? "Video" : "Audio";
            result.Add(new(Guid.NewGuid().ToString("N"), Path.GetFullPath(path), kind, Path.GetFileNameWithoutExtension(path), false, metadata.Audio, metadata.Duration) { AudioStreams = metadata.AudioStreams });
        }
        return result;
    }

    private async Task<(int Width, int Height, double Duration, bool Video, bool Audio, IReadOnlyList<int> AudioStreams)> ProbeEditingMediaAsync(string path, CancellationToken cancellation)
    {
        var probe = Path.Combine(Path.GetDirectoryName(_ffmpegPath)!, "ffprobe.exe");
        var info = new ProcessStartInfo(probe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var value in new[] { "-v", "error", "-show_streams", "-show_format", "-of", "json", path }) info.ArgumentList.Add(value);
        using var process = Process.Start(info)!;
        using var registration = cancellation.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellation); if (process.ExitCode != 0) throw new InvalidDataException(await errors);
        using var document = JsonDocument.Parse(await output); var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.FirstOrDefault(stream => stream.GetProperty("codec_type").GetString() == "video");
        var duration = document.RootElement.GetProperty("format").TryGetProperty("duration", out var d) && double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;
        var audio = streams.Where(stream => stream.GetProperty("codec_type").GetString() == "audio").ToArray();
        var labels = audio.Select((stream, index) => AudioTrackPolicy.ReadLabel(stream, index + 1)).ToArray();
        var audioMap = audio.Where((stream, index) => !AudioTrackPolicy.IsRedundantCombined(labels[index], labels)).Select(stream => stream.GetProperty("index").GetInt32()).ToArray();
        return (video.ValueKind == JsonValueKind.Object ? video.GetProperty("width").GetInt32() : 0, video.ValueKind == JsonValueKind.Object ? video.GetProperty("height").GetInt32() : 0,
            duration, video.ValueKind == JsonValueKind.Object, audioMap.Length > 0, audioMap);
    }

    private async Task RenderEditingPreviewAsync(EditingSnapshot snapshot, CancellationToken cancellation)
    {
        if (snapshot.Clips.Count == 0) throw new InvalidOperationException("Add a glitch before rendering a preview.");
        if (!await EnsureEngineAsync()) throw new InvalidOperationException("The media engine is unavailable.");
        var folder = Path.Combine(_settingsDirectory, "EditPreview", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        SetBusy(true, "Rendering the stitched preview (up to 30 seconds)…");
        try
        {
            var clips = new List<EditingClip>(); double remaining = 30;
            foreach (var clip in snapshot.Clips) { if (remaining <= 0) break; var end = Math.Min(clip.End, clip.Start + remaining * clip.Effects.Speed); clips.Add(clip with { End = end }); remaining -= (end - clip.Start) / clip.Effects.Speed; }
            var source = await ProbeEditingMediaAsync(_videoPath!, cancellation); var width = Math.Max(2, Math.Min(1280, source.Width) / 2 * 2); var height = Math.Max(2, (int)(width * source.Height / (double)source.Width) / 2 * 2);
            var parts = new List<string>();
            for (var i = 0; i < clips.Count; i++)
            {
                var clip = clips[i]; var part = Path.Combine(folder, $"part{i}.mkv"); parts.Add(part);
                await RunExportProcessAsync(ExportPlan.Build(_videoPath!, part, [new(clip.Start, clip.End) { Effects = clip.Effects }], GetExportAudio(), new("Balanced", "libx264", 2, Intermediate: true) { CanvasWidth = width, CanvasHeight = height }), cancellation, _ => { });
            }
            var manifest = Path.Combine(folder, "join.txt"); await File.WriteAllLinesAsync(manifest, parts.Select(part => $"file '{Path.GetFileName(part)}'"), cancellation);
            var joined = Path.Combine(folder, "joined.mkv"); await RunExportProcessAsync(["-y", "-v", "error", "-f", "concat", "-safe", "0", "-i", manifest, "-c", "copy", joined], cancellation, _ => { });
            var resolved = MediaRenderPlan.Resolve(clips, snapshot.Assets, snapshot.Media, _durationSeconds);
            var captions = await WriteCaptionFilesAsync(folder, resolved, cancellation);
            var output = Path.Combine(folder, "preview.mp4");
            await RunExportProcessAsync(MediaRenderPlan.Build(joined, output, resolved, width, height, clips.Sum(clip => clip.Duration), GetExportAudio().Any(track => !track.Muted && track.Volume > 0), captions, new("Balanced", "libx264", 2)), cancellation, _ => { });
            var owner = Application.Current.Windows.OfType<EditingToolsWindow>().FirstOrDefault(window => window.IsVisible) as Window ?? this;
            var preview = new EditedPreviewWindow(owner, output); preview.ShowDialog();
        }
        finally
        {
            SetBusy(false); var root = Path.GetFullPath(Path.Combine(_settingsDirectory, "EditPreview")) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(folder).StartsWith(root, StringComparison.OrdinalIgnoreCase)) try { Directory.Delete(folder, true); } catch { }
        }
    }

    private static async Task<Dictionary<string, string>> WriteCaptionFilesAsync(string folder, IReadOnlyList<ResolvedMedia> media, CancellationToken cancellation)
    {
        var files = new Dictionary<string, string>();
        foreach (var entry in media.Where(entry => entry.Placement.Kind == "Text"))
        { var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".txt"); await File.WriteAllTextAsync(path, entry.Placement.Text, cancellation); files[entry.Placement.Id] = path; }
        return files;
    }
}
