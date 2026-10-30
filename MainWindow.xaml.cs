using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FlyleafLib;
using FlyleafLib.MediaPlayer;

namespace ReelGood;

public partial class MainWindow : Window
{
    private static readonly HttpClient LocalAiClient = new() { Timeout = TimeSpan.FromMinutes(30) };
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _zoomTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly ObservableCollection<CueItem> _cues = [];
    private readonly ObservableCollection<ScanCategory> _scanCategories = [];
    private readonly ObservableCollection<CueItem> _visibleCues = [];
    private readonly ObservableCollection<MediaLibraryItem> _mediaLibrary = [];
    private readonly CancellationTokenSource _notificationLifetime = new();
    private readonly List<AudioTrack> _audioTracks = [];
    private readonly List<string> _audioScanNotices = [];
    private readonly List<string> _videoScanNotices = [];
    private readonly List<VideoEditSegment> _videoEditSegments = [];
    private int _videoLayerCount = 1;
    private readonly Dictionary<VideoEditSegment, Border> _videoSegmentBlocks = [];
    private readonly HashSet<int> _removedAudioStreamIndices = [];
    private readonly List<MediaElement> _stemPlayers = [];
    private readonly SemaphoreSlim _previewOpenGate = new(1, 1);
    private Player _previewPlayer = null!;
    private readonly List<CheckBox> _stemMutes = [];
    private readonly List<Slider> _stemVolumes = [];
    private readonly Dictionary<int, double[]> _waveforms = [];
    private readonly Dictionary<string, EventPreference> _eventPreferences = new(StringComparer.OrdinalIgnoreCase);
    private string _copilotEndpoint = "http://localhost:11434";
    private string _copilotModel = "gemma4:e2b";
    private bool _copilotLearnFromFeedback = true;
    private CopilotWindow? _copilotWindow;
    private CueItem? _selectedCue;
    private VideoEditSegment? _selectedVideoSegment;
    private static string ApplicationDirectory => Path.GetDirectoryName(Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;
    private readonly string _settingsDirectory = Path.Combine(ApplicationDirectory, "ReelGoodData");
    private string LaughModelRoot => Path.Combine(_settingsDirectory, "LaughDetection");
    private string? _videoPath;
    private string? _projectFilePath;
    private bool _projectHasChanges;
    private int _busyOperations;
    private bool _loadingSource;
    private bool _restoringLibrarySelection;
    private RecordingAudioSession? _recordingAudio;
    private string? _captureVideoPath;
    private Task<string>? _captureErrors;
    private string? _previewSourcePath;
    private bool _usingPreviewProxy;
    private double _pendingPreviewSeek;
    private int _previewGeneration;
    private bool _isPreviewProxyBuilding;
    private Process? _previewProxyProcess;
    private string? _ffmpegPath;
    private readonly Task<string?> _engineInitialization;
    private Process? _captureProcess;
    private string? _captureOutputPath;
    private Canvas? _ruler;
    private Canvas? _playhead;
    private Canvas? _videoLane;
    private Border? _videoClip;
    private Border? _reviewClipOverlay;
    private Border? _leftTrimHandle;
    private Border? _rightTrimHandle;
    private TextBlock? _videoClipLabel;
    private object? _selectedTimelineItem;
    private Border? _selectedTimelineBorder;
    private double _durationSeconds = 1;
    private double _pixelsPerSecond = 2.2;
    private double _clipStartSeconds;
    private double _clipEndSeconds = 1;
    private bool _dragTrimStart;
    private bool _dragTrimEnd;
    private bool _isMuted;
    private bool _seeking;
    private bool _isClosing;
    internal bool IsShuttingDown => _isClosing;
    private bool _allowCloseAfterCleanup;
    private bool _isLaughScanRunning;

    public MainWindow() : this(null) { }

    internal MainWindow(string? dataDirectory)
    {
        if (dataDirectory is not null) _settingsDirectory = Path.GetFullPath(dataDirectory);
        InitializeComponent();
        WindowWorkArea.Attach(this);
        GlitchyHost.ChatRequested += (_, _) => OpenCopilot_Click(this, new RoutedEventArgs());
        var previewConfig = new Config();
        previewConfig.Player.AutoPlay = false;
        previewConfig.Player.SeekAccurate = true;
        previewConfig.Video.BackColor = Colors.Black;
        _previewPlayer = new Player(previewConfig);
        _previewPlayer.SeekCompleted += PreviewSeekCompleted;
        VideoPreviewHost.Player = _previewPlayer;
        LoadEventPreferences();
        LoadCopilotSettings();
        UpdateContextInputs();
        CueList.ItemsSource = _visibleCues;
        ScanCategoryList.ItemsSource = _scanCategories;
        MediaLibraryList.ItemsSource = _mediaLibrary;
        // Project media is restored only by Open project; every launch starts empty.
        EngineStatus.Text = "Preparing built-in media engine…";
        _engineInitialization = InitializeBundledEngineAsync();
        BuildTimeline();
        _clock.Tick += (_, _) => UpdateClock();
        _clock.Start();
        _zoomTimer.Tick += (_, _) => { _zoomTimer.Stop(); BuildTimeline(); };
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += Window_Closing;
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowCloseAfterCleanup) return;
        e.Cancel = true;
        if (_isClosing) return;
        if (!ResolveUnsavedProjectChanges("Save before closing?")) return;

        _isClosing = true;
        Hide(); ShowInTaskbar = false;
        _notificationLifetime.Cancel();
        GlitchyHost.Stop();
        _clock.Stop(); _zoomTimer.Stop(); _taskTicker?.Stop();
        RootGrid.IsEnabled = false;
        StatusText.Text = "Closing · releasing playback resources…";
        var shutdownWatch = Stopwatch.StartNew();
        void LogClose(string stage)
        {
            try { File.AppendAllText(Path.Combine(_settingsDirectory, "shutdown.log"), $"{DateTimeOffset.Now:O} · {shutdownWatch.Elapsed.TotalMilliseconds:0} ms · {stage}\n"); } catch { }
        }
        LogClose("Close accepted after save decision");
        LogClose("Editor hidden; cleanup continues without rendering");
        Mouse.OverrideCursor = null;
        foreach (var owned in OwnedWindows.Cast<Window>().ToArray()) owned.Hide();
        try { _copilotWindow?.Close(); } catch (Exception ex) { LogClose($"Glitchy close: {ex.Message}"); }
        try { _previewPlayer.Pause(); _previewPlayer.Audio.Mute = true; } catch (Exception ex) { LogClose($"Playback pause: {ex.Message}"); }
        StopStemPlayers(); LogClose("Separate audio players closed");
        // Let already-started work unwind before releasing objects it may still use.
        // The hidden window keeps its dispatcher alive for async/native callbacks.
        if (_busyOperations > 0 || _loadingSource) LogClose("Waiting in background for current work to finish");
        while (_busyOperations > 0 || _loadingSource) await Task.Delay(50);
        try { await _engineInitialization; } catch (Exception ex) { LogClose($"Media engine setup: {ex.Message}"); }
        StopStemPlayers();
        var playbackCleanup = PlaybackShutdown.ReleaseAsync(() =>
        {
            _previewPlayer.Audio.Mute = true;
            VideoPreviewHost.Player = null;
            VideoPreviewHost.Dispose();
            LogClose("Video surface detached");
        }, () => _previewPlayer.Dispose());
        StopPreviewProxyProcess();
        try
        {
            await StopCaptureAsync();
            LogClose("Recording finalized (if active)");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Recording shutdown reported an error: {ex.Message}";
        }
        finally
        {
            try
            {
                await playbackCleanup;
                LogClose("Native video player disposed");
            }
            catch (Exception ex) { LogClose($"Playback cleanup reported: {ex.Message}"); }
            _allowCloseAfterCleanup = true;
            LogClose("Closing window");
            Close();
        }
    }

    private TimeSpan PreviewPosition => TimeSpan.FromTicks(Math.Max(0, _previewPlayer.CurTime));
    private double PreviewSeconds => PreviewPosition.TotalSeconds;

    private async Task OpenPreviewAsync(string path)
    {
        await _previewOpenGate.WaitAsync();
        try
        {
            if (_isClosing) return;
            SetPreviewPlaybackRequested(false);
            foreach (var stem in _stemPlayers) try { stem.Pause(); } catch { }
            _previewSeekPending = false;
            _previewSourcePath = Path.GetFullPath(path);
            _previewPlayer.Audio.Mute = ShouldMutePreviewAudio;
            VideoPreviewHost.Visibility = Visibility.Visible;
            PreviewEmpty.Visibility = Visibility.Collapsed;

            var opened = new TaskCompletionSource<OpenCompletedArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<OpenCompletedArgs> onOpened = (_, args) => opened.TrySetResult(args);
            _previewPlayer.OpenCompleted += onOpened;
            try
            {
                _previewPlayer.OpenAsync(_previewSourcePath);
                var result = await opened.Task.WaitAsync(TimeSpan.FromMinutes(2));
                if (!result.Success)
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? "Flyleaf could not open this video." : result.Error);
                if (_isClosing) { _previewPlayer.Pause(); _previewPlayer.Audio.Mute = true; return; }
            }
            finally
            {
                _previewPlayer.OpenCompleted -= onOpened;
            }

            _durationSeconds = Math.Max(1, _previewPlayer.Duration / (double)TimeSpan.TicksPerSecond);
            SeekSlider.Maximum = _durationSeconds;
            DurationText.Text = FormatTime(_durationSeconds);
            PreviewStatus.Text = _usingPreviewProxy ? "Smooth preview · 720p" : "Preview ready · Flyleaf / Direct3D";
            SmoothPreviewButton.IsEnabled = true;
            if (_pendingPreviewSeek > 0)
            {
                var seek = _pendingPreviewSeek;
                _pendingPreviewSeek = 0;
                SeekTo(seek);
            }
            BuildTimeline();
        }
        finally
        {
            _previewOpenGate.Release();
        }
    }

    private void WindowHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        try { DragMove(); } catch { }
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void SaveProject_Click(object sender, RoutedEventArgs e) => SaveProject();

    private bool SaveProject()
    {
        if (string.IsNullOrWhiteSpace(_videoPath) || !File.Exists(_videoPath))
        {
            CyberDialog.Show(this, "Import a video before saving a GlitchStich project.", "Nothing to save", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var targetPath = _projectFilePath;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save GlitchStich project",
                FileName = $"{Path.GetFileNameWithoutExtension(_videoPath)}.glitchstich",
                DefaultExt = ".glitchstich",
                AddExtension = true,
                Filter = "GlitchStich stitch|*.glitchstich;*.reelgood"
            };
            if (dialog.ShowDialog(this) != true) return false;
            targetPath = dialog.FileName;
        }

        try
        {
            var document = new ProjectDocument
            {
                Version = 1,
                VideoPath = _videoPath,
                MediaLibraryPaths = _mediaLibrary.Select(item => item.Path).ToList(),
                EditingAssets = _editingAssets.ToList(), TimedMedia = _timedMedia.ToList(),
                EditingMode = (int)CurrentEditorialMode,
                SecondaryEditingMode = CurrentSecondaryEditorialMode is { } secondary ? (int)secondary : null,
                ProtectedSceneRanges = _protectedSceneRanges.ToList(),
                QuietDowntimeRanges = _quietDowntimeRanges.ToList(),
                Prompt = PreferenceBox.Text,
                GameTitle = GameTitleBox.Text,
                GameMode = GameModeBox.Text,
                IsIrl = IRLModeCheck.IsChecked == true,
                ClipStartSeconds = _clipStartSeconds,
                ClipEndSeconds = _clipEndSeconds,
                VideoLayerCount = _videoLayerCount,
                DraftBackup = _draftBackup,
                RemovedAudioStreamIndices = _removedAudioStreamIndices.ToList(),
                VideoSegments = _videoEditSegments.Select(segment => new SavedVideoSegment(segment.StartSeconds, segment.EndSeconds, segment.TrackIndex, segment.Effects)).ToList(),
                AudioTracks = _audioTracks.Select(track => new SavedAudioTrack(track.StreamIndex, track.Label, track.Codec, track.Channels, track.WavPath, track.ExternalSourcePath, track.Volume, track.IsMuted, track.IsCombinedMix, track.DuplicateOf, track.DuplicateFingerprint)).ToList(),
                Cues = _cues.Select(cue => new SavedCue(cue.TimeSeconds, cue.EventLabel, cue.TrackLabel, cue.EventTimeSeconds,
                    cue.Confidence, cue.AdjustedConfidence, cue.ClipEndSeconds, cue.Decision, cue.AiSuggestion, cue.FeedbackScope)).ToList()
            };
            var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
            ProjectFileStore.Write(targetPath, json);
            _projectFilePath = Path.GetFullPath(targetPath);
            _projectHasChanges = false;
            ProjectName.Text = Path.GetFileNameWithoutExtension(_projectFilePath);
            StatusText.Text = $"Project saved: {Path.GetFileName(_projectFilePath)}";
            return true;
        }
        catch (Exception ex)
        {
            CyberDialog.Show(this, $"GlitchStich could not save the project. Your current edit is still open.\n\n{ex.Message}", "Project save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (_busyOperations > 0 || _loadingSource || _captureProcess is not null) return;
        var dialog = new OpenFileDialog { Title = "Open GlitchStich project", Filter = "GlitchStich stitch|*.glitchstich;*.reelgood", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;

        ProjectDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ProjectDocument>(File.ReadAllText(dialog.FileName));
            if (document is null || document.Version != 1) throw new InvalidDataException("This project file is empty or uses an unsupported version.");
            if (string.IsNullOrWhiteSpace(document.VideoPath) || !File.Exists(document.VideoPath))
                throw new FileNotFoundException("The source video saved in this project could not be found. Restore it at its original path and open the project again.", document.VideoPath);
        }
        catch (Exception ex)
        {
            CyberDialog.Show(this, $"GlitchStich could not open this project.\n\n{ex.Message}", "Project could not be opened", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!ResolveUnsavedProjectChanges("Open another project?")) return;
        SetBusy(true, "Opening project…");
        try
        {
            if (!await LoadVideoAsync(document.VideoPath!)) return;
            RestoreProjectMediaLibrary(document);
            EditModeBox.SelectedIndex = Math.Clamp(document.EditingMode, 0, 2);
            SecondaryEditModeBox.SelectedIndex = document.SecondaryEditingMode is >= 0 and <= 2 && document.SecondaryEditingMode != document.EditingMode ? document.SecondaryEditingMode.Value + 1 : 0;
            _protectedSceneRanges.AddRange((document.ProtectedSceneRanges ?? []).Where(range => EditorialPolicy.Valid(range, _durationSeconds)));
            _quietDowntimeRanges.AddRange((document.QuietDowntimeRanges ?? []).Where(range => EditorialPolicy.Valid(range, _durationSeconds)));

            _removedAudioStreamIndices.Clear();
            foreach (var index in document.RemovedAudioStreamIndices) _removedAudioStreamIndices.Add(index);
            await ProbeTracksAsync();

            foreach (var savedTrack in document.AudioTracks)
            {
                if (savedTrack.ExternalSourcePath is not null)
                {
                    if (!File.Exists(savedTrack.ExternalSourcePath)) continue;
                    _audioTracks.Add(new AudioTrack(-1, savedTrack.Label, savedTrack.Codec, savedTrack.Channels)
                    {
                        ExternalSourcePath = savedTrack.ExternalSourcePath,
                        WavPath = File.Exists(savedTrack.WavPath) ? savedTrack.WavPath : null,
                        Volume = savedTrack.Volume, IsMuted = savedTrack.IsMuted, DuplicateOf = savedTrack.DuplicateOf, DuplicateFingerprint = savedTrack.DuplicateFingerprint
                    });
                }
                else
                {
                    var track = _audioTracks.FirstOrDefault(item => item.StreamIndex == savedTrack.StreamIndex);
                    if (track is not null)
                    {
                        if (File.Exists(savedTrack.WavPath)) track.WavPath = savedTrack.WavPath;
                        track.Volume = savedTrack.Volume;
                        track.IsMuted = savedTrack.IsCombinedMix is null && track.IsCombinedMix ? true : savedTrack.IsMuted;
                        track.DuplicateOf = savedTrack.DuplicateOf; track.DuplicateFingerprint = savedTrack.DuplicateFingerprint;
                    }
                }
            }

            _waveforms.Clear();
            for (var i = 0; i < _audioTracks.Count; i++)
            {
                var wav = _audioTracks[i].WavPath;
                if (!string.IsNullOrWhiteSpace(wav) && File.Exists(wav))
                    _waveforms[i] = await Task.Run(() => ReadWaveform(wav!, 1800));
            }
            _videoEditSegments.Clear();
            foreach (var segment in document.VideoSegments)
                if (segment.EndSeconds > segment.StartSeconds)
                    _videoEditSegments.Add(new VideoEditSegment(segment.StartSeconds, segment.EndSeconds) { TrackIndex = Math.Max(0, segment.TrackIndex), Effects = segment.Effects ?? new() });
            _videoLayerCount = Math.Max(1, document.VideoLayerCount);
            _draftBackup = document.DraftBackup;
            _selectedVideoSegment = _videoEditSegments.FirstOrDefault();
            _selectedTimelineItem = null;
            _cues.Clear();
            foreach (var savedCue in document.Cues)
            {
                var savedTrack = document.AudioTracks.FirstOrDefault(t => string.Equals(t.Label, savedCue.TrackLabel, StringComparison.OrdinalIgnoreCase));
                var restoredTrack = savedTrack is null ? null : _audioTracks.FirstOrDefault(t => savedTrack.ExternalSourcePath is not null
                    ? string.Equals(t.ExternalSourcePath, savedTrack.ExternalSourcePath, StringComparison.OrdinalIgnoreCase)
                    : !t.IsExternal && t.StreamIndex == savedTrack.StreamIndex);
                var cue = new CueItem(savedCue.TimeSeconds, savedCue.EventLabel, restoredTrack?.Label ?? savedCue.TrackLabel, savedCue.EventTimeSeconds,
                    savedCue.Confidence, savedCue.AdjustedConfidence, savedCue.ClipEndSeconds)
                { Decision = savedCue.Decision, AiSuggestion = savedCue.AiSuggestion, FeedbackScope = savedCue.FeedbackScope };
                _cues.Add(cue);
            }
            PreferenceBox.Text = document.Prompt;
            GameTitleBox.Text = document.GameTitle;
            GameModeBox.Text = document.GameMode;
            IRLModeCheck.IsChecked = document.IsIrl;
            UpdateContextInputs();
            _clipStartSeconds = Math.Clamp(document.ClipStartSeconds, 0, _durationSeconds);
            _clipEndSeconds = Math.Clamp(document.ClipEndSeconds, _clipStartSeconds, _durationSeconds);
            _previewPlayer.Audio.Mute = ShouldMutePreviewAudio;
            BuildTimeline();
            RefreshScanCategories(document.Prompt);
            _projectFilePath = Path.GetFullPath(dialog.FileName);
            _projectHasChanges = false;
            ProjectName.Text = Path.GetFileNameWithoutExtension(_projectFilePath);
            _editingAssets.AddRange(document.EditingAssets ?? []); _timedMedia.AddRange(document.TimedMedia ?? []);
            _editHistory.Clear(); _historyCurrent = CaptureEdit(); BuildTimeline();
            StatusText.Text = $"Project opened: {Path.GetFileName(_projectFilePath)}";
        }
        catch (Exception ex)
        {
            CyberDialog.Show(this, $"GlitchStich could not restore the complete project.\n\n{ex.Message}", "Project restore issue", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { SetBusy(false); }
    }

    private bool ResolveUnsavedProjectChanges(string title)
    {
        if (!_projectHasChanges) return true;
        var answer = CyberDialog.Show(this,
            "Save your current project before continuing? Unsaved timeline glitches, scanned bookmarks, and review decisions will be lost.",
            title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.No) return true;
        return answer switch
        {
            MessageBoxResult.Yes => SaveProject(),
            _ => false
        };
    }

    private void MarkProjectChanged()
    {
        TrackEditHistory();
        _projectHasChanges = true;
        if (ProjectName is not null && !ProjectName.Text.EndsWith(" *", StringComparison.Ordinal)) ProjectName.Text += " *";
    }

    private bool _glitchyChatOpening;
    private async void OpenCopilot_Click(object sender, RoutedEventArgs e)
    {
        if (_isClosing || _glitchyChatOpening) return;
        if (_copilotWindow is { IsVisible: true }) { _copilotWindow.Activate(); return; }
        _glitchyChatOpening = true;
        try
        {
            var chat = new CopilotWindow(this, _copilotEndpoint, _copilotModel, _copilotLearnFromFeedback,
                BuildCopilotContext, SaveCopilotSettings, ApplyCopilotProposal, ReviewFootageForChatAsync);
            _copilotWindow = chat;
            var openingFlight = CancellationTokenSource.CreateLinkedTokenSource(_notificationLifetime.Token);
            chat.Glitchy.Visibility = Visibility.Hidden;
            chat.ReturnRequested = async () =>
            {
                if (_glitchyChatOpening)
                {
                    openingFlight.Cancel();
                    while (_glitchyChatOpening && !_isClosing) await Task.Delay(15);
                }
                if (!_isClosing) { chat.Glitchy.Visibility = Visibility.Visible; await GlitchyTransfer.JumpAsync(chat.Glitchy, GlitchyHost, chat, _notificationLifetime.Token); }
            };
            chat.Closed += (_, _) =>
            {
                openingFlight.Cancel(); openingFlight.Dispose();
                if (ReferenceEquals(_copilotWindow, chat)) _copilotWindow = null;
                if (!_isClosing && GlitchyHost.Visibility != Visibility.Visible) { GlitchyHost.Visibility = Visibility.Visible; GlitchyHost.Arrive(); }
            };
            chat.Show();
            await GlitchyTransfer.JumpAsync(GlitchyHost, chat.Glitchy, chat, openingFlight.Token);
            if (!_isClosing && chat.IsVisible) chat.Activate();
        }
        catch (Exception ex)
        {
            if (!_isClosing) { GlitchyHost.Visibility = Visibility.Visible; GlitchyHost.Say("I couldn't open chat: " + ex.Message); }
        }
        finally { _glitchyChatOpening = false; }
    }
    private string BuildCopilotContext(bool includeFeedback)
    {
        var context = new StringBuilder();
        context.AppendLine("GlitchStich scan context. The video/audio models found candidates; these labels and scores may be wrong. Do not treat them as verified funny moments.");
        context.AppendLine(EditorialPolicy.Instructions(CurrentEditorialMode, CurrentSecondaryEditorialMode));
        context.AppendLine($"Review evidence: {_protectedSceneRanges.Count} protected scene-context ranges; {_quietDowntimeRanges.Count} proposed quiet downtime cuts (not applied). Story mode retains unreviewed scenes. Quiet audio alone is not disposable.");
        context.AppendLine($"Source identity: {_videoPath}; duration seconds: {_durationSeconds.ToString(CultureInfo.InvariantCulture)}.");
        context.AppendLine($"Imported media: {(_videoPath is null ? "none" : Path.GetFileName(_videoPath))}.");
        if (!string.IsNullOrWhiteSpace(PreferenceBox.Text)) context.AppendLine($"Current scan request: {PreferenceBox.Text.Trim()}");
        if (IRLModeCheck.IsChecked == true) context.AppendLine("Visual context: IRL mode.");
        else context.AppendLine(DeclaredFootageContext);
        context.AppendLine($"Current results: {_cues.Count} bookmark(s); {_cues.Count(c => c.Decision == "Keep")} kept; {_cues.Count(c => c.Decision == "Skip")} skipped.");
        if (_cues.Count > 0)
        {
            context.AppendLine("Current candidate bookmarks (maximum 30; time / label / source / score / user decision):");
            foreach (var cue in _cues.OrderByDescending(c => c.Decision is not null).ThenByDescending(c => c.AdjustedConfidence).Take(30))
                context.AppendLine(FormattableString.Invariant($"id={_cues.IndexOf(cue)} / start={cue.TimeSeconds:0.###} / end={cue.ClipEndSeconds:0.###} / eventTime={cue.EventTimeSeconds:0.###} / {cue.EventLabel} / {cue.TrackLabel} / score={cue.AdjustedConfidence:0.00} / {cue.Decision ?? "unreviewed"} / AI review note: {cue.AiSuggestion ?? "none"}"));
        }
        if (!string.IsNullOrWhiteSpace(_lastFootageReview)) context.AppendLine("Most recent actual footage review:\n" + _lastFootageReview);
        if (includeFeedback)
        {
            context.AppendLine("User's locally learned Keep/Skip preferences by event label:");
            if (!ScopedEventPreferences.Any()) context.AppendLine("No reviewed feedback for this game/mode/editing intent yet.");
            else foreach (var pair in ScopedEventPreferences.OrderByDescending(p => p.Value.Kept + p.Value.Skipped).Take(30))
                context.AppendLine($"{pair.Key}: kept {pair.Value.Kept}, skipped {pair.Value.Skipped}");
            try
            {
                var approvedPath = Path.Combine(_settingsDirectory, "preferences-by-context.json");
                if (File.Exists(approvedPath))
                {
                    var approved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(approvedPath));
                    if (approved is not null && approved.TryGetValue(CurrentPreferenceScope, out var preference))
                        context.AppendLine($"User-approved preference for this game/mode/editing intent: {preference}");
                }
            }
            catch { }
        }
        if (includeFeedback)
        {
            try
            {
                var path = Path.Combine(_settingsDirectory, "copilot-corrections.jsonl");
                if (File.Exists(path))
                {
                    context.AppendLine("Recent user-approved corrections (historical preferences, not current bookmark IDs):");
                    foreach (var correction in ScopedCorrections(path).TakeLast(6)) context.AppendLine(correction);
                }
            }
            catch { }
        }
        context.AppendLine("Privacy: chat context and optional sampled frames/audio review are processed by local Ollama. Source files are not sent to cloud services. Only explicitly reviewed windows have media evidence.");
        return context.ToString();
    }

    private void LoadCopilotSettings()
    {
        try
        {
            var path = Path.Combine(_settingsDirectory, "copilot.json");
            if (!File.Exists(path)) return;
            var settings = JsonSerializer.Deserialize<CopilotSettings>(File.ReadAllText(path));
            if (settings is null) return;
            if (!string.IsNullOrWhiteSpace(settings.Endpoint)) _copilotEndpoint = settings.Endpoint;
            if (!string.IsNullOrWhiteSpace(settings.Model)) _copilotModel = settings.Model;
            _copilotLearnFromFeedback = settings.LearnFromFeedback;
        }
        catch { /* Invalid assistant settings should not block the editor. */ }
    }

    private void SaveCopilotSettings(string endpoint, string model, bool learnFromFeedback)
    {
        _copilotEndpoint = string.IsNullOrWhiteSpace(endpoint) ? "http://localhost:11434" : endpoint;
        _copilotModel = string.IsNullOrWhiteSpace(model) ? "gemma4:e2b" : model;
        _copilotLearnFromFeedback = learnFromFeedback;
        try
        {
            Directory.CreateDirectory(_settingsDirectory);
            File.WriteAllText(Path.Combine(_settingsDirectory, "copilot.json"), JsonSerializer.Serialize(new CopilotSettings(_copilotEndpoint, _copilotModel, _copilotLearnFromFeedback), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { StatusText.Text = $"Could not save Glitchy settings: {ex.Message}"; }
    }

    private async void SmoothPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_videoPath is null) return;
        if (_isPreviewProxyBuilding)
        {
            StatusText.Text = "Preparing the smooth preview in the background.";
            return;
        }
        if (_usingPreviewProxy)
        {
            _pendingPreviewSeek = PreviewSeconds;
            _previewPlayer.Stop();
            _usingPreviewProxy = false;
            SmoothPreviewButton.Content = "◈  Smooth preview";
            PreviewStatus.Text = "Original playback";
            try { await OpenPreviewAsync(_videoPath); }
            catch (Exception ex) { StatusText.Text = $"Could not switch to original playback: {ex.Message}"; }
            return;
        }
        if (!await EnsureEngineAsync()) return;
        await BuildAndActivatePreviewProxyAsync(_videoPath, _previewGeneration);
    }

    private string GetPreviewProxyPath(string source)
    {
        var info = new FileInfo(source);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{source}|{info.Length}|{info.LastWriteTimeUtc.Ticks}"))).ToLowerInvariant();
        return Path.Combine(_settingsDirectory, "PreviewCache", $"{key}.mp4");
    }

    private async Task BuildAndActivatePreviewProxyAsync(string source, int generation)
    {
        var proxy = GetPreviewProxyPath(source);
        var temp = proxy + $".{generation}.partial.mp4";
        _isPreviewProxyBuilding = true;
        SmoothPreviewButton.IsEnabled = false;
        SmoothPreviewButton.Content = "◈  Preparing smooth preview…";
        if (generation == _previewGeneration)
            PreviewStatus.Text = "Preparing optimized playback · original remains available";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(proxy)!);
            if (!File.Exists(proxy) || new FileInfo(proxy).Length == 0)
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                var completed = await RunPreviewTranscodeAsync(source, temp, useCudaDecode: true, useNvenc: true);
                if (generation != _previewGeneration || _isClosing) return;
                if (!completed)
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                    completed = await RunPreviewTranscodeAsync(source, temp, useCudaDecode: false, useNvenc: true);
                    if (generation != _previewGeneration || _isClosing) return;
                }
                if (!completed)
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                    completed = await RunPreviewTranscodeAsync(source, temp, useCudaDecode: false, useNvenc: false);
                    if (generation != _previewGeneration || _isClosing) return;
                }
                if (!completed) throw new InvalidOperationException("Could not make the smooth playback copy. The original video is still available.");
                File.Move(temp, proxy, overwrite: true);
            }

            if (generation != _previewGeneration || _isClosing || !string.Equals(_videoPath, source, StringComparison.OrdinalIgnoreCase)) return;
            if (VideoPreviewHost.Visibility == Visibility.Visible)
            {
                _pendingPreviewSeek = _previewPlayer.CanPlay ? PreviewSeconds : 0;
                _previewPlayer.Stop();
                _usingPreviewProxy = true;
                SmoothPreviewButton.Content = "◈  Original preview";
                PreviewStatus.Text = "Smooth preview · 720p · scans and exports use original";
                StatusText.Text = "Smooth playback copy is ready. Scan and export still use the source video.";
                await OpenPreviewAsync(proxy);
            }
        }
        catch (Exception ex)
        {
            if (generation == _previewGeneration)
            {
                SmoothPreviewButton.Content = "◈  Try smooth preview";
                PreviewStatus.Text = "Original playback · smooth copy unavailable";
                StatusText.Text = $"Could not prepare smooth preview: {ex.Message}";
            }
        }
        finally
        {
            if (generation == _previewGeneration)
            {
                _isPreviewProxyBuilding = false;
                SmoothPreviewButton.IsEnabled = _previewSourcePath is not null;
                if (!_usingPreviewProxy && string.IsNullOrWhiteSpace(PreviewStatus.Text))
                    PreviewStatus.Text = "Original playback";
            }
        }
    }

    private async Task<bool> RunPreviewTranscodeAsync(string source, string destination, bool useCudaDecode, bool useNvenc)
    {
        var start = new ProcessStartInfo(_ffmpegPath!)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in new[] { "-y", "-hide_banner", "-loglevel", "error" }) start.ArgumentList.Add(arg);
        if (useCudaDecode) { start.ArgumentList.Add("-hwaccel"); start.ArgumentList.Add("cuda"); }
        foreach (var arg in new[] { "-i", source, "-map", "0:v:0", "-map", "0:a:0?", "-vf", "scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2,fps=30,setsar=1" }) start.ArgumentList.Add(arg);
        if (useNvenc)
            foreach (var arg in new[] { "-c:v", "h264_nvenc", "-preset", "p2", "-rc", "vbr", "-cq", "28", "-b:v", "0" }) start.ArgumentList.Add(arg);
        else
            foreach (var arg in new[] { "-c:v", "libx264", "-preset", "ultrafast", "-crf", "29", "-threads", "2" }) start.ArgumentList.Add(arg);
        foreach (var arg in new[] { "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "96k", "-ac", "2", "-movflags", "+faststart", destination }) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        process.Start();
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
        _previewProxyProcess = process;
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (ReferenceEquals(_previewProxyProcess, process)) _previewProxyProcess = null;
        _ = await errors;
        return process.ExitCode == 0 && File.Exists(destination) && new FileInfo(destination).Length > 0;
    }

    private void StopPreviewProxyProcess()
    {
        var process = _previewProxyProcess;
        _previewProxyProcess = null;
        if (process is null) return;
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add media to library",
            Filter = "Supported media|*.mp4;*.mkv;*.mov;*.avi;*.wmv;*.webm;*.wav;*.mp3;*.m4a;*.aac;*.flac;*.ogg;*.wma;*.png|Video files|*.mp4;*.mkv;*.mov;*.avi;*.wmv;*.webm|Audio files|*.wav;*.mp3;*.m4a;*.aac;*.flac;*.ogg;*.wma|PNG images|*.png|All files|*.*",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;
        if (_videoPath is not null)
        {
            SetBusy(true, "Importing editing assets…");
            try
            {
                var imported = await ImportEditingAssetsAsync(dialog.FileNames, CancellationToken.None);
                _editingAssets.AddRange(imported.Where(asset => !_editingAssets.Any(old => old.Path.Equals(asset.Path, StringComparison.OrdinalIgnoreCase))));
                MarkProjectChanged(); StatusText.Text = "Media imported as editing assets. Open Stitch tools → Media / captions to place it or allow AI placement.";
            }
            catch (Exception ex) { StatusText.Text = "Media import failed: " + ex.Message; }
            finally { SetBusy(false); }
            return;
        }
        var added = new List<MediaLibraryItem>();
        foreach (var path in dialog.FileNames)
        {
            var fullPath = Path.GetFullPath(path);
            if (_mediaLibrary.Any(item => string.Equals(item.Path, fullPath, StringComparison.OrdinalIgnoreCase))) continue;
            var item = MediaLibraryItem.FromPath(fullPath);
            _mediaLibrary.Add(item);
            added.Add(item);
        }
        if (added.Count > 0)
        {
            if (_videoPath is not null) MarkProjectChanged();
            MediaLibraryList.SelectedItem = added[0];
            StatusText.Text = $"Added {added.Count} media item(s) to the library.";
        }
        else StatusText.Text = "Those media files are already in the library.";
    }

    private async void MediaLibraryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoringLibrarySelection || _busyOperations > 0 || _loadingSource || _captureProcess is not null || MediaLibraryList.SelectedItem is not MediaLibraryItem item) return;
        if (item.Kind == MediaKind.Video && !string.Equals(_videoPath, item.Path, StringComparison.OrdinalIgnoreCase)
            && !ResolveUnsavedProjectChanges("Save before changing video?"))
        {
            _restoringLibrarySelection = true;
            try { MediaLibraryList.SelectedItem = e.RemovedItems.Count > 0 ? e.RemovedItems[0] : null; }
            finally { _restoringLibrarySelection = false; }
            return;
        }
        AddAudioToTimelineButton.Visibility = item.Kind == MediaKind.Audio ? Visibility.Visible : Visibility.Collapsed;
        MediaName.Text = item.Name;
        MediaDetails.Text = $"{item.KindLabel} · {item.Details}";
        if (item.Kind == MediaKind.Video)
        {
            if (string.Equals(_videoPath, item.Path, StringComparison.OrdinalIgnoreCase))
            {
                ImagePreview.Visibility = Visibility.Collapsed; VideoPreviewHost.Visibility = Visibility.Visible;
                PreviewEmpty.Visibility = Visibility.Collapsed; PlayButton.IsEnabled = true; SeekSlider.IsEnabled = true;
            }
            else await LoadVideoAsync(item.Path);
            return;
        }
        if (item.Kind == MediaKind.Image)
        {
            try
            {
                _previewPlayer.Stop();
                VideoPreviewHost.Visibility = Visibility.Collapsed;
                PlayButton.IsEnabled = false;
                SeekSlider.IsEnabled = false;
                PreviewEmpty.Visibility = Visibility.Collapsed;
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(item.Path);
                bitmap.EndInit();
                ImagePreview.Source = bitmap;
                ImagePreview.Visibility = Visibility.Visible;
                PreviewStatus.Text = "PNG still · select a video to return to edit playback";
            }
            catch (Exception ex) { StatusText.Text = $"Could not preview image: {ex.Message}"; }
            return;
        }
        ImagePreview.Visibility = Visibility.Collapsed;
        VideoPreviewHost.Visibility = _videoPath is null ? Visibility.Collapsed : Visibility.Visible;
        PreviewEmpty.Visibility = _videoPath is null ? Visibility.Visible : Visibility.Collapsed;
        PreviewStatus.Text = "Audio asset in library · add it to the timeline to use it";
    }

    private void RestoreProjectMediaLibrary(ProjectDocument document)
    {
        var paths = (document.MediaLibraryPaths ?? []).Concat([document.VideoPath])
            .Concat(document.AudioTracks.Select(track => track.ExternalSourcePath));
        _restoringLibrarySelection = true;
        try
        {
            _mediaLibrary.Clear();
            foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)).Distinct(StringComparer.OrdinalIgnoreCase))
                _mediaLibrary.Add(MediaLibraryItem.FromPath(Path.GetFullPath(path!)));
            MediaLibraryList.SelectedItem = _mediaLibrary.FirstOrDefault(item => string.Equals(item.Path, _videoPath, StringComparison.OrdinalIgnoreCase));
        }
        finally { _restoringLibrarySelection = false; }
    }

    private async void AddLibraryAudio_Click(object sender, RoutedEventArgs e)
    {
        if (MediaLibraryList.SelectedItem is not MediaLibraryItem item || item.Kind != MediaKind.Audio) return;
        if (_videoPath is null)
        {
            StatusText.Text = "Import a video first, then add audio to its timeline.";
            return;
        }
        if (_audioTracks.Any(track => string.Equals(track.ExternalSourcePath, item.Path, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText.Text = "That audio file is already on the timeline.";
            return;
        }
        if (!await EnsureEngineAsync()) return;
        SetBusy(true, "Preparing audio for the timeline…");
        try
        {
            var folder = Path.Combine(_settingsDirectory, "ImportedAudio");
            Directory.CreateDirectory(folder);
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.Path))).ToLowerInvariant();
            var wavPath = Path.Combine(folder, $"{key}.wav");
            if (!File.Exists(wavPath) || new FileInfo(wavPath).Length == 0)
                await RunFfmpegAsync(["-y", "-hide_banner", "-loglevel", "error", "-i", item.Path, "-vn", "-acodec", "pcm_s16le", "-ar", "44100", "-ac", "2", wavPath]);
            var track = new AudioTrack(-1, Path.GetFileNameWithoutExtension(item.Name), "external", 2) { WavPath = wavPath, ExternalSourcePath = item.Path };
            _audioTracks.Add(track);
            _waveforms[_audioTracks.Count - 1] = await Task.Run(() => ReadWaveform(wavPath, 1800));
            await CheckDuplicateAudioAsync();
            _previewPlayer.Audio.Mute = true;
            BuildTimeline();
            MarkProjectChanged();
            StatusText.Text = $"Added {item.Name} as a separate timeline track. The original file is unchanged.";
        }
        catch (Exception ex) { StatusText.Text = $"Could not add audio to timeline: {ex.Message}"; }
        finally { SetBusy(false); }
    }

    private async Task<bool> LoadVideoAsync(string path)
    {
        if (_loadingSource) return false;
        _loadingSource = true;
        SetBusy(true, "Loading video…");
        try
        {
            StopPreviewProxyProcess();
            var generation = ++_previewGeneration;
            StopStemPlayers();
            SetPreviewPlaybackRequested(false);
            _previewPlayer.Stop(); _previewSourcePath = null; _pendingPreviewSeek = 0; _usingPreviewProxy = false;
            _videoPath = Path.GetFullPath(path); _projectFilePath = null; _lastFootageReview = null; _audioTracks.Clear(); _removedAudioStreamIndices.Clear(); _cues.Clear(); _scanCategories.Clear(); _visibleCues.Clear(); _videoEditSegments.Clear(); _videoLayerCount = 1; _draftBackup = null; _selectedVideoSegment = null; _waveforms.Clear(); _selectedCue = null; ClipSelectedCueButton.Visibility = Visibility.Collapsed; AddCueLayerButton.Visibility = Visibility.Collapsed; _clipStartSeconds = 0; _clipEndSeconds = 1;
            _protectedSceneRanges.Clear(); _quietDowntimeRanges.Clear();
            _editingAssets.Clear(); _timedMedia.Clear(); _editHistory.Clear(); _historyCurrent = null; _sequencePlaybackIndex = -1; _editWaveformCache.Clear(); _reviewedSourceRanges.Clear();
            _isMuted = false; _previewPlayer.Audio.Mute = false; MuteButton.Content = "🔊";
            SetSourcePlaybackSpeed(1);
            MediaName.Text = Path.GetFileName(_videoPath); ProjectName.Text = Path.GetFileNameWithoutExtension(_videoPath);
            var info = new FileInfo(_videoPath); MediaDetails.Text = $"{FormatBytes(info.Length)} · local file";
            var proxy = GetPreviewProxyPath(_videoPath);
            _usingPreviewProxy = File.Exists(proxy) && new FileInfo(proxy).Length > 0;
            SmoothPreviewButton.IsEnabled = false;
            SmoothPreviewButton.Content = _usingPreviewProxy ? "◈  Original preview" : "◈  Preparing smooth preview…";
            ImagePreview.Visibility = Visibility.Collapsed; ImagePreview.Source = null; PlayButton.IsEnabled = true; SeekSlider.IsEnabled = true;
            VideoPreviewHost.Visibility = Visibility.Visible; PreviewEmpty.Visibility = Visibility.Collapsed;
            PreviewStatus.Text = _usingPreviewProxy ? "Smooth preview · 720p" : "Loading video · preparing smooth preview"; PlayButton.IsEnabled = true; SeekSlider.IsEnabled = true;
            CueSummary.Text = "Import audio tracks to enable independent scanning.";
            StatusText.Text = "Video loaded. Track probing runs when you choose Extract audio.";
            await OpenPreviewAsync(_usingPreviewProxy ? proxy : _videoPath);
            _clipEndSeconds = _durationSeconds;
            _videoEditSegments.Add(new VideoEditSegment(0, _durationSeconds));
            _selectedVideoSegment = _videoEditSegments[0];
            BuildTimeline();
            if (await EnsureEngineAsync())
            {
                await ProbeTracksAsync();
                if (!_usingPreviewProxy) _ = BuildAndActivatePreviewProxyAsync(_videoPath, generation);
            }
            MarkProjectChanged();
            return true;
        }
        catch (Exception ex) { StatusText.Text = $"Could not load this video: {ex.Message}"; return false; }
        finally { _loadingSource = false; SetBusy(false); }
    }

    private void Play_Click(object sender, RoutedEventArgs e) => TogglePlayback();
    private void TogglePlayback()
    {
        if (_playbackRequested)
        {
            SetPreviewPlaybackRequested(false);
            _previewPlayer.Pause(); foreach (var player in _stemPlayers) try { player.Pause(); } catch { }
        }
        else
        {
            if (_previewSourcePath is null || !_previewPlayer.CanPlay) return;
            if (_selectedCue is null && _videoEditSegments.Count > 0)
            {
                var position = PreviewSeconds;
                var index = _selectedVideoSegment is not null && position >= _selectedVideoSegment.StartSeconds && position < _selectedVideoSegment.EndSeconds ? _videoEditSegments.IndexOf(_selectedVideoSegment) : _videoEditSegments.FindIndex(segment => position >= segment.StartSeconds && position < segment.EndSeconds);
                if (index < 0) StartSequenceClip(0); else ApplySequenceSpeed(index);
            }
            else if (PreviewSeconds < _clipStartSeconds || PreviewSeconds >= _clipEndSeconds) SeekTo(_clipStartSeconds);
            SetPreviewPlaybackRequested(true);
            if (!_previewSeekPending) SetStemPositions(PreviewPosition);
            StartPreviewPlayback();
        }
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        _isMuted = !_isMuted; MuteButton.Content = _isMuted ? "🔇" : "🔊";
        _previewPlayer.Audio.Mute = ShouldMutePreviewAudio;
        ApplyTrackVolumes();
    }

    private void SeekSlider_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _seeking = true;
    private void SeekSlider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { _seeking = false; SeekTo(SeekSlider.Value); }
    private void SeekTo(double seconds)
    {
        if (_previewSourcePath is null) return;
        if (_selectedCue is null) _sequencePlaybackIndex = _selectedVideoSegment is not null && seconds >= _selectedVideoSegment.StartSeconds && seconds < _selectedVideoSegment.EndSeconds ? _videoEditSegments.IndexOf(_selectedVideoSegment) : _videoEditSegments.FindIndex(segment => seconds >= segment.StartSeconds && seconds < segment.EndSeconds);
        _sequenceSeekUntil = DateTime.UtcNow.AddMilliseconds(100);
        var pos = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, _durationSeconds));
        _previewSeekPending = true; _pendingSeekMilliseconds = (int)pos.TotalMilliseconds; _previewSeekStarted = DateTime.UtcNow;
        // Flyleaf signals SeekCompleted for paused seeks. Keep the decoder
        // paused until that callback, then start video and WAVs together.
        _previewPlayer.Pause();
        foreach (var player in _stemPlayers) try { player.Pause(); } catch { }
        try { _previewPlayer.SeekAccurate(_pendingSeekMilliseconds); } catch { _previewSeekPending = false; }
        UpdatePlayhead(pos.TotalSeconds);
    }

    private void CueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CueList.SelectedItem is CueItem cue) PlayCue(cue);
    }

    private void ScanCategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScanCategoryList.SelectedItem is not ScanCategory category) return;
        _visibleCues.Clear();
        foreach (var cue in category.Items.OrderBy(item => item.Decision != "Keep" && item.AiSuggestion?.StartsWith("AI suggests SKIP:", StringComparison.OrdinalIgnoreCase) == true).ThenBy(item => item.TimeSeconds)) _visibleCues.Add(cue);
        CueSummary.Text = $"{category.Name}  ·  {category.Count} bookmark(s) · {_cues.Count} total";
    }

    private void PlayCue(CueItem cue)
    {
        PauseDraftPreview();
        SetSourcePlaybackSpeed(1);
        SelectCueForClip(cue);
        SeekTo(cue.TimeSeconds);
        StatusText.Text = cue.EventTimeSeconds > cue.TimeSeconds
            ? $"Reviewing setup at {FormatTime(cue.TimeSeconds)} · {cue.EventLabel} near {FormatTime(cue.EventTimeSeconds)} · {cue.TrackLabel}"
            : $"Reviewing {FormatTime(cue.TimeSeconds)} · {cue.SourceLabel}";
    }

    private void SelectCueForClip(CueItem cue)
    {
        _selectedCue = cue;
        var start = Math.Clamp(cue.TimeSeconds, 0, Math.Max(0, _durationSeconds - .25));
        var end = Math.Clamp(cue.ClipEndSeconds, start + .25, _durationSeconds);
        if (end - start < .25) end = Math.Min(_durationSeconds, start + 2);
        _selectedVideoSegment = null;
        _clipStartSeconds = start;
        _clipEndSeconds = end;
        ClipSelectedCueButton.Visibility = Visibility.Visible;
        AddCueLayerButton.Visibility = Visibility.Visible;
        RemoveTimelineItemButton.IsEnabled = false;
        UpdateVideoClipVisuals();
        TimelineScroll.ScrollToHorizontalOffset(Math.Max(0, start * _pixelsPerSecond - 250));
        TimelineStatus.Text = $"Selected {cue.SourceLabel} · glowing range is the glitch · drag either edge outward to add context";
    }

    private void SelectVideoSegment(VideoEditSegment segment)
    {
        _selectedCue = null;
        ClipSelectedCueButton.Visibility = Visibility.Collapsed; AddCueLayerButton.Visibility = Visibility.Collapsed;
        AddCueLayerButton.Visibility = Visibility.Collapsed;
        _selectedVideoSegment = segment;
        _clipStartSeconds = segment.StartSeconds;
        _clipEndSeconds = segment.EndSeconds;
        _selectedTimelineItem = VideoTimelineItem.Instance;
        BuildTimeline();
        if (_videoSegmentBlocks.TryGetValue(segment, out var block)) SelectTimelineItem(VideoTimelineItem.Instance, block);
        RemoveTimelineItemButton.IsEnabled = true;
        UpdateVideoClipVisuals();
    }

    private void SplitVideoAtPlayhead_Click(object sender, RoutedEventArgs e)
    {
        if (_videoEditSegments.Count == 0) { StatusText.Text = "Import a video before splitting glitches."; return; }
        var splitAt = PreviewSeconds;
        var segment = _selectedVideoSegment is not null && splitAt > _selectedVideoSegment.StartSeconds && splitAt < _selectedVideoSegment.EndSeconds
            ? _selectedVideoSegment
            : _videoEditSegments.FirstOrDefault(item => splitAt > item.StartSeconds && splitAt < item.EndSeconds);
        if (segment is null || splitAt - segment.StartSeconds < .25 || segment.EndSeconds - splitAt < .25)
        { StatusText.Text = "Place the playhead inside a timeline glitch, at least 0.25 seconds from either edge."; return; }
        var index = _videoEditSegments.IndexOf(segment);
        var right = new VideoEditSegment(splitAt, segment.EndSeconds) { TrackIndex = _videoLayerCount++, Effects = segment.Effects };
        segment.EndSeconds = splitAt;
        _videoEditSegments.Insert(index + 1, right);
        MarkProjectChanged();
        _selectedVideoSegment = right;
        _selectedTimelineItem = VideoTimelineItem.Instance;
        BuildTimeline();
        StatusText.Text = $"Split at {FormatTime(splitAt)}. The two pieces can now be trimmed or removed separately.";
    }

    private void AddCueLayer_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCue is null) return;
        var start = Math.Clamp(_selectedCue.TimeSeconds, 0, _durationSeconds);
        var end = Math.Clamp(_selectedCue.ClipEndSeconds, start, _durationSeconds);
        if (end - start < .25) { StatusText.Text = "This bookmark is too short to add as an edit glitch."; return; }
        SplitTimelineAt(start);
        SplitTimelineAt(end);
        var clipParts = _videoEditSegments.Where(segment => segment.StartSeconds >= start - .01 && segment.EndSeconds <= end + .01).ToArray();
        if (clipParts.Length == 0) { StatusText.Text = "The bookmark range does not overlap an editable video glitch."; return; }
        var layer = _videoLayerCount++;
        foreach (var segment in clipParts) segment.TrackIndex = layer;
        MarkProjectChanged();
        _selectedCue = null;
        ClipSelectedCueButton.Visibility = Visibility.Collapsed; AddCueLayerButton.Visibility = Visibility.Collapsed;
        AddCueLayerButton.Visibility = Visibility.Collapsed;
        _selectedVideoSegment = clipParts[0];
        _clipStartSeconds = clipParts[0].StartSeconds;
        _clipEndSeconds = clipParts[0].EndSeconds;
        _selectedTimelineItem = VideoTimelineItem.Instance;
        BuildTimeline();
        TimelineScroll.ScrollToHorizontalOffset(Math.Max(0, start * _pixelsPerSecond - 250));
        StatusText.Text = $"Added bookmark as an editable glitch on V{layer + 1}. It stays in the source timeline order.";
    }

    private void SplitTimelineAt(double seconds)
    {
        var segment = _videoEditSegments.FirstOrDefault(item => seconds > item.StartSeconds + .01 && seconds < item.EndSeconds - .01);
        if (segment is null) return;
        var index = _videoEditSegments.IndexOf(segment);
        var right = new VideoEditSegment(seconds, segment.EndSeconds) { TrackIndex = segment.TrackIndex };
        segment.EndSeconds = seconds;
        _videoEditSegments.Insert(index + 1, right);
    }

    private void ClipSelectedCue_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCue is null) return;
        ExportClip_Click(sender, e);
    }

    private void RefreshScanCategories(string prompt)
    {
        _scanCategories.Clear();
        var names = prompt.Split([',', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
        if (names.Count == 0) names.Add("All moments");
        var assigned = new HashSet<CueItem>();
        foreach (var name in names)
        {
            var matches = _cues.Where(cue => !assigned.Contains(cue) && CueMatchesCategory(cue, name)).ToList();
            foreach (var cue in matches) assigned.Add(cue);
            _scanCategories.Add(new ScanCategory(name, matches.Count, matches));
        }
        var leftovers = _cues.Where(cue => !assigned.Contains(cue)).ToList();
        if (leftovers.Count > 0) _scanCategories.Add(new ScanCategory("Other moments", leftovers.Count, leftovers));
        ScanCategoryList.SelectedIndex = _scanCategories.Count > 0 ? 0 : -1;
        if (ScanCategoryList.SelectedItem is ScanCategory category)
        {
            _visibleCues.Clear(); foreach (var cue in category.Items.OrderBy(item => item.Decision != "Keep" && item.AiSuggestion?.StartsWith("AI suggests SKIP:", StringComparison.OrdinalIgnoreCase) == true).ThenBy(item => item.TimeSeconds)) _visibleCues.Add(cue);
        }
        CueSummary.Text = $"{_cues.Count} bookmarks in {_scanCategories.Count} lists";
    }

    private static bool CueMatchesCategory(CueItem cue, string category)
    {
        if (category.Equals("All moments", StringComparison.OrdinalIgnoreCase)) return true;
        var label = cue.EventLabel.ToLowerInvariant();
        var name = category.ToLowerInvariant();
        var words = System.Text.RegularExpressions.Regex.Split(name, "[^a-z0-9]+")
            .Where(word => word.Length > 2 && word is not "the" and not "and" and not "with" and not "moments").ToArray();
        if (label.Contains(name, StringComparison.Ordinal) || words.Any(word => label.Contains(word, StringComparison.Ordinal))) return true;
        if ((name.Contains("funny") || name.Contains("comedy") || name.Contains("joke")) && IsLaughterEvent(label)) return true;
        if ((name.Contains("eliminat") || name.Contains("kill")) && (label.Contains("eliminat") || label.Contains("kill") || label.Contains("knockout") || label.Contains("defeat"))) return true;
        return false;
    }

    private void ReviewCue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CueItem cue, Tag: string decision }) return;
        if (cue.Decision == decision) return;
        if (cue.Decision is not null) AddEventFeedback(cue.EventLabel, cue.Decision, -1, cue.FeedbackScope);
        cue.Decision = decision;
        cue.FeedbackScope = CurrentPreferenceScope;
        AddEventFeedback(cue.EventLabel, decision, 1);
        SaveFeedback(); MarkProjectChanged(); DrawCueMarkers();
        CueSummary.Text = $"{_cues.Count} cues · {_cues.Count(c => c.Decision is not null)} reviewed";
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_isClosing || _busyOperations > 0 || _loadingSource) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.FocusedElement is not TextBox)
        {
            if (e.Key == Key.Z) { if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) RedoEdit_Click(this, new RoutedEventArgs()); else UndoEdit_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
            if (e.Key == Key.Y) { RedoEdit_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
        }
        if (e.Key == Key.Space && Keyboard.FocusedElement is not TextBox and not Button)
        { e.Handled = true; TogglePlayback(); }
        if (e.Key == Key.M && Keyboard.FocusedElement is not TextBox) Mute_Click(this, new RoutedEventArgs());
    }

    private async void ExtractAudio_Click(object sender, RoutedEventArgs e)
    {
        if (_captureProcess is not null) { StatusText.Text = "Stop recording before starting this operation."; return; }
        if (!await EnsureEngineAsync() || _videoPath is null) return;
        SetBusy(true, "Inspecting audio streams…");
        try
        {
            PauseDraftPreview();
            await ProbeTracksAsync();
            if (_audioTracks.Count == 0) { StatusText.Text = "This video has no audio tracks."; return; }
            var outputFolder = Path.Combine(Path.GetDirectoryName(_videoPath)!, Path.GetFileNameWithoutExtension(_videoPath) + "_audio");
            Directory.CreateDirectory(outputFolder);
            foreach (var track in _audioTracks.Where(track => track.StreamIndex >= 0))
            {
                var index = _audioTracks.IndexOf(track) + 1;
                track.WavPath = Path.Combine(outputFolder, $"track_{index:00}.wav");
                StatusText.Text = $"Extracting audio track {index} of {_audioTracks.Count}…";
                await RunFfmpegAsync(["-y", "-hide_banner", "-loglevel", "error", "-i", _videoPath, "-map", $"0:{track.StreamIndex}", "-vn", "-c:a", "pcm_s16le", track.WavPath]);
            }
            await CheckDuplicateAudioAsync();
            _previewPlayer.Audio.Mute = true;
            StatusText.Text = $"Extracted {_audioTracks.Count} separate WAV track(s). Building compact waveforms…";
            _waveforms.Clear();
            for (var i = 0; i < _audioTracks.Count; i++)
            {
                var wav = _audioTracks[i].WavPath!;
                _waveforms[i] = await Task.Run(() => ReadWaveform(wav, 1800));
            }
            BuildTimeline();
            MarkProjectChanged();
            CueSummary.Text = $"{_audioTracks.Count} separate audio track(s) ready to scan.";
            StatusText.Text = $"WAV tracks saved under {outputFolder} · {_audioTracks.Count(track => track.IsMuted && (track.IsCombinedMix || track.DuplicateOf is not null))} redundant track(s) muted to avoid doubled playback.";
        }
        catch (Exception ex) { StatusText.Text = $"Audio extraction failed: {ex.Message}"; }
        finally { SetBusy(false); }
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (_captureProcess is not null) { StatusText.Text = "Stop recording before starting this operation."; return; }
        if (_isLaughScanRunning) return;
        if (_videoPath is null) { StatusText.Text = "Import a video before starting a scan."; return; }
        if (!await EnsureEngineAsync() || _videoPath is null) return;
        if (!double.TryParse(ContextSecondsBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var contextSeconds) || !double.IsFinite(contextSeconds) || contextSeconds < 0 || contextSeconds > 15)
        { StatusText.Text = "Enter a lead-in between 0 and 15 seconds."; return; }
        if (!double.TryParse(LaughThresholdBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var threshold) || !double.IsFinite(threshold) || threshold < 0.05 || threshold > 0.99)
        { StatusText.Text = "Enter a match score between 0.05 and 0.99."; return; }
        if (!double.TryParse(ClipDurationBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var clipDuration) || !double.IsFinite(clipDuration) || clipDuration < 2 || clipDuration > 300)
        { StatusText.Text = "Choose a glitch length between 2 and 300 seconds."; return; }
        var query = EditorialPolicy.ScanQuery(PreferenceBox.Text, CurrentEditorialMode, CurrentSecondaryEditorialMode);
        _selectedCue = null;
        ClipSelectedCueButton.Visibility = Visibility.Collapsed; AddCueLayerButton.Visibility = Visibility.Collapsed;

        _audioScanNotices.Clear();
        _videoScanNotices.Clear();
        _isLaughScanRunning = true;
        SetBusy(true, "Preparing the audio and video scanners…");
        var audioScanned = false;
        var videoScanned = false;
        var aiVerifyRequested = AiVerifyCandidatesCheck.IsChecked == true;
        var aiReviewedCount = 0;
        string? aiReviewIssue = null;
        string? audioIssue = null;
        string? videoIssue = null;
        try
        {
            try
            {
                if (_audioTracks.Count == 0) await ProbeTracksAsync();
                if (_audioTracks.Count == 0)
                {
                    audioIssue = "No audio tracks found";
                }
                else if (!await EnsureAudioEventModelAsync())
                {
                    audioIssue = "Audio model setup canceled";
                }
                else
                {
                    if (_audioTracks.Any(t => string.IsNullOrWhiteSpace(t.WavPath) || !File.Exists(t.WavPath)))
                    {
                        StatusText.Text = "Extracting separate WAV tracks before scanning audio…";
                        var folder = Path.Combine(Path.GetDirectoryName(_videoPath)!, Path.GetFileNameWithoutExtension(_videoPath) + "_audio"); Directory.CreateDirectory(folder);
                        foreach (var track in _audioTracks.Where(track => track.StreamIndex >= 0 && (string.IsNullOrWhiteSpace(track.WavPath) || !File.Exists(track.WavPath))))
                        {
                            var index = _audioTracks.IndexOf(track) + 1;
                            track.WavPath = Path.Combine(folder, $"track_{index:00}.wav");
                            await RunFfmpegAsync(["-y", "-hide_banner", "-loglevel", "error", "-i", _videoPath, "-map", $"0:{track.StreamIndex}", "-vn", "-c:a", "pcm_s16le", track.WavPath]);
                        }
                        _previewPlayer.Audio.Mute = true;
                        _waveforms.Clear();
                        for (var i = 0; i < _audioTracks.Count; i++) { var wav = _audioTracks[i].WavPath!; _waveforms[i] = await Task.Run(() => ReadWaveform(wav, 1800)); }
                        BuildTimeline();
                    }
                    await CheckDuplicateAudioAsync(); BuildTimeline();
                    ReportTaskProgress("Loading audio model", 0, 0);
                    StatusText.Text = "Scanning separate audio tracks for your prompt…";
                    var audioDetections = await RunAudioEventScanAsync(query, threshold);
                    foreach (var oldCue in _cues.Where(c => _audioTracks.Any(t => string.Equals(t.Label, c.TrackLabel, StringComparison.OrdinalIgnoreCase))).ToArray()) _cues.Remove(oldCue);
                    foreach (var detection in audioDetections.OrderBy(d => d.StartSeconds))
                    {
                        var adjusted = AdjustForPreference(detection.EventLabel, detection.Confidence);
                        if (adjusted < threshold) continue;
                        var leadIn = IsLaughterEvent(detection.EventLabel) ? Math.Max(contextSeconds, 4.0) : contextSeconds;
                        var bookmark = Math.Max(0, detection.StartSeconds - leadIn);
                        _cues.Add(new CueItem(bookmark, detection.EventLabel, detection.TrackLabel, detection.StartSeconds, detection.Confidence, adjusted, detection.EndSeconds));
                    }
                    audioScanned = true;
                }
            }
            catch (Exception ex) { audioIssue = ex.Message; }

            try
            {
                if (!await EnsureVideoModelAsync())
                {
                    videoIssue = "Video model setup canceled";
                }
                else
                {
                    ReportTaskProgress("Loading video model", 0, 0);
                    StatusText.Text = "Scanning video frames with the same prompt…";
                    var videoDetections = await RunVideoScanAsync(query, threshold, contextSeconds, clipDuration,
                        GameTitleBox.Text.Trim(), GameModeBox.Text.Trim(), IRLModeCheck.IsChecked == true);
                    foreach (var oldCue in _cues.Where(c => string.Equals(c.TrackLabel, "Video frames", StringComparison.OrdinalIgnoreCase)).ToArray()) _cues.Remove(oldCue);
                    foreach (var detection in videoDetections.OrderBy(d => d.StartSeconds))
                    {
                        var adjusted = AdjustForPreference(detection.EventLabel, detection.Confidence);
                        var isReviewCandidate = detection.EventLabel.StartsWith("Closest video candidate:", StringComparison.OrdinalIgnoreCase);
                        if (!isReviewCandidate && adjusted < threshold) continue;
                        _cues.Add(new CueItem(detection.StartSeconds, detection.EventLabel, detection.TrackLabel,
                            detection.EventStartSeconds, detection.Confidence, adjusted, detection.EndSeconds));
                    }
                    videoScanned = true;
                }
            }
            catch (Exception ex) { videoIssue = ex.Message; }

            if (aiVerifyRequested)
            {
                try
                {
                    if (await EnsureAiReviewModelAsync())
                        aiReviewedCount = await ReviewAiCandidatesAsync(query);
                    else aiReviewIssue = "AI review was skipped; the audio/video scan results are still available.";
                }
                catch (Exception ex) { aiReviewIssue = $"AI review stopped: {ex.Message}"; }
            }

            ReportTaskProgress("Scan finished", 1, 1);
            var ordered = _cues.OrderBy(c => c.TimeSeconds).ToArray(); _cues.Clear(); foreach (var cue in ordered) _cues.Add(cue);
            var audioCount = _cues.Count(c => _audioTracks.Any(t => string.Equals(t.Label, c.TrackLabel, StringComparison.OrdinalIgnoreCase)));
            var videoCount = _cues.Count(c => string.Equals(c.TrackLabel, "Video frames", StringComparison.OrdinalIgnoreCase));
            var videoCandidateCount = _cues.Count(c => c.TrackLabel == "Video frames" && c.EventLabel.StartsWith("Closest video candidate:", StringComparison.OrdinalIgnoreCase));
            CueSummary.Text = $"{_cues.Count} bookmarks · {audioCount} audio · {videoCount} video";
            var statusParts = new List<string>();
            if (audioScanned) statusParts.Add($"Audio found {audioCount}"); else if (audioIssue is not null) statusParts.Add($"Audio: {audioIssue}");
            if (videoScanned && videoCandidateCount > 0) statusParts.Add($"No strong visual hits; added {videoCandidateCount} closest frames to review");
            else if (videoScanned) statusParts.Add($"Video found {videoCount}"); else if (videoIssue is not null) statusParts.Add($"Video: {videoIssue}");
            if (aiReviewedCount > 0)
            {
                var aiKeeps = _lastReviewKeeps;
                var aiSkips = _lastReviewSkips;
                var aiUnsure = _lastReviewUncertain;
                statusParts.Add($"Glitchy: {aiReviewedCount} windows checked ({aiKeeps} keep · {aiSkips} skip · {aiUnsure} unsure)");
            }
            else if (aiReviewIssue is not null) statusParts.Add(aiReviewIssue);
            statusParts.AddRange(_audioScanNotices);
            statusParts.AddRange(_videoScanNotices);
            StatusText.Text = string.Join("  ·  ", statusParts);
            var visualContext = IRLModeCheck.IsChecked == true ? "IRL footage" : string.IsNullOrWhiteSpace(GameTitleBox.Text) ? "general footage" : GameTitleBox.Text.Trim();
            PreferenceStatus.Text = $"The same prompt was checked against audio events and {visualContext} frames. Laughter gets at least 4 seconds before it; video bookmarks use {clipDuration:0.#}-second glitches. Keep/Skip stays under your control.";
            if (aiReviewedCount > 0) PreferenceStatus.Text += $" Glitchy reviewed {aiReviewedCount} sampled windows, attached evidence to candidates, and bookmarked supported scout findings. Your Keep/Skip decisions stay unchanged.";
            if (aiReviewIssue is not null) PreferenceStatus.Text += $" {aiReviewIssue}";
            else if (aiVerifyRequested && aiReviewedCount == 0) PreferenceStatus.Text += " No AI candidates were reviewed.";
            if (_audioScanNotices.Count > 0) PreferenceStatus.Text += " " + string.Join(" ", _audioScanNotices);
            if (_videoScanNotices.Count > 0) PreferenceStatus.Text += " " + string.Join(" ", _videoScanNotices);
            BuildTimeline();
            RefreshScanCategories(query);
            if (audioScanned || videoScanned) MarkProjectChanged();
        }
        catch (Exception ex) { StatusText.Text = $"Combined scan failed: {ex.Message}"; }
        finally { _isLaughScanRunning = false; SetBusy(false); }
    }

    private async Task<bool> EnsureAiReviewModelAsync()
    {
        if (!Uri.TryCreate(_copilotEndpoint.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttp || !baseUri.IsLoopback)
        {
            CyberDialog.Show(this, "AI candidate review needs a local Ollama service. Open Glitchy and connect to Ollama on this PC; remote model servers are not used for media review.", "Local AI review unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        JsonDocument tags;
        try
        {
            using var connectionTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await LocalAiClient.GetAsync(new Uri(baseUri, "api/tags"), connectionTimeout.Token);
            response.EnsureSuccessStatusCode();
            tags = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        }
        catch (Exception ex)
        {
            CyberDialog.Show(this, $"Start Ollama from Glitchy before enabling AI verification. This scan will continue without the second pass.\n\n{ex.Message}", "Ollama is not connected", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        using (tags)
        {
            var installed = tags.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array &&
                models.EnumerateArray().Any(item => item.TryGetProperty("name", out var name) && string.Equals(name.GetString(), "gemma4:e2b", StringComparison.OrdinalIgnoreCase));
            if (installed) return true;
        }

        var answer = CyberDialog.Show(this,
            "AI verification needs Gemma 4 E2B, a local model that can review the candidate's sampled video frames and short audio snippet. It is a several-GB download and Ollama saves it in its configured model folder. Make sure Ollama is configured to store models on D: before continuing. Download it now?",
            "Prepare AI candidate review", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return false;
        await PullAiReviewModelAsync(baseUri);
        return true;
    }

    private async Task PullAiReviewModelAsync(Uri baseUri)
    {
        StatusText.Text = "Preparing the local multimodal review model…";
        TaskProgressLabel.Text = "MODEL SETUP";
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "api/pull"))
        { Content = JsonContent.Create(new { name = "gemma4:e2b", stream = true }) };
        using var response = await LocalAiClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var status = JsonDocument.Parse(line);
            var item = status.RootElement;
            if (item.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.GetString() ?? "Ollama could not download Gemma 4 E2B.");
            var message = item.TryGetProperty("status", out var state) ? state.GetString() ?? "Downloading" : "Downloading";
            if (item.TryGetProperty("total", out var total) && item.TryGetProperty("completed", out var completed) && total.TryGetInt64(out var bytes) && bytes > 0 && completed.TryGetInt64(out var done))
                StatusText.Text = $"{message} · {FormatBytes(done)} / {FormatBytes(bytes)}";
            else StatusText.Text = message;
            if (item.TryGetProperty("total", out var size) && size.TryGetDouble(out var totalBytes) && item.TryGetProperty("completed", out var downloaded) && downloaded.TryGetDouble(out var downloadedBytes))
                ReportTaskProgress("Model download (bytes)", downloadedBytes, totalBytes);
        }
    }

    private async Task<int> ReviewAiCandidatesAsync(string query)
    {
        if (_isClosing) return 0;
        using var cancellation = new CancellationTokenSource();
        var progress = new Window { Owner = this, Title = "Glitchy footage review", Width = 480, SizeToContent = SizeToContent.Height, WindowStyle = WindowStyle.None, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush("#0B1520"), Foreground = Brush("#EAF5FF") };
        var frame = new Border { BorderBrush = Brush("#51D9E4"), BorderThickness = new Thickness(1), Padding = new Thickness(18) }; CyberShape.SetCut(frame, 16); progress.Content = frame;
        var panel = new StackPanel(); frame.Child = panel;
        panel.Children.Add(new TextBlock { Text = "GLITCHY // FOOTAGE REVIEW", Foreground = Brush("#F8D35C"), Margin = new Thickness(0, 0, 0, 10) });
        var description = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        description.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Text") { Source = StatusText }); panel.Children.Add(description);
        var bar = new ProgressBar { Height = 10, Foreground = Brush("#51D9E4"), Margin = new Thickness(0, 0, 0, 12) };
        bar.SetBinding(ProgressBar.ValueProperty, new System.Windows.Data.Binding("Value") { Source = TaskProgress }); panel.Children.Add(bar);
        var cancel = new Button { Content = "Cancel Glitchy review · keep detector results" }; cancel.Click += (_, _) => cancellation.Cancel(); panel.Children.Add(cancel);
        bool finished = false; progress.Closing += (_, args) => { if (!finished) { cancellation.Cancel(); args.Cancel = true; } };
        progress.Show();
        try
        {
            var review = await ReviewFootageBatchAsync(query, true, cancellation.Token);
            _lastFootageReview = review.Evidence;
            return _lastReviewedWindowCount;
        }
        finally { finished = true; progress.Close(); }
    }

    private async Task<bool> EnsureVideoModelAsync()
    {
        SyncScannerSources();
        var modelRoot = Path.Combine(LaughModelRoot, "clip-video");
        var marker = Path.Combine(LaughModelRoot, "clip-video-ready.marker");
        var worker = Path.Combine(LaughModelRoot, "src", "clip_video_scan.py");
        var bundledWorker = Path.Combine(ApplicationDirectory, "Resources", "LaughDetection", "src", "clip_video_scan.py");
        var model = Path.Combine(modelRoot, "onnx", "model_quantized.onnx");
        var tokenizer = Path.Combine(modelRoot, "tokenizer.json");
        if (File.Exists(bundledWorker))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(worker)!);
            if (!File.Exists(worker) || !string.Equals(File.ReadAllText(worker), File.ReadAllText(bundledWorker), StringComparison.Ordinal))
                File.Copy(bundledWorker, worker, overwrite: true);
        }
        var scannerVersion = File.Exists(marker) ? File.ReadAllText(marker) : "";
        var runtimeConfigured = scannerVersion.Contains("GPU-capable", StringComparison.OrdinalIgnoreCase)
            || scannerVersion.Contains("CPU fallback", StringComparison.OrdinalIgnoreCase);
        if (runtimeConfigured && File.Exists(worker) && File.Exists(model) && File.Exists(tokenizer)) return true;
        var setupScript = Path.Combine(ApplicationDirectory, "Resources", "LaughDetection", "video_setup.ps1");
        if (!File.Exists(setupScript)) throw new FileNotFoundException("The video scan setup file is missing beside GlitchStich.", setupScript);
        var answer = CyberDialog.Show(this,
            "GlitchStich will update the local visual scanner and keep its model and runtime on the same drive as the app. The quantized detector uses CPU inference; video processing can use NVIDIA acceleration. This may download additional runtime packages. Video stays on this PC; only sampled frames are temporary. The visual model can still miss extremely brief movement or context-dependent jokes.\n\nUpdate the video scanner now?",
            "Enable GPU video scanning", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer != MessageBoxResult.Yes) return false;
        var python = Path.Combine(LaughModelRoot, "python", "python.exe");
        if (!File.Exists(python)) throw new FileNotFoundException("Run the audio scan setup once to prepare GlitchStich's local Python runtime.", python);
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(setupScript)!
        };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-ExecutionPolicy"); start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File"); start.ArgumentList.Add(setupScript);
        start.ArgumentList.Add("-InstallRoot"); start.ArgumentList.Add(LaughModelRoot);
        using var process = new Process { StartInfo = start };
        process.Start();
        var errorTask = process.StandardError.ReadToEndAsync();
        while (await process.StandardOutput.ReadLineAsync() is { } line)
            if (!string.IsNullOrWhiteSpace(line)) StatusText.Text = line;
        await process.WaitForExitAsync();
        var errors = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(errors) ? "Video scanner setup did not complete." : errors.Trim());
        return File.Exists(marker) && File.Exists(worker) && File.Exists(model) && File.Exists(tokenizer);
    }

    private async Task<List<AudioEventDetection>> RunVideoScanAsync(string query, double threshold, double contextSeconds, double clipDuration, string gameTitle, string gameMode, bool isIrl)
    {
        var outputPath = Path.Combine(LaughModelRoot, $"video-scan-output-{Guid.NewGuid():N}.json");
        var python = Path.Combine(LaughModelRoot, "python", "python.exe");
        var worker = Path.Combine(LaughModelRoot, "src", "clip_video_scan.py");
        var modelRoot = Path.Combine(LaughModelRoot, "clip-video");
        var model = Path.Combine(modelRoot, "onnx", "model_quantized.onnx");
        var tokenizer = Path.Combine(modelRoot, "tokenizer.json");
        var providerName = "CPU";
        try
        {
            var start = new ProcessStartInfo(python)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                RedirectStandardError = true, WorkingDirectory = LaughModelRoot
            };
            var scanTempDirectory = Path.Combine(LaughModelRoot, "temp");
            Directory.CreateDirectory(scanTempDirectory);
            start.Environment["TEMP"] = scanTempDirectory;
            start.Environment["TMP"] = scanTempDirectory;
            start.Environment["TMPDIR"] = scanTempDirectory;
            start.ArgumentList.Add("-u"); start.ArgumentList.Add(worker);
            start.ArgumentList.Add("--video"); start.ArgumentList.Add(_videoPath!);
            start.ArgumentList.Add("--ffmpeg"); start.ArgumentList.Add(_ffmpegPath!);
            start.ArgumentList.Add("--model"); start.ArgumentList.Add(model);
            start.ArgumentList.Add("--tokenizer"); start.ArgumentList.Add(tokenizer);
            start.ArgumentList.Add("--output"); start.ArgumentList.Add(outputPath);
            start.ArgumentList.Add("--query"); start.ArgumentList.Add(query);
            if (!isIrl && !string.IsNullOrWhiteSpace(gameTitle)) { start.ArgumentList.Add("--game"); start.ArgumentList.Add(gameTitle); }
            if (!isIrl && !string.IsNullOrWhiteSpace(gameMode)) { start.ArgumentList.Add("--game-mode"); start.ArgumentList.Add(gameMode); }
            var fortniteExamples = @"D:\Videos\NVidiaFortnite";
            if (!isIrl && gameTitle.Contains("fortnite", StringComparison.OrdinalIgnoreCase) && Directory.Exists(fortniteExamples))
            {
                start.ArgumentList.Add("--examples-dir"); start.ArgumentList.Add(fortniteExamples);
                start.ArgumentList.Add("--examples-cache"); start.ArgumentList.Add(Path.Combine(_settingsDirectory, "fortnite-example-embeddings.npz"));
            }
            if (isIrl) start.ArgumentList.Add("--irl");
            start.ArgumentList.Add("--threshold"); start.ArgumentList.Add(threshold.ToString(CultureInfo.InvariantCulture));
            start.ArgumentList.Add("--context"); start.ArgumentList.Add(contextSeconds.ToString(CultureInfo.InvariantCulture));
            start.ArgumentList.Add("--window"); start.ArgumentList.Add(clipDuration.ToString(CultureInfo.InvariantCulture));
            using var process = new Process { StartInfo = start };
            process.Start();
            try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            var errorTask = process.StandardError.ReadToEndAsync();
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                var parts = line.Split('|');
                if (parts.Length == 3 && parts[0] == "VERIFY")
                {
                    StatusText.Text = $"Checking outcome indicators · {parts[1]} of {parts[2]}";
                    if (TryProgressNumber(parts[1], out var done) && TryProgressNumber(parts[2], out var total)) ReportTaskProgress("Outcome checks", done, total);
                }
                else if (parts.Length == 3 && parts[0] == "SCANNING")
                {
                    StatusText.Text = $"Comparing video frames on {providerName} · {parts[1]} of {parts[2]}";
                    if (TryProgressNumber(parts[1], out var done) && TryProgressNumber(parts[2], out var total)) ReportTaskProgress("Video frames", done, total);
                }
                else if (parts.Length >= 2 && parts[0] == "PROVIDER")
                {
                    providerName = parts[1] == "CUDA" ? "NVIDIA GPU" : "CPU";
                    EngineStatus.Text = parts[1] == "CUDA" ? "Visual model · NVIDIA CUDA" : "Visual model · CPU mode";
                    StatusText.Text = parts[1] == "CUDA" ? "NVIDIA GPU is analyzing video frames…" : "Analyzing video frames on CPU…";
                }
                else if (line.StartsWith("GPU_FALLBACK|", StringComparison.Ordinal))
                    StatusText.Text = "NVIDIA acceleration could not start; switching this scan to CPU…";
                else if (line.StartsWith("EXTRACT|", StringComparison.Ordinal))
                {
                    StatusText.Text = "Extracting temporary frames twice per second…";
                    if (parts.Length >= 2 && TryProgressNumber(parts[1], out var seconds)) ReportTaskProgress("Frame extraction (seconds)", seconds, _durationSeconds);
                }
                else if (parts.Length == 3 && parts[0] == "EXAMPLES")
                    StatusText.Text = parts[1] == "cached"
                        ? $"Loaded {parts[2]} cached Fortnite example frames"
                        : parts[1] == "active"
                            ? $"Fortnite examples enabled · {parts[2]} reference frames"
                            : $"Preparing local Fortnite examples · {parts[1]} of {parts[2]} glitches";
            }
            await process.WaitForExitAsync();
            var errors = await errorTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(errors) ? "Visual model inference failed." : errors.Trim());
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            if (result.RootElement.TryGetProperty("notices", out var notices))
                foreach (var notice in notices.EnumerateArray())
                    if (!string.IsNullOrWhiteSpace(notice.GetString())) _videoScanNotices.Add(notice.GetString()!);
            var detections = new List<AudioEventDetection>();
            foreach (var segment in result.RootElement.GetProperty("segments").EnumerateArray())
                detections.Add(new AudioEventDetection(segment.GetProperty("start").GetDouble(), segment.GetProperty("end").GetDouble(),
                    segment.GetProperty("confidence").GetDouble(), segment.GetProperty("track").GetString() ?? "Video frames",
                    segment.GetProperty("event").GetString() ?? query, segment.GetProperty("eventStart").GetDouble()));
            return detections;
        }
        finally { try { File.Delete(outputPath); } catch { } }
    }

    private void SyncScannerSources()
    {
        var bundled = Path.Combine(ApplicationDirectory, "Resources", "LaughDetection", "src");
        var installed = Path.Combine(LaughModelRoot, "src");
        if (!Directory.Exists(bundled)) throw new DirectoryNotFoundException("The bundled scanner source folder is missing beside GlitchStich.");
        Directory.CreateDirectory(installed);
        foreach (var source in Directory.EnumerateFiles(bundled).Where(file => Path.GetExtension(file) is ".py" or ".ps1"))
        {
            var destination = Path.Combine(installed, Path.GetFileName(source));
            if (!File.Exists(destination) || File.ReadAllText(source) != File.ReadAllText(destination))
                File.Copy(source, destination, true);
        }
    }

    private async Task<bool> EnsureAudioEventModelAsync()
    {
        SyncScannerSources();
        var python = Path.Combine(LaughModelRoot, "python", "python.exe");
        var worker = Path.Combine(LaughModelRoot, "src", "yamnet_scan.py");
        var marker = Path.Combine(LaughModelRoot, "yamnet-ready.marker");
        if (File.Exists(marker) && File.Exists(python) && File.Exists(worker)) return true;

        var setupScript = Path.Combine(ApplicationDirectory, "Resources", "LaughDetection", "setup.ps1");
        if (!File.Exists(setupScript))
            throw new FileNotFoundException("The audio event model setup files are missing beside GlitchStich.", setupScript);
        var answer = CyberDialog.Show(this,
            "GlitchStich needs to install its local audio event detector the first time you scan. This downloads about 2 GB of runtime and model files and uses roughly 2 GB of disk space beside the app on the same drive. Your video stays on this PC.\n\nInstall the audio event detector now?",
            "Install audio event detector", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer != MessageBoxResult.Yes) return false;

        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(setupScript)!
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(setupScript);
        start.ArgumentList.Add("-InstallRoot");
        start.ArgumentList.Add(LaughModelRoot);
        start.Environment["TFHUB_CACHE_DIR"] = Path.Combine(LaughModelRoot, "yamnet-cache");
        using var process = new Process { StartInfo = start };
        process.Start();
        var errorTask = process.StandardError.ReadToEndAsync();
        while (await process.StandardOutput.ReadLineAsync() is { } line)
            if (!string.IsNullOrWhiteSpace(line)) StatusText.Text = line;
        await process.WaitForExitAsync();
        var errors = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(errors) ? "Audio event detector setup did not complete." : errors.Trim());
        return File.Exists(marker) && File.Exists(python) && File.Exists(worker);
    }

    private async Task<List<AudioEventDetection>> RunAudioEventScanAsync(string query, double threshold)
    {
        if (_audioTracks.Any(t => t.IsCombinedMix))
            _audioScanNotices.Add("Combined mix excluded from audio scan; separate PC and microphone tracks are scanned.");
        var inputPath = Path.Combine(LaughModelRoot, $"scan-input-{Guid.NewGuid():N}.json");
        var outputPath = Path.Combine(LaughModelRoot, $"scan-output-{Guid.NewGuid():N}.json");
        var python = Path.Combine(LaughModelRoot, "python", "python.exe");
        var worker = Path.Combine(LaughModelRoot, "src", "yamnet_scan.py");
        await File.WriteAllTextAsync(inputPath, JsonSerializer.Serialize(new
        {
            tracks = _audioTracks.Where(track => !track.IsCombinedMix && (track.DuplicateOf is null || !track.IsMuted)).Select(track => new { path = track.WavPath, label = track.Label })
        }));
        try
        {
            var start = new ProcessStartInfo(python)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = LaughModelRoot
            };
            start.Environment["TF_CPP_MIN_LOG_LEVEL"] = "3";
            start.Environment["TF_USE_LEGACY_KERAS"] = "1";
            start.Environment["TFHUB_CACHE_DIR"] = Path.Combine(LaughModelRoot, "yamnet-cache");
            start.Environment["OMP_NUM_THREADS"] = Math.Clamp(Environment.ProcessorCount / 2, 1, 8).ToString(CultureInfo.InvariantCulture);
            start.ArgumentList.Add("-u");
            start.ArgumentList.Add(worker);
            start.ArgumentList.Add("--input-manifest"); start.ArgumentList.Add(inputPath);
            start.ArgumentList.Add("--output"); start.ArgumentList.Add(outputPath);
            start.ArgumentList.Add("--query"); start.ArgumentList.Add(query);
            // Keep the model's candidate pool permissive; personalized ranking is applied below.
            start.ArgumentList.Add("--threshold"); start.ArgumentList.Add("0.05");
            using var process = new Process { StartInfo = start };
            process.Start();
            var errorTask = process.StandardError.ReadToEndAsync();
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (line.StartsWith("SCANNING|", StringComparison.Ordinal))
                {
                    var parts = line.Split('|');
                    if (parts.Length == 3)
                    {
                        StatusText.Text = $"Scanning {parts[1]} · {parts[2]} seconds";
                        var times = parts[2].Split('/');
                        if (times.Length == 2 && TryProgressNumber(times[0], out var done) && TryProgressNumber(times[1], out var total))
                            ReportTaskProgress($"Audio {parts[1]} (seconds)", done, total);
                    }
                }
                else if (line.StartsWith("CLASSES|", StringComparison.Ordinal))
                    PreferenceStatus.Text = $"Looking for: {line[8..]}";
            }
            await process.WaitForExitAsync();
            var errors = await errorTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(errors) ? "Audio event model inference failed." : errors.Trim());
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            if (result.RootElement.TryGetProperty("notices", out var notices))
                foreach (var notice in notices.EnumerateArray())
                    if (!string.IsNullOrWhiteSpace(notice.GetString())) _audioScanNotices.Add(notice.GetString()!);
            var detections = new List<AudioEventDetection>();
            foreach (var segment in result.RootElement.GetProperty("segments").EnumerateArray())
                detections.Add(new AudioEventDetection(
                    segment.GetProperty("start").GetDouble(),
                    segment.GetProperty("end").GetDouble(),
                    segment.GetProperty("confidence").GetDouble(),
                    segment.GetProperty("track").GetString() ?? "Audio track",
                    segment.GetProperty("event").GetString() ?? "Sound event"));
            return detections;
        }
        finally
        {
            try { File.Delete(inputPath); } catch { }
            try { File.Delete(outputPath); } catch { }
        }
    }

    private async Task ProbeTracksAsync()
    {
        if (_videoPath is null || !await EnsureEngineAsync()) return;
        var probe = Path.Combine(Path.GetDirectoryName(_ffmpegPath!)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        if (!File.Exists(probe)) throw new FileNotFoundException("ffprobe.exe must be beside ffmpeg.exe to inspect separate tracks.", probe);
        var result = await RunProcessAsync(probe, ["-v", "error", "-select_streams", "a", "-show_entries", "stream=index,codec_name,channels:stream_tags=title,name,language", "-of", "json", _videoPath]);
        using var document = JsonDocument.Parse(result.Stdout);
        var previousTracks = _audioTracks.Where(track => !track.IsExternal).ToDictionary(track => track.StreamIndex);
        var importedTracks = _audioTracks.Where(track => track.IsExternal).ToArray();
        _audioTracks.Clear();
        var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var labelsByIndex = streams.Select((stream, i) => (Index: stream.GetProperty("index").GetInt32(), Label: AudioTrackPolicy.ReadLabel(stream, i + 1)))
            .ToDictionary(item => item.Index, item => item.Label);
        var labels = labelsByIndex.Values.ToArray();
        foreach (var stream in streams)
        {
            var index = stream.GetProperty("index").GetInt32();
            if (_removedAudioStreamIndices.Contains(index)) continue;
            var title = labelsByIndex[index];
            var channels = stream.TryGetProperty("channels", out var ch) ? ch.GetInt32() : 0;
            var codec = stream.TryGetProperty("codec_name", out var co) ? co.GetString() ?? "audio" : "audio";
            var track = new AudioTrack(index, string.IsNullOrWhiteSpace(title) ? $"Audio track {_audioTracks.Count + 1}" : title!, codec, channels);
            track.IsCombinedMix = AudioTrackPolicy.IsRedundantCombined(title, labels);
            track.IsMuted = track.IsCombinedMix;
            if (previousTracks.TryGetValue(index, out var previous))
            { track.Volume = previous.Volume; track.IsMuted = previous.IsMuted; track.WavPath = previous.WavPath; track.DuplicateOf = previous.DuplicateOf; track.DuplicateFingerprint = previous.DuplicateFingerprint; }
            _audioTracks.Add(track);
        }
        _audioTracks.AddRange(importedTracks);
        MediaDetails.Text = $"{FormatBytes(new FileInfo(_videoPath).Length)} · {_audioTracks.Count} audio stream(s)";
        if (_audioTracks.Count == 0) MediaDetails.Text += " · no audio";
        StatusText.Text = _audioTracks.Count == 0 ? "No audio tracks found." : $"Detected {_audioTracks.Count} separate audio stream(s).";
    }

    private void BuildTimeline()
    {
        RemoveDuplicateAudioButton.Visibility = _audioTracks.Any(track => track.IsMuted && (track.IsCombinedMix || track.DuplicateOf is not null)) ? Visibility.Visible : Visibility.Collapsed;
        RestoreDraftButton.Visibility = _draftBackup is null ? Visibility.Collapsed : Visibility.Visible;
        TimelineTracks.Children.Clear(); TimelineTracks.RowDefinitions.Clear(); TimelineTracks.ColumnDefinitions.Clear(); _stemMutes.Clear(); _stemVolumes.Clear();
        _selectedTimelineBorder = null;
        if ((_selectedTimelineItem is AudioTrack selectedAudio && !_audioTracks.Contains(selectedAudio)) || (_selectedTimelineItem is VideoTimelineItem && _videoPath is null))
        {
            _selectedTimelineItem = null;
            RemoveTimelineItemButton.IsEnabled = false;
        }
        if (_selectedVideoSegment is not null && !_videoEditSegments.Contains(_selectedVideoSegment)) _selectedVideoSegment = null;
        var width = Math.Max(1200, _durationSeconds * _pixelsPerSecond);
        var audioCount = _audioTracks.Count(t => !string.IsNullOrWhiteSpace(t.WavPath) && File.Exists(t.WavPath));
        EnsureStemPlayers();
        TimelineTracks.Width = width; TimelineTracks.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) }); TimelineTracks.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width - 118) });
        var videoLayerCount = Math.Max(_videoLayerCount, _videoEditSegments.Count == 0 ? 1 : _videoEditSegments.Max(segment => segment.TrackIndex) + 1);
        TimelineTracks.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        for (var i = 0; i < videoLayerCount; i++) TimelineTracks.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
        for (var i = 0; i < audioCount; i++) TimelineTracks.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) });
        AddLaneLabel("TIME", 0);
        var ruler = new Canvas { Width = width - 118, Height = 32, Background = Brush("#101316") }; _ruler = ruler;
        var interval = _durationSeconds > 1800 ? 120 : _durationSeconds > 600 ? 60 : _durationSeconds > 120 ? 15 : 5;
        for (var second = 0d; second <= _durationSeconds; second += interval)
        {
            var x = second * _pixelsPerSecond;
        ruler.Children.Add(new System.Windows.Shapes.Line { X1 = x, X2 = x, Y1 = 18, Y2 = 31, Stroke = Brush("#626970"), StrokeThickness = 1 });
            var text = new TextBlock { Text = FormatTime(second), FontSize = 9, Foreground = Brush("#B2B8BE") }; Canvas.SetLeft(text, x + 4); Canvas.SetTop(text, 2); ruler.Children.Add(text);
        }
        ruler.MouseLeftButtonDown += (_, e) => { SeekTo(e.GetPosition(ruler).X / _pixelsPerSecond); e.Handled = true; };
        Grid.SetColumn(ruler, 1); TimelineTracks.Children.Add(ruler);
        _videoSegmentBlocks.Clear(); _videoLane = null; _videoClip = null; _videoClipLabel = null;
        _reviewClipOverlay = null; _leftTrimHandle = null; _rightTrimHandle = null;
        for (var trackIndex = 0; trackIndex < videoLayerCount; trackIndex++)
        {
            var row = trackIndex + 1;
            AddLaneLabel($"V{trackIndex + 1}  VIDEO", row);
            var videoLane = MakeVideoClipLane(width - 118, 50, trackIndex);
            Grid.SetRow(videoLane, row); Grid.SetColumn(videoLane, 1); TimelineTracks.Children.Add(videoLane);
        }
        if (_selectedTimelineItem is VideoTimelineItem && _selectedVideoSegment is not null && _videoSegmentBlocks.TryGetValue(_selectedVideoSegment, out var activeVideoClip))
            SelectTimelineItem(_selectedTimelineItem, activeVideoClip);
        var laneIndex = 0;
        foreach (var track in _audioTracks.Where(t => !string.IsNullOrWhiteSpace(t.WavPath) && File.Exists(t.WavPath)))
        {
            var row = laneIndex + videoLayerCount + 1; var title = new TextBlock { Text = $"A{laneIndex + 1}  {track.Label}{(track.DuplicateOf is not null ? " (duplicate)" : track.IsCombinedMix ? " (combined mix)" : "")}", Foreground = Brush("#E0E3E6"), FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 7, 4, 0) };
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(7, 28, 0, 0) };
            var mute = new CheckBox { ToolTip = track.IsCombinedMix ? "Already contains PC and microphone audio. Muted by default and excluded from scans to prevent duplicates. Unmute to include this mix in playback/export." : "Mute this audio track in playback and export.", IsChecked = track.IsMuted, Content = "Mute", FontSize = 9, Foreground = Brush("#A0A6AC"), VerticalAlignment = VerticalAlignment.Center };
            var volume = new Slider { Minimum = 0, Maximum = 1, Value = track.Volume, Width = 72, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            controls.Children.Add(mute); controls.Children.Add(volume);
            var labelGrid = new Grid(); labelGrid.Children.Add(title); labelGrid.Children.Add(controls); Grid.SetRow(labelGrid, row); TimelineTracks.Children.Add(labelGrid);
            var audioLane = MakeClipLane(width - 118, 72, string.Empty);
            var audioBorder = (Border)audioLane.Children[0];
            if (ReferenceEquals(_selectedTimelineItem, track)) SelectTimelineItem(track, audioBorder);
            labelGrid.MouseLeftButtonDown += (_, e) => { SelectTimelineItem(track, audioBorder); e.Handled = true; };
            audioLane.MouseLeftButtonDown += (_, e) => { SelectTimelineItem(track, audioBorder); SeekTo(e.GetPosition(audioLane).X / _pixelsPerSecond); };
            if (_waveforms.TryGetValue(_audioTracks.IndexOf(track), out var values)) DrawWaveform(audioLane, values);
            Grid.SetRow(audioLane, row); Grid.SetColumn(audioLane, 1); TimelineTracks.Children.Add(audioLane);
            mute.Checked += (_, _) => SetAudioTrackMuted(track, true);
            mute.Unchecked += (_, _) => SetAudioTrackMuted(track, false);
            volume.ValueChanged += (_, _) => { track.Volume = volume.Value; ApplyTrackVolumes(); MarkProjectChanged(); };
            _stemMutes.Add(mute); _stemVolumes.Add(volume); laneIndex++;
        }
        var mediaCount = AddTimedMediaLanes(width - 118, videoLayerCount + audioCount + 1);
        var trackAreaHeight = 32 + videoLayerCount * 50 + audioCount * 72 + mediaCount * 44;
        _playhead = new Canvas { Width = 2, Height = trackAreaHeight, Background = Brush("#52F3FF"), IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = new TranslateTransform(118, 0) };
        Grid.SetColumnSpan(_playhead, 2); Grid.SetRowSpan(_playhead, videoLayerCount + audioCount + mediaCount + 1); TimelineTracks.Children.Add(_playhead); UpdatePlayhead(PreviewSeconds);
        TimelineTracks.Height = trackAreaHeight; DrawCueMarkers(); ApplyTrackVolumes();
        TimelineStatus.Text = $"Source-time view · {videoLayerCount} video layers · {audioCount} WAV tracks · {mediaCount} media/text placements · sequence order and effects in Stitch tools";
    }

    private void AddLaneLabel(string text, int row)
    {
        var block = new TextBlock { Text = text, Foreground = Brush("#9DA4AA"), FontWeight = FontWeights.SemiBold, FontSize = 9, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(block, row); TimelineTracks.Children.Add(block);
    }

    private Canvas MakeClipLane(double width, double height, string title)
    {
        var lane = new Canvas { Width = width, Height = height, Background = Brush("#101316") };
        lane.Children.Add(new Border { Width = Math.Max(1, width - 4), Height = height - 10, Margin = new Thickness(2, 5, 0, 0), CornerRadius = new CornerRadius(4), Background = Brush("#0F2230"), BorderBrush = Brush("#27516A"), BorderThickness = new Thickness(1) });
        if (!string.IsNullOrEmpty(title)) lane.Children.Add(new TextBlock { Text = title, Foreground = Brush("#9AF7FF"), FontSize = 9, Margin = new Thickness(9, 7, 0, 0), IsHitTestVisible = false });
        return lane;
    }

    private Canvas MakeVideoClipLane(double width, double height, int trackIndex)
    {
        var lane = new Canvas { Width = width, Height = height, Background = Brush("#101316") };
        var hasVideo = _videoPath is not null;
        var activeLane = trackIndex == (_selectedVideoSegment?.TrackIndex ?? 0);
        if (activeLane) _videoLane = lane;
        foreach (var (segment, index) in _videoEditSegments.Where(segment => segment.TrackIndex == trackIndex).Select((segment, index) => (segment, index)))
        {
            var selected = ReferenceEquals(segment, _selectedVideoSegment);
            var label = new TextBlock { Text = selected ? $"GLITCH {index + 1}  ·  drag edges to trim" : $"GLITCH {index + 1}", Foreground = Brush("#D7F2FF"), FontSize = 9, Margin = new Thickness(10, 7, 0, 0), IsHitTestVisible = false };
            var block = new Border { Height = height - 10, CornerRadius = new CornerRadius(3), Background = Brush(selected ? "#12475D" : "#18283D"), BorderBrush = Brush(selected ? "#52F3FF" : "#34718A"), BorderThickness = new Thickness(1), Child = label, ToolTip = $"Glitch {index + 1}: {FormatTime(segment.StartSeconds)}–{FormatTime(segment.EndSeconds)}" };
            block.MouseLeftButtonDown += (_, e) => { SelectVideoSegment(segment); SeekTo(segment.StartSeconds); e.Handled = true; };
            _videoSegmentBlocks[segment] = block;
            lane.Children.Add(block);
            if (selected) { _videoClip = block; _videoClipLabel = label; }
        }
        if (!hasVideo)
        {
            lane.Children.Add(new TextBlock { Text = "NO VIDEO LOADED", Foreground = Brush("#8D9BB7"), FontSize = 9, Margin = new Thickness(10, 7, 0, 0), IsHitTestVisible = false });
        }
        if (activeLane)
        {
            _reviewClipOverlay = new Border { Height = height - 10, CornerRadius = new CornerRadius(1), Background = Brush("#2852F3FF"), BorderBrush = Brush("#52F3FF"), BorderThickness = new Thickness(2), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            lane.Children.Add(_reviewClipOverlay);
            _leftTrimHandle = new Border { Width = 8, Height = height - 10, Background = Brush("#52F3FF"), CornerRadius = new CornerRadius(1, 0, 0, 1), Cursor = Cursors.SizeWE, ToolTip = "Drag to set glitch in-point" };
            _rightTrimHandle = new Border { Width = 8, Height = height - 10, Background = Brush("#52F3FF"), CornerRadius = new CornerRadius(0, 1, 1, 0), Cursor = Cursors.SizeWE, ToolTip = "Drag to set glitch out-point" };
            if (!hasVideo) { _leftTrimHandle.Visibility = Visibility.Collapsed; _rightTrimHandle.Visibility = Visibility.Collapsed; }
            lane.Children.Add(_leftTrimHandle); lane.Children.Add(_rightTrimHandle);
            _leftTrimHandle.MouseLeftButtonDown += (_, e) => { if (_videoPath is null) return; _dragTrimStart = true; lane.CaptureMouse(); e.Handled = true; };
            _rightTrimHandle.MouseLeftButtonDown += (_, e) => { if (_videoPath is null) return; _dragTrimEnd = true; lane.CaptureMouse(); e.Handled = true; };
            lane.MouseMove += (_, e) =>
            {
                if (!_dragTrimStart && !_dragTrimEnd) return;
                var seconds = Math.Clamp(e.GetPosition(lane).X / _pixelsPerSecond, 0, _durationSeconds);
                if (_dragTrimStart) _clipStartSeconds = Math.Min(seconds, _clipEndSeconds - .25);
                if (_dragTrimEnd) _clipEndSeconds = Math.Max(seconds, _clipStartSeconds + .25);
                if (_selectedCue is null && _selectedVideoSegment is not null)
                { _selectedVideoSegment.StartSeconds = _clipStartSeconds; _selectedVideoSegment.EndSeconds = _clipEndSeconds; }
                MarkProjectChanged();
                UpdateVideoClipVisuals(); TimelineStatus.Text = $"Trim range  {FormatTime(_clipStartSeconds)} – {FormatTime(_clipEndSeconds)}  ·  drag either edge to adjust";
            };
            lane.MouseLeftButtonUp += (_, e) =>
            {
                if (_dragTrimStart || _dragTrimEnd) { _dragTrimStart = false; _dragTrimEnd = false; lane.ReleaseMouseCapture(); e.Handled = true; }
            };
        }
        lane.MouseLeftButtonDown += (_, e) =>
        {
            if (_videoPath is null) return;
            var seconds = e.GetPosition(lane).X / _pixelsPerSecond;
            var segment = _videoEditSegments.FirstOrDefault(item => item.TrackIndex == trackIndex && seconds >= item.StartSeconds && seconds <= item.EndSeconds);
            if (segment is not null) SelectVideoSegment(segment);
            SeekTo(seconds);
        };
        UpdateVideoClipVisuals(); return lane;
    }

    private void SelectTimelineItem(object item, Border border)
    {
        if (_selectedTimelineBorder is not null)
        {
            _selectedTimelineBorder.BorderBrush = Brush(_selectedTimelineItem is VideoTimelineItem ? "#A66BFF" : "#27516A");
            _selectedTimelineBorder.BorderThickness = new Thickness(1);
        }
        _selectedTimelineItem = item;
        _selectedTimelineBorder = border;
        border.BorderBrush = Brush("#52F3FF");
        border.BorderThickness = new Thickness(2);
        RemoveTimelineItemButton.IsEnabled = true;
        TimelineStatus.Text = item is AudioTrack track
            ? $"Selected audio: {track.Label} · Remove selected removes its extracted WAV and track from this project"
            : "Selected video · Remove selected clears it from this project; the original file stays on disk";
    }

    private void RemoveTimelineItem_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTimelineItem is TimedMedia media) { _timedMedia.Remove(media); _selectedTimelineItem = null; BuildTimeline(); MarkProjectChanged(); return; }
        if (_selectedTimelineItem is AudioTrack track)
        {
            var answer = CyberDialog.Show(this,
                $"Remove {track.Label} from this project? The source and WAV stay on disk so Undo can restore the track.",
                "Remove audio track", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
            StopStemPlayers();
            try
            {
                var index = _audioTracks.IndexOf(track);
                if (track.StreamIndex >= 0) _removedAudioStreamIndices.Add(track.StreamIndex);
                if (index >= 0)
                {
                    var reindexed = _waveforms.Where(pair => pair.Key != index).ToDictionary(pair => pair.Key > index ? pair.Key - 1 : pair.Key, pair => pair.Value);
                    _waveforms.Clear(); foreach (var pair in reindexed) _waveforms[pair.Key] = pair.Value;
                }
                _audioTracks.Remove(track);
                if (_selectedCue is not null && string.Equals(_selectedCue.TrackLabel, track.Label, StringComparison.OrdinalIgnoreCase))
                { _selectedCue = null; ClipSelectedCueButton.Visibility = Visibility.Collapsed; AddCueLayerButton.Visibility = Visibility.Collapsed; }
                if (!string.IsNullOrWhiteSpace(track.WavPath))
                {
                    var extractedFolder = Path.GetDirectoryName(track.WavPath);
                    try { if (extractedFolder is not null && Directory.Exists(extractedFolder) && !Directory.EnumerateFileSystemEntries(extractedFolder).Any()) Directory.Delete(extractedFolder); } catch { }
                }
                _previewPlayer.Audio.Mute = ShouldMutePreviewAudio;
                // Bookmarks stay anchored to the source; removing a track is an undoable mix edit.
                _selectedTimelineItem = null;
                RemoveTimelineItemButton.IsEnabled = false;
                BuildTimeline();
                MarkProjectChanged();
                CueSummary.Text = $"{_cues.Count} bookmarks · {_audioTracks.Count} audio track(s)";
                StatusText.Text = $"Removed {track.Label} from this project; original source files were preserved.";
            }
            catch (Exception ex) { StatusText.Text = $"Could not remove the audio track: {ex.Message}"; }
            return;
        }
        if (_selectedTimelineItem is VideoTimelineItem && _selectedVideoSegment is not null)
        {
            var segment = _selectedVideoSegment;
            var answer = CyberDialog.Show(this,
                $"Remove this timeline glitch ({FormatTime(segment.StartSeconds)}–{FormatTime(segment.EndSeconds)}) from the edit? The original video stays on disk.",
                "Remove timeline glitch", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
            _videoEditSegments.Remove(segment);
            _selectedVideoSegment = _videoEditSegments.FirstOrDefault();
            _selectedTimelineItem = null; RemoveTimelineItemButton.IsEnabled = false;
            BuildTimeline();
            MarkProjectChanged();
            StatusText.Text = _videoEditSegments.Count == 0 ? "All video glitches were removed from the edit. The source file remains available." : "Removed the selected glitch from the edit.";
        }
    }

    private void UpdateVideoClipVisuals()
    {
        if (_videoLane is null || _leftTrimHandle is null || _rightTrimHandle is null) return;
        if (_videoPath is null)
        {
            return;
        }
        foreach (var pair in _videoSegmentBlocks)
        {
            var segment = pair.Key; var block = pair.Value;
            var left = segment.StartSeconds * _pixelsPerSecond; var right = segment.EndSeconds * _pixelsPerSecond;
            block.Width = Math.Max(8, right - left); Canvas.SetLeft(block, left); Canvas.SetTop(block, 5);
            block.BorderBrush = Brush(ReferenceEquals(segment, _selectedVideoSegment) ? "#52F3FF" : "#34718A");
            block.Background = Brush(ReferenceEquals(segment, _selectedVideoSegment) ? "#12475D" : "#18283D");
        }
        var trimStart = _selectedCue is not null || _selectedVideoSegment is null ? _clipStartSeconds : _selectedVideoSegment.StartSeconds;
        var trimEnd = _selectedCue is not null || _selectedVideoSegment is null ? _clipEndSeconds : _selectedVideoSegment.EndSeconds;
        var leftEdge = trimStart * _pixelsPerSecond; var rightEdge = trimEnd * _pixelsPerSecond;
        if (_reviewClipOverlay is not null)
        {
            _reviewClipOverlay.Visibility = _selectedCue is null ? Visibility.Collapsed : Visibility.Visible;
            _reviewClipOverlay.Width = Math.Max(8, rightEdge - leftEdge); Canvas.SetLeft(_reviewClipOverlay, leftEdge); Canvas.SetTop(_reviewClipOverlay, 5);
        }
        Canvas.SetLeft(_leftTrimHandle, leftEdge); Canvas.SetTop(_leftTrimHandle, 5);
        Canvas.SetLeft(_rightTrimHandle, rightEdge - _rightTrimHandle.Width); Canvas.SetTop(_rightTrimHandle, 5);
    }

    private void DrawWaveform(Canvas lane, double[] values)
    {
        if (values.Length == 0) return;
        var points = new PointCollection(values.Length * 2); var center = lane.Height / 2; var amplitude = lane.Height * .38;
        for (var i = 0; i < values.Length; i++)
        {
            var x = 3 + (lane.Width - 6) * i / Math.Max(1, values.Length - 1); var h = Math.Max(1, amplitude * values[i]);
            points.Add(new Point(x, center - h)); points.Add(new Point(x, center + h));
        }
        lane.Children.Add(new System.Windows.Shapes.Polyline { Points = points, Stroke = Brush("#52F3FF"), StrokeThickness = 1, IsHitTestVisible = false });
    }

    private static double[] ReadWaveform(string path, int bins)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 256, FileOptions.SequentialScan);
        using var reader = new BinaryReader(stream);
        if (new string(reader.ReadChars(4)) != "RIFF") return []; _ = reader.ReadInt32(); if (new string(reader.ReadChars(4)) != "WAVE") return [];
        ushort channels = 0, bits = 0; long start = 0, length = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            var id = new string(reader.ReadChars(4)); var size = reader.ReadUInt32(); var next = stream.Position + size + (size & 1);
            if (id == "fmt ") { _ = reader.ReadUInt16(); channels = reader.ReadUInt16(); _ = reader.ReadInt32(); _ = reader.ReadInt32(); _ = reader.ReadUInt16(); bits = reader.ReadUInt16(); }
            else if (id == "data") { start = stream.Position; length = size; break; } stream.Position = Math.Min(next, stream.Length);
        }
        if (channels == 0 || bits != 16 || length <= 0) return [];
        bins = Math.Clamp(bins, 100, 2400); var frames = length / (channels * 2L); var bytesPerBin = (int)Math.Max(channels * 2L, Math.Min(int.MaxValue, frames / bins * channels * 2L)); var buffer = new byte[bytesPerBin]; var values = new double[bins]; stream.Position = start;
        for (var i = 0; i < bins && stream.Position < start + length; i++)
        {
            var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, start + length - stream.Position)); double peak = 0;
            for (var j = 0; j + 1 < read; j += 2) peak = Math.Max(peak, Math.Abs((short)(buffer[j] | buffer[j + 1] << 8) / 32768.0)); values[i] = peak;
        }
        var max = values.Max(); if (max > 0) for (var i = 0; i < values.Length; i++) values[i] = Math.Sqrt(values[i] / max); return values;
    }

    private void DrawCueMarkers()
    {
        if (_ruler is null) return;
        foreach (var item in _ruler.Children.OfType<Border>().Where(b => Equals(b.Tag, "cue-marker")).ToArray()) _ruler.Children.Remove(item);
        foreach (var cue in _cues)
        {
            var marker = new Border { Tag = "cue-marker", Width = 9, Height = 10, CornerRadius = new CornerRadius(2), Background = Brush(cue.Decision switch { "Keep" => "#65D27C", "Skip" => "#747B81", _ => "#52F3FF" }), ToolTip = $"{FormatTime(cue.TimeSeconds)} setup · {cue.EventLabel} at {FormatTime(cue.EventTimeSeconds)} · score {cue.AdjustedConfidence:0.00} · {cue.TrackLabel} · {cue.Decision ?? "Review"}" };
            Canvas.SetLeft(marker, Math.Clamp(cue.TimeSeconds * _pixelsPerSecond, 0, Math.Max(0, _ruler.Width - 10))); Canvas.SetTop(marker, 19);
            marker.MouseLeftButtonDown += (_, e) => { e.Handled = true; CueList.SelectedItem = cue; PlayCue(cue); };
            _ruler.Children.Add(marker);
        }
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized) return; _pixelsPerSecond = ZoomSlider.Value; _zoomTimer.Stop(); _zoomTimer.Start();
    }
    private void UpdateClock()
    {
        if (_previewSourcePath is null || !_previewPlayer.CanPlay) return;
        SynchronizeStemPlayback();
        if (_previewSeekPending) return;
        var seconds = PreviewSeconds; CurrentTime.Text = FormatTime(seconds);
        if (!_seeking) SeekSlider.Value = Math.Min(SeekSlider.Maximum, seconds);
        UpdatePlayhead(seconds);
        if (_playbackRequested && _selectedCue is null && _videoEditSegments.Count > 0 && AdvanceSequencePlayback(seconds)) return;
        if (_playbackRequested && (_selectedCue is not null || _videoEditSegments.Count == 0) && seconds >= _clipEndSeconds)
        {
            _previewPlayer.Pause(); foreach (var player in _stemPlayers) try { player.Pause(); } catch { }
            SetPreviewPlaybackRequested(false); SeekTo(_clipEndSeconds);
        }
    }
    private void UpdatePlayhead(double seconds)
    {
        if (_playhead?.RenderTransform is TranslateTransform transform)
            transform.X = 118 + Math.Clamp(seconds, 0, _durationSeconds) * _pixelsPerSecond;
    }
    private void SetStemPositions(TimeSpan position) { foreach (var player in _stemPlayers) try { player.Position = position; } catch { } }
    private bool HasExtractedAudioStems => _audioTracks.Any(track => !string.IsNullOrWhiteSpace(track.WavPath) && File.Exists(track.WavPath));

    private bool ShouldMutePreviewAudio => _isMuted || HasExtractedAudioStems || _removedAudioStreamIndices.Count > 0;

    private void ApplyTrackVolumes()
    {
        _previewPlayer.Audio.Mute = ShouldMutePreviewAudio;
        var tracks = _audioTracks.Where(t => !string.IsNullOrWhiteSpace(t.WavPath) && File.Exists(t.WavPath)).ToArray();
        for (var i = 0; i < _stemPlayers.Count && i < tracks.Length; i++)
        { var clipVolume = _selectedCue is null && _sequencePlaybackIndex >= 0 && _sequencePlaybackIndex < _videoEditSegments.Count ? _videoEditSegments[_sequencePlaybackIndex].Effects.Volume : 1; _stemPlayers[i].Volume = Math.Clamp(tracks[i].Volume * clipVolume, 0, 1); _stemPlayers[i].IsMuted = _isMuted || tracks[i].IsMuted; }
    }
    private void StopStemPlayers()
    {
        foreach (var player in _stemPlayers) { try { player.Close(); player.Source = null; } catch { } RootGrid.Children.Remove(player); }
        _stemPlayers.Clear(); _stemMutes.Clear(); _stemVolumes.Clear();
        _readyStemPlayers.Clear(); _lastStemResync.Clear();
    }

    private void EnsureStemPlayers()
    {
        var sources = _audioTracks.Where(t => !string.IsNullOrWhiteSpace(t.WavPath) && File.Exists(t.WavPath)).Select(t => t.WavPath!).ToArray();
        var same = sources.Length == _stemPlayers.Count && sources.Select((source, i) => _stemPlayers[i].Tag as string == source).All(matches => matches);
        if (same) return;
        StopStemPlayers();
        foreach (var source in sources)
        {
            var audio = _audioTracks.First(track => track.WavPath == source);
            var player = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Stop, Width = 1, Height = 1, Opacity = .01, IsHitTestVisible = false, Tag = source, IsMuted = _isMuted || audio.IsMuted, Volume = audio.Volume };
            player.MediaOpened += (_, _) =>
            {
                if (_isClosing || !_stemPlayers.Contains(player)) return;
                try
                {
                    _readyStemPlayers.Add(player); player.SpeedRatio = _sourcePlaybackSpeed;
                    player.Position = PreviewPosition; ApplyTrackVolumes();
                    if (_previewSeekPending || !_playbackRequested) player.Pause();
                    else player.Play();
                }
                catch { }
            };
            player.Source = new Uri(source);
            RootGrid.Children.Add(player); Grid.SetRow(player, 0); Grid.SetColumnSpan(player, 2); _stemPlayers.Add(player);
        }
        ResumeStemPlayback();
    }

    private void AddCueAtPlayhead_Click(object sender, RoutedEventArgs e)
    {
        if (_videoPath is null) { StatusText.Text = "Import a video before adding a cue."; return; }
        AddCue(PreviewSeconds, "Manual bookmark");
        StatusText.Text = $"Added a review marker at {FormatTime(PreviewSeconds)}.";
    }

    private async void ExportClip_Click(object sender, RoutedEventArgs e)
    {
        if (_captureProcess is not null) { StatusText.Text = "Stop recording before starting this operation."; return; }
        if (_videoPath is null) { StatusText.Text = "Import a video before exporting."; return; }
        if (!await EnsureEngineAsync()) return;
        if (_clipEndSeconds - _clipStartSeconds < .25) { StatusText.Text = "Choose a longer trim range."; return; }
        var dialog = new SaveFileDialog { Title = "Export trimmed video", FileName = $"{Path.GetFileNameWithoutExtension(_videoPath)}_stitch.mp4", Filter = "MP4 video|*.mp4" };
        if (dialog.ShowDialog(this) != true) return;
        await ExportEditedVideoAsync(dialog.FileName, [new ExportSegment(_clipStartSeconds, _clipEndSeconds) { Effects = _selectedVideoSegment?.Effects }]);
    }

    private async void ExportTimeline_Click(object sender, RoutedEventArgs e)
    {
        if (_busyOperations > 0 || _captureProcess is not null) return;
        if (_videoPath is null || _videoEditSegments.Count == 0) { StatusText.Text = "Add video glitches before exporting."; return; }
        if (!await EnsureEngineAsync()) return;
        var dialog = new SaveFileDialog { Title = "Export stitch", FileName = $"{Path.GetFileNameWithoutExtension(_videoPath)}_stitch.mp4", Filter = "MP4 video|*.mp4" };
        if (dialog.ShowDialog(this) != true) return;
        await ExportEditedVideoAsync(dialog.FileName, _videoEditSegments.Select(segment => new ExportSegment(segment.StartSeconds, segment.EndSeconds) { Effects = segment.Effects }).ToArray());
    }

    private ExportAudio[] GetExportAudio() => _audioTracks
        .Where(t => (!(t.IsCombinedMix || t.DuplicateOf is not null) || !t.IsMuted) && (!t.IsExternal || (!string.IsNullOrWhiteSpace(t.WavPath) && File.Exists(t.WavPath))))
        .Select(t => new ExportAudio(t.StreamIndex, File.Exists(t.WavPath) ? t.WavPath : null, t.Volume, t.IsMuted)).ToArray();

    private void AddCue(double seconds, string label)
    {
        if (_cues.Any(c => c.SourceLabel == label && Math.Abs(c.TimeSeconds - seconds) < 1)) return;
        _cues.Add(new CueItem(seconds, label)); MarkProjectChanged(); CueSummary.Text = $"{_cues.Count} moments · review each cue"; DrawCueMarkers();
    }

    private static bool IsLaughterEvent(string eventLabel)
    {
        var label = eventLabel.ToLowerInvariant();
        return label.Contains("laugh") || label.Contains("chuckle") || label.Contains("giggle");
    }

    private void SaveFeedback()
    {
        try
        {
            Directory.CreateDirectory(_settingsDirectory);
            var feedback = _cues.Where(c => c.Decision is not null).Select(c => new { c.TimeSeconds, c.EventTimeSeconds, c.Confidence, c.SourceLabel, c.Decision, video = _videoPath });
            File.WriteAllText(Path.Combine(_settingsDirectory, "review-feedback.json"), JsonSerializer.Serialize(feedback, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(_settingsDirectory, "event-preferences.json"), JsonSerializer.Serialize(_eventPreferences, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { StatusText.Text = $"Could not save review choice: {ex.Message}"; }
    }

    private void LoadEventPreferences()
    {
        try
        {
            var path = Path.Combine(_settingsDirectory, "event-preferences.json");
            if (!File.Exists(path)) return;
            var saved = JsonSerializer.Deserialize<Dictionary<string, EventPreference>>(File.ReadAllText(path));
            if (saved is null) return;
            foreach (var pair in saved) _eventPreferences[pair.Key] = pair.Value;
        }
        catch { /* A bad preference file must not prevent editing. */ }
    }

    private void PreferenceBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (PreferencePlaceholder is not null)
            PreferencePlaceholder.Visibility = string.IsNullOrEmpty(PreferenceBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (_videoPath is not null) MarkProjectChanged();
    }

    private void PreferenceBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        e.Handled = true;
        Analyze_Click(sender, new RoutedEventArgs());
    }

    private void GameTitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (GameTitlePlaceholder is not null)
            GameTitlePlaceholder.Visibility = string.IsNullOrEmpty(GameTitleBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (_videoPath is not null) { if (_busyOperations == 0) _quietDowntimeRanges.Clear(); MarkProjectChanged(); }
    }

    private void IRLMode_Checked(object sender, RoutedEventArgs e)
    {
        UpdateContextInputs();
        if (_videoPath is not null) { if (_busyOperations == 0) _quietDowntimeRanges.Clear(); MarkProjectChanged(); }
        if (PreferenceStatus is null) return;
        PreferenceStatus.Text = IRLModeCheck.IsChecked == true
            ? "IRL selected: visual matching focuses on real-life footage and ignores the game title. Audio still uses your prompt."
            : "Optional game title narrows the visual scan. Audio matches your prompt to known sound types.";
    }

    private void UpdateContextInputs()
    {
        if (GameTitleBox is null || IRLModeCheck is null) return;
        var realLife = IRLModeCheck.IsChecked == true;
        GameTitleBox.IsEnabled = !realLife;
        GameTitleBox.Opacity = realLife ? 0.42 : 1;
        if (GameModeBox is not null) { GameModeBox.IsEnabled = !realLife; GameModeBox.Opacity = realLife ? 0.42 : 1; }
        if (GameTitlePlaceholder is not null)
            GameTitlePlaceholder.Visibility = string.IsNullOrEmpty(GameTitleBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddEventFeedback(string eventLabel, string? decision, int direction, string? scope = null)
    {
        if (decision is null) return;
        var key = (scope ?? CurrentPreferenceScope) + "::" + eventLabel;
        if (!_eventPreferences.TryGetValue(key, out var preference))
            _eventPreferences[key] = preference = new EventPreference();
        if (decision == "Keep") preference.Kept = Math.Max(0, preference.Kept + direction);
        else preference.Skipped = Math.Max(0, preference.Skipped + direction);
    }

    private double AdjustForPreference(string eventLabel, double score)
    {
        if (!_eventPreferences.TryGetValue(CurrentPreferenceScope + "::" + eventLabel, out var preference)) return score;
        var evidence = preference.Kept + preference.Skipped;
        if (evidence == 0) return score;
        var adjustment = 0.12 * (preference.Kept - preference.Skipped) / (evidence + 2.0);
        return Math.Clamp(score + adjustment, 0, 1);
    }

    private async void ApplyPreference_Click(object sender, RoutedEventArgs e)
    {
        var preference = PreferenceBox.Text.Trim();
        if (preference.Length == 0) { PreferenceStatus.Text = "Enter a preference first."; return; }
        var confirm = CyberDialog.Show(this, $"Save this preference for future cue reviews?\n\n{preference}\n\nThis saves your scan guidance locally. Keep/Skip choices also adapt how strongly matching sound types are ranked.", "Approve preference change", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) { PreferenceStatus.Text = "Preference change dismissed."; return; }
        try
        {
            Directory.CreateDirectory(_settingsDirectory);
            var profilePath = Path.Combine(_settingsDirectory, "preferences-by-context.json");
            var profiles = File.Exists(profilePath) ? JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(profilePath)) ?? [] : new Dictionary<string, string>();
            profiles[CurrentPreferenceScope] = preference;
            ProjectFileStore.Write(profilePath, JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
            PreferenceStatus.Text = "Saved locally with your approval. Keep/Skip choices tune event ranking over time; pretrained model weights stay unchanged.";
        }
        catch (Exception ex) { PreferenceStatus.Text = $"Could not save preference: {ex.Message}"; }
    }

    private void RescanPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (_videoPath is null)
        {
            PreferenceStatus.Text = "Import a video before rescanning cues.";
            return;
        }
        PreferenceStatus.Text = "Rescanning audio tracks and video frames with the same prompt.";
        Analyze_Click(sender, e);
    }

    private void SetBusy(bool busy, string? message = null)
    {
        var wasIdle = _busyOperations == 0;
        _busyOperations = Math.Max(0, _busyOperations + (busy ? 1 : -1));
        busy = _busyOperations > 0;
        RootGrid.IsEnabled = !_isClosing;
        foreach (UIElement child in RootGrid.Children)
            if (!ReferenceEquals(child, GlitchyHost) && !ReferenceEquals(child, HeaderPanel)) child.IsEnabled = !busy && !_isClosing;
        void SetHeaderActions(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Button button) button.IsEnabled = !_isClosing && (button.Content?.ToString() is "—" or "□" or "×" || !busy);
                else SetHeaderActions(child);
            }
        }
        SetHeaderActions(HeaderPanel);
        Mouse.OverrideCursor = null;
        RootGrid.Cursor = busy && !_isClosing ? Cursors.Wait : null;
        GlitchyHost.SetBusy(busy);
        TaskProgress.IsIndeterminate = false;
        if (busy && wasIdle && !_isClosing) BeginTaskProgress();
        if (!busy) { _taskTicker?.Stop(); _taskElapsed.Stop(); _progressCaption = "READY / LAST TASK ENDED"; RefreshTaskCaption(); }
        if (message is not null) StatusText.Text = message;
    }
    private async Task<bool> EnsureEngineAsync()
    {
        if (_isClosing) return false;
        try { _ffmpegPath = await _engineInitialization; }
        catch (Exception ex)
        {
            EngineStatus.Text = "Built-in media engine unavailable";
            StatusText.Text = $"Could not prepare GlitchStich's built-in FFmpeg tools: {ex.Message}";
            return false;
        }
        if (_isClosing) return false;
        if (HasAdjacentProbe(_ffmpegPath))
        {
            EngineStatus.Text = $"Media engine ready · {Path.GetFileName(_ffmpegPath)}";
            return true;
        }
        EngineStatus.Text = "Built-in media engine unavailable";
        StatusText.Text = "GlitchStich could not find its built-in FFmpeg tools. Reinstall the app to restore them.";
        return false;
    }

    private Task<string?> InitializeBundledEngineAsync() => Task.Run<string?>(() =>
    {
        var logFile = Path.Combine(_settingsDirectory, "engine-startup.log");
        void Log(string message)
        {
            try { Directory.CreateDirectory(_settingsDirectory); File.AppendAllText(logFile, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"); } catch { }
        }
        try
        {
            var installFolder = Path.Combine(_settingsDirectory, "tools");
            var ffmpeg = Path.Combine(installFolder, "ffmpeg.exe");
            var ffprobe = Path.Combine(installFolder, "ffprobe.exe");
            var assembly = Assembly.GetExecutingAssembly();
            var resources = new[] { (Name: "ReelGood.tools.ffmpeg.exe", Path: ffmpeg), (Name: "ReelGood.tools.ffprobe.exe", Path: ffprobe) };

            var needsInstall = false;
            foreach (var item in resources)
            {
                using var embedded = assembly.GetManifestResourceStream(item.Name)
                    ?? throw new FileNotFoundException($"Embedded media tool {item.Name} is missing.");
                if (!File.Exists(item.Path) || new FileInfo(item.Path).Length != embedded.Length) needsInstall = true;
            }
            if (needsInstall)
            {
                Directory.CreateDirectory(installFolder);
                foreach (var item in resources)
                {
                    Log($"Extracting {item.Name}");
                    using (var source = assembly.GetManifestResourceStream(item.Name)
                        ?? throw new FileNotFoundException($"Embedded media tool {item.Name} is missing."))
                    {
                        using var target = new FileStream(item.Path, FileMode.Create, FileAccess.Write, FileShare.None);
                        source.CopyTo(target);
                    }
                    Log($"Extracted {item.Name}");
                    Log($"Installed {item.Name}");
                }
            }
            if (!HasAdjacentProbe(ffmpeg)) throw new FileNotFoundException("The built-in FFmpeg tools did not install correctly.");
            Log("Media tools ready");
            return ffmpeg;
        }
        catch (Exception ex)
        {
            Log($"Bundled media tool setup failed: {ex}");
            // Allow source builds without embedded tools to use an existing local installation.
            var fallback = FindFfmpeg();
            if (fallback is not null) return fallback;
            throw;
        }
    });

    private async Task RunFfmpegAsync(IEnumerable<string> args, double? expectedDuration = null)
    {
        var arguments = args.ToArray();
        var expected = expectedDuration ?? _durationSeconds;
        var durationIndex = Array.IndexOf(arguments, "-t");
        if (expectedDuration is null && durationIndex >= 0 && durationIndex + 1 < arguments.Length && TryProgressNumber(arguments[durationIndex + 1], out var length)) expected = length;
        else if (expectedDuration is null && (arguments.Contains("-filter_complex") || arguments.Contains("-ss"))) expected = 0;
        var start = new ProcessStartInfo(_ffmpegPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-progress", "pipe:1", "-nostats" }.Concat(arguments)) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        ReportTaskProgress("Media processing (seconds)", 0, expected);
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            if (line.StartsWith("out_time_us=", StringComparison.Ordinal) && TryProgressNumber(line[12..], out var micros))
            {
                if (expected > 0) ReportTaskProgress("Media processing (seconds)", micros / 1_000_000, expected);
                else { _progressCaption = $"MEDIA PROCESSING / {FormatTime(micros / 1_000_000)} WRITTEN"; RefreshTaskCaption(); }
            }
        }
        await process.WaitForExitAsync();
        var error = await errors;
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "FFmpeg media operation failed." : error.Trim());
        ReportTaskProgress("Media processing finished", 1, 1);
    }

    private Process StartFfmpeg(IEnumerable<string> args)
    {
        var start = new ProcessStartInfo(_ffmpegPath!) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in args) start.ArgumentList.Add(arg); var process = new Process { StartInfo = start }; process.Start(); _ = process.StandardError.ReadToEndAsync(); return process;
    }
    private static async Task<(string Stdout, string Stderr, int ExitCode)> RunProcessAsync(string file, IEnumerable<string> args)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start }; process.Start(); var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync(); return (await stdout, await stderr, process.ExitCode);
    }
    private static string? FindFfmpeg()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) { var candidate = Path.Combine(folder.Trim('"'), "ffmpeg.exe"); if (HasAdjacentProbe(candidate)) return candidate; }
        return null;
    }
    private static bool HasAdjacentProbe(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path) && File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "ffprobe.exe"));
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private static string FormatTime(double seconds) { var t = TimeSpan.FromSeconds(Math.Max(0, seconds)); return t.TotalHours >= 1 ? $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}"; }
    private static string FormatBytes(long bytes) => bytes >= 1_000_000_000 ? $"{bytes / 1_000_000_000d:0.0} GB" : $"{bytes / 1_000_000d:0.0} MB";

    private sealed class ProjectDocument
    {
        public ProjectDocument() { }
        public int Version { get; set; } = 1;
        public string? VideoPath { get; set; }
        public List<string>? MediaLibraryPaths { get; set; }
        public List<EditingAsset>? EditingAssets { get; set; }
        public List<TimedMedia>? TimedMedia { get; set; }
        public int EditingMode { get; set; }
        public int? SecondaryEditingMode { get; set; }
        public List<EditorialRange>? ProtectedSceneRanges { get; set; }
        public List<EditorialRange>? QuietDowntimeRanges { get; set; }
        public string Prompt { get; set; } = "";
        public string GameTitle { get; set; } = "";
        public string GameMode { get; set; } = "";
        public bool IsIrl { get; set; }
        public double ClipStartSeconds { get; set; }
        public double ClipEndSeconds { get; set; }
        public int VideoLayerCount { get; set; } = 1;
        public List<int> RemovedAudioStreamIndices { get; set; } = [];
        public List<SavedVideoSegment> VideoSegments { get; set; } = [];
        public List<SavedAudioTrack> AudioTracks { get; set; } = [];
        public List<SavedCue> Cues { get; set; } = [];
        public SavedDraftBackup? DraftBackup { get; set; }
    }

    private sealed record SavedVideoSegment(double StartSeconds, double EndSeconds, int TrackIndex, ClipEffects? Effects = null);
    private sealed record SavedAudioTrack(int StreamIndex, string Label, string Codec, int Channels, string? WavPath, string? ExternalSourcePath, double Volume = .8, bool IsMuted = false, bool? IsCombinedMix = null, string? DuplicateOf = null, string? DuplicateFingerprint = null);
    private sealed record SavedCue(double TimeSeconds, string EventLabel, string TrackLabel, double EventTimeSeconds,
        double Confidence, double AdjustedConfidence, double ClipEndSeconds, string? Decision, string? AiSuggestion, string? FeedbackScope = null);

    private enum MediaKind { Video, Audio, Image }
    private sealed class MediaLibraryItem
    {
        public required string Path { get; init; }
        public required string Name { get; init; }
        public required string Details { get; init; }
        public required string Icon { get; init; }
        public required MediaKind Kind { get; init; }
        public string KindLabel => Kind switch { MediaKind.Video => "Video", MediaKind.Audio => "Audio", _ => "PNG image" };

        public static MediaLibraryItem FromPath(string path)
        {
            var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
            var kind = extension == ".png" ? MediaKind.Image : extension is ".wav" or ".mp3" or ".m4a" or ".aac" or ".flac" or ".ogg" or ".wma" ? MediaKind.Audio : MediaKind.Video;
            var info = new FileInfo(path);
            return new MediaLibraryItem
            {
                Path = path,
                Name = info.Name,
                Details = $"{(kind == MediaKind.Image ? "PNG" : info.Extension.TrimStart('.').ToUpperInvariant())} · {FormatBytes(info.Length)}",
                Icon = kind == MediaKind.Audio ? "♫" : kind == MediaKind.Image ? "▧" : "▣",
                Kind = kind
            };
        }
    }

    private sealed class AudioTrack(int streamIndex, string label, string codec, int channels)
    {
        public int StreamIndex { get; } = streamIndex; public string Label { get; } = label; public string Codec { get; } = codec; public int Channels { get; } = channels; public string? WavPath { get; set; }
        public string? ExternalSourcePath { get; set; }
        public double Volume { get; set; } = .8;
        public bool IsMuted { get; set; }
        public bool IsCombinedMix { get; set; }
        public string? DuplicateOf { get; set; }
        public string? DuplicateFingerprint { get; set; }
        public bool IsExternal => !string.IsNullOrWhiteSpace(ExternalSourcePath);
    }
    private sealed class VideoEditSegment(double startSeconds, double endSeconds)
    {
        public double StartSeconds { get; set; } = startSeconds;
        public double EndSeconds { get; set; } = endSeconds;
        public int TrackIndex { get; set; }
        public ClipEffects Effects { get; set; } = new();
    }
    private sealed record ScanCategory(string Name, int Count, List<CueItem> Items);
    private sealed class VideoTimelineItem
    {
        public static VideoTimelineItem Instance { get; } = new();
        private VideoTimelineItem() { }
    }
    private sealed record AudioEventDetection(double StartSeconds, double EndSeconds, double Confidence, string TrackLabel, string EventLabel, double? EventStart = null)
    {
        public double EventStartSeconds => EventStart ?? StartSeconds;
    }

    private sealed class EventPreference
    {
        public EventPreference() { }
        public int Kept { get; set; }
        public int Skipped { get; set; }
    }

    private sealed record CopilotSettings(string Endpoint, string Model, bool LearnFromFeedback);

    private sealed class CueItem : INotifyPropertyChanged
    {
        public CueItem(double timeSeconds, string sourceLabel) : this(timeSeconds, sourceLabel, "", timeSeconds, 0, 0, timeSeconds + 2) { }
        public CueItem(double timeSeconds, string eventLabel, string trackLabel, double eventTimeSeconds, double confidence, double adjustedConfidence, double clipEndSeconds)
        { TimeSeconds = timeSeconds; EventLabel = eventLabel; TrackLabel = trackLabel; EventTimeSeconds = eventTimeSeconds; Confidence = confidence; AdjustedConfidence = adjustedConfidence; ClipEndSeconds = clipEndSeconds; }
        public double TimeSeconds { get; } public string EventLabel { get; } public string TrackLabel { get; }
        public string SourceLabel => string.IsNullOrEmpty(TrackLabel) ? EventLabel : $"{EventLabel} · {TrackLabel}";
        public double EventTimeSeconds { get; } public double ClipEndSeconds { get; } public double Confidence { get; } public double AdjustedConfidence { get; }
        private string? _decision; public string? Decision { get => _decision; set { _decision = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); } }
        public string? FeedbackScope { get; set; }
        private string? _aiSuggestion; public string? AiSuggestion { get => _aiSuggestion; set { _aiSuggestion = value; OnPropertyChanged(nameof(DisplayText)); } }
        public string DisplayText => $"{FormatTime(TimeSeconds)} setup  ·  {EventLabel} {FormatTime(EventTimeSeconds)}  ·  {(TrackLabel == "Glitchy footage" ? "AI candidate · unscored" : $"score {AdjustedConfidence:0.00}")}  ·  {SourceLabel}{(Decision is null ? "  ·  review" : $"  ·  {Decision}")}{(AiSuggestion is null ? "" : $"\n{AiSuggestion}")}";
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}







