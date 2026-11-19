using System.Windows;
using System.Windows.Controls.Primitives;
namespace ByteFlow;
public partial class MainWindow
{
    private void ToggleTopmost(object sender, RoutedEventArgs e)
    {
        Topmost = ((ToggleButton)sender).IsChecked == true;
    }
}
