using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Moq;
using TechHub.App;
using TechHub.App.Services;
using TechHub.App.ViewModels;
using TechHub.Core.Models;
using TechHub.Core.Services;

namespace TechHub.Ui.Tests;

public sealed class BindingRegressionTests
{
    internal static DriverInfo Device(uint problemCode = 0) => new("TEST", "Dispositivo de demonstração", "Fabricante de teste", "System",
        "oem1.inf", "Fornecedor de teste", "1.0.0", null, true, "Assinante de teste", problemCode, problemCode == 0 ? "OK" : "Error");

    [Fact]
    public Task LegacyDefaultCheckboxBindingReproducesReadOnlyPropertyException() => WpfTestHost.RunAsync(() =>
    {
        var checkbox = new CheckBox();
        var error = Assert.Throws<InvalidOperationException>(() => checkbox.SetBinding(CheckBox.IsCheckedProperty,
            new Binding(nameof(DriverInfo.HasProblem)) { Source = Device() }));
        Assert.Contains(nameof(DriverInfo.HasProblem), error.Message);
    });

    [Theory]
    [InlineData("drivers", 1080)]
    [InlineData("drivers", 1360)]
    [InlineData("diagnostics", 1080)]
    [InlineData("diagnostics", 1360)]
    public Task MaintenanceViewsRenderWithDataAndDarkSurfaces(string module, int width) => WpfTestHost.RunAsync(() =>
    {
        // Load the real compiled XAML, but detach the content before assigning test data.
        // This deliberately cannot call any Windows service or execute a repair.
        var shell = new MainWindow(null!);
        var navigation = (TabControl)shell.FindName("Navigation");
        var tab = navigation.Items.Cast<TabItem>().Single(item => item.Uid == module);
        var content = (FrameworkElement)tab.Content;
        tab.Content = null;
        shell.Close();
        var driverVm = new DriversViewModel(Mock.Of<IDriverService>(), Mock.Of<IActionRunner>(), Mock.Of<IActionConfirmationService>())
        {
            BackupInfName = "oem1.inf", StatusMessage = "Inventário de demonstração — nenhuma alteração realizada."
        };
        driverVm.Drivers.Add(Device());
        driverVm.Drivers.Add(Device(28));
        content.DataContext = new
        {
            Diagnostics = new
            {
                OperatingSystem = "Windows — dados de teste", OsVersion = "10.0", UptimeText = "2 dias",
                StatusMessage = "Prévia automatizada — nenhuma ação executada no computador.", IsRefreshing = false,
                Disks = new[] { new DiagnosticDiskInfo("SSD de demonstração", "OK", 512_000_000_000, null) },
                Recommendations = new[] { "Nenhuma alteração foi realizada.", "Dados usados somente para validar a interface." }
            },
            Drivers = driverVm
        };
        var surface = new Border
        {
            Width = width - 260, Height = 620, Child = content,
            Background = (Brush)Application.Current.FindResource("HubBackground")
        };
        TextElement.SetForeground(surface, (Brush)Application.Current.FindResource("HubInk"));
        surface.Measure(new Size(surface.Width, surface.Height));
        surface.Arrange(new Rect(0, 0, surface.Width, surface.Height));
        surface.UpdateLayout();
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        surface.UpdateLayout();
        var grids = Descendants<DataGrid>(surface).ToArray();
        foreach (var button in Descendants<Button>(surface).Where(button => button.Visibility == Visibility.Visible))
        {
            var bounds = button.TransformToAncestor(surface).TransformBounds(new Rect(button.RenderSize));
            Assert.True(bounds.Left >= 0 && bounds.Right <= surface.Width + 1,
                $"Button '{button.Content}' extends outside {surface.Width}: {bounds}");
        }
        Assert.NotEmpty(grids);
        Assert.All(grids, grid => Assert.True(grid.Items.Count > 0));
        foreach (var column in grids.SelectMany(grid => grid.Columns).OfType<DataGridCheckBoxColumn>())
            Assert.Equal(BindingMode.OneWay, Assert.IsType<Binding>(column.Binding).Mode);
        if (module == "drivers") Assert.NotEmpty(Descendants<CheckBox>(surface));
        Assert.All(Descendants<Border>(surface).Where(border => border.Background is SolidColorBrush), border =>
        {
            var color = ((SolidColorBrush)border.Background).Color;
            // Explicit white/light card regressions must never reappear.
            Assert.False(color.A > 0 && color.R > 225 && color.G > 225 && color.B > 225);
        });
        var bitmap = new RenderTargetBitmap((int)surface.Width, (int)surface.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var destination = Environment.GetEnvironmentVariable("TECHHUB_UI_SNAPSHOT_DIR");
        if (!string.IsNullOrWhiteSpace(destination))
        {
            Directory.CreateDirectory(destination);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(destination, $"{module}-{width}.png"));
            encoder.Save(stream);
        }
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject element) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            var child = VisualTreeHelper.GetChild(element, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
