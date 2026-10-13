using System.IO;
using System.Text.Json;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Network;

public sealed class NetworkConsentStore(IApplicationPaths applicationPaths) : INetworkConsentStore
{
    private string ConsentPath => Path.Combine(applicationPaths.Current.DataDirectory, "network-consent.json");

    public async Task<bool> HasConsentAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ConsentPath)) return false;
        await using var stream = File.OpenRead(ConsentPath);
        var document = await JsonSerializer.DeserializeAsync<ConsentDocument>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return document?.Granted == true;
    }

    public async Task GrantConsentAsync(CancellationToken cancellationToken = default)
    {
        var document = new ConsentDocument(true, DateTimeOffset.UtcNow);
        await using var stream = File.Create(ConsentPath);
        await JsonSerializer.SerializeAsync(stream, document, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private sealed record ConsentDocument(bool Granted, DateTimeOffset GrantedAt);
}
