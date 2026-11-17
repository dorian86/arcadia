using System.Windows.Controls;
using System.Windows.Threading;

namespace Arcadia.Views;

public partial class PromptDialog : UserControl
{
    public PromptDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            Input.Focus();
            Input.SelectAll();
        });
    }
}
