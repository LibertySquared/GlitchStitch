using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shell;

namespace ReelGood;

internal sealed class DraftEditWindow : Window
{
    private readonly IReadOnlyList<DraftBookmark> _bookmarks;
    private readonly double _duration;
    private readonly Action<DraftClip> _preview;
    private readonly ListBox _list = new();
    private readonly CheckBox _includeUnreviewed = new() { Content = "Include unreviewed matches", IsChecked = true };
    private readonly TextBox _merge = new() { Text = "1", Width = 40 };
    private readonly TextBox _tail = new() { Text = "1", Width = 40 };
    private readonly ComboBox _mode = new() { ItemsSource = new[] { "Story / preserve context", "Competitive highlights", "Comedy / player reactions" }, SelectedIndex = 1, MinWidth = 180 };
    private readonly ComboBox _secondaryMode = new() { ItemsSource = new[] { "None — one editing mode", "Story / preserve context", "Competitive highlights", "Comedy / player reactions" }, SelectedIndex = 0, Margin = new Thickness(0, 4, 0, 4) };
    private readonly CheckBox _trimQuiet = new() { Content = "Remove reviewed quiet downtime", IsChecked = true, Margin = new Thickness(0, 6, 0, 4) };
    private readonly TextBlock _modeHint = new() { TextWrapping = TextWrapping.Wrap, FontSize = 10, Foreground = Ink("#9FBBC9") };
    private readonly TextBlock _cutDetails = new() { TextWrapping = TextWrapping.Wrap, FontSize = 10, Foreground = Ink("#9FBBC9") };
    private DraftReviewContext? _editorialContext;
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Foreground = Ink("#9FBBC9"), FontSize = 11 };
    private readonly Button _apply = new() { Content = "Apply draft to timeline", Padding = new Thickness(12, 8, 12, 8) };
    private List<DraftChoice> _choices = [];
    private DraftEditPlan? _plan;
    private bool _ready;
    private bool _settingsDirty;
    public IReadOnlyList<DraftClip>? ApprovedClips { get; private set; }
    public event Action? DraftReady;

    public DraftEditWindow(Window owner, IReadOnlyList<DraftBookmark> bookmarks, double duration, Action<DraftClip> preview, Action stopPreview)
    {
        _bookmarks = bookmarks; _duration = duration; _preview = preview;
        Owner = owner; Title = "GlitchStich / Draft stitch"; Width = 560; Height = 760; MinWidth = 540; MinHeight = 600;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowWorkArea.Attach(this);
        Background = Ink("#080E17"); Foreground = Ink("#EAF5FF");
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0) });
        var frame = new Border { BorderBrush = Ink("#51D9E4"), BorderThickness = new Thickness(1), Padding = new Thickness(14), Margin = new Thickness(4), Background = Background };
        CyberShape.SetCut(frame, 18); Content = frame;
        var grid = new Grid(); frame.Child = grid;
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            grid.RowDefinitions.Add(new RowDefinition { Height = height });
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var close = new Button { Content = "×", Padding = new Thickness(10, 3, 10, 3) };
        close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        header.Children.Add(new TextBlock { Text = "DRAFT EDIT // REVIEW THE CUT", Foreground = Ink("#F8D35C"), FontFamily = new FontFamily("Consolas"), FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
        header.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is TextBlock text && text.Text.StartsWith("DRAFT EDIT", StringComparison.Ordinal)) DragMove(); };
        grid.Children.Add(header);
        var settings = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        settings.Children.Add(_mode);
        settings.Children.Add(new TextBlock { Text = "SECOND MODE (OPTIONAL)", FontSize = 9, Foreground = Ink("#9FBBC9"), Margin = new Thickness(0, 5, 0, 0) });
        settings.Children.Add(_secondaryMode); settings.Children.Add(_modeHint);
        _modeHint.Text = "Highlight drafts select bookmarks; weak matches need Keep. Story mode retains unreviewed scenes. Quiet audio alone is never a removal reason.";
        settings.Children.Add(_trimQuiet);
        var cutInfo = new Expander { Header = "Scene protection / proposed quiet cuts", Content = new ScrollViewer { Content = _cutDetails, MaxHeight = 95, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Margin = new Thickness(0, 2, 0, 3) };
        settings.Children.Add(cutInfo);
        var options = new WrapPanel { Margin = new Thickness(0, 9, 0, 6) };
        _includeUnreviewed.Margin = new Thickness(0, 5, 14, 5); options.Children.Add(_includeUnreviewed);
        AddSetting(options, "Join gap (s)", _merge); AddSetting(options, "End padding (s)", _tail);
        settings.Children.Add(options);
        var update = new Button { Content = "Update draft", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 5, 12, 5) };
        update.Click += (_, _) => Rebuild();
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        tools.Children.Add(update);
        var stop = new Button { Content = "■ Stop preview", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 5, 12, 5) };
        stop.Click += (_, _) => stopPreview(); tools.Children.Add(stop); settings.Children.Add(tools);
        Grid.SetRow(settings, 1); grid.Children.Add(settings);
        _list.Margin = new Thickness(0, 0, 0, 10);
        _list.ItemTemplate = (DataTemplate)XamlReader.Parse("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Border Background="#111D2A" BorderBrush="#294755" BorderThickness="1" Margin="0,3" Padding="9">
                <DockPanel>
                  <Button DockPanel.Dock="Right" Content="▶ Play clip" Margin="7,0,0,0" Padding="8,5" VerticalAlignment="Center"/>
                  <CheckBox IsChecked="{Binding IsIncluded, Mode=TwoWay}"><StackPanel>
                    <TextBlock Text="{Binding Title}" Foreground="#EAF5FF" FontWeight="SemiBold"/>
                    <TextBlock Text="{Binding Details}" Foreground="#9FBBC9" FontSize="10" TextWrapping="Wrap" Margin="0,4,0,0"/>
                  </StackPanel></CheckBox>
                </DockPanel>
              </Border>
            </DataTemplate>
            """);
        _list.AddHandler(Button.ClickEvent, new RoutedEventHandler((sender, e) =>
        {
            if (e.OriginalSource is Button { DataContext: DraftChoice choice }) { _preview(choice.Clip); e.Handled = true; }
        }));
        Grid.SetRow(_list, 2); grid.Children.Add(_list);
        Grid.SetRow(_summary, 3); grid.Children.Add(_summary);
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(new TextBlock { Text = "Apply replaces the video timeline. Restore pre-draft brings back the previous stitch, even after save/reopen. Audio settings and bookmarks are kept. Export remains a separate step.", TextWrapping = TextWrapping.Wrap, FontSize = 10, Foreground = Ink("#9FBBC9"), Margin = new Thickness(0, 0, 0, 8) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) }; cancel.Click += (_, _) => Close(); buttons.Children.Add(cancel);
        _apply.Click += (_, _) => { ApprovedClips = _choices.Where(choice => choice.IsIncluded).Select(choice => choice.Clip).ToArray(); DialogResult = true; };
        buttons.Children.Add(_apply); footer.Children.Add(buttons); Grid.SetRow(footer, 4); grid.Children.Add(footer);
        _includeUnreviewed.Checked += SettingsChanged; _includeUnreviewed.Unchecked += SettingsChanged;
        _mode.SelectionChanged += SettingsChanged;
        _secondaryMode.SelectionChanged += SettingsChanged;
        _trimQuiet.Checked += SettingsChanged; _trimQuiet.Unchecked += SettingsChanged;
        _merge.TextChanged += SettingsChanged; _tail.TextChanged += SettingsChanged;
        Loaded += (_, _) => { _ready = true; Rebuild(); };
    }

    public EditorialMode SelectedEditingMode => (EditorialMode)Math.Clamp(_mode.SelectedIndex, 0, 2);
    public EditorialMode? SelectedSecondaryEditingMode => _secondaryMode.SelectedIndex is >= 1 and <= 3 && _secondaryMode.SelectedIndex - 1 != _mode.SelectedIndex
        ? (EditorialMode)(_secondaryMode.SelectedIndex - 1) : null;
    public void ConfigureEditorialContext(DraftReviewContext context)
    {
        _editorialContext = context; _mode.SelectedIndex = (int)context.Mode;
        _secondaryMode.SelectedIndex = context.SecondaryMode is { } secondary && secondary != context.Mode ? (int)secondary + 1 : 0;
        _trimQuiet.IsEnabled = context.QuietDowntime.Count > 0;
        _cutDetails.Text = $"{context.ProtectedContext.Count} identified scene-context range(s) protected. {context.QuietDowntime.Count} proposed quiet cut(s).\n" +
            "Story mode preserves unreviewed footage. Highlight/comedy modes preserve identified context; sampled review cannot establish the full boundaries of every cutscene.\n" +
            string.Join("\n", context.QuietDowntime.Select(range => $"{Stamp(range.Start)}–{Stamp(range.End)} · {range.Reason}"));
    }

    private void SettingsChanged(object sender, RoutedEventArgs e)
    {
        if (_secondaryMode.SelectedIndex == _mode.SelectedIndex + 1) _secondaryMode.SelectedIndex = 0;
        if (!_ready) return;
        _settingsDirty = true;
        _apply.IsEnabled = false;
        _summary.Text = "Settings changed. Click Update draft to review the new selection.";
    }
    private void Rebuild()
    {
        if (!double.TryParse(_merge.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var gap) || !double.IsFinite(gap) || gap < 0 || gap > 10 ||
            !double.TryParse(_tail.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var tail) || !double.IsFinite(tail) || tail < 0 || tail > 10)
        { _apply.IsEnabled = false; _summary.Text = "Join gap and end padding must be from 0 to 10 seconds."; return; }
        _plan = DraftEditPlanner.Build(_bookmarks, _duration, _includeUnreviewed.IsChecked == true, gap, tail);
        if (_editorialContext is not null)
            _plan = EditorialPolicy.Apply(_plan, _bookmarks, _duration, _editorialContext with { Mode = SelectedEditingMode, SecondaryMode = SelectedSecondaryEditingMode }, _trimQuiet.IsChecked == true);
        _settingsDirty = false;
        _choices = _plan.Clips.Select((clip, index) => new DraftChoice(clip, index + 1, RefreshSummary)).ToList();
        _list.ItemsSource = _choices; RefreshSummary();
        if (_choices.Count > 0) DraftReady?.Invoke();
    }
    private void RefreshSummary()
    {
        if (_plan is null) return;
        var selected = _choices.Where(choice => choice.IsIncluded).ToArray();
        _apply.IsEnabled = selected.Length > 0 && !_settingsDirty;
        _summary.Text = $"{selected.Length} glitches selected · {Stamp(selected.Sum(choice => choice.Clip.Duration))} draft / {Stamp(_duration)} source\n" +
            $"{_plan.IncludedBookmarks} bookmarks grouped · {_plan.SkippedBookmarks} skipped · {_plan.WeakCandidatesExcluded} weak matches excluded · {_plan.UnreviewedExcluded} unreviewed excluded · {_plan.InvalidBookmarks} invalid ranges excluded.";
        if (_editorialContext is not null)
            _summary.Text += $"\n{(_plan.StoryContinuity ? "Story continuity retained" : "Selected moments + identified scene context")} · {_plan.ProtectedContextCount} context ranges · {_plan.RemovedQuietSeconds:0.0}s reviewed quiet downtime removed.";
        if (_plan.StoryContinuity) _summary.Text += "\nSkip marks do not delete story scenes; only reviewed quiet cuts are removed here.";
        var overlaps = _plan.SkippedOverlaps == 0 ? 0 : _bookmarks.Count(bookmark => bookmark.Decision == "Skip" &&
            selected.Any(choice => bookmark.Start < choice.Clip.EndSeconds && bookmark.End > choice.Clip.StartSeconds));
        if (overlaps > 0) _summary.Text += $"\nReview overlap: {overlaps} skipped bookmark(s) share footage with included moments. Skipping a bookmark does not erase another included moment.";
        if (_plan.Clips.Count == 0) _summary.Text += "\nNo eligible glitches. Keep a bookmark or include unreviewed matches, then update the draft.";
    }
    private static void AddSetting(Panel panel, string title, TextBox input)
    {
        panel.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0), FontSize = 10 });
        input.Padding = new Thickness(4); input.Margin = new Thickness(0, 0, 10, 0); panel.Children.Add(input);
    }
    private static SolidColorBrush Ink(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private static string Stamp(double seconds) => TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss\.f" : @"mm\:ss\.f");
    private sealed class DraftChoice(DraftClip clip, int number, Action changed) : INotifyPropertyChanged
    {
        public DraftClip Clip { get; } = clip;
        public string Title => $"{number:00} / {Stamp(Clip.StartSeconds)} – {Stamp(Clip.EndSeconds)} · {Clip.Duration:0.0}s";
        public string Details => $"{Clip.BookmarkIds.Count} bookmark(s) · {Clip.Description}";
        private bool _included = true;
        public bool IsIncluded { get => _included; set { _included = value; PropertyChanged?.Invoke(this, new(nameof(IsIncluded))); changed(); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
