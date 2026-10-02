using System.Text.Json.Serialization;

namespace SteamMultiInstance.Launcher.SteamGuard;

/// <summary>
/// Модель .maFile — файла Steam Desktop Authenticator / мобильного
/// аутентификатора. Содержит секреты уровня пароля (shared_secret даёт
/// генерировать Steam Guard коды, identity_secret — подтверждать трейды).
/// Храни эти файлы вне репозитория (см. launcher/SteamGuard/README.md) и
/// никогда не коммить их — это эквивалент пароля + доступа к 2FA аккаунта.
/// </summary>
public sealed class MobileAuthFile
{
    [JsonPropertyName("shared_secret")]
    public string SharedSecret { get; set; } = string.Empty;

    [JsonPropertyName("identity_secret")]
    public string IdentitySecret { get; set; } = string.Empty;

    [JsonPropertyName("account_name")]
    public string AccountName { get; set; } = string.Empty;

    [JsonPropertyName("Session")]
    public SessionData? Session { get; set; }

    public sealed class SessionData
    {
        // Реальные .maFile (Steam Desktop Authenticator) отдают SteamID как
        // JSON-строку, а не число — отсюда AllowReadingFromString.
        [JsonPropertyName("SteamID")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long SteamId { get; set; }
    }
}
