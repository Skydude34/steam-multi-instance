using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SteamMultiInstance.Launcher;

public sealed record StoredAccount(string Login, string EncryptedPasswordBase64);

/// <summary>
/// Список известных аккаунтов (добавленных через импорт logpass.txt) —
/// отдельно от списка "что сейчас запущено" (InstanceManager), чтобы в
/// GUI можно было выбрать аккаунт и запустить его, а не вводить ID
/// руками каждый раз. Пароль шифруется DPAPI (привязан к текущему
/// пользователю Windows) — сам файл никогда не коммитится (см.
/// .gitignore). InstanceId аккаунта — это его логин, как и имя .maFile.
/// </summary>
public sealed class AccountStore
{
    public string AccountsPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamMultiInstance", "accounts.json");

    public List<StoredAccount> LoadAll()
    {
        if (!File.Exists(AccountsPath))
        {
            return new List<StoredAccount>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<StoredAccount>>(File.ReadAllText(AccountsPath))
                   ?? new List<StoredAccount>();
        }
        catch (JsonException)
        {
            return new List<StoredAccount>();
        }
    }

    public bool Has(string login) =>
        LoadAll().Any(a => string.Equals(a.Login, login, StringComparison.OrdinalIgnoreCase));

    public void Upsert(string login, string password)
    {
        var accounts = LoadAll();

        byte[] encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser);
        var record = new StoredAccount(login, Convert.ToBase64String(encrypted));

        int idx = accounts.FindIndex(a => string.Equals(a.Login, login, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            accounts[idx] = record;
        }
        else
        {
            accounts.Add(record);
        }

        SaveAll(accounts);
    }

    public void Remove(string login)
    {
        var accounts = LoadAll();
        accounts.RemoveAll(a => string.Equals(a.Login, login, StringComparison.OrdinalIgnoreCase));
        SaveAll(accounts);
    }

    /// <summary>Импортирует строки "login:password" (по одной на строку) из текстового файла.</summary>
    public int ImportFromLogPassFile(string filePath)
    {
        int imported = 0;
        foreach (string rawLine in File.ReadAllLines(filePath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int sep = line.IndexOf(':');
            if (sep <= 0)
            {
                continue;
            }

            string login = line[..sep].Trim();
            string password = line[(sep + 1)..].Trim();
            if (login.Length == 0 || password.Length == 0)
            {
                continue;
            }

            Upsert(login, password);
            imported++;
        }

        return imported;
    }

    private void SaveAll(List<StoredAccount> accounts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AccountsPath)!);
        File.WriteAllText(AccountsPath, JsonSerializer.Serialize(accounts));
    }
}
