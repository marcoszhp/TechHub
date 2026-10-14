using System.IO;
using System.Windows;
using System.Windows.Threading;
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
    private string? _logsDirectory;
    private bool _closingAfterFatalError;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Falha inesperada na interface; encerrando para evitar estado inconsistente");
        e.Handled = true;
        if (_closingAfterFatalError) return;
        _closingAfterFatalError = true;
        MessageBox.Show($"O TechHub encontrou um erro inesperado e precisa ser fechado.\n\nOs registros já salvos permanecem no computador. Alterações ainda não salvas podem ser perdidas.\n\nDetalhes: {_logsDirectory ?? "pasta de logs da aplicação"}",
            "TechHub — reinicialização necessária", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Falha assíncrona não observada");
        e.SetObserved();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
        var paths = new ApplicationPathsProvider();
        _logsDirectory = paths.Current.LogsDirectory;

        _host = Host.CreateDefaultBuilder(e.Args)
            .UseSerilog((_, loggerConfiguration) => loggerConfiguration
                .MinimumLevel.Information()
                .Enrich.WithProperty("ApplicationVersion", typeof(App).Assembly.GetName().Version?.ToString(3))
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
        Log.Information("Iniciando TechHub {Version}; executável {Executable}",
            typeof(App).Assembly.GetName().Version?.ToString(3), Environment.ProcessPath);
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
