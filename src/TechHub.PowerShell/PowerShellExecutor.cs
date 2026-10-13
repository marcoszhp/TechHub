using System.Management.Automation;
using TechHub.Core.Services;

namespace TechHub.PowerShell;

public sealed class PowerShellExecutor : IPowerShellExecutor
{
    public async Task<string> ExecuteAsync(string script, CancellationToken cancellationToken = default)
    {
        using var shell = System.Management.Automation.PowerShell.Create();
        shell.AddScript(script);
        using var registration = cancellationToken.Register(() => shell.Stop());
        var output = await Task.Run(() => shell.Invoke(), cancellationToken).ConfigureAwait(false);
        if (shell.HadErrors)
        {
            var errors = string.Join(Environment.NewLine, shell.Streams.Error.Select(error => error.ToString()));
            throw new InvalidOperationException(errors);
        }

        return string.Join(Environment.NewLine, output.Select(item => item?.ToString()).Where(item => !string.IsNullOrWhiteSpace(item)));
    }
}
