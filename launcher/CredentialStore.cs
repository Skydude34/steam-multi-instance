using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SteamMultiInstance.Launcher;

/// <summary>
/// Локальное хранилище логин/пароль на инстанс. Пароль шифруется через
/// Windows DPAPI (ProtectedData) с привязкой к текущему пользователю Windows —
/// расшифровать файл может только та же учётка на той же машине, это не
/// просто текст на диске. Логины/пароли НИКОГДА не уходят в чат/лог — только
/// хранятся локально и читаются самим приложением для автологина.
///
/// Файлы лежат вне рабочей копии репозитория (см. .gitignore: credentials/).
/// </summary>
public sealed class CredentialStore
{
    private sealed record StoredRecord(string Login, string EncryptedPasswordBase64);

    public string CredentialsDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamMultiInstance", "credentials");

    public void Save(string instanceId, string login, string password)
    {
        Directory.CreateDirectory(CredentialsDirectory);

        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[] encrypted = ProtectedData.Protect(passwordBytes, null, DataProtectionScope.CurrentUser);

        var record = new StoredRecord(login, Convert.ToBase64String(encrypted));
        string path = CredentialPath(instanceId);
        File.WriteAllText(path, JsonSerializer.Serialize(record));
    }

    public bool TryLoad(string instanceId, out string login, out string password)
    {
        login = string.Empty;
        password = string.Empty;

        string path = CredentialPath(instanceId);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var record = JsonSerializer.Deserialize<StoredRecord>(File.ReadAllText(path));
            if (record is null)
            {
                return false;
            }

            byte[] encrypted = Convert.FromBase64String(record.EncryptedPasswordBase64);
            byte[] decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);

            login = record.Login;
            password = Encoding.UTF8.GetString(decrypted);
            return true;
        }
        catch
        {
            // Повреждённый файл или DPAPI не смог расшифровать (например,
            // файл скопирован на другую машину/учётку) — считаем, что
            // учётных данных нет, не кидаем исключение наружу.
            return false;
        }
    }

    public bool Has(string instanceId) => File.Exists(CredentialPath(instanceId));

    public void Delete(string instanceId)
    {
        string path = CredentialPath(instanceId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string CredentialPath(string instanceId) =>
        Path.Combine(CredentialsDirectory, $"{instanceId}.cred");
}
