using System.Windows.Controls;
using System.Windows.Threading;

namespace Arcadia.Views;

public partial class EditGameDialog : UserControl
{
    public EditGameDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        });
    }
}
