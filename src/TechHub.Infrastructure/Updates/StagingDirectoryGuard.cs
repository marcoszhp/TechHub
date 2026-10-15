using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TechHub.Infrastructure.Updates;

// Pin ancestors from the volume root down. Excluding FILE_SHARE_DELETE prevents
// replacing an inspected directory with a junction while writing/moving/cleaning.
public sealed class StagingDirectoryGuard : IDisposable
{
    private readonly List<SafeFileHandle> _handles = [];

    public static StagingDirectoryGuard Acquire(string path)
    {
        var guard = new StagingDirectoryGuard();
        try
        {
            var ancestors = new Stack<string>();
            for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
                ancestors.Push(current);
            foreach (var directory in ancestors)
            {
                var handle = CreateFile(directory, 0x80, 0x1 | 0x2, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    handle.Dispose();
                    if (error is 2 or 3) break; // Remaining descendants do not exist yet.
                    throw new IOException("Não foi possível proteger a pasta de atualização.", new Win32Exception(error));
                }
                guard._handles.Add(handle);
                if (!GetFileInformationByHandleEx(handle, 9, out var info, 8))
                    throw new IOException("Não foi possível verificar a pasta de atualização.", new Win32Exception(Marshal.GetLastWin32Error()));
                if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 || (info.Attributes & (uint)FileAttributes.Directory) == 0)
                    throw new IOException("A pasta de atualização contém um redirecionamento de arquivos não permitido.");
            }
            return guard;
        }
        catch { guard.Dispose(); throw; }
    }

    public void Dispose()
    {
        for (var i = _handles.Count - 1; i >= 0; i--) _handles[i].Dispose();
        _handles.Clear();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTagInfo { public uint Attributes; public uint ReparseTag; }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out AttributeTagInfo info, uint size);
}
