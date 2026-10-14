using System.Windows;
using System.Windows.Controls;
using TechHub.App.ViewModels;

namespace TechHub.App.Views;

public partial class NetworkView : UserControl
{
    public NetworkView() => InitializeComponent();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is NetworkViewModel network) await network.InitializeAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is NetworkViewModel network) network.CancelScan();
    }
}
