using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Arcadia.Controls;
using Arcadia.Models;
using Arcadia.Services;

namespace Arcadia.Views.Shells;

/// <summary>
/// The short startup animation of the console themes, shown when Arcadia opens on one of them (any key,
/// click or controller button skips it). Original drawings and sounds, only the mood of each family is evoked:
/// a light line that widens on PlayStation, two halves that click together on Switch, the "press A" screen on Wii.
/// </summary>
public sealed class ShellBootScreen : Grid
{
    private readonly DispatcherTimer _end;
    private bool _closing;

    public event Action? Finished;
    public bool IsRunning => !_closing;

    public ShellBootScreen(ConsoleTheme theme, SoundService sound)
    {
        var scaler = new DesignScaler();
        var stage = new Grid();
        scaler.Child = stage;
        TimeSpan length;
        switch (theme.Family)
        {
            case "ps":
                Background = Brushes.Black;
                BuildPs(stage);
                sound.Play(Sfx.BootPs);
                length = TimeSpan.FromSeconds(2.9);
                break;
            case "switch":
                Background = theme.IsLight ? new SolidColorBrush(Color.FromRgb(0xEB, 0xEB, 0xEB)) : new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x16));
                BuildSwitch(stage, theme.IsLight, sound);
                length = TimeSpan.FromSeconds(2.3);
                break;
            default: // wii
                Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xF8));
                BuildWii(stage);
                sound.Play(Sfx.BootWii);
                length = TimeSpan.FromSeconds(3.6);
                break;
        }
        Children.Add(scaler);
        _end = new DispatcherTimer { Interval = length };
        _end.Tick += (_, _) => Skip();
        _end.Start();
    }

    /// <summary>Fades out (also on any input).</summary>
    public void Skip()
    {
        if (_closing) return;
        _closing = true;
        _end.Stop();
        IsHitTestVisible = false;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => Finished?.Invoke();
        BeginAnimation(OpacityProperty, fade);
    }

    private static TextBlock Wordmark(double size, Brush brush, double spacing) => new()
    {
        Text = string.Join(new string(' ', (int)spacing), "ARCADIA".ToCharArray()),
        FontFamily = new FontFamily("Segoe UI Light"),
        FontSize = size,
        Foreground = brush,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static DoubleAnimation Anim(double from, double to, double ms, double delay = 0, IEasingFunction? ease = null) =>
        new(from, to, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = ease ?? new CubicEase { EasingMode = EasingMode.EaseOut } };

    // ---- PlayStation: a line of light widens, the name appears with a glow --------------------
    private static void BuildPs(Grid stage)
    {
        var glow = new Border
        {
            Width = 1300, Height = 3, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0, 120, 170, 255), 0), new(Color.FromArgb(255, 210, 230, 255), 0.5), new(Color.FromArgb(0, 120, 170, 255), 1),
            }, 0),
            Effect = new BlurEffect { Radius = 6 },
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(0, 1),
        };
        var halo = new Border
        {
            Width = 900, Height = 300, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0,
            Background = new RadialGradientBrush(Color.FromArgb(70, 70, 110, 255), Color.FromArgb(0, 0, 0, 0)),
        };
        var name = Wordmark(46, Brushes.White, 2);
        name.Margin = new Thickness(0, 0, 0, 120);
        name.Opacity = 0;
        name.Effect = new DropShadowEffect { Color = Color.FromRgb(140, 170, 255), BlurRadius = 24, ShadowDepth = 0, Opacity = 0.8 };
        stage.Children.Add(halo);
        stage.Children.Add(glow);
        stage.Children.Add(name);
        ((ScaleTransform)glow.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, Anim(0, 1, 1100, 150, new QuinticEase { EasingMode = EasingMode.EaseInOut }));
        halo.BeginAnimation(OpacityProperty, Anim(0, 1, 900, 500));
        name.BeginAnimation(OpacityProperty, Anim(0, 1, 900, 900));
        glow.BeginAnimation(OpacityProperty, Anim(1, 0.25, 900, 1500));
    }

    // ---- Switch: the two legs of Arcadia's "A" slide in and click together --------------------
    private static void BuildSwitch(Grid stage, bool light, SoundService sound)
    {
        var colour = light ? Color.FromRgb(0x2D, 0x2D, 0x2D) : Colors.White;
        (Border Leg, TranslateTransform Move) Leg(bool left)
        {
            var move = new TranslateTransform(left ? -440 : 440, 0);
            var leg = new Border
            {
                Width = 46, Height = 200, CornerRadius = new CornerRadius(23), Background = new SolidColorBrush(colour),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 60),
                RenderTransformOrigin = new Point(0.5, 0.5),
                // leaning towards each other: their tops meet in the middle, like an A
                RenderTransform = new TransformGroup { Children = { new RotateTransform(left ? 18 : -18), new TranslateTransform(left ? -32 : 32, 0), move } },
            };
            return (leg, move);
        }
        var (leftLeg, leftMove) = Leg(true);
        var (rightLeg, rightMove) = Leg(false);
        var bar = new Border
        {
            Width = 84, Height = 20, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(colour),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 60, 0, 0),
            Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(0.3, 1),
        };
        var name = Wordmark(34, new SolidColorBrush(colour), 1);
        name.Margin = new Thickness(0, 280, 0, 0);
        name.Opacity = 0;
        stage.Children.Add(leftLeg);
        stage.Children.Add(rightLeg);
        stage.Children.Add(bar);
        stage.Children.Add(name);
        var snap = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 };
        leftMove.BeginAnimation(TranslateTransform.XProperty, Anim(-440, 0, 380, 250, snap));
        rightMove.BeginAnimation(TranslateTransform.XProperty, Anim(440, 0, 380, 250, snap));
        // the click when both legs meet, then the crossbar snaps in
        var click = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(560) };
        click.Tick += (_, _) => { click.Stop(); sound.Play(Sfx.BootSwitch); };
        click.Start();
        bar.BeginAnimation(OpacityProperty, Anim(0, 1, 120, 640));
        ((ScaleTransform)bar.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, Anim(0.3, 1, 260, 640, snap));
        name.BeginAnimation(OpacityProperty, Anim(0, 1, 500, 950));
    }

    // ---- Wii: the calm "press A" screen -------------------------------------------------------
    private static void BuildWii(Grid stage)
    {
        var grey = new SolidColorBrush(Color.FromRgb(0x4A, 0x55, 0x60));
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0 };
        panel.Children.Add(new TextBlock
        {
            Text = "⚠  CONSEIL", FontSize = 40, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xBE, 0xED)),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        panel.Children.Add(new Border { Height = 3, Width = 900, Background = new SolidColorBrush(Color.FromRgb(0xD3, 0xDA, 0xE1)), Margin = new Thickness(0, 22, 0, 30) });
        panel.Children.Add(new TextBlock
        {
            Text = "Pense à faire une pause de temps en temps,\net à bien t'installer pour jouer.",
            FontSize = 30, Foreground = grey, TextAlignment = TextAlignment.Center, LineHeight = 46, HorizontalAlignment = HorizontalAlignment.Center,
        });
        var press = new TextBlock
        {
            Text = "Appuie sur Ⓐ pour continuer.", FontSize = 28, Foreground = grey, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 90, 0, 0),
        };
        panel.Children.Add(press);
        stage.Children.Add(panel);
        panel.BeginAnimation(OpacityProperty, Anim(0, 1, 600, 150));
        press.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.3, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(900) });
    }
}
