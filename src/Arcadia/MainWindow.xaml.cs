using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Arcadia.Models;
using Arcadia.Services;
using Arcadia.ViewModels;

namespace Arcadia;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly GamepadService _pad = new();
    private Game? _shownDetail;
    private readonly RadialGradientBrush _ambientTop;
    private readonly RadialGradientBrush _ambientBottom;
    private bool _fullscreen;
    private WindowState _stateBeforeTv;

    /// <summary>When true, closing really quits (otherwise "close to tray" may hide the window).</summary>
    public bool AllowClose { get; set; }

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        RestorePlacement();

        _ambientTop = CreateAmbientBrush(vm.AmbientColor);
        _ambientBottom = CreateAmbientBrush(vm.AmbientColor);
        AmbientTop.Fill = _ambientTop;
        AmbientBottom.Fill = _ambientBottom;

        SourceInitialized += OnSourceInitialized;
        StateChanged += (_, _) => UpdateMaximizeGlyph();
        Activated += (_, _) => _vm.RecheckPathsQuietly();
        Loaded += (_, _) => PlayStartupAnimation();
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += OnPreviewMouseDown;
        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += OnDragLeave;
        Drop += OnDrop;

        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.MinimizeRequested += () => WindowState = WindowState.Minimized;
        vm.RestoreRequested += ShowFromBackground;
        vm.UiThemeChanged += LoadShell;
        vm.Shell.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ConsoleShellViewModel.Sheet)) UpdateSheet(); };
        Loaded += (_, _) => LoadShell();

        // Gamepad: TV mode navigation, Start + Back opens the TV mode
        _pad.CanHandle = () => IsActive && WindowState != WindowState.Minimized && !_vm.Library.Games.Any(g => g.IsRunning);
        _pad.Pressed += OnPadPressed;
        _pad.Start();
    }

    // ------------------------------------------------------------------------------------
    // Visual flourishes
    // ------------------------------------------------------------------------------------

    private static RadialGradientBrush CreateAmbientBrush(Color c) => new(new GradientStopCollection
    {
        new(Color.FromArgb(0xC0, c.R, c.G, c.B), 0),
        new(Color.FromArgb(0x50, c.R, c.G, c.B), 0.45),
        new(Color.FromArgb(0x00, c.R, c.G, c.B), 1),
    });

    private void AnimateAmbient(Color c)
    {
        foreach (var brush in new[] { _ambientTop, _ambientBottom })
        {
            var alphas = new byte[] { 0xC0, 0x50, 0x00 };
            for (var i = 0; i < 3; i++)
            {
                var target = Color.FromArgb(alphas[i], c.R, c.G, c.B);
                brush.GradientStops[i].BeginAnimation(GradientStop.ColorProperty,
                    new ColorAnimation(target, TimeSpan.FromMilliseconds(900)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
            }
        }
    }

    private void PlayStartupAnimation()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        RootGrid.Opacity = 0;
        var scale = new ScaleTransform(0.985, 0.985);
        RootGrid.RenderTransformOrigin = new Point(0.5, 0.5);
        RootGrid.RenderTransform = scale;
        RootGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = ease });
    }

    /// <summary>The trophy banner slides down from the top, then back up (its content stays during the exit).</summary>
    private void AnimateTrophyBanner(TrophyItem? trophy)
    {
        if (TrophyBannerView.RenderTransform is not TranslateTransform t) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        if (trophy != null)
        {
            TrophyBannerView.DataContext = trophy;
            TrophyBannerView.Visibility = Visibility.Visible;
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-140, 0, TimeSpan.FromMilliseconds(520)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 } });
            TrophyBannerView.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
        }
        else
        {
            var up = new DoubleAnimation(0, -140, TimeSpan.FromMilliseconds(380)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            up.Completed += (_, _) => { if (_vm.TrophyBanner == null) TrophyBannerView.Visibility = Visibility.Collapsed; };
            t.BeginAnimation(TranslateTransform.YProperty, up);
            TrophyBannerView.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
        }
    }

    // ------------------------------------------------------------------------------------
    // Navigation animations
    // ------------------------------------------------------------------------------------

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.TrophyBanner):
                AnimateTrophyBanner(_vm.TrophyBanner);
                break;
            case nameof(MainViewModel.Section):
                AnimateIn(_vm.Section switch
                {
                    Section.Home => HomePage,
                    Section.Settings => SettingsPage,
                    Section.Stats => StatsPage,
                    _ => LibraryPage,
                }, 14);
                if (_vm.IsLibrarySection)
                {
                    LibraryPage.ScrollToTop();
                    if (!string.IsNullOrEmpty(_vm.SearchText) && !LibraryPage.SearchBox.IsKeyboardFocused)
                        Dispatcher.BeginInvoke(() => LibraryPage.FocusSearch(selectAll: false), System.Windows.Threading.DispatcherPriority.Input);
                }
                break;
            case nameof(MainViewModel.DetailGame):
                UpdateDetail();
                break;
            case nameof(MainViewModel.AmbientColor):
                AnimateAmbient(_vm.AmbientColor);
                break;
            case nameof(MainViewModel.IsTvMode):
                UpdateTvMode();
                UpdateShellVisibility();
                break;
            case nameof(MainViewModel.ShowShell):
                UpdateShellVisibility();
                break;
        }
    }

    private void UpdateDetail()
    {
        var game = _vm.DetailGame;
        if (game != null)
        {
            var wasHidden = _shownDetail == null;
            _shownDetail = game;
            DetailPage.DataContext = game;
            DetailPage.Visibility = Visibility.Visible;
            DetailPage.ScrollToTop();
            if (wasHidden) AnimateIn(DetailPage, 26, 320);
            DetailPage.PlayEntrance();
        }
        else if (_shownDetail != null)
        {
            _shownDetail = null;
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (_, _) =>
            {
                if (_shownDetail != null) return;
                DetailPage.Visibility = Visibility.Collapsed;
                DetailPage.DataContext = null;
                DetailPage.BeginAnimation(OpacityProperty, null);
                DetailPage.Opacity = 1;
            };
            DetailPage.BeginAnimation(OpacityProperty, fade);
        }
    }

    private static void AnimateIn(FrameworkElement element, double offset, int durationMs = 280)
    {
        var duration = TimeSpan.FromMilliseconds(durationMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var shift = new TranslateTransform(0, offset);
        element.RenderTransform = shift;
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, 0, duration) { EasingFunction = ease });
    }

    // ------------------------------------------------------------------------------------
    // TV mode
    // ------------------------------------------------------------------------------------

    private void UpdateTvMode()
    {
        _pad.FastMode = _vm.IsTvMode;
        if (_vm.IsTvMode)
        {
            _stateBeforeTv = WindowState;
            _fullscreen = true;
            if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
            TvPage.Visibility = Visibility.Visible;
            TvPage.Show();
            TvPage.Focus();
        }
        else
        {
            TvPage.Hide(() =>
            {
                TvPage.Visibility = Visibility.Collapsed;
                _fullscreen = false;
                WindowState = WindowState.Normal;
                if (_stateBeforeTv == WindowState.Maximized) WindowState = WindowState.Maximized;
            });
        }
    }

    // ------------------------------------------------------------------------------------
    // Console-style themes
    // ------------------------------------------------------------------------------------

    private Views.Shells.ConsoleShell? _shell;
    private bool _shellShown;

    private bool ShellActive => _shell != null && _vm.ShowShell && !_vm.IsTvMode;

    private static Views.Shells.ConsoleShell? CreateShell(string key) => key switch
    {
        "wii" => new Views.Shells.WiiShell(),
        "wiiu" => new Views.Shells.WiiUShell(),
        "switch" => new Views.Shells.SwitchShell(light: false),
        "switch-light" => new Views.Shells.SwitchShell(light: true),
        "ps3" => new Views.Shells.Ps3Shell(),
        "ps4" => new Views.Shells.Ps4Shell(),
        "ps5" => new Views.Shells.Ps5Shell(),
        _ => null,
    };

    /// <summary>Builds the view of the current theme (or none for the default interface).</summary>
    private void LoadShell()
    {
        if (_shell != null)
        {
            if (_shellShown) _shell.OnHidden();
            // unplug the previous theme completely: a theme still listening to the shared selection would keep
            // applying its own rules (the Switch home shows 12 games and used to pull the PS5 back to the 12th)
            _shell.DataContext = null;
        }
        _shellShown = false;
        _vm.Shell.Sheet?.Dispose();
        _vm.Shell.Sheet = null;
        _shell = CreateShell(_vm.CurrentTheme.Key);
        if (_shell != null)
        {
            _vm.Shell.WithSettings = _vm.CurrentTheme.Key == "ps3";
            _vm.Shell.Rebuild(force: true);
            _vm.Shell.ResetSelection();
            _shell.DataContext = _vm.Shell;
        }
        ShellHost.Content = _shell;
        UpdateShellVisibility();
    }

    private void UpdateShellVisibility()
    {
        var show = ShellActive;
        _pad.FastMode = _vm.IsTvMode || show;
        // caption buttons stay readable on the light themes
        var dark = show && _vm.CurrentTheme.IsLight;
        foreach (var b in CaptionButtons.Children.OfType<Button>())
        {
            if (dark) b.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x5D, 0x66));
            else b.ClearValue(ForegroundProperty);
        }
        ApplyNotificationStyle(show ? _vm.CurrentTheme : null);
        if (show == _shellShown) return;
        _shellShown = show;
        if (show)
        {
            ShellHost.Visibility = Visibility.Visible;
            _shell!.OnShown();
        }
        else
        {
            _shell?.OnHidden();
            ShellHost.Visibility = Visibility.Collapsed;
        }
    }

    private Views.Shells.ShellBootScreen? _boot;

    /// <summary>Startup animation of the console theme (Arcadia opened on a theme). Returns false for the default interface.</summary>
    public bool PlayBootScreen()
    {
        if (!_vm.IsShellTheme) return false;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!ShellActive) return;
            var boot = new Views.Shells.ShellBootScreen(_vm.CurrentTheme, _vm.Sound);
            Grid.SetColumnSpan(boot, 2);
            Panel.SetZIndex(boot, 16);
            boot.Finished += () =>
            {
                RootGrid.Children.Remove(boot);
                if (_boot == boot) _boot = null;
                _shell?.Focus();
            };
            _boot = boot;
            RootGrid.Children.Add(boot);
        });
        return true;
    }

    /// <summary>Any key, click or controller button skips the startup animation.</summary>
    private bool SkipBoot()
    {
        if (_boot is not { IsRunning: true } boot) return false;
        boot.Skip();
        return true;
    }

    /// <summary>
    /// Notifications (toasts and trophy banner) drawn like the theme on screen: square and dark at the top
    /// on PlayStation, pill-shaped at the top right on Switch, white with a blue outline on Wii.
    /// </summary>
    private void ApplyNotificationStyle(Models.ConsoleTheme? theme)
    {
        static SolidColorBrush B(uint argb) => new(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        var keys = new[] { "ToastBg", "ToastStroke", "ToastFg", "ToastMuted", "ToastRadius" };
        (Brush Bg, Brush Stroke, Brush Fg, Brush Muted, double Radius, HorizontalAlignment Toasts, VerticalAlignment ToastsV, HorizontalAlignment Banner)? style =
            theme?.Family switch
            {
                "ps" => (B(0xF21C1F27), B(0x33FFFFFF), Brushes.White, B(0xB3FFFFFF), 6, HorizontalAlignment.Right, VerticalAlignment.Top, HorizontalAlignment.Left),
                "switch" when theme.IsLight => (B(0xF7FFFFFF), B(0xFFDCDCDC), B(0xFF2D2D2D), B(0xFF6E6E6E), 30, HorizontalAlignment.Right, VerticalAlignment.Top, HorizontalAlignment.Center),
                "switch" => (B(0xF7464646), B(0x00000000), Brushes.White, B(0xFFB4B4B4), 30, HorizontalAlignment.Right, VerticalAlignment.Top, HorizontalAlignment.Center),
                "wii" => (B(0xF7FFFFFF), B(0xFF34BEED), B(0xFF4A5560), B(0xFF7A838C), 24, HorizontalAlignment.Right, VerticalAlignment.Bottom, HorizontalAlignment.Center),
                _ => null,
            };
        if (style is not { } s)
        {
            foreach (var k in keys) Resources.Remove(k);
            ToastHost.VerticalAlignment = VerticalAlignment.Bottom;
            ToastHost.Margin = new Thickness(0, 0, 28, 26);
            TrophyBannerView.HorizontalAlignment = HorizontalAlignment.Center;
            TrophyBannerView.Margin = new Thickness(0, 24, 0, 0);
            TrophyBannerView.CornerRadius = new CornerRadius(18);
            return;
        }
        Resources["ToastBg"] = s.Bg;
        Resources["ToastStroke"] = s.Stroke;
        Resources["ToastFg"] = s.Fg;
        Resources["ToastMuted"] = s.Muted;
        Resources["ToastRadius"] = new CornerRadius(s.Radius);
        ToastHost.HorizontalAlignment = s.Toasts;
        ToastHost.VerticalAlignment = s.ToastsV;
        ToastHost.Margin = s.ToastsV == VerticalAlignment.Top ? new Thickness(0, 64, 28, 0) : new Thickness(0, 0, 28, 26);
        TrophyBannerView.HorizontalAlignment = s.Banner;
        TrophyBannerView.Margin = s.Banner == HorizontalAlignment.Left ? new Thickness(40, 24, 0, 0) : s.Banner == HorizontalAlignment.Right ? new Thickness(0, 64, 28, 0) : new Thickness(0, 24, 0, 0);
        TrophyBannerView.CornerRadius = new CornerRadius(s.Radius);
    }

    /// <summary>Dev check (--keys): sends a key to the current console theme.</summary>
    public void DevKey(Key key)
    {
        if (ShellActive && _vm.Dialogs.Count == 0 && Views.Shells.ConsoleShell.MapKey(key) is { } pad) ShellPad(pad);
    }

    /// <summary>A controller button in a console theme: to its game page when open, else to the theme.</summary>
    private void ShellPad(PadButton button)
    {
        if (_vm.Shell.Sheet is { } sheet) sheet.HandlePad(button);
        else _shell!.HandlePad(button);
    }

    /// <summary>The game page of a console theme slides in over it, and fades out.</summary>
    private void UpdateSheet()
    {
        var sheet = _vm.Shell.Sheet;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        if (sheet != null)
        {
            SheetHost.Content = new Views.Shells.ShellGameSheet { DataContext = sheet };
            SheetHost.Visibility = Visibility.Visible;
            var scale = new ScaleTransform(1.04, 1.04);
            SheetHost.RenderTransformOrigin = new Point(0.5, 0.5);
            SheetHost.RenderTransform = scale;
            SheetHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.04, 1, TimeSpan.FromMilliseconds(360)) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.04, 1, TimeSpan.FromMilliseconds(360)) { EasingFunction = ease });
        }
        else if (SheetHost.Visibility == Visibility.Visible)
        {
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fade.Completed += (_, _) =>
            {
                if (_vm.Shell.Sheet != null) return;
                SheetHost.Visibility = Visibility.Collapsed;
                SheetHost.Content = null;
                _shell?.Focus();
            };
            SheetHost.BeginAnimation(OpacityProperty, fade);
        }
    }

    private void OnPadPressed(PadButton button)
    {
        if (SkipBoot()) return;
        if (ShellActive)
        {
            if (_vm.Dialogs.Count > 0)
            {
                if (button is PadButton.B) _vm.CloseTopDialog();
                return;
            }
            ShellPad(button);
            return;
        }
        if (_vm.IsTvMode)
        {
            if (_vm.Dialogs.Count > 0)
            {
                if (button is PadButton.B) _vm.CloseTopDialog();
                return;
            }
            TvPage.HandlePad(button);
            return;
        }
        // In the desktop interface the controller only opens the TV mode
        if (button is PadButton.Start or PadButton.Back && _vm.Dialogs.Count == 0) _vm.EnterTvModeCommand.Execute(null);
    }

    // ------------------------------------------------------------------------------------
    // Keyboard & mouse
    // ------------------------------------------------------------------------------------

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (SkipBoot()) { e.Handled = true; return; }
        if (e.Key == Key.F11)
        {
            e.Handled = true;
            if (_vm.IsTvMode) _vm.ExitTvModeCommand.Execute(null);
            else _vm.EnterTvModeCommand.Execute(null);
            return;
        }
        if (_vm.IsTvMode)
        {
            if (_vm.Dialogs.Count > 0)
            {
                if (e.Key == Key.Escape) { _vm.CloseTopDialog(); e.Handled = true; }
                return;
            }
            e.Handled = TvPage.HandleKey(e.Key);
            return;
        }
        if (ShellActive)
        {
            if (_vm.Dialogs.Count > 0)
            {
                if (e.Key == Key.Escape) { _vm.CloseTopDialog(); e.Handled = true; }
                return;
            }
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O) { _vm.AddGamesCommand.Execute(null); e.Handled = true; return; }
            if (Keyboard.FocusedElement is not TextBox && Views.Shells.ConsoleShell.MapKey(e.Key) is { } pad)
            {
                ShellPad(pad);
                e.Handled = true;
            }
            return;
        }

        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        var inTextBox = Keyboard.FocusedElement is TextBox;
        if (e.Key == Key.Escape)
        {
            if (_vm.CloseTopDialog()) { e.Handled = true; return; }
            if (_vm.DetailGame != null) { _vm.CloseDetailCommand.Execute(null); e.Handled = true; return; }
            if (_vm.HasSelection) { _vm.ClearSelection(); e.Handled = true; return; }
            if (!string.IsNullOrEmpty(_vm.SearchText)) { _vm.SearchText = ""; e.Handled = true; return; }
            if (_vm.ShowReturnToShell) { _vm.ReturnToShellCommand.Execute(null); e.Handled = true; }
            return;
        }
        if (_vm.Dialogs.Count > 0)
        {
            if (_vm.Dialogs[^1] is LightboxViewModel lightbox)
            {
                if (e.Key == Key.Right) { lightbox.Next(); e.Handled = true; }
                if (e.Key == Key.Left) { lightbox.Previous(); e.Handled = true; }
            }
            return;
        }

        if (ctrl && e.Key == Key.F)
        {
            e.Handled = true;
            _vm.DetailGame = null;
            if (_vm.Section == Section.Home) HomePage.FocusSearch();
            else
            {
                if (!_vm.IsLibrarySection) _vm.Section = Section.All;
                LibraryPage.FocusSearch();
            }
        }
        else if (ctrl && e.Key == Key.O)
        {
            e.Handled = true;
            _vm.AddGamesCommand.Execute(null);
        }
        else if (ctrl && e.Key == Key.A && !inTextBox && _vm.IsLibrarySection && _vm.DetailGame == null)
        {
            e.Handled = true;
            _vm.SelectAllCommand.Execute(null);
        }
        else if (e.Key == Key.F5)
        {
            e.Handled = true;
            _vm.CheckPathsCommand.Execute(null);
        }
        else if (e.Key == Key.Back && _vm.DetailGame != null && !inTextBox)
        {
            e.Handled = true;
            _vm.CloseDetailCommand.Execute(null);
        }
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (SkipBoot()) { e.Handled = true; return; }
        if (e.ChangedButton == MouseButton.XButton1 && _vm.Dialogs.Count == 0)
        {
            // mouse "back" button = B in a console theme (closes the Wii channel screen, the Switch grid…)
            if (ShellActive) { ShellPad(PadButton.B); e.Handled = true; }
            else if (_vm.IsTvMode) { _vm.ExitTvModeCommand.Execute(null); e.Handled = true; }
            else if (_vm.DetailGame != null) { _vm.CloseDetailCommand.Execute(null); e.Handled = true; }
            else if (_vm.ShowReturnToShell) { _vm.ReturnToShellCommand.Execute(null); e.Handled = true; }
        }
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (AddButton.ContextMenu is not { } menu) return;
        menu.DataContext = DataContext;
        menu.PlacementTarget = AddButton;
        menu.Placement = PlacementMode.Top;
        menu.IsOpen = true;
    }

    // ------------------------------------------------------------------------------------
    // Drag & drop
    // ------------------------------------------------------------------------------------

    private static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (!HasFiles(e) || _vm.Dialogs.Count > 0 || _vm.IsTvMode) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Copy;
        _vm.IsDragOver = true;
        e.Handled = true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasFiles(e) && _vm.Dialogs.Count == 0 && !_vm.IsTvMode ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        var p = e.GetPosition(this);
        if (p.X <= 0 || p.Y <= 0 || p.X >= ActualWidth || p.Y >= ActualHeight) _vm.IsDragOver = false;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        _vm.IsDragOver = false;
        if (!HasFiles(e) || _vm.Dialogs.Count > 0 || _vm.IsTvMode) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            Activate();
            await _vm.ImportPathsAsync(paths);
        }
    }

    // ------------------------------------------------------------------------------------
    // Window chrome & tray
    // ------------------------------------------------------------------------------------

    public void ShowFromBackground()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void UpdateMaximizeGlyph()
    {
        var max = WindowState == WindowState.Maximized;
        Controls.Ui.SetGlyph(MaxButton, max ? "" : "");
        MaxButton.ToolTip = max ? "Restaurer" : "Agrandir";
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        try
        {
            int round = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
            int dark = 1;
            DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
            int border = 0x00382822; // COLORREF (BGR) of the stroke colour
            DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));
        }
        catch { /* Windows 10: attributes unsupported */ }
        UpdateMaximizeGlyph();
    }

    /// <summary>Maximised: fit the work area (taskbar visible). TV mode: cover the whole monitor.</summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg == WM_GETMINMAXINFO)
        {
            var monitor = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
            if (monitor != IntPtr.Zero)
            {
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    var area = _fullscreen ? info.rcMonitor : info.rcWork;
                    var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                    mmi.ptMaxPosition.X = area.Left - info.rcMonitor.Left;
                    mmi.ptMaxPosition.Y = area.Top - info.rcMonitor.Top;
                    mmi.ptMaxSize.X = area.Right - area.Left;
                    mmi.ptMaxSize.Y = area.Bottom - area.Top;
                    Marshal.StructureToPtr(mmi, lParam, true);
                }
            }
        }
        return IntPtr.Zero;
    }

    // ------------------------------------------------------------------------------------
    // Placement persistence
    // ------------------------------------------------------------------------------------

    private void RestorePlacement()
    {
        var p = _vm.Settings.Window;
        if (p == null || p.Width < MinWidth || p.Height < MinHeight) return;
        var rect = new Rect(p.Left, p.Top, p.Width, p.Height);
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!screen.IntersectsWith(rect)) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = p.Left;
        Top = p.Top;
        Width = p.Width;
        Height = p.Height;
        if (p.Maximized) WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        if (_fullscreen) return;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (bounds.IsEmpty) return;
        _vm.Settings.Window = new WindowPlacement
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            Maximized = WindowState == WindowState.Maximized,
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SavePlacement();
        if (!AllowClose && _vm.Settings.CloseToTray && _vm.Settings.ShowTrayIcon)
        {
            e.Cancel = true;
            if (_vm.IsTvMode) _vm.ExitTvModeCommand.Execute(null);
            Hide();
            _vm.Shutdown(); // save now: the window may stay hidden for a long time
            return;
        }
        _vm.Shutdown();
        base.OnClosing(e);
    }

    // ------------------------------------------------------------------------------------
    // Native
    // ------------------------------------------------------------------------------------

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public int dwFlags;
    }
}
