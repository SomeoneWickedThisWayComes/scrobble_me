// Налаштування застосунку: ключі last.fm API, сесія, автозапуск, пауза скроблінгу.
// Читаються при старті App, змінюються у вікні налаштувань. Файл — %APPDATA%\ScrobbleMe\settings.json.
// Секрети (API secret, ключ сесії) шифруються DPAPI — розшифрувати їх може лише той самий
// користувач Windows. Пароль last.fm не зберігається взагалі. Див. docs/decisions.md, D-5.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScrobbleMe.Services;

public sealed class AppSettings
{
    public string ApiKey { get; set; } = "";
    public string? ApiSecretProtected { get; set; }
    public string? UserName { get; set; }
    public string? SessionKeyProtected { get; set; }
    public bool StartWithWindows { get; set; }
    public bool ScrobblingEnabled { get; set; } = true;

    [JsonIgnore]
    public string ApiSecret
    {
        get => Unprotect(ApiSecretProtected);
        set => ApiSecretProtected = Protect(value);
    }

    [JsonIgnore]
    public string SessionKey
    {
        get => Unprotect(SessionKeyProtected);
        set => SessionKeyProtected = Protect(value);
    }

    // Додаткова «сіль» для DPAPI, щоб інші програми не розшифрували ці дані випадково.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ScrobbleMe.v1");

    private static string? Protect(string value) =>
        string.IsNullOrEmpty(value)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));

    private static string Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return "";
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Файл скопійовано з іншого ПК/користувача — треба просто увійти заново.
            return "";
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.Settings))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.Settings)) ?? new();
        }
        catch (Exception ex)
        {
            Log.Error("читання налаштувань", ex);
        }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        var tmp = AppPaths.Settings + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, AppPaths.Settings, overwrite: true);
    }
}
