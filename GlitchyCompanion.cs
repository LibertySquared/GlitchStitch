using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace ReelGood;

public sealed class GlitchyCompanion : Grid
{
    public event EventHandler? ChatRequested;
    private readonly GlitchyRig _rig;
    private readonly Viewport3D _viewport;
    private readonly Grid _characterHost;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly Stopwatch _time = new();
    private readonly ScaleTransform3D _gameScale = new(), _cardsScale = new();
    private readonly AxisAngleRotation3D _cardTurn = new(new Vector3D(0, 0, 1), 0);
    private readonly TranslateTransform3D _cardSlide = new();
    private readonly TranslateTransform3D _bodyMove = new();
    private readonly TranslateTransform _arrival = new();
    private readonly SolidColorBrush _screenBrush = new(Color.FromRgb(46, 220, 230));
    private bool _thinking, _chatMode, _jumping, _busy;
    private double _landingUntil;
    private double _talkUntil, _nextTip = 20, _hideTipAt = double.PositiveInfinity;
    private int _tip;
    private double _previousPose = double.NaN, _propVisibility;
    public GlitchySpeechBubble Bubble { get; } = new();
    public FrameworkElement CharacterSurface => _characterHost;
    public bool RemindersEnabled { get; set; } = true;
    public bool AnimationRunning => _timer.IsEnabled;
    private static readonly string[] Tips = {
        "I'm Glitchy. I can review your scanned glitches, suggest a tighter prompt and help build a draft stitch. Click me to chat; you approve the changes.",
        "Want less manual cutting? Choose your editing modes, then Start scan. I can help review the bookmarks and suggest what belongs in your stitch.",
        "I can help refine a glitch's setup and ending, review detector misses, and suggest a draft. Your Apply and Keep/Skip choices stay in charge."
    };
    public GlitchyCompanion()
    {
        MinHeight = 126; ClipToBounds = false;
        ColumnDefinitions.Add(new ColumnDefinition()); ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(142) });
        Bubble.HorizontalAlignment = HorizontalAlignment.Right; Children.Add(Bubble);
        Bubble.Dismissed += (_, _) => { _nextTip = _time.Elapsed.TotalSeconds + 600; _hideTipAt = double.PositiveInfinity; _talkUntil = 0; };
        _characterHost = new Grid { Width = 142, Height = 132, VerticalAlignment = VerticalAlignment.Bottom, Background = Brushes.Transparent, Cursor = Cursors.Hand, Focusable = true, ToolTip = "Chat with Glitchy", RenderTransform = _arrival };
        AutomationProperties.SetName(_characterHost, "Glitchy — click or press Enter to chat");
        _characterHost.RowDefinitions.Add(new RowDefinition()); _characterHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
        var scene = new Model3DGroup();
        scene.Children.Add(new AmbientLight(Color.FromRgb(96, 114, 145)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(220, 242, 255), new Vector3D(-.3, -.5, -1)));
        scene.Children.Add(new PointLight(Color.FromRgb(255, 65, 200), new Point3D(-3, 3, 4)));
        _rig = new GlitchyRig(); _rig.Model.Transform = _bodyMove; scene.Children.Add(_rig.Model);
        var game = new Model3DGroup { Transform = _gameScale };
        GlitchyRig.BevelBox(game, 0, -.42, 1.05, 1.18, .62, .16, .09, GlitchyRig.Surface("#1F2942", "#C77AB8", .1));
        var screen = new MaterialGroup(); screen.Children.Add(new DiffuseMaterial(_screenBrush)); screen.Children.Add(new EmissiveMaterial(_screenBrush));
        GlitchyRig.Box(game, 0, -.41, 1.15, .62, .38, .025, screen);
        foreach (var x in new[] { -.44, .44 }) GlitchyRig.Cylinder(game, x, -.42, 1.16, .07, .025, GlitchyRig.Surface("#1B2440", "#FF61D8", .3));
        scene.Children.Add(game);
        var cards = new Model3DGroup { Transform = _cardsScale };
        for (var i = 0; i < 3; i++)
        {
            var card = new Model3DGroup();
            GlitchyRig.BevelBox(card, -.27 + i * .27, -.36, 1.14 + i * .025, .44, .67, .025, .025, GlitchyRig.Surface(i == 1 ? "#BEE6EE" : "#6E6C9B", "#6EEFFF", .15));
            GlitchyRig.Box(card, -.27 + i * .27, -.36, 1.165 + i * .025, .14, .14, .016, GlitchyRig.Surface("#802A67", "#FF65D8", .5));
            if (i == 2) { var transforms = new Transform3DGroup(); transforms.Children.Add(new RotateTransform3D(_cardTurn, new Point3D(0, -.36, 1.2))); transforms.Children.Add(_cardSlide); card.Transform = transforms; }
            cards.Children.Add(card);
        }
        scene.Children.Add(cards);
        _viewport = new Viewport3D { Camera = new PerspectiveCamera(new Point3D(5, 2.0, 11), new Vector3D(-5, -2.0, -11), new Vector3D(0, 1, 0), 35), IsHitTestVisible = false };
        _viewport.Children.Add(new ModelVisual3D { Content = scene }); _characterHost.Children.Add(_viewport);
        var name = new TextBlock { Text = "GLITCHY // CHAT", FontFamily = new FontFamily("Consolas"), FontSize = 8, Foreground = Brushes.Cyan, HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetRow(name, 1); _characterHost.Children.Add(name);
        Grid.SetColumn(_characterHost, 1); Children.Add(_characterHost);
        _characterHost.MouseLeftButtonUp += (_, e) => { e.Handled = true; ChatRequested?.Invoke(this, EventArgs.Empty); };
        _characterHost.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { e.Handled = true; ChatRequested?.Invoke(this, EventArgs.Empty); } };
        var menu = new ContextMenu { Background = new SolidColorBrush(Color.FromRgb(12, 20, 34)), Foreground = Brushes.Cyan, BorderBrush = new SolidColorBrush(Color.FromRgb(49, 81, 109)), BorderThickness = new Thickness(1) }; var reminders = new MenuItem { Header = "Pause help reminders", IsCheckable = true };
        reminders.Checked += (_, _) => RemindersEnabled = false; reminders.Unchecked += (_, _) => { RemindersEnabled = true; _nextTip = _time.Elapsed.TotalSeconds + 20; };
        menu.Items.Add(reminders); _characterHost.ContextMenu = menu;
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => { _time.Start(); UpdateTimer(); };
        Unloaded += (_, _) => { _timer.Stop(); _time.Stop(); };
        IsVisibleChanged += (_, _) => UpdateTimer();
        Loaded += (_, _) =>
        {
            if (Window.GetWindow(this) is Window owner)
            {
                owner.StateChanged -= OwnerStateChanged; owner.StateChanged += OwnerStateChanged;
            }
        };
        Unloaded += (_, _) => { if (Window.GetWindow(this) is Window owner) owner.StateChanged -= OwnerStateChanged; };
        RenderPose(0);
    }
    private void OwnerStateChanged(object? sender, EventArgs e) => UpdateTimer();
    private void UpdateTimer()
    {
        var window = Window.GetWindow(this);
        if (IsLoaded && IsVisible && window?.WindowState != WindowState.Minimized) _timer.Start(); else _timer.Stop();
    }
    public void SetBusy(bool busy) => _busy = busy;
    public void SetChatMode() { _chatMode = true; _nextTip = double.PositiveInfinity; }
    public void SetJumping(bool value) { _jumping = value; RenderPose(_time.Elapsed.TotalSeconds); }
    public void Say(string message, bool persistent = true)
    {
        _thinking = false; Bubble.ShowMessage(message); _talkUntil = _time.Elapsed.TotalSeconds + 4;
        _hideTipAt = persistent ? double.PositiveInfinity : _time.Elapsed.TotalSeconds + 14;
    }
    public void Think(string message)
    {
        _thinking = true; _thinkingMessage = message; _hideTipAt = double.PositiveInfinity;
        Bubble.ShowMessage(message); _talkUntil = double.PositiveInfinity;
    }
    private string _thinkingMessage = "Thinking";
    public void Stop() { _timer.Stop(); _time.Stop(); _arrival.BeginAnimation(TranslateTransform.YProperty, null); }
    public void Arrive()
    {
        _arrival.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimationUsingKeyFrames {
            KeyFrames = { new EasingDoubleKeyFrame(-28, KeyTime.FromTimeSpan(TimeSpan.Zero)), new EasingDoubleKeyFrame(7, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(170))), new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(290)), new CubicEase { EasingMode = EasingMode.EaseOut }) }
        });
        if (Bubble.Visibility == Visibility.Visible) Bubble.ShowMessage(Bubble.Message);
        _jumping = true; _landingUntil = _time.Elapsed.TotalSeconds + .16;
        _talkUntil = _time.Elapsed.TotalSeconds + 1.5;
        _previousPose = double.NaN; RenderPose(_time.Elapsed.TotalSeconds);
    }
    private void Tick()
    {
        if (Window.GetWindow(this)?.WindowState == WindowState.Minimized) return;
        var t = _time.Elapsed.TotalSeconds;
        if (_landingUntil > 0 && t >= _landingUntil) { _jumping = false; _landingUntil = 0; }
        RenderPose(t);
        if (_thinking) Bubble.SetThinkingText(_thinkingMessage + new string('.', 1 + (int)(t * 2) % 3));
        if (!_chatMode && !_busy && RemindersEnabled && t >= _nextTip && !_jumping)
        {
            Say(Tips[_tip++ % Tips.Length], false); _nextTip = t + 240;
        }
        if (t >= _hideTipAt) { _hideTipAt = double.PositiveInfinity; Bubble.Dismiss(); }
    }
    internal void RenderPose(double t)
    {
        var blend = double.IsNaN(_previousPose) || t < _previousPose ? 1 : Math.Clamp((t - _previousPose) * 12, 0, 1);
        _previousPose = t;
        void Turn(AxisAngleRotation3D joint, double target) => joint.Angle += (target - joint.Angle) * blend;
        var talking = t < _talkUntil;
        var phase = t % 32; var gaming = phase < 16;
        var local = phase % 16;
        var propAmount = Math.Clamp(Math.Min(local / .4, (16 - local) / .4), 0, 1);
        if (_jumping || talking) propAmount = 0;
        _propVisibility += (propAmount - _propVisibility) * blend;
        propAmount = _propVisibility;
        _gameScale.ScaleX = _gameScale.ScaleY = _gameScale.ScaleZ = gaming ? propAmount : 0;
        _cardsScale.ScaleX = _cardsScale.ScaleY = _cardsScale.ScaleZ = gaming ? 0 : propAmount;
        _cardTurn.Angle = Math.Sin(t * 2.5) * 17; _cardSlide.OffsetX = Math.Sin(t * 2.5) * .17;
        _screenBrush.Color = Color.FromRgb((byte)(30 + 20 * Math.Sin(t * 4)), (byte)(170 + 45 * Math.Sin(t * 3)), 230);
        _bodyMove.OffsetY = _jumping ? .10 : .035 * Math.Sin(t * 2);
        Turn(_rig._spineBend, _jumping ? 14 : 3 + 2 * Math.Sin(t * 1.8));
        Turn(_rig._spineTwist, talking ? 4 * Math.Sin(t * 3) : 1.8 * Math.Sin(t));
        Turn(_rig._headTilt, _jumping ? -8 : talking ? -4 + 5 * Math.Sin(t * 5) : 2 * Math.Sin(t * 1.6));
        Turn(_rig._headTurn, talking ? -12 : 6 * Math.Sin(t * .65));
        for (var i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;
            Turn(_rig._shoulders[i], side * (_jumping ? 95 : talking ? 30 + 12 * Math.Sin(t * 5 + i) : -18 + (gaming ? 0 : 8 * Math.Sin(t * 2.5 + i))));
            Turn(_rig._armSwings[i], _jumping ? -55 : talking ? -16 : -18 + Math.Sin(t * (gaming ? 7 : 2.5) + i) * 4);
            Turn(_rig._elbows[i], _jumping ? -70 : talking ? -60 + 18 * Math.Sin(t * 4 + i) : -86 + Math.Sin(t * 5 + i) * 4);
            Turn(_rig._wrists[i], _jumping ? -10 : talking ? 16 * Math.Sin(t * 5) : Math.Sin(t * (gaming ? 9 : 2.5) + i) * 10);
            Turn(_rig._hips[i], _jumping ? -70 : 0); Turn(_rig._knees[i], _jumping ? 110 : 4);
            Turn(_rig._ankles[i], _jumping ? -20 : -4);
            Turn(_rig._coatFlaps[i], _jumping ? 25 : Math.Sin(t * 2 + i) * 3);
            Turn(_rig._reels[i], t * (i == 0 ? 12 : -14));
        }
    }
}




