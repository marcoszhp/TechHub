using System.Windows;
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
}
