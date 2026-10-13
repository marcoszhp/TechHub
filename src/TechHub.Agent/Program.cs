using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using TechHub.Core.Services;
using TechHub.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTechHubInfrastructure();
builder.Services.AddSingleton<IPowerShellExecutor, TechHub.PowerShell.PowerShellExecutor>();
var app = builder.Build();
var token = Environment.GetEnvironmentVariable("TECHHUB_AGENT_TOKEN");
if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Defina TECHHUB_AGENT_TOKEN antes de iniciar o agente.");

app.MapGet("/health", () => Results.Ok(new { service = "TechHub.Agent", status = "ok", utc = DateTimeOffset.UtcNow }));
app.MapGet("/api/v1/inventory", async (HttpRequest request, IDashboardService dashboard, IPrinterService printers, CancellationToken cancellationToken) =>
{
    if (!TokenMatches(request, token)) return Results.Unauthorized();
    var snapshot = await dashboard.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
    var printerInventory = await printers.GetInventoryAsync(cancellationToken).ConfigureAwait(false);
    return Results.Ok(new { collectedAt = DateTimeOffset.UtcNow, dashboard = snapshot, printers = printerInventory });
});
app.MapPost("/api/v1/actions/preview", (HttpRequest request, ActionRequestDto dto, IActionRunner runner) =>
{
    if (!TokenMatches(request, token)) return Results.Unauthorized();
    return Results.BadRequest(new { error = "Somente inventário seguro está habilitado pelo agente nesta fase." });
});
await app.RunAsync();

static bool TokenMatches(HttpRequest request, string expected)
{
    if (!request.Headers.TryGetValue("X-TechHub-Token", out var supplied)) return false;
    var left = Encoding.UTF8.GetBytes(supplied.ToString());
    var right = Encoding.UTF8.GetBytes(expected);
    return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
}

sealed record ActionRequestDto(string? ActionId);
