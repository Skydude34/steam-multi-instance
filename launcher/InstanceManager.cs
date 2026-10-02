using System.Diagnostics;
using System.IO;

namespace SteamMultiInstance.Launcher;

/// <summary>
/// Запускает инстансы через native-лончер (injector/smi_launcher.exe), который
/// сам поднимает процесс suspended, инжектирует injector_hooks.dll и резюмирует.
/// Эта обёртка отвечает за профиль (отдельная папка данных на инстанс) и за
/// Job Object, в который попадает всё дерево процессов.
/// </summary>
public sealed class InstanceManager
{
    private readonly Dictionary<string, GameInstance> _instances = new();

    /// <summary>Путь к smi_launcher.exe — собирается из CMake-проекта в injector/build.</summary>
    public string NativeLauncherPath { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "smi_launcher.exe");

    /// <summary>Корневая папка для профилей инстансов (отдельные Steam userdata/конфиги).</summary>
    public string ProfilesRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamMultiInstance", "profiles");

    public IReadOnlyCollection<GameInstance> Instances => _instances.Values;

    /// <summary>
    /// Запускает новый инстанс targetExePath под именем instanceId.
    /// ВАЖНО: это пока каркас — профиль steam-клиента (копия/junction папки
    /// Steam с отдельным loginusers.vdf) нужно готовить отдельно, здесь лишь
    /// создаётся папка под данные инстанса и пробрасывается переменная
    /// SMI_PROFILE_DIR, которую может использовать сама цель или обёртка над
    /// Steam (см. README, раздел "Разведение профилей").
    /// </summary>
    public GameInstance Launch(string instanceId, string targetExePath, string[]? extraArgs = null)
    {
        if (_instances.ContainsKey(instanceId))
        {
            throw new InvalidOperationException($"Инстанс \"{instanceId}\" уже запущен.");
        }

        if (!File.Exists(NativeLauncherPath))
        {
            throw new FileNotFoundException(
                "smi_launcher.exe не найден — сначала собери injector/ (см. README).",
                NativeLauncherPath);
        }

        string profileDir = Path.Combine(ProfilesRoot, instanceId);
        Directory.CreateDirectory(profileDir);

        var startInfo = new ProcessStartInfo
        {
            FileName = NativeLauncherPath,
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        startInfo.ArgumentList.Add(instanceId);
        startInfo.ArgumentList.Add(targetExePath);
        foreach (var arg in extraArgs ?? Array.Empty<string>())
        {
            startInfo.ArgumentList.Add(arg);
        }
        startInfo.EnvironmentVariables["SMI_INSTANCE_ID"] = instanceId;
        startInfo.EnvironmentVariables["SMI_PROFILE_DIR"] = profileDir;

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Process.Start вернул null.");

        var job = new NativeJobObject($"SMI_{instanceId}_{process.Id}");
        job.Assign(process.Handle);

        var instance = new GameInstance(instanceId, profileDir, process, job);
        _instances[instanceId] = instance;
        return instance;
    }

    public void Kill(string instanceId)
    {
        if (_instances.TryGetValue(instanceId, out var instance))
        {
            instance.Kill();
            instance.Dispose();
            _instances.Remove(instanceId);
        }
    }

    public void KillAll()
    {
        foreach (var instance in _instances.Values)
        {
            instance.Kill();
            instance.Dispose();
        }
        _instances.Clear();
    }
}
