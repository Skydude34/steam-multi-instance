using System.IO;

namespace SteamMultiInstance.Launcher;

/// <summary>
/// Запускает инстансы через Sandboxie-Plus: каждый инстанс — отдельный
/// "бокс" (изолированный namespace ядра + файловая песочница для всего
/// дерева процессов), поэтому Steam не видит "уже запущенный" клиент
/// другого инстанса и каждый получает свой чистый профиль автоматически —
/// без DLL-инъекций и без хрупких хуков CreateMutexW/FindWindowW, которые
/// использовались раньше (см. injector/, помечен как deprecated).
/// </summary>
public sealed class InstanceManager
{
    private readonly Dictionary<string, GameInstance> _instances = new();
    private readonly SandboxieController _sandbox = new();

    public string StartExePath
    {
        get => _sandbox.StartExePath;
        set => _sandbox.StartExePath = value;
    }

    public string SbieIniExePath
    {
        get => _sandbox.SbieIniExePath;
        set => _sandbox.SbieIniExePath = value;
    }

    public IReadOnlyCollection<GameInstance> Instances => _instances.Values;

    /// <summary>
    /// Запускает новый инстанс targetExePath под именем instanceId — создаёт
    /// (если ещё нет) Sandboxie-бокс с таким именем и запускает процесс в нём.
    /// </summary>
    public GameInstance Launch(string instanceId, string targetExePath, string[]? extraArgs = null)
    {
        if (_instances.ContainsKey(instanceId))
        {
            throw new InvalidOperationException($"Инстанс \"{instanceId}\" уже запущен.");
        }

        if (!File.Exists(_sandbox.StartExePath))
        {
            throw new FileNotFoundException(
                "Start.exe (Sandboxie-Plus) не найден — установи Sandboxie-Plus (см. README).",
                _sandbox.StartExePath);
        }

        _sandbox.LaunchInBox(instanceId, targetExePath, extraArgs);

        string profileDir = Path.Combine(@"C:\Sandbox", Environment.UserName, instanceId);

        var instance = new GameInstance(instanceId, profileDir, _sandbox);
        _instances[instanceId] = instance;
        return instance;
    }

    public void Kill(string instanceId)
    {
        if (_instances.TryGetValue(instanceId, out var instance))
        {
            instance.Kill();
            _instances.Remove(instanceId);
        }
    }

    public void KillAll()
    {
        foreach (var instance in _instances.Values)
        {
            instance.Kill();
        }
        _instances.Clear();
    }
}
