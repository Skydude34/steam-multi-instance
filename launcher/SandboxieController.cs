using System.Diagnostics;

namespace SteamMultiInstance.Launcher;

/// <summary>
/// Тонкая обёртка над Start.exe/SbieIni.exe (Sandboxie-Plus). Каждый
/// инстанс — это отдельный "бокс": свой namespace именованных объектов
/// ядра (мьютексы/пайпы/секции) и отдельная файловая песочница для всего
/// дерева процессов (включая steamservice/steamwebhelper), поэтому Steam
/// не видит "уже запущенный" клиент другого инстанса. Это пришло на смену
/// подходу через DLL-инъекцию и хуки CreateMutexW/FindWindowW в injector/
/// (см. README) — тот ломал UI Steam и не мог развести steamservice,
/// т.к. хукал только один процесс, а не всё дерево.
/// </summary>
public sealed class SandboxieController
{
    public string StartExePath { get; set; } =
        @"C:\Program Files\Sandboxie-Plus\Start.exe";

    public string SbieIniExePath { get; set; } =
        @"C:\Program Files\Sandboxie-Plus\SbieIni.exe";

    public void EnsureBoxExists(string boxName)
    {
        RunAndWait(SbieIniExePath, $"set {boxName} Enabled y");
    }

    public void LaunchInBox(string boxName, string targetExePath, string[]? extraArgs = null)
    {
        EnsureBoxExists(boxName);

        string args = $"/box:{boxName} \"{targetExePath}\"";
        if (extraArgs is { Length: > 0 })
        {
            args += " " + string.Join(' ', extraArgs);
        }

        RunFireAndForget(StartExePath, args);
    }

    public void TerminateBox(string boxName)
    {
        RunAndWait(StartExePath, $"/box:{boxName} /terminate");
    }

    /// <summary>PID'ы всех процессов, которые сейчас работают в данном боксе.</summary>
    public int[] ListPids(string boxName)
    {
        string output = RunAndCapture(StartExePath, $"/box:{boxName} /listpids");
        string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Первая строка — количество процессов, дальше по одному PID на строку.
        return lines.Skip(1)
            .Select(line => int.TryParse(line, out int pid) ? pid : (int?)null)
            .Where(pid => pid.HasValue)
            .Select(pid => pid!.Value)
            .ToArray();
    }

    private static void RunFireAndForget(string exe, string args)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }

    private static void RunAndWait(string exe, string args)
    {
        using Process? process = Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        process?.WaitForExit();
    }

    private static string RunAndCapture(string exe, string args)
    {
        using Process? process = Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        });
        if (process is null) return string.Empty;

        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }
}
