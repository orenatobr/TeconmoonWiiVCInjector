using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WiiVCInjector;

/// <summary>Replacement for the Windows registry key and Properties.Settings of the original.</summary>
public sealed class AppSettings
{
    public const string Zeros = "00000000000000000000000000000000";
    public string WiiUCommonKey { get; set; } = Zeros;
    public string TitleKey { get; set; } = Zeros;
    public string AncastKey { get; set; } = Zeros;
    public string BannersRepository { get; set; } = "https://raw.githubusercontent.com/UWUVCI-PRIME/UWUVCI-IMAGES/master/";
    public string OutputPath { get; set; } = "";
    public string OutputPathFixed { get; set; } = "";
    public string GameFilePath { get; set; } = "";

    /// <summary>settings.json location; WIIVC_SETTINGS overrides it (used by the self-test).</summary>
    public static string FilePath =>
        Environment.GetEnvironmentVariable("WIIVC_SETTINGS") is { Length: > 0 } p ? p :
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                     "Library", "Application Support", "WiiVCInjector", "settings.json");

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>MD5 checks of the three keys, same hashes as the original.</summary>
public static class KeyCheck
{
    public static bool Md5Is(string text, string hex) =>
        Convert.ToHexString(MD5.HashData(Encoding.ASCII.GetBytes(text))) == hex;

    public static bool CommonKey(string t) => Md5Is(t, "35AC5994972279331D97094FA2FB97FC");
    public static bool TitleKey(string t) => Md5Is(t, "F94BD88EBB7AA93867E630615F271C9F");
    public static bool AncastKey(string t) => Md5Is(t, "318D1F9D98FB08E77C7FE177AA490543");
}
