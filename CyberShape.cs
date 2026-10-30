using System.Windows;
using System.Windows.Media;

namespace ReelGood;

// A single cut-corner silhouette shared by the studio, dialogs, and intro.
public static class CyberShape
{
    public static readonly DependencyProperty CutProperty = DependencyProperty.RegisterAttached(
        "Cut", typeof(double), typeof(CyberShape), new PropertyMetadata(0d, Changed));
    public static void SetCut(DependencyObject element, double value) => element.SetValue(CutProperty, value);
    public static double GetCut(DependencyObject element) => (double)element.GetValue(CutProperty);
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element) return;
        element.SizeChanged -= Resize;
        element.SizeChanged += Resize;
        Resize(element, null!);
    }
    private static void Resize(object sender, SizeChangedEventArgs args)
    {
        var element = (FrameworkElement)sender;
        var w = element.ActualWidth; var h = element.ActualHeight;
        var cut = Math.Min(GetCut(element), Math.Min(w, h) / 3);
        var shape = new StreamGeometry();
        using (var context = shape.Open())
        {
            context.BeginFigure(new Point(cut, 0), true, true);
            context.PolyLineTo(new[] { new Point(w, 0), new Point(w, h - cut), new Point(w - cut, h), new Point(0, h), new Point(0, cut) }, true, false);
        }
        shape.Freeze(); element.Clip = shape;
    }
}
