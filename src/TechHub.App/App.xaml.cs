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
using TechHub.Core.Services;
using TechHub.Core.Updates;
using TechHub.Infrastructure.Updates;

namespace TechHub.App;

public partial class App : Application
{
    private IHost? _host;
    private string? _logsDirectory;
    private bool _closingAfterFatalError;
    private bool _shutdownStarted;
    private bool _hostStopped;
    private Mutex? _runningMarker;
    private MachineUpdateMarker? _machineRunningMarker;

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
        var isMachine = UpdatePolicy.SamePath(AppContext.BaseDirectory, UpdatePolicy.DefaultDirectory(UpdateInstallMode.Machine));
        if (Mutex.TryOpenExisting("Local\\TechHub.Update.InProgress", out var updateMarker) ||
            isMachine && MachineUpdateMarker.Exists(MachineUpdateMarker.Updating))
        {
            updateMarker?.Dispose();
            MessageBox.Show("Uma atualização está em andamento. Aguarde a conclusão para abrir o TechHub.", "TechHub");
            Shutdown();
            return;
        }
        _runningMarker = new Mutex(false, "Local\\TechHub.App.Running");
        if (isMachine)
        {
            _machineRunningMarker = MachineUpdateMarker.Create(MachineUpdateMarker.Running, out _);
            // Closes the race between the first check and publishing this app's running marker.
            if (MachineUpdateMarker.Exists(MachineUpdateMarker.Updating)) { Shutdown(); return; }
        }
        if (Mutex.TryOpenExisting("Local\\TechHub.Update.InProgress", out var startedUpdate))
        {
            startedUpdate.Dispose();
            Shutdown();
            return;
        }
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
                services.AddSingleton<IUpdateInstaller, DesktopUpdateInstaller>();
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
        mainWindow.Closing += async (_, args) =>
        {
            if (_shutdownStarted || _closingAfterFatalError) return;
            args.Cancel = true;
            var dashboard = _host.Services.GetRequiredService<DashboardViewModel>();
            if (dashboard.InstallUpdateCommand.IsRunning)
            {
                MessageBox.Show(mainWindow, "Aguarde a atualização ou cancele o download antes de fechar.", "TechHub");
                return;
            }
            await ShutdownForUpdateAsync();
        };
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

    internal async Task ShutdownForUpdateAsync()
    {
        if (_shutdownStarted) return;
        _shutdownStarted = true;
        if (_host is not null && !_hostStopped)
        {
            try { await _host.StopAsync(TimeSpan.FromSeconds(3)); }
            catch (Exception exception) { Log.Warning(exception, "Falha ao parar serviços no encerramento"); }
            _hostStopped = true;
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _runningMarker?.Dispose();
        _machineRunningMarker?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
