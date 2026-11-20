using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;

namespace Arcadia.Views;

/// <summary>What the "achievement unlocked" popup shows.</summary>
public sealed record AchievementNotice(string Name, string Detail, string? Icon);

/// <summary>
/// Small "achievement unlocked" card in the bottom-right corner of the screen, above the game (borderless and
/// windowed games; a game in exclusive full screen hides it). It never takes the focus and lets clicks through.
/// </summary>
public partial class AchievementPopup : Window
{
    private static readonly Queue<AchievementNotice> Queue = new();
    private static AchievementPopup? _current;

    public AchievementPopup(AchievementNotice notice)
    {
        InitializeComponent();
        DataContext = notice;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_TOPMOST);
        };
        Loaded += (_, _) => PlaceAndAnimate();
    }

    /// <summary>Shows the notices one after the other.</summary>
    public static void Enqueue(AchievementNotice notice)
    {
        Queue.Enqueue(notice);
        if (_current == null) ShowNext();
    }

    private static void ShowNext()
    {
        if (Queue.Count == 0) { _current = null; return; }
        _current = new AchievementPopup(Queue.Dequeue());
        _current.Show();
    }

    private void PlaceAndAnimate()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth;
        Top = area.Bottom - ActualHeight;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
        Slide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, new DoubleAnimation(60, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });

        var hide = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5.5) };
        hide.Tick += (_, _) =>
        {
            hide.Stop();
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(380));
            fade.Completed += (_, _) =>
            {
                Close();
                ShowNext();
            };
            Card.BeginAnimation(OpacityProperty, fade);
            Slide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, new DoubleAnimation(0, 40, TimeSpan.FromMilliseconds(380)));
        };
        hide.Start();
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8, WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
