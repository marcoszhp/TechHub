using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TechHub.Core.Models;
using TechHub.Core.Services;
using TechHub.Core.Updates;
using TechHub.Infrastructure.Workspace;
using TechHub.Updater;

// Separate test executable. It is never shipped and does not add bypasses to the production entry point.
if (args.Length != 2) throw new ArgumentException("Expected isolated artifact directory and baseline installer.");
var root = Path.GetFullPath(args[0]);
if (!root.Contains(Path.DirectorySeparatorChar + "artifacts" + Path.DirectorySeparatorChar + "update-integration" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Integration writes must remain inside the dedicated artifacts directory.");
if (args[1] == "--verify-release")
{
    Directory.CreateDirectory(root);
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    client.DefaultRequestHeaders.UserAgent.ParseAdd("TechHub-Integration/1.3");
    using var metadata = JsonDocument.Parse(await client.GetStringAsync("https://api.github.com/repos/marcoszhp/TechHub/releases/latest"));
    var version = metadata.RootElement.GetProperty("tag_name").GetString()![1..];
    var asset = metadata.RootElement.GetProperty("assets").EnumerateArray().Single(a => a.GetProperty("name").GetString() == $"TechHub-User-Setup-{version}.exe");
    var trustJob = new UpdateJob(1, 1, "unused", UpdateInstallMode.User, "0.0.0", version,
        asset.GetProperty("digest").GetString()![7..], asset.GetProperty("size").GetInt64());
    using var verifier = new WindowsUpdateRuntime();
    await verifier.VerifyReleaseAsync(trustJob, CancellationToken.None);
    bool rejected = false;
    try { await verifier.VerifyReleaseAsync(trustJob with { Sha256 = new string('0', 64) }, CancellationToken.None); }
    catch (InvalidDataException) { rejected = true; }
    if (!rejected) throw new Exception("Trust boundary accepted a changed digest.");
    var trustEvidence = new { OfficialRelease = version, CanonicalMetadataAccepted = true, TamperedDigestRejected = true, ExecutedPackage = false };
    await File.WriteAllTextAsync(Path.Combine(root, "trust-evidence.json"), JsonSerializer.Serialize(trustEvidence));
    Console.WriteLine(JsonSerializer.Serialize(trustEvidence));
    return;
}
var install = Path.Combine(root, "Install");
Directory.CreateDirectory(install);
using var identity = WindowsIdentity.GetCurrent();
var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
if (elevated) throw new InvalidOperationException("Run fixture as the normal user, not as administrator.");
var baselineStart = new ProcessStartInfo(Path.GetFullPath(args[1])) { UseShellExecute = false, CreateNoWindow = true };
foreach (var arg in new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/SP-", "/NORESTART", "/DIR=" + install }) baselineStart.ArgumentList.Add(arg);
using (var baseline = Process.Start(baselineStart)!)
{
    await baseline.WaitForExitAsync();
    if (baseline.ExitCode != 0) throw new Exception("Baseline fixture install failed: " + baseline.ExitCode);
}
var data = Path.Combine(install, "Data");
Directory.CreateDirectory(data);
var database = Path.Combine(data, "techhub.db");
await using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
{
    await connection.OpenAsync();
    using var command = connection.CreateCommand();
    command.CommandText = "CREATE TABLE sentinel(value TEXT); INSERT INTO sentinel VALUES ('preserve-saved-work');";
    await command.ExecuteNonQueryAsync();
}
await File.WriteAllTextAsync(Path.Combine(data, "settings.json"), "{\"sentinel\":\"preserve-settings\"}");
var before = Directory.GetFiles(data).ToDictionary(p => Path.GetFileName(p)!, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
var backupPath = Path.Combine(root, "backup-before-update.zip");
await new WorkspaceBackupService(new FixturePaths(install, data, database)).ExportAsync(backupPath);
using (var zip = ZipFile.OpenRead(backupPath))
    if (zip.GetEntry("techhub.db") is null || zip.GetEntry("settings/settings.json") is null) throw new Exception("Backup incomplete.");
var parentStart = new ProcessStartInfo(Path.Combine(install, "TechHub.App.exe")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = install };
parentStart.ArgumentList.Add("--parent");
using var parent = Process.Start(parentStart)!;
await WaitForFile(Path.Combine(install, "parent-ready"));
var installer = Path.Combine(root, "TechHub-User-Setup-1.3.0.exe");
var job = new UpdateJob(parent.Id, parent.StartTime.ToUniversalTime().Ticks, install, UpdateInstallMode.User,
    "1.2.1", "1.3.0", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installer))), new FileInfo(installer).Length);
await File.WriteAllTextAsync(Path.Combine(root, "apply-authorized"), "Fixture consent");
using var runtime = new FixtureRuntime();
var applying = new UpdateApplyEngine(runtime).ApplyAsync(job, root);
await Task.Delay(400);
if (applying.IsCompleted || runtime.ReadInstalledVersion(job) != "1.2.1") throw new Exception("Updater did not wait for the real parent.");
await File.WriteAllTextAsync(Path.Combine(install, "release-parent"), "exit normally");
var result = await applying.WaitAsync(TimeSpan.FromMinutes(2));
await WaitForFile(Path.Combine(install, "restart.json"));
using var restart = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(install, "restart.json")));
if (restart.RootElement.GetProperty("Version").GetString() != "1.3.0" || restart.RootElement.GetProperty("Elevated").GetBoolean()
    || restart.RootElement.GetProperty("User").GetString() != identity.User?.Value) throw new Exception("Restart version/user/integrity mismatch.");
foreach (var (name, hash) in before)
    if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(data, name!)))) != hash) throw new Exception("Data changed: " + name);
var evidence = new { Result = result.Success, InstalledVersion = runtime.ReadInstalledVersion(job), ParentExited = parent.HasExited,
    DataPreserved = true, BackupVerified = true, SameUserRestart = true, Elevated = false,
    TrustBoundary = "Fixture substitutes GitHub lookup and host entrypoint policy; WPF handoff and production trust need separate evidence." };
await File.WriteAllTextAsync(Path.Combine(root, "evidence.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(evidence));

static async Task WaitForFile(string path)
{
    var deadline = DateTime.UtcNow.AddSeconds(25);
    while (!File.Exists(path)) { if (DateTime.UtcNow > deadline) throw new TimeoutException(path); await Task.Delay(100); }
}

sealed class FixtureRuntime : IUpdateApplyRuntime, IDisposable
{
    private readonly WindowsUpdateRuntime real = new();
    public Task WaitForParentAsync(UpdateJob job, CancellationToken token) => real.WaitForParentAsync(job, token);
    public Task VerifyReleaseAsync(UpdateJob job, CancellationToken token) => Task.CompletedTask;
    public void AssertNoOtherInstances() => real.AssertNoOtherInstances();
    public Task<int> InstallAsync(UpdateJob job, string directory, CancellationToken token) => real.InstallAsync(job, directory, token);
    public string ReadInstalledVersion(UpdateJob job) => real.ReadInstalledVersion(job);
    public void Restart(UpdateJob job) => real.Restart(job);
    public void Dispose() => real.Dispose();
}

sealed class FixturePaths(string root, string data, string database) : IApplicationPaths
{
    public ApplicationPaths Current { get; } = new(StorageMode.Installed, root, data, Path.Combine(data, "Logs"), database, Path.Combine(root, "portable.flag"));
}
