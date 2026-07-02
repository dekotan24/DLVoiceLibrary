using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DLVoiceLibrary.Services;

/// <summary>
/// Webメディアサーバの設定。%LOCALAPPDATA%\DLVoiceLibrary\webserver.json に保存する。
/// パスワードは SHA-256 + ソルトのハッシュのみ保持する (照合専用、平文復元は不可)。
/// </summary>
public class WebServerSettings
{
    public int Port { get; set; } = 7870;

    /// <summary>true = LAN内の他デバイスからのアクセスを許可 (0.0.0.0)、false = このPCのみ (127.0.0.1)</summary>
    public bool BindAll { get; set; } = true;

    /// <summary>アプリ起動時にサーバを自動開始する</summary>
    public bool AutoStart { get; set; }

    public string Username { get; set; } = "admin";

    /// <summary>SHA-256(salt + password) の16進。空なら認証なし</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public string PasswordSalt { get; set; } = string.Empty;

    public bool HasPassword => !string.IsNullOrEmpty(PasswordHash);

    public void SetPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            PasswordHash = string.Empty;
            PasswordSalt = string.Empty;
            return;
        }
        PasswordSalt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        PasswordHash = ComputeHash(PasswordSalt, password);
    }

    public bool VerifyPassword(string password)
    {
        if (!HasPassword) return true;
        var hash = ComputeHash(PasswordSalt, password);
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(hash), Convert.FromHexString(PasswordHash));
    }

    private static string ComputeHash(string salt, string password)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(salt + password)));

    public static string SettingsPath => Path.Combine(App.AppDataRoot, "webserver.json");

    public static WebServerSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return JsonSerializer.Deserialize<WebServerSettings>(File.ReadAllText(SettingsPath))
                       ?? new WebServerSettings();
            }
        }
        catch
        {
            // 壊れた設定ファイルはデフォルトで上書き起動できるようにする
        }
        return new WebServerSettings();
    }

    public void Save()
    {
        File.WriteAllText(SettingsPath,
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
