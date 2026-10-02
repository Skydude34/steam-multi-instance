using System.IO;
using System.Text.Json;

namespace SteamMultiInstance.Launcher.SteamGuard;

/// <summary>
/// Читает .maFile для конкретного инстанса из отдельной (не в репозитории!)
/// папки. Ожидаемое имя файла: "&lt;instanceId&gt;.maFile", например
/// "alice.maFile" для инстанса "alice".
/// </summary>
public sealed class MobileAuthStore
{
    /// <summary>
    /// По умолчанию — %LOCALAPPDATA%\SteamMultiInstance\mafiles, вне папки
    /// репозитория, чтобы .maFile не попал в git по ошибке. Можно переопределить.
    /// </summary>
    public string MaFilesDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamMultiInstance", "mafiles");

    public bool TryLoad(string instanceId, out MobileAuthFile? authFile)
    {
        authFile = null;
        string path = Path.Combine(MaFilesDirectory, $"{instanceId}.maFile");
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(path);
            authFile = JsonSerializer.Deserialize<MobileAuthFile>(json);
            return authFile is not null && !string.IsNullOrEmpty(authFile.SharedSecret);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Текущий Steam Guard код для инстанса, либо null, если .maFile не найден/битый.</summary>
    public string? GetCurrentCode(string instanceId)
    {
        if (!TryLoad(instanceId, out var authFile) || authFile is null)
        {
            return null;
        }
        return SteamTotpGenerator.GenerateCode(authFile.SharedSecret);
    }
}
