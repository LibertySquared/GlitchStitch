using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ReelGood;

public sealed class GlitchySpeechBubble : Grid
{
    private readonly TextBlock _text;
    private readonly Button _close;
    private readonly ScrollViewer _scroll;
    public double MaxMessageHeight { get => _scroll.MaxHeight; set => _scroll.MaxHeight = value; }
    private readonly TranslateTransform _slide = new();
    public event EventHandler? Dismissed;
    public string Message => _text.Text;
    public bool CanDismiss { get => _close.Visibility == Visibility.Visible; set => _close.Visibility = value ? Visibility.Visible : Visibility.Collapsed; }
    public GlitchySpeechBubble()
    {
        MaxWidth = 650; VerticalAlignment = VerticalAlignment.Center; Margin = new Thickness(8, 2, 0, 0);
        RenderTransform = _slide;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
        ColumnDefinitions.Add(new ColumnDefinition()); ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        var ink = new SolidColorBrush(Color.FromRgb(82, 243, 255));
        var shell = new Border { Background = new SolidColorBrush(Color.FromRgb(14, 26, 42)), BorderBrush = ink, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 7, 7, 8) };
        var content = new Grid(); content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition()); content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(new TextBlock { Text = "GLITCHY", Foreground = ink, FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 9, VerticalAlignment = VerticalAlignment.Center });
        _close = new Button { Content = "×", Width = 23, Height = 21, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(8, 0, 0, 2), Background = Brushes.Transparent, Foreground = ink, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, ToolTip = "Dismiss Glitchy's bubble" };
        _close.Click += (_, _) => Dismiss(); Grid.SetColumn(_close, 1); content.Children.Add(_close);
        _text = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(224, 240, 250)), FontSize = 11, LineHeight = 16 };
        _scroll = new ScrollViewer { Content = _text, MaxHeight = 103, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 3, 2, 0) };
        Grid.SetRow(_scroll, 1); Grid.SetColumnSpan(_scroll, 2); content.Children.Add(_scroll); shell.Child = content; Children.Add(shell);
        var tail = new Path { Data = Geometry.Parse("M0,0 L12,9 L0,17"), Fill = shell.Background, Stroke = ink, StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-1, 22, 0, 0) };
        Grid.SetColumn(tail, 1); Children.Add(tail);
        Visibility = Visibility.Collapsed;
    }
    public void ShowMessage(string message, bool animated = true)
    {
        _text.Text = message; Visibility = Visibility.Visible;
        BeginAnimation(OpacityProperty, null); _slide.BeginAnimation(TranslateTransform.YProperty, null);
        Opacity = 1; _slide.Y = 0;
        if (!animated) return;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(9, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
    public void SetThinkingText(string message) => _text.Text = message;
    public void Dismiss()
    {
        if (Visibility != Visibility.Visible) return;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) => { Visibility = Visibility.Collapsed; Dismissed?.Invoke(this, EventArgs.Empty); };
        BeginAnimation(OpacityProperty, fade);
        _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -7, TimeSpan.FromMilliseconds(160)));
    }
}

