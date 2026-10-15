using System.Reflection;
using System.Security.Principal;
using System.Text.Json;

if (args is ["--parent"])
{
    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "parent-ready"), "ready");
    var deadline = DateTime.UtcNow.AddSeconds(45);
    while (!File.Exists(Path.Combine(AppContext.BaseDirectory, "release-parent")) && DateTime.UtcNow < deadline)
        await Task.Delay(100);
    return;
}
using var identity = WindowsIdentity.GetCurrent();
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "restart.json"), JsonSerializer.Serialize(new
{
    Version = Assembly.GetExecutingAssembly().GetName().Version!.ToString(3),
    User = identity.User?.Value,
    Elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)
}));
