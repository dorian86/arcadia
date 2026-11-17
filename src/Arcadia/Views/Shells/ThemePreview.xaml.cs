using System.Windows;
using System.Windows.Controls;

namespace Arcadia.Views.Shells;

/// <summary>Miniature of an interface theme, drawn in XAML (no screenshots).</summary>
public partial class ThemePreview : UserControl
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.Register(
        nameof(Key), typeof(string), typeof(ThemePreview), new PropertyMetadata("default"));

    public string Key
    {
        get => (string)GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    public ThemePreview() => InitializeComponent();
}
