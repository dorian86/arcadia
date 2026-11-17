using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Arcadia.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public bool UseHidden { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is true;
        if (Invert) b = !b;
        return b ? Visibility.Visible : UseHidden ? Visibility.Hidden : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value != null && !(value is string s && string.IsNullOrWhiteSpace(s));
        if (Invert) hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class CountToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var any = value switch
        {
            int i => i > 0,
            System.Collections.ICollection c => c.Count > 0,
            _ => false,
        };
        if (Invert) any = !any;
        return any ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>value == parameter → true (and back: true → parameter). Used for radio buttons bound to enums.</summary>
public sealed class EqualsToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Equals(value, parameter);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter != null ? parameter : Binding.DoNothing;
}

public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var eq = Equals(value, parameter);
        if (Invert) eq = !eq;
        return eq ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>value >= parameter (star ratings).</summary>
public sealed class AtLeastConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int v && int.TryParse(parameter?.ToString(), out var p) && v >= p;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class CountToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is int i && i > 0;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>0 → null (hides the badge), n → "n".</summary>
public sealed class CountToBadgeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i && i > 0 ? i.ToString(culture) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Deterministic, tasteful gradient for a game without artwork.</summary>
public sealed class NameToBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, Brush> Cache = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = value as string ?? "";
        if (Cache.TryGetValue(name, out var cached)) return cached;

        uint hash = 2166136261;
        foreach (var c in name) hash = (hash ^ c) * 16777619;
        var hue = hash % 360;
        var c1 = FromHsl(hue, 0.42, 0.30);
        var c2 = FromHsl((hue + 40) % 360, 0.50, 0.16);
        var brush = new LinearGradientBrush(c1, c2, new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        Cache[name] = brush;
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;

    private static Color FromHsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = l - c / 2;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}

/// <summary>True when both values are the same instance (current item of a carousel...).</summary>
public sealed class SameMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] != null && ReferenceEquals(values[0], values[1]);

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True when both values are equal (value comparison).</summary>
public sealed class EqualsMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && Equals(values[0], values[1]);

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Ratio (0..1) × parameter → length, for simple bar charts.</summary>
public sealed class RatioToLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var ratio = value is double d ? d : 0;
        var max = double.TryParse(parameter as string, NumberStyles.Any, CultureInfo.InvariantCulture, out var m) ? m : 100;
        return Math.Max(2, ratio * max);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>"#RRGGBB" → SolidColorBrush (accent colour of a game cover).</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var c = Arcadia.Services.ColorExtractor.Parse(value as string);
        if (c == null) return parameter is Brush fallback ? fallback : Brushes.Transparent;
        var b = new SolidColorBrush(c.Value);
        b.Freeze();
        return b;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>"#RRGGBB" → soft radial glow brush (ambient light behind the detail page).</summary>
public sealed class HexToGlowConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var c = Arcadia.Services.ColorExtractor.Parse(value as string) ?? Arcadia.Services.ThemeService.Current.A;
        var b = new RadialGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(0xB0, c.R, c.G, c.B), 0),
            new(Color.FromArgb(0x38, c.R, c.G, c.B), 0.5),
            new(Color.FromArgb(0x00, c.R, c.G, c.B), 1),
        });
        b.Freeze();
        return b;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>First letters of the title ("The Witcher 3" → "TW").</summary>
public sealed class InitialsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var words = (value as string ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                           .Where(w => char.IsLetterOrDigit(w[0])).ToList();
        if (words.Count == 0) return "?";
        if (words.Count == 1) return words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant();
        return string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
