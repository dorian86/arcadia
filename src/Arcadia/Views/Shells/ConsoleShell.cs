using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Arcadia.Services;
using Arcadia.ViewModels;

namespace Arcadia.Views.Shells;

/// <summary>
/// Base of the console-style themes. The view model (<see cref="ConsoleShellViewModel"/>) holds the lists and
/// the selection; a theme draws them and turns the arrows / controller into moves that suit its layout.
/// </summary>
public abstract class ConsoleShell : UserControl
{
    protected ConsoleShellViewModel? Vm { get; private set; }
    private bool _pendingSelection;

    protected ConsoleShell()
    {
        Focusable = true;
        FocusVisualStyle = null;
        AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler(OnPadButtonClick));
        DataContextChanged += (_, _) =>
        {
            if (Vm != null)
            {
                Vm.PropertyChanged -= OnVmChanged;
                Vm.Rebuilt -= OnVmRebuilt;
            }
            Vm = DataContext as ConsoleShellViewModel;
            if (Vm != null)
            {
                Vm.PropertyChanged += OnVmChanged;
                Vm.Rebuilt += OnVmRebuilt;
            }
        };
    }

    protected static MainViewModel Main => MainViewModel.Instance;

    public virtual void OnShown()
    {
        Vm?.Start();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            OnRebuilt();
            OnSelectionChanged(animate: false);
            Focus();
        });
    }

    public virtual void OnHidden() => Vm?.Stop();

    /// <summary>The lists changed (games added, renamed, favourites…).</summary>
    protected virtual void OnRebuilt() { }

    /// <summary>The selected game or category changed.</summary>
    protected virtual void OnSelectionChanged(bool animate) { }

    private void OnVmRebuilt() => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
    {
        if (!IsVisible) return; // refreshed by OnShown
        OnRebuilt();
        OnSelectionChanged(animate: false);
    });

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsVisible) return; // a theme in the background never reacts (OnShown refreshes it)
        if (e.PropertyName is not (nameof(ConsoleShellViewModel.Index) or nameof(ConsoleShellViewModel.CategoryIndex))) return;
        // index and category often change together: update once, after layout
        if (_pendingSelection) return;
        _pendingSelection = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _pendingSelection = false;
            OnSelectionChanged(animate: true);
        });
    }

    // ------------------------------------------------------------------------------------
    // Input
    // ------------------------------------------------------------------------------------

    public bool HandleKey(Key key)
    {
        if (MapKey(key) is not { } p) return false;
        HandlePad(p);
        return true;
    }

    /// <summary>The keyboard equivalent of the controller buttons in every theme.</summary>
    public static PadButton? MapKey(Key key)
    {
        return key switch
        {
            Key.Left => PadButton.Left,
            Key.Right => PadButton.Right,
            Key.Up => PadButton.Up,
            Key.Down => PadButton.Down,
            Key.Enter or Key.Space => PadButton.A,
            Key.Escape or Key.Back => PadButton.B,
            Key.F => PadButton.Y,
            Key.R => PadButton.X,
            Key.PageUp => PadButton.LB,
            Key.PageDown or Key.Tab => PadButton.RB,
            Key.Apps or Key.O => PadButton.Start,
            _ => null,
        };
    }

    public abstract void HandlePad(PadButton button);

    // ------------------------------------------------------------------------------------
    // Mouse
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// Any button carrying <c>sh:ConsoleShell.Pad="B"</c> (on-screen hints such as "Ⓑ Retour", category arrows…)
    /// acts like that controller button when clicked.
    /// </summary>
    public static readonly DependencyProperty PadProperty = DependencyProperty.RegisterAttached(
        "Pad", typeof(string), typeof(ConsoleShell), new PropertyMetadata(null));

    public static string? GetPad(DependencyObject d) => (string?)d.GetValue(PadProperty);
    public static void SetPad(DependencyObject d, string? value) => d.SetValue(PadProperty, value);

    private int _wheel;

    private void OnPadButtonClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && Enum.TryParse<PadButton>(GetPad(d), out var pad))
        {
            e.Handled = true;
            HandlePad(pad);
        }
    }

    /// <summary>One notch of the wheel (or the equivalent on a touchpad): +1 = towards the user / next.</summary>
    protected virtual bool HandleWheel(int step) => false;

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        _wheel -= e.Delta;
        var handled = false;
        while (Math.Abs(_wheel) >= 120)
        {
            var step = Math.Sign(_wheel);
            _wheel -= step * 120;
            handled |= HandleWheel(step);
        }
        if (handled) e.Handled = true;
        else if (Math.Abs(e.Delta) >= 120) _wheel = 0; // let scroll viewers have it
    }

    // ------------------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------------------

    private System.Drawing.Point _lastHover = new(int.MinValue, int.MinValue);

    /// <summary>
    /// True when the pointer itself moved onto an element. When a row slides under a still mouse, WPF also raises
    /// MouseEnter: selecting on those would make the row run away to its end.
    /// </summary>
    protected bool HoverIsReal()
    {
        var p = System.Windows.Forms.Cursor.Position;
        if (p == _lastHover) return false;
        _lastHover = p;
        return true;
    }

    protected static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    protected static void Animate(Animatable target, DependencyProperty property, double to, int ms, IEasingFunction? ease = null) =>
        target.BeginAnimation(property, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? Ease });

    protected static void Animate(UIElement target, DependencyProperty property, double to, int ms, IEasingFunction? ease = null) =>
        target.BeginAnimation(property, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? Ease });

    protected static ScaleTransform ScaleOf(UIElement element)
    {
        if (element.RenderTransform is ScaleTransform { IsFrozen: false } s) return s;
        s = new ScaleTransform();
        element.RenderTransform = s;
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        return s;
    }

    protected static void ScaleTo(UIElement element, double scale, int ms)
    {
        var s = ScaleOf(element);
        Animate(s, ScaleTransform.ScaleXProperty, scale, ms);
        Animate(s, ScaleTransform.ScaleYProperty, scale, ms);
    }

    /// <summary>Container of the n-th item of an ItemsControl (null while not generated).</summary>
    protected static FrameworkElement? ItemAt(ItemsControl list, int index) =>
        index >= 0 && index < list.Items.Count ? list.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement : null;

    /// <summary>Opens the add-games choices as a small menu under an element.</summary>
    protected static void ShowAddMenu(FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        void Add(string header, string glyph, ICommand command)
        {
            menu.Items.Add(new MenuItem
            {
                Header = header,
                Command = command,
                Icon = new TextBlock { Text = glyph, Style = (Style)Application.Current.FindResource("Glyph"), FontSize = 13 },
            });
        }
        Add("Détecter les jeux installés…", "", Main.ScanPcCommand);
        Add("Ajouter des fichiers…", "", Main.AddGamesCommand);
        Add("Chercher dans un dossier…", "", Main.ImportFolderCommand);
        menu.IsOpen = true;
    }
}
