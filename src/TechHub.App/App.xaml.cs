using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using TechHub.App.Services;
using TechHub.App.ViewModels;
using TechHub.Infrastructure;
using TechHub.Infrastructure.Storage;

namespace TechHub.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
        var paths = new ApplicationPathsProvider();

        _host = Host.CreateDefaultBuilder(e.Args)
            .UseSerilog((_, loggerConfiguration) => loggerConfiguration
                .MinimumLevel.Information()
                .WriteTo.File(
                    Path.Combine(paths.Current.LogsDirectory, "techhub-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    shared: true))
            .ConfigureServices(services =>
            {
                services.AddTechHubInfrastructure();
                services.AddSingleton<TechHub.Core.Services.IPowerShellExecutor, TechHub.PowerShell.PowerShellExecutor>();
                services.AddSingleton(paths);
                services.AddSingleton<TechHub.Core.Services.IApplicationPaths>(paths);
                services.AddSingleton<IActionConfirmationService, MessageBoxActionConfirmationService>();
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<PrintersViewModel>();
                services.AddSingleton<NetworkViewModel>();
                services.AddSingleton<DiagnosticsViewModel>();
                services.AddSingleton<SecurityViewModel>();
                services.AddSingleton<DriversViewModel>();
                services.AddSingleton<CleaningViewModel>();
                services.AddSingleton<TicketsViewModel>();
                services.AddSingleton<ReportsViewModel>();
                services.AddSingleton<SoftwareViewModel>();
                services.AddSingleton<AutomationViewModel>();
                services.AddSingleton<AssistantViewModel>();
                services.AddSingleton<WorkspaceViewModel>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
        await _host.Services.GetRequiredService<MainViewModel>().InitializeAsync();
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Falha ao iniciar o TechHub");
            MessageBox.Show($"O TechHub não conseguiu iniciar.\n\n{exception.Message}\n\nConsulte a pasta Logs em %LocalAppData%\\TechHub (ou Data no modo portátil).", "TechHub — falha na inicialização", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
