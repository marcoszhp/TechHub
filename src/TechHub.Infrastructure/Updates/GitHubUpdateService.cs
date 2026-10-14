using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Updates;

public sealed class GitHubUpdateService : IUpdateService
{
    private const string Repository = "https://api.github.com/repos/marcoszhp/TechHub/releases/latest";
    private const string R2BaseUrl = "https://pub-0a9a35c748ee4501b97e4d3730e295bc.r2.dev/releases";
    private readonly HttpClient _httpClient = CreateClient();

    public async Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken = default)
    {
        var current = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
        try
        {
            using var response = await _httpClient.GetAsync(Repository, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = json.RootElement;
            var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v') ?? current;
            var releaseUrl = root.GetProperty("html_url").GetString() ?? "https://github.com/marcoszhp/TechHub/releases";
            var downloadUrl = Version.TryParse(tag, out var latest) && Version.TryParse(current, out var installed) && latest > installed
                ? $"{R2BaseUrl}/{tag}/TechHub-User-Setup-{tag}.exe"
                : null;
            var available = downloadUrl is not null;
            return new UpdateInfo(available, current, tag, releaseUrl, downloadUrl,
                available ? $"Nova versão disponível: {tag}." : $"Você está usando a versão {current}, a mais recente.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            return new UpdateInfo(false, current, current, "https://github.com/marcoszhp/TechHub/releases", null,
                $"Não foi possível verificar atualizações: {exception.Message}");
        }
    }

    public async Task<string> DownloadAndLaunchAsync(UpdateInfo update, CancellationToken cancellationToken = default)
    {
        if (!update.IsAvailable || string.IsNullOrWhiteSpace(update.DownloadUrl))
            throw new InvalidOperationException("Nenhuma atualização disponível para instalar.");
        var downloadUrl = update.DownloadUrl;

        var directory = Path.Combine(Path.GetTempPath(), "TechHub", "Updates");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, $"TechHub-User-Setup-{update.LatestVersion}.exe");
        await using (var source = await _httpClient.GetStreamAsync(downloadUrl, cancellationToken).ConfigureAwait(false))
        await using (var destination = File.Create(file))
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);

        Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
        return file;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TechHub", "1.1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
