using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TechHub.App;

namespace TechHub.Ui.Tests;

public sealed class UpdateLayoutTests
{
    [Theory]
    [InlineData(1080)]
    [InlineData(1360)]
    public Task UpdateCardRendersAtSupportedWidths(int width) => WpfTestHost.RunAsync(() =>
    {
        var shell = new MainWindow(null!);
        var navigation = (TabControl)shell.FindName("Navigation");
        var tab = navigation.Items.Cast<TabItem>().Single(item => item.Uid == "dashboard");
        var content = (FrameworkElement)tab.Content;
        tab.Content = null;
        shell.Close();
        content.DataContext = new { Dashboard = new { CurrentVersion = "1.2.1", AvailableVersion = "1.3.0", UpdateAvailable = true,
            UpdateStatus = "Baixando e verificando o instalador…", UpdateProgressPercent = 45d, IsCheckingUpdate = false,
            UpdateProgressText = "45% — prévia com dados simulados", ReleaseNotes = "Atualização integrada; dados locais preservados." } };
        var surface = new Border { Width = width - 260, Height = 1020, Child = content,
            Background = (Brush)Application.Current.FindResource("HubBackground") };
        TextElement.SetForeground(surface, (Brush)Application.Current.FindResource("HubInk"));
        surface.Measure(new Size(surface.Width, surface.Height));
        surface.Arrange(new Rect(0, 0, surface.Width, surface.Height));
        surface.UpdateLayout();
        var buttons = Descendants<Button>(surface).Where(b => b.Visibility == Visibility.Visible).ToArray();
        Assert.Contains(buttons, b => Equals(b.Content, "Atualizar e reiniciar"));
        foreach (var button in buttons)
        {
            var bounds = button.TransformToAncestor(surface).TransformBounds(new Rect(button.RenderSize));
            Assert.True(bounds.Left >= 0 && bounds.Right <= surface.Width + 1, $"Botão fora da área: {button.Content}");
        }
        Assert.Equal(45d, Assert.Single(Descendants<ProgressBar>(surface)).Value);
        var destination = Environment.GetEnvironmentVariable("TECHHUB_UI_SNAPSHOT_DIR");
        if (string.IsNullOrWhiteSpace(destination)) return;
        Directory.CreateDirectory(destination);
        var bitmap = new RenderTargetBitmap((int)surface.Width, (int)surface.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(destination, $"update-{width}.png"));
        encoder.Save(stream);
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
