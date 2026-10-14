using System.Windows;
using System.Windows.Controls;
using TechHub.App.ViewModels;
using Wpf.Ui.Controls;

namespace TechHub.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void OnNavigationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, Navigation) || DataContext is not MainViewModel model ||
            Navigation.SelectedItem is not TabItem tab) return;
        try { await model.LoadModuleAsync(tab.Uid); }
        catch (Exception exception) { Serilog.Log.Error(exception, "Falha ao trocar para o módulo {Module}", tab.Uid); }
    }

}
