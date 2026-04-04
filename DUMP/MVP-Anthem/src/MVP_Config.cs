using CounterStrikeSharp.API;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MVPAnthem;

public class PluginConfig
{
    public string Version { get; set; } = "3.1.0";
    public Settings_Config Settings { get; set; } = new();
    public Database_Config Database { get; set; } = new();
    public Commands_Config Commands { get; set; } = new();
    public Timer_Config Timer { get; set; } = new();
}

public class MVPSettingsConfig
{
    public string Version { get; set; } = "1.0.0";
    public Dictionary<string, CategorySettings> MVPSettings { get; set; } = new();
}

public class Settings_Config
{
    [JsonPropertyName("DisablePlayerDefaultMVP")]
    public bool DisablePlayerDefaultMVP { get; set; } = true;

    [JsonPropertyName("CDN_URL")]
    public string CDN_URL { get; set; } = "";
}

public class Database_Config
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3306;
    public string Database { get; set; } = "cs2_mvp";
    public string User { get; set; } = "root";
    public string Password { get; set; } = "";
    public string SslMode { get; set; } = "None";
}

public class Timer_Config
{
    [JsonPropertyName("CenterHtmlDuration")]
    public int CenterHtmlDuration { get; set; } = 7;

    [JsonPropertyName("SoundDuration")]
    public float SoundDuration { get; set; } = 10.0f;
}

public class CategorySettings
{
    public List<string> CategoryFlags { get; set; } = new();
    public Dictionary<string, MVP_Settings> MVPs { get; set; } = new();
}

public class MVP_Settings
{
    public string MVPName { get; set; } = string.Empty;
    public string MVPSound { get; set; } = string.Empty;

    public bool EnablePreview { get; set; } = true;
    public bool ShowChatMessage { get; set; } = true;
    public bool ShowHtmlMessage { get; set; } = true;
    public string SteamID { get; set; } = string.Empty;
    public List<string> Flags { get; set; } = new();
}

public class Commands_Config
{
    public List<string> MVPCommands { get; set; } = new() { "mvp", "music" };
}

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Read = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static readonly JsonSerializerOptions Write = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
}

public static class ConfigLoader
{
    private static readonly string ConfigPath;

    static ConfigLoader()
    {
        string assemblyName = Assembly.GetExecutingAssembly().GetName().Name ?? string.Empty;

        ConfigPath = Path.Combine(
            Server.GameDirectory,
            "csgo", "addons", "counterstrikesharp", "configs", "plugins",
            assemblyName, "config.json"
        );
    }

    public static PluginConfig Load()
    {
        if (!File.Exists(ConfigPath))
            CreateDefaultConfig();

        return LoadConfigFromFile();
    }

    private static PluginConfig LoadConfigFromFile()
    {
        try
        {
            string configText = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<PluginConfig>(configText, JsonOptions.Read) ?? new PluginConfig();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MVP-Anthem] Error loading config: {ex.Message}");
            return new PluginConfig();
        }
    }

    private static void CreateDefaultConfig()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);

        var defaultConfig = new PluginConfig
        {
            Version = "3.1.0",
            Settings = new Settings_Config
            {
                DisablePlayerDefaultMVP = true,
                CDN_URL = ""
            },
            Database = new Database_Config
            {
                Host = "localhost",
                Port = 3306,
                Database = "cs2_mvp",
                User = "root",
                Password = "",
                SslMode = "None"
            },
            Commands = new Commands_Config
            {
                MVPCommands = new List<string> { "mvp", "music" }
            },
            Timer = new Timer_Config
            {
                CenterHtmlDuration = 7,
                SoundDuration = 10.0f
            }
        };

        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(defaultConfig, JsonOptions.Write));
    }
}

public static class MVPSettingsLoader
{
    private static readonly string MVPSettingsPath;
    private static readonly HttpClient HttpClient = new();

    static MVPSettingsLoader()
    {
        string assemblyName = Assembly.GetExecutingAssembly().GetName().Name ?? string.Empty;

        MVPSettingsPath = Path.Combine(
            Server.GameDirectory,
            "csgo", "addons", "counterstrikesharp", "configs", "plugins",
            assemblyName, "mvp-settings.json"
        );

        HttpClient.Timeout = TimeSpan.FromSeconds(10);
        HttpClient.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
        HttpClient.DefaultRequestHeaders.Add("Pragma", "no-cache");
    }

    public static async Task<MVPSettingsConfig> LoadOrFetchAsync()
    {
        MVPSettingsConfig? localConfig = null;

        if (File.Exists(MVPSettingsPath))
            localConfig = LoadFromFile();

        string cdnUrl = MVPAnthem.Instance.Config.Settings.CDN_URL;

        if (!string.IsNullOrWhiteSpace(cdnUrl))
            try
            {
                Console.WriteLine($"[MVP-Anthem] Checking for MVP settings updates from CDN: {cdnUrl}");
                var cdnConfig = await FetchFromCDNAsync(cdnUrl);

                if (cdnConfig != null)
                {
                    if (localConfig == null || cdnConfig.Version != localConfig.Version)
                    {
                        Console.WriteLine($"[MVP-Anthem] Version changed: {cdnConfig.Version} (local: {localConfig?.Version ?? "none"})");
                        SaveToFile(cdnConfig);
                        return cdnConfig;
                    }
                    else
                    {
                        Console.WriteLine($"[MVP-Anthem] MVP settings are up to date (version: {localConfig.Version})");
                        return localConfig;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MVP-Anthem] Failed to fetch from CDN: {ex.Message}");
            }

        if (localConfig != null)
        {
            Console.WriteLine("[MVP-Anthem] Using local MVP settings file");
            return localConfig;
        }

        Console.WriteLine("[MVP-Anthem] Creating default MVP settings file");
        var defaultConfig = CreateDefaultMVPSettings();
        SaveToFile(defaultConfig);
        return defaultConfig;
    }

    private static async Task<MVPSettingsConfig?> FetchFromCDNAsync(string cdnUrl)
    {
        try
        {
            var bustUrl = cdnUrl + (cdnUrl.Contains('?') ? "&" : "?") + $"t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var response = await HttpClient.GetAsync(bustUrl);
            response.EnsureSuccessStatusCode();
            string jsonContent = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<MVPSettingsConfig>(jsonContent, JsonOptions.Read);
        }
        catch
        {
            return null;
        }
    }

    private static MVPSettingsConfig? LoadFromFile()
    {
        try
        {
            string configText = File.ReadAllText(MVPSettingsPath);
            return JsonSerializer.Deserialize<MVPSettingsConfig>(configText, JsonOptions.Read);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MVP-Anthem] Error loading MVP settings file: {ex.Message}");
            return null;
        }
    }

    private static void SaveToFile(MVPSettingsConfig config)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MVPSettingsPath)!);
            File.WriteAllText(MVPSettingsPath, JsonSerializer.Serialize(config, JsonOptions.Write));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MVP-Anthem] Error saving MVP settings file: {ex.Message}");
        }
    }

    private static MVPSettingsConfig CreateDefaultMVPSettings()
    {
        return new MVPSettingsConfig
        {
            Version = "1.0.0",
            MVPSettings = new Dictionary<string, CategorySettings>
            {
                {
                    "PUBLIC MVP", new CategorySettings
                    {
                        CategoryFlags = new List<string>(),
                        MVPs = new Dictionary<string, MVP_Settings>
                        {
                            {
                                "mvp.1", new MVP_Settings
                                {
                                    MVPName = "Ai Dua Em Ve",
                                    MVPSound = "MVP.001_ai_dua_em_ve",
                                    EnablePreview = true,
                                    ShowChatMessage = true,
                                    ShowHtmlMessage = true,
                                    SteamID = "",
                                    Flags = new List<string>()
                                }
                            },
                            {
                                "mvp.2", new MVP_Settings
                                {
                                    MVPName = "Babe Get My Gun",
                                    MVPSound = "MVP.001_babegetmygun",
                                    EnablePreview = true,
                                    ShowChatMessage = true,
                                    ShowHtmlMessage = true,
                                    SteamID = "",
                                    Flags = new List<string>()
                                }
                            }
                        }
                    }
                },
                {
                    "VIP MVP", new CategorySettings
                    {
                        CategoryFlags = new List<string> { "@css/vip" },
                        MVPs = new Dictionary<string, MVP_Settings>
                        {
                            {
                                "mvp.vip.1", new MVP_Settings
                                {
                                    MVPName = "Despacito Mixi",
                                    MVPSound = "MVP.001F_despacito_mixi",
                                    EnablePreview = true,
                                    ShowChatMessage = true,
                                    ShowHtmlMessage = true,
                                    SteamID = "",
                                    Flags = new List<string>()
                                }
                            }
                        }
                    }
                }
            }
        };
    }
}
