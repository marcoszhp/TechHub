using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TechHub.Infrastructure.Updates;

/// <summary>Presence markers, not ownership locks. Authenticated sessions may only observe/hold a handle.</summary>
public sealed class MachineUpdateMarker : IDisposable
{
    public const string Updating = "Global\\TechHub.Update.Machine.InProgress";
    public const string Running = "Global\\TechHub.App.Machine.Running";
    private const uint Synchronize = 0x00100000;
    private readonly SafeWaitHandle handle;
    private MachineUpdateMarker(SafeWaitHandle handle) => this.handle = handle;
    public void Dispose() => handle.Dispose();

    public static MachineUpdateMarker Create(string name, out bool created)
    {
        ValidateName(name);
        // No NULL DACL. AU may only open for SYNCHRONIZE (also what Inno AppMutex uses).
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor("D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;0x00100000;;;AU)", 1, out var descriptor, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
            var marker = CreateMutexEx(ref attributes, name, 0, Synchronize);
            var error = Marshal.GetLastWin32Error();
            if (marker.IsInvalid) { marker.Dispose(); throw new Win32Exception(error, "Não foi possível coordenar a atualização entre sessões do Windows."); }
            created = error != 183;
            return new MachineUpdateMarker(marker);
        }
        finally { LocalFree(descriptor); }
    }

    public static bool Exists(string name)
    {
        ValidateName(name);
        using var marker = OpenMutex(Synchronize, false, name);
        if (!marker.IsInvalid) return true;
        var error = Marshal.GetLastWin32Error();
        if (error == 2) return false;
        throw new Win32Exception(error, "Não foi possível verificar se há outra atualização. Tente novamente após fechar as outras sessões.");
    }

    private static void ValidateName(string name)
    {
        if (name != Updating && name != Running) throw new ArgumentException("Nome de coordenação não permitido.", nameof(name));
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int Inherit; }
    [DllImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string descriptor, uint revision, out IntPtr security, out uint size);
    [DllImport("kernel32.dll", EntryPoint = "CreateMutexExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle CreateMutexEx(ref SecurityAttributes attributes, string name, uint flags, uint access);
    [DllImport("kernel32.dll", EntryPoint = "OpenMutexW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle OpenMutex(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, string name);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
