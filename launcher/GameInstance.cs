using System.Diagnostics;

namespace SteamMultiInstance.Launcher;

/// <summary>Один запущенный инстанс (конкретный профиль + процесс + Job Object).</summary>
public sealed class GameInstance : IDisposable
{
    public string InstanceId { get; }
    public string ProfileDirectory { get; }
    public Process Process { get; }
    public NativeJobObject Job { get; }

    public int Pid => Process.HasExited ? -1 : Process.Id;
    public bool IsRunning => !Process.HasExited;

    public GameInstance(string instanceId, string profileDirectory, Process process, NativeJobObject job)
    {
        InstanceId = instanceId;
        ProfileDirectory = profileDirectory;
        Process = process;
        Job = job;
    }

    public void Kill()
    {
        // TerminateJobObject кладёт весь дерево процессов сразу — не нужно
        // отдельно убивать дочерние процессы (например, саму игру, запущенную
        // из-под Steam).
        Job.KillAll();
    }

    public void Dispose()
    {
        Job.Dispose();
        Process.Dispose();
    }
}
