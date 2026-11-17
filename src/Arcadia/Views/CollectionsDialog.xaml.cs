using System.Windows.Controls;
using System.Windows.Input;
using Arcadia.ViewModels;

namespace Arcadia.Views;

public partial class CollectionsDialog : UserControl
{
    public CollectionsDialog() => InitializeComponent();

    private void NewBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not CollectionsDialogViewModel vm) return;
        e.Handled = true;
        vm.AddCommand.Execute(null);
    }
}
