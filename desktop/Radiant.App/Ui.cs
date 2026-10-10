using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Radiant.App;

internal static class Ui
{
    public static readonly Brush Background = Brush("#182234");
    public static readonly Brush Surface = Brush("#202D41");
    public static readonly Brush BorderColor = Brush("#354359");
    public static readonly Brush Foreground = Brush("#E4ECF8");
    public static readonly Brush Muted = Brush("#A2AFC3");
    public static readonly Brush Accent = Brush("#B1C7FC");
    public static Brush Brush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    public static StackPanel Stack(params UIElement[] children)
    {
        var panel = new StackPanel();
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }
    public static StackPanel Row(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var child in children)
        {
            if (child is FrameworkElement element) element.Margin = new Thickness(0, 0, 12, 0);
            panel.Children.Add(child);
        }
        return panel;
    }
    public static TextBlock Text(string value, double size = 14, Brush? color = null, bool bold = false)
        => new() { Text = value, FontSize = size, Foreground = color ?? Foreground,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    public static Border Card(UIElement child, double padding = 22)
        => new() { Child = child, Background = Surface, BorderBrush = BorderColor,
            BorderThickness = new Thickness(1), Padding = new Thickness(padding),
            CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 16) };
    public static Button Button(string label, Action action, bool primary = false)
    {
        var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left };
        if (primary) { button.Background = Accent; button.Foreground = Background; button.FontWeight = FontWeights.SemiBold; }
        button.Click += (_, _) => action();
        return button;
    }
    public static string Bytes(long bytes) => bytes < 0 ? "Unavailable" : bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB"
        : $"{bytes / (double)(1L << 20):0.0} MB";
    public static Border Metric(string label, string value, string note)
        => Card(Stack(Text(label.ToUpperInvariant(), 11, Muted, true), Text(value, 32, Accent, true), Text(note, 12, Muted)));
}
