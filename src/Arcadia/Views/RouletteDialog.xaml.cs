using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Arcadia.ViewModels;

namespace Arcadia.Views;

/// <summary>"Case opening" style roulette: the strip decelerates and stops on the drawn game.</summary>
public partial class RouletteDialog : UserControl
{
    private const double Pitch = 170 + 18; // item width + margin
    private RouletteViewModel? _vm;
    private int _lastIndex = -1;
    private bool _ticking;

    public RouletteDialog()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.SpinRequested -= OnSpinRequested;
            _vm = DataContext as RouletteViewModel;
            if (_vm != null) _vm.SpinRequested += OnSpinRequested;
        };
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => _vm?.SpinCommand.Execute(null));
        Unloaded += (_, _) => StopTicking();
    }

    private void OnSpinRequested() =>
        // wait for the new strip to be laid out
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, Spin);

    private void Spin()
    {
        if (_vm == null) return;
        var t = (TranslateTransform)StripHost.RenderTransform;
        var centre = Viewport.ActualWidth / 2;
        var jitter = (Random.Shared.NextDouble() - 0.5) * 120; // do not always stop dead centre
        var target = centre - (RouletteViewModel.WinnerIndex * Pitch + 85) + jitter;
        ResetScales();

        t.BeginAnimation(TranslateTransform.XProperty, null);
        t.X = centre - 85 + Pitch * 2; // start slightly to the right
        var anim = new DoubleAnimation(target, TimeSpan.FromSeconds(5.2))
        {
            EasingFunction = new PowerEase { Power = 4, EasingMode = EasingMode.EaseOut },
        };
        anim.Completed += (_, _) => Finish();
        _lastIndex = -1;
        StartTicking();
        t.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private void Finish()
    {
        StopTicking();
        if (StripHost.ItemContainerGenerator.ContainerFromIndex(RouletteViewModel.WinnerIndex) is ContentPresenter cp
            && cp.RenderTransform is ScaleTransform s && !s.IsFrozen)
        {
            var pop = new DoubleAnimation(1.12, TimeSpan.FromMilliseconds(420)) { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
            s.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }
        _vm?.OnSpinCompleted();
    }

    private void ResetScales()
    {
        for (var i = 0; i < StripHost.Items.Count; i++)
        {
            if (StripHost.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter cp) continue;
            if (cp.RenderTransform is not ScaleTransform s || s.IsFrozen) cp.RenderTransform = s = new ScaleTransform();
            s.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            s.ScaleX = s.ScaleY = 1;
        }
    }

    // A tick each time a cover passes under the marker: the sound slows down with the strip
    private void StartTicking()
    {
        if (_ticking) return;
        _ticking = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopTicking()
    {
        if (!_ticking) return;
        _ticking = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var x = ((TranslateTransform)StripHost.RenderTransform).X;
        var index = (int)Math.Floor((Viewport.ActualWidth / 2 - x) / Pitch);
        if (index == _lastIndex) return;
        _lastIndex = index;
        _vm?.OnTick();
    }
}
