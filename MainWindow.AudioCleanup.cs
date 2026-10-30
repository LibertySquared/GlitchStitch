using System.IO;
using System.Windows;

namespace ReelGood;

public partial class MainWindow
{
    private string? _audioFingerprintSignature;
    private bool _updatingAudioMuteControls;
    private void SetAudioTrackMuted(AudioTrack track, bool muted)
    {
        if (_updatingAudioMuteControls) return;
        track.IsMuted = muted;
        if (!muted)
            foreach (var index in AudioTrackPolicy.Conflicts(_audioTracks.IndexOf(track), _audioTracks.Select(item => new AudioPlaybackTrack(item.IsCombinedMix, item.IsExternal, item.DuplicateFingerprint)).ToArray()))
                _audioTracks[index].IsMuted = true;
        _updatingAudioMuteControls = true;
        try
        {
            var tracks = _audioTracks.Where(item => File.Exists(item.WavPath)).ToArray();
            for (var i = 0; i < Math.Min(tracks.Length, _stemMutes.Count); i++) _stemMutes[i].IsChecked = tracks[i].IsMuted;
        }
        finally { _updatingAudioMuteControls = false; }
        ApplyTrackVolumes(); MarkProjectChanged();
        RemoveDuplicateAudioButton.Visibility = _audioTracks.Any(item => item.IsMuted && (item.IsCombinedMix || item.DuplicateOf is not null)) ? Visibility.Visible : Visibility.Collapsed;
    }
    private async Task CheckDuplicateAudioAsync(CancellationToken cancellation = default)
    {
        if (_audioTracks.Any(track => !track.IsExternal && !track.IsCombinedMix && !track.IsMuted))
            foreach (var combined in _audioTracks.Where(track => track.IsCombinedMix && !track.IsMuted)) { combined.IsMuted = true; MarkProjectChanged(); }
        var tracks = _audioTracks.Where(track => !track.IsCombinedMix && File.Exists(track.WavPath)).OrderBy(track => track.IsMuted).ToArray();
        var signature = string.Join("|", tracks.Select(track => $"{track.WavPath}:{new FileInfo(track.WavPath!).Length}:{File.GetLastWriteTimeUtc(track.WavPath!).Ticks}"));
        if (_audioFingerprintSignature == signature) return;
        var hashes = new Dictionary<string, AudioTrack>();
        foreach (var track in tracks)
        {
            StatusText.Text = $"Checking duplicate audio · {track.Label}";
            var progress = new Progress<(long Done, long Total)>(item => ReportTaskProgress("Duplicate check (bytes)", item.Done, item.Total));
            try
            {
                var hash = await Task.Run(() => AudioDuplicateDetector.Fingerprint(track.WavPath!, (done, total) => ((IProgress<(long, long)>)progress).Report((done, total)), cancellation));
                track.DuplicateFingerprint = hash;
                if (hashes.TryGetValue(hash, out var retained)) { if (track.DuplicateOf != retained.Label || !track.IsMuted) MarkProjectChanged(); track.DuplicateOf = retained.Label; track.IsMuted = true; }
                else { hashes[hash] = track; track.DuplicateOf = null; }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { StatusText.Text = $"Could not compare {track.Label}: {ex.Message}"; }
        }
        _audioFingerprintSignature = signature;
    }

    private void RemoveDuplicateAudio_Click(object sender, RoutedEventArgs e)
    {
        if (_busyOperations > 0 || _captureProcess is not null) return;
        var redundant = _audioTracks.Where(track => track.IsMuted && (track.IsCombinedMix || track.DuplicateOf is not null)).ToArray();
        if (redundant.Length == 0) { StatusText.Text = "No muted duplicate tracks to remove."; return; }
        if (CyberDialog.Show(this, $"Remove these muted redundant tracks from the project?\n\n{string.Join("\n", redundant.Select(track => track.Label))}\n\nSource recordings and WAV files stay on disk. Distinct microphone and game tracks stay available.", "Remove duplicate audio", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        PauseDraftPreview(); StopStemPlayers();
        var original = _audioTracks.ToArray();
        // Reuse existing waveform arrays without rereading audio.
        var previous = _waveforms.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var track in redundant) { if (track.StreamIndex >= 0) _removedAudioStreamIndices.Add(track.StreamIndex); _audioTracks.Remove(track); }
        _waveforms.Clear();
        for (var i = 0; i < _audioTracks.Count; i++) if (previous.TryGetValue(Array.IndexOf(original, _audioTracks[i]), out var waveform)) _waveforms[i] = waveform;
        foreach (var cue in _cues.Where(cue => redundant.Any(track => track.Label == cue.TrackLabel) && !_audioTracks.Any(track => track.Label == cue.TrackLabel)).ToArray()) _cues.Remove(cue);
        _selectedCue = null; _selectedTimelineItem = null; ClipSelectedCueButton.Visibility = Visibility.Collapsed; AddCueLayerButton.Visibility = Visibility.Collapsed;
        BuildTimeline(); RefreshScanCategories(PreferenceBox.Text.Trim()); MarkProjectChanged();
        StatusText.Text = $"Removed {redundant.Length} redundant track(s) from playback, scanning, and export.";
    }
}
