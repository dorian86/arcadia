using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Arcadia.Views;

public partial class LibraryView : UserControl
{
    public LibraryView() => InitializeComponent();

    public void FocusSearch(bool selectAll = true)
    {
        SearchBox.Focus();
        if (selectAll) SearchBox.SelectAll();
        else SearchBox.CaretIndex = SearchBox.Text.Length;
    }

    public void ScrollToTop() => Grid.ScrollToTop();

    private void SortButton_Click(object sender, RoutedEventArgs e)
    {
        if (SortButton.ContextMenu is not { } menu) return;
        menu.DataContext = DataContext;
        menu.PlacementTarget = SortButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}
