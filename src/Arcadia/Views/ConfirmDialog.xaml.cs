using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Arcadia.ViewModels;

namespace Arcadia.Views;

public partial class ConfirmDialog : UserControl
{
    public ConfirmDialog()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Apply();
        Loaded += (_, _) => (ConfirmDanger.IsVisible ? ConfirmDanger : ConfirmPrimary).Focus();
    }

    private void Apply()
    {
        if (DataContext is not ConfirmDialogViewModel vm || !vm.IsDanger) return;
        ConfirmPrimary.Visibility = Visibility.Collapsed;
        ConfirmDanger.Visibility = Visibility.Visible;
        IconBg.Fill = (Brush)FindResource("DangerSoft");
        IconGlyph.Foreground = (Brush)FindResource("Danger");
    }
}
