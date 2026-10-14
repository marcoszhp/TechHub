using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TechHub.Core.Models;
using TechHub.Core.Services;
using TechHub.Infrastructure.Workspace;

namespace TechHub.Tests;

public sealed class WorkspaceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "TechHub-workspace-tests-" + Guid.NewGuid().ToString("N"));
    private readonly TestPaths paths;
    private readonly SqliteWorkspaceClientStore store;
    private readonly WorkspaceBackupService backup;

    public WorkspaceTests()
    {
        paths = new TestPaths(Path.Combine(root, "Data"));
        Directory.CreateDirectory(paths.Current.DataDirectory);
        store = new(paths);
        backup = new(paths);
    }

    [Fact]
    public async Task Store_CreatesUpdatesSearchesAndPersistsClientsAndEquipment()
    {
        var client = await store.SaveClientAsync(new(0, "  Ana  ", "  ana@example.test ", " Oficina "));
        Assert.True(client.Id > 0);
        Assert.Equal("Ana", client.Name);
        var equipment = await store.SaveEquipmentAsync(new(0, client.Id, " Notebook ", " ABC-123 ", " RAM "));
        Assert.Equal("ABC-123", equipment.SerialNumber);
        var other = await store.SaveClientAsync(new(0, "Bruno", "", ""));
        Assert.Empty(await store.FindEquipmentAsync(other.Id));
        Assert.Single(await store.FindClientsAsync("EXAMPLE"));
        Assert.Single(await store.FindEquipmentAsync(client.Id, "abc-123"));
        Assert.Empty(await store.FindClientsAsync("' OR 1=1 --"));
        Assert.Empty(await store.FindEquipmentAsync(client.Id, "%"));

        await store.SaveClientAsync(client with { Name = "Ana Maria", Contact = "Novo contato" });
        await store.SaveEquipmentAsync(equipment with { Name = "Desktop", Notes = "SSD" });
        var reopened = new SqliteWorkspaceClientStore(paths);
        Assert.Equal("Ana Maria", Assert.Single(await reopened.FindClientsAsync("Maria")).Name);
        Assert.Equal("SSD", Assert.Single(await reopened.FindEquipmentAsync(client.Id, "desktop")).Notes);
        Assert.Equal(2, (await reopened.FindClientsAsync()).Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Store_RejectsBlankNames(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveClientAsync(new(0, name, "", "")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveEquipmentAsync(new(0, 1, name, "", "")));
    }

    [Fact]
    public async Task Store_RejectsOversizeNegativeIdsMissingRowsAndWrongOwner()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveClientAsync(new(-1, "Ana", "", "")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveClientAsync(new(0, new string('a', 201), "", "")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveClientAsync(new(0, "Ana", new string('a', 501), "")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveClientAsync(new(0, "Ana", "", new string('a', 4001))));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveEquipmentAsync(new(0, 0, "PC", "", "")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveClientAsync(new(999, "Ana", "", "")));
        await Assert.ThrowsAsync<SqliteException>(() => store.SaveEquipmentAsync(new(0, 999, "PC", "", "")));
        var client = await store.SaveClientAsync(new(0, "Ana", "", ""));
        var other = await store.SaveClientAsync(new(0, "Bruno", "", ""));
        var equipment = await store.SaveEquipmentAsync(new(0, client.Id, "PC", "", ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveEquipmentAsync(equipment with { ClientId = other.Id }));
        Assert.Equal(client.Id, Assert.Single(await store.FindEquipmentAsync(client.Id)).ClientId);
    }

    [Fact]
    public async Task Backup_RoundTripsCommittedWalAndJsonWithoutReplacingActiveData()
    {
        var client = await store.SaveClientAsync(new(0, "Ana", "", ""));
        await using var live = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = paths.Current.DatabasePath, Pooling = false }.ToString());
        await live.OpenAsync();
        await using var command = live.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;";
        await command.ExecuteNonQueryAsync();
        await store.SaveEquipmentAsync(new(0, client.Id, "PC no WAL", "123", ""));
        Assert.True(new FileInfo(paths.Current.DatabasePath + "-wal").Length > 0);
        var settings = Path.Combine(paths.Current.DataDirectory, "Automation", "plan.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        await File.WriteAllTextAsync(settings, "{\"enabled\":true}");
        Directory.CreateDirectory(paths.Current.LogsDirectory);
        await File.WriteAllTextAsync(Path.Combine(paths.Current.LogsDirectory, "ignore.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(paths.Current.DataDirectory, "ignore.txt"), "excluded");

        var zip = Path.Combine(root, "backup.zip");
        var exported = await backup.ExportAsync(zip);
        Assert.Equal(1, exported.SettingsFiles);
        Assert.True(exported.Bytes > 0);
        using (var archive = ZipFile.OpenRead(zip))
            Assert.Equal(new[] { "manifest.json", "settings/Automation/plan.json", "techhub.db" },
                archive.Entries.Select(e => e.FullName).OrderBy(n => n).ToArray());

        await store.SaveClientAsync(new(0, "Depois do backup", "", ""));
        var target = EmptyTarget();
        var restored = await backup.RestoreAsync(zip, target);
        Assert.Equal(1, restored.SettingsFiles);
        var copy = new SqliteWorkspaceClientStore(new TestPaths(target));
        Assert.Single(await copy.FindClientsAsync());
        Assert.Equal("PC no WAL", Assert.Single(await copy.FindEquipmentAsync(client.Id)).Name);
        Assert.Equal("{\"enabled\":true}", await File.ReadAllTextAsync(Path.Combine(target, "Automation", "plan.json")));
        Assert.Equal(2, (await store.FindClientsAsync()).Count);
        Assert.Equal(Path.Combine(root, "Data", "techhub.db"), paths.Current.DatabasePath);
    }

    [Fact]
    public async Task Backup_RejectsActiveAncestorAndNonemptyDestinations()
    {
        await store.SaveClientAsync(new(0, "Ana", "", ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            backup.ExportAsync(Path.Combine(paths.Current.DataDirectory, "unsafe.zip")));
        var zip = Path.Combine(root, "backup.zip");
        await backup.ExportAsync(zip);
        await Assert.ThrowsAsync<InvalidOperationException>(() => backup.RestoreAsync(zip, paths.Current.DataDirectory));
        await Assert.ThrowsAsync<InvalidOperationException>(() => backup.RestoreAsync(zip, root));
        var target = EmptyTarget();
        var sentinel = Path.Combine(target, "keep.txt");
        await File.WriteAllTextAsync(sentinel, "keep");
        await Assert.ThrowsAsync<InvalidOperationException>(() => backup.RestoreAsync(zip, target));
        Assert.Equal("keep", await File.ReadAllTextAsync(sentinel));
        Assert.Single(await store.FindClientsAsync());
    }

    [Theory]
    [InlineData("../escaped.json")]
    [InlineData("settings/../../escaped.json")]
    [InlineData("settings/..\\escaped.json")]
    [InlineData("/escaped.json")]
    [InlineData("C:/escaped.json")]
    [InlineData("settings/file.json:stream")]
    [InlineData("settings/CON.json")]
    [InlineData("settings/file.json.")]
    [InlineData("settings/manifest.json")]
    [InlineData("unexpected.exe")]
    public async Task Backup_RejectsUnsafeZipEntriesWithoutPublishing(string entry)
    {
        var target = EmptyTarget();
        var zip = CreateArchive(entry);
        await Assert.ThrowsAsync<InvalidDataException>(() => backup.RestoreAsync(zip, target));
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        Assert.False(File.Exists(Path.Combine(root, "escaped.json")));
        Assert.Empty(Directory.EnumerateDirectories(root, ".techhub-restore-*"));
    }

    [Theory]
    [InlineData("settings/a.json", "settings/A.json", 0)]
    [InlineData("settings/link.json", null, 0xA000)]
    public async Task Backup_RejectsDuplicateOrSymlinkEntries(string entry, string? duplicate, int unixType)
    {
        var zip = CreateArchive(entry, duplicate, unixType);
        var target = EmptyTarget();
        await Assert.ThrowsAsync<InvalidDataException>(() => backup.RestoreAsync(zip, target));
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
    }

    [Fact]
    public async Task Backup_RejectsInvalidManifestAndInvalidDatabase()
    {
        var target = EmptyTarget();
        var badManifest = CreateArchive(null, format: "Other");
        await Assert.ThrowsAsync<InvalidDataException>(() => backup.RestoreAsync(badManifest, target));
        var badDatabase = CreateArchive(null);
        await Assert.ThrowsAsync<SqliteException>(() => backup.RestoreAsync(badDatabase, target));
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        Assert.Empty(Directory.EnumerateDirectories(root, ".techhub-restore-*"));
    }

    [Theory]
    [InlineData(" example.test ", "example.test")]
    [InlineData("192.168.1.10", "192.168.1.10")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    [InlineData("pc-01.local.", "pc-01.local.")]
    public void Host_AcceptsOneHost(string input, string expected) =>
        Assert.Equal(expected, WindowsWorkspaceToolsService.ValidateHost(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-n 100")]
    [InlineData("host & whoami")]
    [InlineData("host;whoami")]
    [InlineData("https://example.test")]
    [InlineData("host\nwhoami")]
    [InlineData("host name")]
    [InlineData("fe80::1%12")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("255.255.255.255")]
    [InlineData("224.0.0.1")]
    [InlineData("ff02::1")]
    public void Host_RejectsOptionsCommandsAndNonUnicast(string? input) =>
        Assert.Throws<ArgumentException>(() => WindowsWorkspaceToolsService.ValidateHost(input));

    [Fact]
    public void Diagnostics_UseFixedArgumentsWithoutStartingProcesses()
    {
        Assert.Throws<ArgumentException>(() => WindowsWorkspaceToolsService.ValidateHost("192.168.1.1", hostnameOnly: true));
        Assert.Throws<ArgumentException>(() => WindowsWorkspaceToolsService.ValidateHost(new string('a', 64) + ".test"));
        Assert.Throws<ArgumentException>(() => WindowsWorkspaceToolsService.ValidateHost(new string('a', 254)));
        var ping = WindowsWorkspaceToolsService.CreateDiagnosticStartInfo(WorkspaceDiagnostic.PingHost, "example.test");
        Assert.Equal(new[] { "-n", "4", "-w", "1000", "example.test" }, ping.ArgumentList.ToArray());
        Assert.False(ping.UseShellExecute);
        Assert.True(ping.RedirectStandardOutput);
        Assert.True(ping.RedirectStandardError);
        var ip = WindowsWorkspaceToolsService.CreateDiagnosticStartInfo(WorkspaceDiagnostic.IpConfiguration, null);
        Assert.Equal(new[] { "/all" }, ip.ArgumentList.ToArray());
        var dns = WindowsWorkspaceToolsService.CreateDiagnosticStartInfo(WorkspaceDiagnostic.NameLookup, "example.test");
        Assert.Equal(new[] { "-timeout=2", "-retry=1", "example.test" }, dns.ArgumentList.ToArray());
        Assert.Throws<ArgumentException>(() => WindowsWorkspaceToolsService.CreateDiagnosticStartInfo((WorkspaceDiagnostic)999, null));
        Assert.Throws<ArgumentException>(() => WindowsWorkspaceToolsService.CreateDiagnosticStartInfo(WorkspaceDiagnostic.PingHost, "host & whoami"));
    }

    [Theory]
    [InlineData("taskmgr", "taskmgr.exe")]
    [InlineData("resmon", "resmon.exe")]
    [InlineData("eventvwr", "mmc.exe")]
    [InlineData("devmgmt", "mmc.exe")]
    [InlineData("services", "mmc.exe")]
    [InlineData("diskmgmt", "mmc.exe")]
    public void Tools_OnlyBuildAllowlistedAbsolutePaths(string id, string executable)
    {
        var info = WindowsWorkspaceToolsService.CreateToolStartInfo(id);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, executable), info.FileName);
        Assert.False(info.UseShellExecute);
        if (executable == "mmc.exe")
            Assert.Equal(Path.Combine(Environment.SystemDirectory, id + ".msc"), Assert.Single(info.ArgumentList));
        Assert.Throws<ArgumentException>(() => WindowsWorkspaceToolsService.CreateToolStartInfo("cmd"));
    }

    private string EmptyTarget()
    {
        var target = Path.Combine(root, "Restored-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        return target;
    }

    private string CreateArchive(string? extra, string? duplicate = null, int unixType = 0, string format = "TechHub.Workspace")
    {
        var zip = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        Write("techhub.db", "not a SQLite database");
        Write("manifest.json", JsonSerializer.Serialize(new
        {
            Format = format, Version = 1, CreatedAt = DateTimeOffset.UtcNow,
            SettingsFiles = extra?.StartsWith("settings/", StringComparison.Ordinal) == true ? 1 : 0
        }));
        if (extra is not null)
        {
            var entry = Write(extra, "{}");
            if (unixType != 0) entry.ExternalAttributes = unixType << 16;
        }
        if (duplicate is not null) Write(duplicate, "{}");
        return zip;

        ZipArchiveEntry Write(string name, string text)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(text);
            return entry;
        }
    }

    public void Dispose()
    {
        // Only this fixture's unique temporary directory is removed.
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed class TestPaths(string data) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = new(StorageMode.Portable, data, data,
            Path.Combine(data, "Logs"), Path.Combine(data, "techhub.db"), Path.Combine(data, "portable.flag"));
    }
}

