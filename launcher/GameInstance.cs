namespace SteamMultiInstance.Launcher;

/// <summary>Один запущенный инстанс — это один Sandboxie-бокс с именем InstanceId.</summary>
public sealed class GameInstance
{
    private readonly SandboxieController _sandbox;

    public string InstanceId { get; }
    public string BoxName => InstanceId;

    /// <summary>
    /// Файловая песочница Sandboxie для этого бокса — стандартное
    /// расположение по умолчанию (C:\Sandbox\&lt;пользователь&gt;\&lt;бокс&gt;).
    /// Может отличаться, если в Sandboxie настроен нестандартный корень.
    /// </summary>
    public string ProfileDirectory { get; }

    public GameInstance(string instanceId, string profileDirectory, SandboxieController sandbox)
    {
        InstanceId = instanceId;
        ProfileDirectory = profileDirectory;
        _sandbox = sandbox;
    }

    /// <summary>PID первого найденного процесса в боксе, либо -1, если бокс пуст.</summary>
    public int Pid
    {
        get
        {
            int[] pids = _sandbox.ListPids(BoxName);
            return pids.Length > 0 ? pids[0] : -1;
        }
    }

    public bool IsRunning => _sandbox.ListPids(BoxName).Length > 0;

    /// <summary>Завершает все процессы в боксе этого инстанса разом.</summary>
    public void Kill() => _sandbox.TerminateBox(BoxName);
}
