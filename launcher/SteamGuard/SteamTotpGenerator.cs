using System.Security.Cryptography;

namespace SteamMultiInstance.Launcher.SteamGuard;

/// <summary>
/// Генератор Steam Guard кодов из shared_secret (.maFile) — та же схема,
/// что в мобильном приложении Steam и в Steam Desktop Authenticator:
/// стандартный TOTP (HMAC-SHA1, окно 30 секунд) с заменой алфавита вывода
/// на набор символов, который использует Steam ("23456789BCDFGHJKMNPQRTVWXY" —
/// без визуально похожих 0/O/1/I и т.п.).
/// </summary>
public static class SteamTotpGenerator
{
    private const string SteamGuardAlphabet = "23456789BCDFGHJKMNPQRTVWXY";
    private const int CodeLength = 5;
    private const int TimeStepSeconds = 30;

    /// <summary>Текущий 5-символьный Steam Guard код для данного shared_secret (base64).</summary>
    public static string GenerateCode(string sharedSecretBase64, DateTimeOffset? now = null)
    {
        byte[] secret = Convert.FromBase64String(sharedSecretBase64);
        long timeStep = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / TimeStepSeconds;

        byte[] counter = BitConverter.GetBytes(timeStep);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counter); // TOTP требует big-endian 8-байтовый счётчик
        }

        byte[] hash;
        using (var hmac = new HMACSHA1(secret))
        {
            hash = hmac.ComputeHash(counter);
        }

        // Динамическая отсечка (RFC 4226), как в обычном TOTP.
        int offset = hash[^1] & 0x0F;
        int truncatedHash =
            ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF);

        var code = new char[CodeLength];
        for (int i = 0; i < CodeLength; i++)
        {
            code[i] = SteamGuardAlphabet[truncatedHash % SteamGuardAlphabet.Length];
            truncatedHash /= SteamGuardAlphabet.Length;
        }

        return new string(code);
    }

    /// <summary>Сколько секунд осталось до смены текущего кода — удобно для UI (прогресс-бар/таймер).</summary>
    public static int SecondsUntilNextCode(DateTimeOffset? now = null)
    {
        long unix = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        return TimeStepSeconds - (int)(unix % TimeStepSeconds);
    }
}
