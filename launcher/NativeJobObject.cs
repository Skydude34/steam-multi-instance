using System.Runtime.InteropServices;

namespace SteamMultiInstance.Launcher;

/// <summary>
/// Тонкая обёртка над Win32 Job Object. Один Job Object на инстанс: все
/// процессы, которые инстанс порождает (сам .exe + его дети), висят в одной
/// группе, и при закрытии хендла job'а (или явном Kill) всё дерево
/// завершается одним вызовом — не нужно обходить процессы вручную.
/// </summary>
public sealed class NativeJobObject : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    private readonly IntPtr _handle;

    public NativeJobObject(string name)
    {
        _handle = CreateJobObjectW(IntPtr.Zero, name);
        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"CreateJobObjectW failed: {Marshal.GetLastWin32Error()}");
        }

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
            },
        };
        uint size = (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        if (!SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, ref info, size))
        {
            throw new InvalidOperationException(
                $"SetInformationJobObject failed: {Marshal.GetLastWin32Error()}");
        }
    }

    public void Assign(IntPtr processHandle)
    {
        if (!AssignProcessToJobObject(_handle, processHandle))
        {
            throw new InvalidOperationException(
                $"AssignProcessToJobObject failed: {Marshal.GetLastWin32Error()}");
        }
    }

    /// <summary>Завершает все процессы, приписанные к этому job'у.</summary>
    public void KillAll()
    {
        TerminateJobObject(_handle, 1);
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            CloseHandle(_handle);
        }
    }
}
