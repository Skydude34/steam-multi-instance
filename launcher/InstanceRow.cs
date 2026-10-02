namespace SteamMultiInstance.Launcher;

/// <summary>View-модель одной строки списка инстансов (ListView в MainWindow читает её через binding).</summary>
public sealed class InstanceRow
{
    public required string InstanceId { get; init; }
    public required int Pid { get; init; }
    public required bool IsRunning { get; init; }
    public required string ProfileDirectory { get; init; }
    public string SteamGuardCode { get; init; } = "—";
    public bool HasPassword { get; init; }
    public bool HasMaFile { get; init; }

    public string StatusText => IsRunning ? "работает" : "не запущен";
    public string PasswordStatus => HasPassword ? "есть" : "—";
    public string MaFileStatus => HasMaFile ? "есть" : "—";
}
