using System.Windows;
using System.Windows.Threading;
using TechHub.App.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace TechHub.Ui.Tests;

// Owns an in-memory dispatcher. No application host, real inventories or native window are started.
internal static class WpfTestHost
{
    private static readonly Lazy<Task<Dispatcher>> DispatcherTask = new(Start);

    private static Task<Dispatcher> Start()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                application.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Dark });
                application.Resources.MergedDictionaries.Add(new ControlsDictionary());
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/TechHub.App;component/Styles/WorkspaceTheme.xaml")
                });
                application.Resources.Add("SecurityStatus", new SecurityStatusConverter());
                ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }
            catch (Exception error) { ready.TrySetException(error); }
        }) { IsBackground = true, Name = "TechHub WPF regression tests" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task;
    }

    public static async Task RunAsync(Action action)
    {
        var dispatcher = await DispatcherTask.Value;
        await dispatcher.InvokeAsync(action).Task;
    }
}
