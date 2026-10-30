using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Win32;

namespace ReelGood;

internal sealed record MediaSuggestion(TimedMedia Media, string Reason);

internal sealed class EditingToolsWindow : Window
{
    private List<EditingClip> _clips; private List<EditingAsset> _assets; private List<TimedMedia> _media;
    private readonly EditingSnapshot _initial; private readonly double _duration; private readonly EditHistory _history = new();
    private readonly ListBox _clipList = new(), _assetList = new(), _mediaList = new(), _suggestionList = new();
    private readonly Dictionary<string, TextBox> _clipFields = new(), _mediaFields = new();
    private readonly TextBox _description = new(), _caption = new() { AcceptsReturn = true, Height = 62, TextWrapping = TextWrapping.Wrap }, _request = new() { AcceptsReturn = true, Height = 68, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _allowAi = new() { Content = "Allow AI to use this asset" };
    private readonly ComboBox _rotation = new() { ItemsSource = new[] { 0, 90, 180, 270 }, SelectedIndex = 0 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Ink("#9FBBC9") };
    private readonly Grid _body = new(); private readonly List<FrameworkElement> _pages = [];
    private Panel? _navigation;
    private readonly Button _apply = new() { Content = "Apply stitch to project" }, _cancelWork = new() { Content = "Cancel task", IsEnabled = false };
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<EditingAsset>>> _import;
    private readonly Func<EditingSnapshot, string, CancellationToken, Task<IReadOnlyList<MediaSuggestion>>> _suggest;
    private readonly Func<EditingSnapshot, CancellationToken, Task> _preview;
    private readonly CancellationTokenSource _life = new(); private CancellationTokenSource? _work; private bool _refreshing;
    private IReadOnlyList<MediaSuggestion> _suggestions = []; private readonly HashSet<string> _accepted = [];
    public EditingSnapshot? Result { get; private set; }

    public EditingToolsWindow(Window owner, EditingSnapshot initial, double duration, double playhead,
        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<EditingAsset>>> import,
        Func<EditingSnapshot, string, CancellationToken, Task<IReadOnlyList<MediaSuggestion>>> suggest,
        Func<EditingSnapshot, CancellationToken, Task> preview)
    {
        Owner = owner; _initial = initial; _clips = initial.Clips.ToList(); _assets = initial.Assets.ToList(); _media = initial.Media.ToList(); _duration = duration; _import = import; _suggest = suggest; _preview = preview;
        Title = "GlitchStich / Stitch tools"; Width = 1020; Height = 790; MinWidth = 900; MinHeight = 620; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize; Background = Ink("#080E17"); Foreground = Ink("#EAF5FF"); ShowInTaskbar = false;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6) });
        var frame = new Border { Background = Background, BorderBrush = Ink("#51D9E4"), BorderThickness = new Thickness(1), Padding = new Thickness(16), Margin = new Thickness(4) }; CyberShape.SetCut(frame, 18); Content = frame;
        var root = new Grid(); foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) root.RowDefinitions.Add(new() { Height = height }); frame.Child = root;
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var close = Button("×", () => Close()); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var title = new TextBlock { Text = "STITCH DECK // GLITCHES · MEDIA · AI", FontFamily = new FontFamily("Consolas"), FontSize = 16, Foreground = Ink("#F8D35C"), VerticalAlignment = VerticalAlignment.Center };
        title.MouseLeftButtonDown += (_, _) => DragMove(); header.Children.Add(title); root.Children.Add(header);
        var nav = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) }; _navigation = nav; var index = 0;
        foreach (var text in new[] { "Glitch editor", "Media / captions", "AI placement" }) { var page = index++; nav.Children.Add(Button(text, () => ShowPage(page))); }
        nav.Children.Add(Button("Undo", Undo)); nav.Children.Add(Button("Redo", Redo));
        var previewButton = Button("Render preview · first 30s", async () => await Work(async token => { SaveClip(); await _preview(Snapshot(), token); }, "Rendering preview with effects and media…")); nav.Children.Add(previewButton);
        Grid.SetRow(nav, 1); root.Children.Add(nav); Grid.SetRow(_body, 2); root.Children.Add(_body);
        BuildClipPage(playhead); BuildMediaPage(playhead); BuildAiPage(); ShowPage(0);
        _status.Margin = new Thickness(0, 10, 0, 8); Grid.SetRow(_status, 3); root.Children.Add(_status);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _cancelWork.Click += (_, _) => _work?.Cancel(); footer.Children.Add(_cancelWork); footer.Children.Add(Button("Cancel", () => Close()));
        _apply.Click += (_, _) => Attempt(() => { SaveClip(); Validate(); Result = Snapshot(); DialogResult = true; }); footer.Children.Add(_apply);
        Grid.SetRow(footer, 4); root.Children.Add(footer); Closed += (_, _) => _life.Cancel();
        RefreshLists(); _clipList.SelectedIndex = _clips.Count > 0 ? 0 : -1;
        _status.Text = "Source files stay unchanged. Glitch order here is export order. Render preview shows effects/media; the main timeline remains a source-time view.";
    }

    private EditingSnapshot Snapshot() => new(_clips.ToArray(), _assets.ToArray(), _media.ToArray()) { Audio = _initial.Audio, RemovedAudio = _initial.RemovedAudio };
    private void Change(Action action) { var before = Snapshot(); action(); if (JsonSerializer.Serialize(before) != JsonSerializer.Serialize(Snapshot())) _history.Push(before); RefreshLists(); }
    private void Restore(EditingSnapshot state) { _clips = state.Clips.ToList(); _assets = state.Assets.ToList(); _media = state.Media.ToList(); RefreshLists(); }
    private void Undo() { if (_history.Undo(Snapshot()) is { } state) Restore(state); }
    private void Redo() { if (_history.Redo(Snapshot()) is { } state) Restore(state); }
    private void ShowPage(int index) { for (var i = 0; i < _pages.Count; i++) _pages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed; }
    private Grid Page(double leftWidth)
    {
        var page = new Grid(); page.ColumnDefinitions.Add(new() { Width = new GridLength(leftWidth) }); page.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); _pages.Add(page); _body.Children.Add(page); return page;
    }
    private void BuildClipPage(double playhead)
    {
        var page = Page(330); var left = new DockPanel { Margin = new Thickness(0, 0, 14, 0) }; page.Children.Add(left);
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(tools, Dock.Top); left.Children.Add(tools);
        tools.Children.Add(Button("Earlier", () => MoveClip(-1))); tools.Children.Add(Button("Later", () => MoveClip(1)));
        tools.Children.Add(Button("Duplicate", () => Attempt(() => { var i = _clipList.SelectedIndex; if (i < 0) return; Change(() => _clips.Insert(i + 1, _clips[i] with { Layer = _clips.Select(clip => clip.Layer).DefaultIfEmpty().Max() + 1 })); _clipList.SelectedIndex = i + 1; })));
        tools.Children.Add(Button("Delete / close gap", () => { var i = _clipList.SelectedIndex; if (i >= 0) Change(() => _clips.RemoveAt(i)); }));
        _clipList.DisplayMemberPath = "Display"; _clipList.SelectionChanged += (_, _) => LoadClip(); left.Children.Add(_clipList);
        var form = new StackPanel(); var scroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(scroll, 1); page.Children.Add(scroll);
        form.Children.Add(new TextBlock { Text = "GLITCH // TRIM / SPEED / LOOK / AUDIO", Foreground = Ink("#52F3FF"), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
        foreach (var label in new[] { "Start (source seconds)", "End (source seconds)", "Speed (0.25–4×)", "Crop left (%)", "Crop top (%)", "Crop right (%)", "Crop bottom (%)", "Volume (%)", "Fade in (seconds)", "Fade out (seconds)", "Brightness (-1 to 1)", "Contrast (0.5–2)" }) AddField(form, label, _clipFields);
        form.Children.Add(new TextBlock { Text = "Rotation (degrees)", Margin = new Thickness(0, 7, 0, 3) }); form.Children.Add(_rotation);
        form.Children.Add(Button("Apply glitch settings", () => Attempt(SaveClip)));
        var split = new TextBox { Text = F(playhead), Margin = new Thickness(0, 8, 0, 4) }; form.Children.Add(new TextBlock { Text = "Split at source time (seconds)", Margin = new Thickness(0, 10, 0, 0) }); form.Children.Add(split);
        form.Children.Add(Button("Split selected glitch", () => Attempt(() =>
        {
            SaveClip(); var i = _clipList.SelectedIndex; if (i < 0) return; var time = Number(split.Text); var clip = _clips[i];
            if (time - clip.Start < .25 || clip.End - time < .25) throw new ArgumentException("Split at least 0.25 seconds from each glitch edge.");
            Change(() => { _clips[i] = clip with { End = time }; _clips.Insert(i + 1, clip with { Start = time, Layer = _clips.Select(item => item.Layer).DefaultIfEmpty().Max() + 1 }); });
        })));
    }
    private void MoveClip(int delta) { var i = _clipList.SelectedIndex; var target = i + delta; if (i < 0 || target < 0 || target >= _clips.Count) return; Change(() => { var clip = _clips[i]; _clips.RemoveAt(i); _clips.Insert(target, clip); }); _clipList.SelectedIndex = target; }
    private void LoadClip()
    {
        if (_refreshing || _clipList.SelectedItem is not EditingClip clip) return; var e = clip.Effects;
        var values = new[] { clip.Start, clip.End, e.Speed, e.CropLeft * 100, e.CropTop * 100, e.CropRight * 100, e.CropBottom * 100, e.Volume * 100, e.FadeIn, e.FadeOut, e.Brightness, e.Contrast }; var i = 0;
        foreach (var box in _clipFields.Values) box.Text = F(values[i++]); _rotation.SelectedItem = e.Rotation;
    }
    private void SaveClip()
    {
        var i = _clipList.SelectedIndex; if (i < 0) return; var v = _clipFields.Values.Select(box => Number(box.Text)).ToArray();
        var clip = _clips[i] with { Start = v[0], End = v[1], Effects = new(v[2], (int)_rotation.SelectedItem, v[3] / 100, v[4] / 100, v[5] / 100, v[6] / 100, v[7] / 100, v[8], v[9], v[10], v[11]) };
        if (clip.Validate(_duration) is { } error) throw new ArgumentException(error); Change(() => _clips[i] = clip); _clipList.SelectedIndex = i;
        _status.Text = "Glitch settings updated in this review. Apply stitches to commit them to the project.";
    }

    private void BuildMediaPage(double playhead)
    {
        var page = Page(355); var left = new Grid { Margin = new Thickness(0, 0, 14, 0) }; foreach (var h in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto }) left.RowDefinitions.Add(new() { Height = h }); page.Children.Add(left);
        var add = Button("＋ Import images / video / audio", async () =>
        {
            var dialog = new OpenFileDialog { Filter = "Media|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.mp4;*.mkv;*.mov;*.webm;*.wav;*.mp3;*.m4a;*.aac;*.flac;*.ogg", Multiselect = true };
            if (dialog.ShowDialog(this) != true) return;
            await Work(async token => { var imported = await _import(dialog.FileNames, token); Change(() => _assets.AddRange(imported.Where(asset => !_assets.Any(old => old.Path.Equals(asset.Path, StringComparison.OrdinalIgnoreCase))))); }, "Reading imported media…");
        }); left.Children.Add(add); _assetList.DisplayMemberPath = "Display"; _assetList.SelectionChanged += (_, _) => LoadAsset(); Grid.SetRow(_assetList, 1); left.Children.Add(_assetList);
        _description.TextWrapping = TextWrapping.Wrap; _description.AcceptsReturn = true; _description.Height = 60; _description.ToolTip = "Describe the meme and when it should be used; this guides AI placement."; Grid.SetRow(_description, 2); left.Children.Add(_description);
        _allowAi.Margin = new Thickness(0, 7, 0, 4); Grid.SetRow(_allowAi, 3); left.Children.Add(_allowAi);
        var guidance = Button("Save asset guidance", SaveAsset); Grid.SetRow(guidance, 4); left.Children.Add(guidance);
        var right = new StackPanel(); Grid.SetColumn(right, 1); page.Children.Add(new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }.AtColumn(1));
        right.Children.Add(new TextBlock { Text = "TIMED MEDIA / TEXT", Foreground = Ink("#52F3FF"), FontWeight = FontWeights.Bold });
        foreach (var label in new[] { "Anchor (source seconds)", "Duration (stitched seconds)", "X (%)", "Y (%)", "Size (%)", "Volume (%)", "Asset offset (seconds)", "Source occurrence (1=first)" }) AddField(right, label, _mediaFields);
        var defaults = new[] { playhead, 3d, 60, 60, 35, 100, 0, 1 }; var n = 0; foreach (var input in _mediaFields.Values) input.Text = F(defaults[n++]);
        right.Children.Add(new TextBlock { Text = "Caption (for Add text)", Margin = new Thickness(0, 8, 0, 3) }); right.Children.Add(_caption);
        var mediaButtons = new WrapPanel(); mediaButtons.Children.Add(Button("Add selected media", () => Attempt(() => SavePlacement(false)))); mediaButtons.Children.Add(Button("Add text", () => Attempt(() => SavePlacement(true)))); mediaButtons.Children.Add(Button("Update placement", () => Attempt(() => SavePlacement(_mediaList.SelectedItem is TimedMedia { Kind: "Text" }, true)))); right.Children.Add(mediaButtons);
        _mediaList.Height = 150; _mediaList.Margin = new Thickness(0, 9, 0, 0); _mediaList.DisplayMemberPath = "Display"; _mediaList.SelectionChanged += (_, _) => LoadPlacement(); right.Children.Add(_mediaList);
        right.Children.Add(Button("Delete selected placement", () => { var index = _mediaList.SelectedIndex; if (index >= 0) Change(() => _media.RemoveAt(index)); }));
    }
    private void LoadAsset() { if (_assetList.SelectedItem is EditingAsset asset) { _description.Text = asset.Description; _allowAi.IsChecked = asset.AllowAi; } }
    private void SaveAsset() { var i = _assetList.SelectedIndex; if (i < 0) return; Change(() => _assets[i] = _assets[i] with { Description = _description.Text.Trim(), AllowAi = _allowAi.IsChecked == true }); _assetList.SelectedIndex = i; }
    private void SavePlacement(bool text, bool update = false)
    {
        var asset = _assetList.SelectedItem as EditingAsset; if (!text && asset is null) throw new ArgumentException("Select an imported media asset first.");
        var values = _mediaFields.Values.Select(box => Number(box.Text)).ToArray(); var index = update ? _mediaList.SelectedIndex : -1;
        if (update && index < 0) throw new ArgumentException("Select a placement to update.");
        if (values[7] < 1 || values[7] > 1001 || values[7] != Math.Floor(values[7])) throw new ArgumentException("Source occurrence must be a whole number from 1 to 1001.");
        var item = new TimedMedia(index >= 0 ? _media[index].Id : Guid.NewGuid().ToString("N"), text ? null : asset!.Id, text ? "Text" : asset!.Kind, values[0], values[1], values[2] / 100, values[3] / 100, values[4] / 100, values[5] / 100, _caption.Text, values[6]) { Occurrence = (int)values[7] - 1 };
        if (item.Validate(_duration, _assets) is { } error) throw new ArgumentException(error);
        if (EditTimeMap.ToOutput(item.SourceAnchor, _clips, item.Occurrence) is null) throw new ArgumentException("That anchor/occurrence is outside the current stitched glitches. Restore the source range or choose another time.");
        Change(() => { if (index >= 0) _media[index] = item; else _media.Add(item); });
    }
    private void LoadPlacement()
    {
        if (_mediaList.SelectedItem is not TimedMedia item) return;
        var values = new[] { item.SourceAnchor, item.Duration, item.X * 100, item.Y * 100, item.Size * 100, item.Volume * 100, item.AssetOffset, item.Occurrence + 1d }; var i = 0;
        foreach (var box in _mediaFields.Values) box.Text = F(values[i++]); _caption.Text = item.Text;
        _assetList.SelectedItem = _assets.FirstOrDefault(asset => asset.Id == item.AssetId);
    }

    private void BuildAiPage()
    {
        var page = new Grid(); foreach (var h in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) page.RowDefinitions.Add(new() { Height = h }); _pages.Add(page); _body.Children.Add(page);
        page.Children.Add(new TextBlock { Text = "Enable AI per asset in Media / captions. Glitchy uses reviewed source evidence, asset guidance and short asset samples. Proposals remain yours to approve; unknown or omitted footage is not assumed to be reviewed.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 9), Foreground = Ink("#9FBBC9") });
        Grid.SetRow(_request, 1); page.Children.Add(_request);
        var suggest = Button("Review footage + suggest media positions", async () => await Work(async token =>
        {
            _suggestions = await _suggest(Snapshot(), _request.Text, token); _accepted.Clear(); _suggestionList.Items.Clear();
            foreach (var item in _suggestions)
            {
                _accepted.Add(item.Media.Id); var check = new CheckBox { IsChecked = true, Content = new TextBlock { Text = item.Media.Display + "\n" + item.Reason, TextWrapping = TextWrapping.Wrap } };
                check.Checked += (_, _) => _accepted.Add(item.Media.Id); check.Unchecked += (_, _) => _accepted.Remove(item.Media.Id);
                _suggestionList.Items.Add(new Border { Child = check, Padding = new Thickness(9), Margin = new Thickness(0, 3, 0, 3), BorderThickness = new Thickness(1), BorderBrush = Ink("#315666") });
            }
            _status.Text = $"{_suggestions.Count} proposed placement(s). Review them, then Add selected proposals and Apply stitches.";
        }, "Reviewing source and planning asset positions · initial local model load can take a minute…")); Grid.SetRow(suggest, 2); page.Children.Add(suggest);
        Grid.SetRow(_suggestionList, 3); page.Children.Add(_suggestionList);
        var add = Button("Add selected proposals", () => Change(() => _media.AddRange(_suggestions.Where(item => _accepted.Contains(item.Media.Id) && !_media.Any(existing => existing.AssetId == item.Media.AssetId && Math.Abs(existing.SourceAnchor - item.Media.SourceAnchor) < .5)).Select(item => item.Media)))); Grid.SetRow(add, 4); page.Children.Add(add);
    }
    private void RefreshLists()
    {
        var index = _clipList.SelectedIndex; var asset = _assetList.SelectedIndex; _refreshing = true;
        _clipList.ItemsSource = null; _clipList.ItemsSource = _clips; _clipList.SelectedIndex = _clips.Count == 0 ? -1 : Math.Clamp(index, 0, _clips.Count - 1);
        _assetList.ItemsSource = null; _assetList.ItemsSource = _assets; _assetList.SelectedIndex = _assets.Count == 0 ? -1 : Math.Clamp(asset, 0, _assets.Count - 1);
        _mediaList.ItemsSource = null; _mediaList.ItemsSource = _media; _refreshing = false; LoadClip(); LoadAsset();
    }
    private void Validate() { foreach (var clip in _clips) if (clip.Validate(_duration) is { } error) throw new ArgumentException(error); foreach (var item in _media) if (item.Validate(_duration, _assets) is { } error) throw new ArgumentException(error); }
    private async Task Work(Func<CancellationToken, Task> action, string status)
    {
        if (_work is not null) return; using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_life.Token); _work = cancel;
        _body.IsEnabled = false; if (_navigation is not null) _navigation.IsEnabled = false; _apply.IsEnabled = false; _cancelWork.IsEnabled = true; _status.Text = status;
        try { await action(cancel.Token); } catch (OperationCanceledException) { _status.Text = "Task canceled; unapplied stitches remain in this review."; } catch (Exception ex) { _status.Text = ex.Message; }
        finally { _work = null; _body.IsEnabled = true; if (_navigation is not null) _navigation.IsEnabled = true; _apply.IsEnabled = true; _cancelWork.IsEnabled = false; }
    }
    private void Attempt(Action action) { try { action(); } catch (Exception ex) { _status.Text = ex.Message; } }
    private static void AddField(Panel parent, string label, IDictionary<string, TextBox> fields)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) }; row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(105) });
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }); var input = new TextBox { Padding = new Thickness(5, 3, 5, 3) }; Grid.SetColumn(input, 1); row.Children.Add(input); parent.Children.Add(row); fields[label] = input;
    }
    private static Button Button(string label, Action action) { var button = new Button { Content = label, Margin = new Thickness(0, 3, 6, 3), Padding = new Thickness(9, 5, 9, 5) }; button.Click += (_, _) => action(); return button; }
    private static double Number(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) && double.IsFinite(number) ? number : throw new ArgumentException("Enter finite numeric values for timing and effects.");
    private static string F(double value) => value.ToString("0.###", CultureInfo.CurrentCulture);
    private static SolidColorBrush Ink(string color) => new((Color)ColorConverter.ConvertFromString(color));
}

internal static class EditingLayoutExtensions
{
    public static T AtColumn<T>(this T element, int column) where T : UIElement { Grid.SetColumn(element, column); return element; }
}

