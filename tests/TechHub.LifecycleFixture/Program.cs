using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using TechHub.App.ViewModels;
using TechHub.Core.Models;
using TechHub.Infrastructure.Storage;
using RealApp = TechHub.App.App;

namespace TechHub.LifecycleFixture;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Evidence? evidence = null;
        string? evidencePath = null;
        try
        {
            if (args.Length != 3 || args[0] is not ("normal-close" or "update-shutdown"))
                throw new ArgumentException("Expected: normal-close|update-shutdown repository-path expected-version");

            var repository = Path.GetFullPath(args[1]);
            var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
            var runDirectory = Directory.GetParent(output) ?? throw new IOException("Missing run directory.");
            Require(Guid.TryParseExact(runDirectory.Name, "N", out _), "Run directory must be a GUID in N format.");
            Require(SamePath(runDirectory.Parent?.FullName ?? "", Path.Combine(repository, "artifacts", "lifecycle")),
                "Fixture output must be under repository/artifacts/lifecycle/GUID/mode.");
            Require(SamePath(output, Path.Combine(runDirectory.FullName, args[0])), "Output must match the requested case.");
            Require(File.Exists(Path.Combine(repository, "tests", "TechHub.LifecycleFixture", "TechHub.LifecycleFixture.csproj")),
                "Repository path is not the fixture repository.");
            RejectReparsePoints(output);
            evidencePath = Path.Combine(output, "lifecycle-evidence.json");
            Require(!File.Exists(evidencePath) && !Directory.Exists(Path.Combine(output, "Data")), "Use a fresh case directory.");
            evidence = new Evidence { Mode = args[0], ProcessId = Environment.ProcessId, BaseDirectory = output,
                ExpectedVersion = args[2], Version = typeof(RealApp).Assembly.GetName().Version?.ToString(3) ?? "",
                DatabasePath = Path.Combine(output, "Data", "techhub.db") };
            Save(evidencePath, evidence);
            Require(evidence.Version == args[2], "App assembly version does not match expected version.");

            // Never fall through to installed-mode paths, even when launched manually.
            using (new FileStream(Path.Combine(output, "portable.flag"), FileMode.CreateNew, FileAccess.Write)) { }
            var paths = new ApplicationPathsProvider().Current;
            Require(paths.Mode == StorageMode.Portable, "Fixture must use portable mode.");
            Require(SamePath(paths.DatabasePath, evidence.DatabasePath), "Database path is not isolated.");

            // Production skips ReadPreviousResult in portable mode. Do not compute or
            // inspect installed-mode update paths here; keep the real installer and host.

            var app = new RealApp();
            app.InitializeComponent();
            var actionStarted = false;
            var timer = new DispatcherTimer(DispatcherPriority.Background, app.Dispatcher)
                { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += async (_, _) =>
            {
                if (actionStarted) return;
                try
                {
                    if (app.MainWindow is not TechHub.App.MainWindow window || !window.IsLoaded || !window.IsVisible ||
                        window.DataContext is not MainViewModel vm || vm.HasActiveWorkForUpdate() ||
                        !vm.LoadStatus.StartsWith("Pronto", StringComparison.Ordinal)) return;
                    actionStarted = true;
                    timer.Stop();
                    Require(vm.Dashboard.StatusMessage.StartsWith("Atualizado em", StringComparison.Ordinal),
                        "Real dashboard query did not succeed.");
                    Require(vm.ApplicationVersion == evidence.Version && vm.Dashboard.CurrentVersion == evidence.Version,
                        "View-model versions do not match App assembly.");
                    Require(File.Exists(evidence.DatabasePath), "App did not create its isolated database.");
                    evidence.Ready = true;
                    evidence.ShutdownRequested = true;
                    Save(evidencePath, evidence);
                    if (evidence.Mode == "normal-close") window.Close();
                    else await app.ShutdownForUpdateAsync();
                }
                catch (Exception exception)
                {
                    timer.Stop();
                    evidence.Error = exception.ToString();
                    Save(evidencePath, evidence);
                    // A failed fixture may request a managed shutdown; it never kills a process.
                    app.Shutdown(1);
                }
            };
            timer.Start();
            evidence.RunStarted = true;
            Save(evidencePath, evidence);
            var watch = Stopwatch.StartNew();
            var exitCode = app.Run();
            timer.Stop();
            evidence.RunReturned = true;
            evidence.RunEvidence = "Run retornou";
            evidence.RunExitCode = exitCode;
            evidence.RunElapsedMilliseconds = watch.ElapsedMilliseconds;
            Save(evidencePath, evidence);
            Require(exitCode == 0 && evidence.Error is null && evidence.Ready && evidence.ShutdownRequested,
                "App did not complete the requested real lifecycle.");
            using (var db = new SqliteConnection(new SqliteConnectionStringBuilder
                   { DataSource = evidence.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                db.Open();
                using var command = db.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'audit_log'";
                Require(Convert.ToInt64(command.ExecuteScalar()) == 1, "Real App audit schema was not created.");
            }
            evidence.DatabaseValidated = true;
            Save(evidencePath, evidence);
            return 0;
        }
        catch (Exception exception)
        {
            if (evidence is not null && evidencePath is not null)
            {
                evidence.Error = exception.ToString();
                Save(evidencePath, evidence);
            }
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static bool SamePath(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);

    private static void RejectReparsePoints(string path)
    {
        for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
            Require((directory.Attributes & FileAttributes.ReparsePoint) == 0, "Reparse points are not allowed in fixture paths.");
    }

    private static void Save(string path, Evidence evidence) =>
        File.WriteAllText(path, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));

    private sealed class Evidence
    {
        public string Mode { get; init; } = "";
        public int ProcessId { get; init; }
        public string BaseDirectory { get; init; } = "";
        public string ExpectedVersion { get; init; } = "";
        public string Version { get; init; } = "";
        public string DatabasePath { get; init; } = "";
        public bool Ready { get; set; }
        public bool ShutdownRequested { get; set; }
        public bool RunStarted { get; set; }
        public bool RunReturned { get; set; }
        public string? RunEvidence { get; set; }
        public int? RunExitCode { get; set; }
        public long? RunElapsedMilliseconds { get; set; }
        public bool DatabaseValidated { get; set; }
        public string? Error { get; set; }
    }
}
