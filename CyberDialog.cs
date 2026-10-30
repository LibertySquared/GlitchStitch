using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ReelGood;

internal static class CyberDialog
{
    public static MessageBoxResult Show(Window? owner, string message, string caption, MessageBoxButton buttons, MessageBoxImage image = MessageBoxImage.Information)
    {
        // Background shutdown must not reopen a hidden editor for a late task prompt.
        if (owner is MainWindow { IsShuttingDown: true }) return MessageBoxResult.Cancel;
        var answer = MessageBoxResult.None;
        var accent = image switch
        {
            MessageBoxImage.Error => Color.FromRgb(255, 79, 216),
            MessageBoxImage.Warning => Color.FromRgb(255, 174, 75),
            _ => Color.FromRgb(82, 243, 255)
        };
        var dialog = new Window
        {
            Title = caption,
            Width = 500,
            MinWidth = 420,
            MaxWidth = 620,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(7, 10, 18)),
            Foreground = new SolidColorBrush(Color.FromRgb(235, 246, 255)),
            FontFamily = new FontFamily("Segoe UI"),
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner
        };

        var frame = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(11, 17, 30)),
            BorderBrush = new SolidColorBrush(accent),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = accent, BlurRadius = 24, ShadowDepth = 0, Opacity = .35 }
        };
        CyberShape.SetCut(frame, 18);
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) });

        var header = new Grid { Background = new SolidColorBrush(Color.FromRgb(15, 26, 43)), Margin = new Thickness(0, 0, 0, 1) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new Border { Background = new SolidColorBrush(accent) });
        var eyebrow = new TextBlock
        {
            Text = "GLITCHSTICH  /  CONFIRMATION",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            Foreground = new SolidColorBrush(accent),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0)
        };
        Grid.SetColumn(eyebrow, 1); header.Children.Add(eyebrow);
        var close = new Button { Content = "×", Width = 42, Height = 36, Margin = new Thickness(0, 4, 4, 4), Background = Brushes.Transparent, Foreground = new SolidColorBrush(accent), BorderThickness = new Thickness(0), FontSize = 22, Cursor = Cursors.Hand };
        close.Click += (_, _) => { answer = MessageBoxResult.Cancel; dialog.Close(); };
        Grid.SetColumn(close, 3); header.Children.Add(close);
        Grid.SetRow(header, 0); layout.Children.Add(header);

        var messageText = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 20,
            Foreground = new SolidColorBrush(Color.FromRgb(222, 235, 248)),
            Margin = new Thickness(22, 20, 22, 20),
            MaxWidth = 570
        };
        var messageScroll = new ScrollViewer { Content = messageText, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(messageScroll, 1); layout.Children.Add(messageScroll);

        var buttonsPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 18, 0) };
        var choices = buttons switch
        {
            MessageBoxButton.YesNo => new[] { ("YES, CONTINUE", MessageBoxResult.Yes), ("CANCEL", MessageBoxResult.No) },
            MessageBoxButton.YesNoCancel => new[] { ("YES", MessageBoxResult.Yes), ("NO", MessageBoxResult.No), ("CANCEL", MessageBoxResult.Cancel) },
            MessageBoxButton.OKCancel => new[] { ("CONTINUE", MessageBoxResult.OK), ("CANCEL", MessageBoxResult.Cancel) },
            _ => new[] { ("ACKNOWLEDGE", MessageBoxResult.OK) }
        };
        foreach (var (label, result) in choices)
        {
            var button = new Button
            {
                Content = label,
                MinWidth = label.Length > 10 ? 128 : 94,
                Height = 34,
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(12, 4, 12, 4),
                Background = result is MessageBoxResult.Yes or MessageBoxResult.OK ? new SolidColorBrush(Color.FromRgb(16, 47, 63)) : new SolidColorBrush(Color.FromRgb(18, 23, 36)),
                Foreground = result is MessageBoxResult.Yes or MessageBoxResult.OK ? new SolidColorBrush(accent) : new SolidColorBrush(Color.FromRgb(178, 196, 218)),
                BorderBrush = result is MessageBoxResult.Yes or MessageBoxResult.OK ? new SolidColorBrush(accent) : new SolidColorBrush(Color.FromRgb(44, 63, 85)),
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
                Cursor = Cursors.Hand,
                IsDefault = result is MessageBoxResult.Yes or MessageBoxResult.OK,
                IsCancel = result == MessageBoxResult.Cancel
            };
            button.Click += (_, _) => { answer = result; dialog.Close(); };
            buttonsPanel.Children.Add(button);
        }
        Grid.SetRow(buttonsPanel, 2); layout.Children.Add(buttonsPanel);
        frame.Child = layout;
        dialog.Content = frame;
        dialog.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { answer = MessageBoxResult.Cancel; dialog.Close(); e.Handled = true; }
        };
        dialog.ShowDialog();
        return answer;
    }
}
