using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Arcadia.ViewModels;

namespace Arcadia.Views;

public partial class CoverSearchDialog : UserControl
{
    public CoverSearchDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            QueryBox.Focus();
            QueryBox.CaretIndex = QueryBox.Text.Length;
        });
    }

    private void QueryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not CoverSearchViewModel vm) return;
        e.Handled = true;
        vm.SearchCommand.Execute(null);
    }
}
