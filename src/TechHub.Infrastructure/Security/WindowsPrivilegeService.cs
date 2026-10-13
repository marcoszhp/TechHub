using System.Security.Principal;
using TechHub.Core.Services;

namespace TechHub.Infrastructure.Security;

public sealed class WindowsPrivilegeService : IPrivilegeService
{
    public bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
