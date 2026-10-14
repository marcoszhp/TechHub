using System.Windows;
using System.Windows.Controls;
using TechHub.App.ViewModels;
using Wpf.Ui.Appearance;
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
        await model.LoadModuleAsync(tab.Uid);
    }

    private void OnLightThemeClick(object sender, RoutedEventArgs e)
    {
        ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica, true);
        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 246, 250));
    }

    private void OnDarkThemeClick(object sender, RoutedEventArgs e)
    {
        ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica, true);
        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 23, 42));
    }
}
