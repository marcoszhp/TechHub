using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TechHub.Core.Models;
using TechHub.Core.Services;
using TechHub.Infrastructure;

var arguments = args.ToList();
if (arguments.Count > 0 && arguments[0].Equals("--cli", StringComparison.OrdinalIgnoreCase)) arguments.RemoveAt(0);
if (arguments.Count == 0 || arguments[0] is "help" or "--help" or "-h")
{
    PrintHelp();
    return 0;
}

using var host = Host.CreateDefaultBuilder(args).ConfigureLogging(logging => logging.ClearProviders()).ConfigureServices(services =>
{
    services.AddTechHubInfrastructure();
    services.AddSingleton<IPowerShellExecutor, TechHub.PowerShell.PowerShellExecutor>();
}).Build();
await host.StartAsync();
var services = host.Services;
try
{
    switch (arguments[0].ToLowerInvariant())
    {
        case "inventory":
            await InventoryAsync(services);
            break;
        case "clean":
            await CleanAsync(services, arguments.Skip(1).ToArray());
            break;
        case "diagnose":
            await DiagnoseAsync(services, string.Join(' ', arguments.Skip(1)));
            break;
        case "subnet":
            Subnet(services, arguments.ElementAtOrDefault(1) ?? "");
            break;
        case "ticket":
            await TicketAsync(services, arguments.Skip(1).ToArray());
            break;
        case "automation":
            await AutomationAsync(services, arguments.Skip(1).ToArray());
            break;
        case "software":
            await SoftwareAsync(services);
            break;
        default:
            Console.Error.WriteLine($"Comando desconhecido: {arguments[0]}");
            PrintHelp();
            return 2;
    }
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { error = exception.Message }));
    return 1;
}
finally { await host.StopAsync(TimeSpan.FromSeconds(3)); }

static async Task InventoryAsync(IServiceProvider services)
{
    var dashboard = await services.GetRequiredService<IDashboardService>().GetSnapshotAsync();
    var drivers = await services.GetRequiredService<IDriverService>().GetInventoryAsync();
    var software = await services.GetRequiredService<ISoftwareService>().GetInventoryAsync();
    Console.WriteLine(JsonSerializer.Serialize(new { dashboard, drivers, software }, new JsonSerializerOptions { WriteIndented = true }));
}

static async Task CleanAsync(IServiceProvider services, string[] args)
{
    var deep = args.Any(arg => arg.Equals("--deep", StringComparison.OrdinalIgnoreCase));
    var confirm = args.Any(arg => arg.Equals("--confirm", StringComparison.OrdinalIgnoreCase));
    var service = services.GetRequiredService<ICleaningService>();
    var preview = await service.AnalyzeAsync(new CleaningOptions(deep ? CleaningMode.Deep : CleaningMode.Safe,
        deep ? [CleaningScope.UserTemp, CleaningScope.SystemTemp, CleaningScope.LogsAndDumps] : [CleaningScope.UserTemp]));
    if (!confirm)
    {
        Console.WriteLine(JsonSerializer.Serialize(preview, new JsonSerializerOptions { WriteIndented = true }));
        return;
    }
    var result = await service.ExecuteAsync(preview, new ActionExecutionContext(true, true, Environment.UserName));
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
}

static async Task DiagnoseAsync(IServiceProvider services, string symptom)
{
    if (string.IsNullOrWhiteSpace(symptom)) throw new ArgumentException("Informe o sintoma.");
    var answer = await services.GetRequiredService<IDiagnosticAssistant>().AskAsync(symptom);
    Console.WriteLine(JsonSerializer.Serialize(answer, new JsonSerializerOptions { WriteIndented = true }));
}

static void Subnet(IServiceProvider services, string cidr)
{
    if (string.IsNullOrWhiteSpace(cidr)) throw new ArgumentException("Informe uma rede IPv4, por exemplo 192.168.1.0/24.");
    Console.WriteLine(JsonSerializer.Serialize(services.GetRequiredService<ISubnetCalculator>().Calculate(cidr), new JsonSerializerOptions { WriteIndented = true }));
}

static async Task TicketAsync(IServiceProvider services, string[] args)
{
    var title = Option(args, "--title") ?? throw new ArgumentException("Use --title e --description.");
    var description = Option(args, "--description") ?? throw new ArgumentException("Use --title e --description.");
    var ticket = await services.GetRequiredService<ITicketService>().CreateAsync(new CreateTicketRequest(title, description, TicketCategory.Outro, TicketPriority.Normal, Environment.UserName));
    Console.WriteLine(JsonSerializer.Serialize(ticket, new JsonSerializerOptions { WriteIndented = true }));
}

static async Task AutomationAsync(IServiceProvider services, string[] args)
{
    var service = services.GetRequiredService<IAutomationService>();
    var action = args.ElementAtOrDefault(0) ?? "list";
    if (action.Equals("list", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine(JsonSerializer.Serialize(await service.GetPlaybooksAsync(), new JsonSerializerOptions { WriteIndented = true }));
        return;
    }
    if (!action.Equals("run", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Use automation list ou automation run <id> [--confirm].");
    var id = args.ElementAtOrDefault(1) ?? throw new ArgumentException("Informe o id do playbook.");
    var result = await service.RunAsync(id, args.Contains("--confirm", StringComparer.OrdinalIgnoreCase), Environment.UserName);
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
}

static async Task SoftwareAsync(IServiceProvider services)
{
    var result = await services.GetRequiredService<ISoftwareService>().GetUpgradeSummaryAsync();
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
}

static string? Option(string[] args, string name)
{
    var index = Array.FindIndex(args, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static void PrintHelp()
{
    Console.WriteLine("TechHub CLI");
    Console.WriteLine("  techhub --cli inventory");
    Console.WriteLine("  techhub --cli clean --safe [--confirm]");
    Console.WriteLine("  techhub --cli clean --deep [--confirm]");
    Console.WriteLine("  techhub --cli diagnose \"sem internet\"");
    Console.WriteLine("  techhub --cli subnet 192.168.1.0/24");
    Console.WriteLine("  techhub --cli ticket --title T --description D");
    Console.WriteLine("  techhub --cli automation list|run <id> [--confirm]");
    Console.WriteLine("  techhub --cli software");
}
