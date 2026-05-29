using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;

namespace zVIPCore;

public partial class Config
{
    [JsonPropertyName("database")]
    public DatabaseConfig DatabaseConfig { get; set; } = new();

    [JsonPropertyName("modules")]
    public ModulesConfig Modules { get; set; } = new();

    [JsonPropertyName("website_url")]
    public string WebsiteUrl { get; set; } = "https://example.com/models";

    [JsonPropertyName("cdn_base_url")]
    public string CdnBaseUrl { get; set; } = "https://cdn.lunie.dev/zSystems_v2/json/fetch/";

    [JsonPropertyName("models_json_filename")]
    public string ModelsJsonFilename { get; set; } = "zModels.json";

    [JsonPropertyName("weapons_json_filename")]
    public string WeaponsJsonFilename { get; set; } = "zWeapons.json";

    [JsonPropertyName("mvp_json_filename")]
    public string MvpJsonFilename { get; set; } = "zMVP.json";



    [JsonPropertyName("center_html_duration")]
    public float CenterHtmlDuration { get; set; } = 7.0f;

    [JsonPropertyName("reload_cooldown_seconds")]
    public float ReloadCooldownSeconds { get; set; } = 120.0f;

    [JsonPropertyName("anti_spam_threshold")]
    public int AntiSpamThreshold { get; set; } = 3;

    [JsonPropertyName("anti_spam_window_seconds")]
    public float AntiSpamWindowSeconds { get; set; } = 15.0f;

    [JsonPropertyName("mvp")]
    public MvpConfig Mvp { get; set; } = new();

    [JsonPropertyName("killstreak")]
    public KillStreakConfig KillStreak { get; set; } = new();

    [JsonPropertyName("join_welcome")]
    public JoinWelcomeConfig JoinWelcome { get; set; } = new();

    [JsonPropertyName("particles")]
    public ParticlesConfig Particles { get; set; } = new();

    [JsonPropertyName("longjump")]
    public LongJumpConfig LongJump { get; set; } = new();

    [JsonPropertyName("doublejump")]
    public DoubleJumpConfig DoubleJump { get; set; } = new();

    public static Config Load(string moduleDirectory)
    {
        var path = GetConfigPath(moduleDirectory);
        var configDir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(configDir);

        var config = File.Exists(path)
            ? LoadExistingConfig(path)
            : CreateDefaultConfig(path);

        // Ensure model/weapon configs exist
        var modelsPath = Path.Combine(configDir, config.ModelsJsonFilename);
        if (!File.Exists(modelsPath))
            PlayerModelsConfig.CreateDefault(modelsPath);

        var weaponsPath = Path.Combine(configDir, config.WeaponsJsonFilename);
        if (!File.Exists(weaponsPath))
            WeaponModelsConfig.CreateDefault(weaponsPath);

        return config;
    }

    private static Config LoadExistingConfig(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Config>(json) ?? new Config();
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[zVIPCore] Error loading config: {ex.Message}");
            return new Config();
        }
    }

    private static string GetConfigPath(string moduleDirectory) =>
        Path.Combine(moduleDirectory, "../../configs/plugins/zVIPCore/zConfig.json");

    public static string GetConfigDirectory(string moduleDirectory) =>
        Path.GetDirectoryName(GetConfigPath(moduleDirectory))!;

    private static Config CreateDefaultConfig(string path)
    {
        var defaultConfig = new Config();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[zVIPCore] Error creating default config: {ex.Message}");
        }

        return defaultConfig;
    }
}

public class DatabaseConfig
{
    [JsonPropertyName("host")]
    public string Host { get; set; } = "localhost";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 3306;

    [JsonPropertyName("database")]
    public string Database { get; set; } = "cs2";

    [JsonPropertyName("user")]
    public string User { get; set; } = "root";

    [JsonPropertyName("password")]
    public string Password { get; set; } = "";

    [JsonPropertyName("min_pool_size")]
    public int MinPoolSize { get; set; } = 2;

    [JsonPropertyName("max_pool_size")]
    public int MaxPoolSize { get; set; } = 20;

    [JsonPropertyName("connection_timeout")]
    public int ConnectionTimeout { get; set; } = 30;

    [JsonPropertyName("command_timeout")]
    public int CommandTimeout { get; set; } = 30;
}

public class ModulesConfig
{
    [JsonPropertyName("player_models_enabled")]
    public bool PlayerModelsEnabled { get; set; } = true;

    [JsonPropertyName("weapons_enabled")]
    public bool WeaponsEnabled { get; set; } = true;

    [JsonPropertyName("smokes_enabled")]
    public bool SmokesEnabled { get; set; } = true;

    [JsonPropertyName("mvp_enabled")]
    public bool MvpEnabled { get; set; } = true;

    [JsonPropertyName("killstreak_enabled")]
    public bool KillStreakEnabled { get; set; } = true;

    [JsonPropertyName("particles_enabled")]
    public bool ParticlesEnabled { get; set; } = true;

    [JsonPropertyName("join_welcome_enabled")]
    public bool JoinWelcomeEnabled { get; set; } = true;

    [JsonPropertyName("longjump_enabled")]
    public bool LongJumpEnabled { get; set; } = true;

    [JsonPropertyName("doublejump_enabled")]
    public bool DoubleJumpEnabled { get; set; } = true;
}

// Player Models Config (zModels.json)
public partial class PlayerModelsConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    [GeneratedRegex(@"(?<=^|\s)//.*|/\*[\s\S]*?\*/", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex CommentPattern();

    [JsonPropertyName("version")]
    public string Version { get; set; } = "0.0.1";

    [JsonPropertyName("Categories")]
    public Dictionary<string, Dictionary<string, PlayerModelData>> Categories { get; set; } = new();

    public static PlayerModelsConfig Load(string moduleDirectory)
    {
        var configDir = Config.GetConfigDirectory(moduleDirectory);
        var config = zVIPCore.Config;
        var path = Path.Combine(configDir, config?.ModelsJsonFilename ?? "zModels.json");

        if (!File.Exists(path))
            return CreateDefaultModels(path);

        try
        {
            var json = File.ReadAllText(path);
            json = CommentPattern().Replace(json, "");
            var result = JsonSerializer.Deserialize<PlayerModelsConfig>(json, JsonOptions);
            return result?.Categories?.Count > 0 ? result : new PlayerModelsConfig();
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[zVIPCore] Error loading player models: {ex.Message}");
            return new PlayerModelsConfig();
        }
    }

    private static PlayerModelsConfig CreateDefaultModels(string path)
    {
        var defaultModels = new PlayerModelsConfig
        {
            Version = "0.0.1",
            Categories = new()
            {
                ["Example Category"] = new()
                {
                    ["Example Model"] = new()
                    {
                        UniqueId = "example_model",
                        Model = "models/player/example.mdl",
                        Slot = "ALL",
                        DisableLeg = false
                    }
                }
            }
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(defaultModels, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[zVIPCore] Error creating default player models: {ex.Message}");
        }

        return defaultModels;
    }

    public static void CreateDefault(string path) => CreateDefaultModels(path);

    public PlayerModelData? FindModelByUniqueId(string uniqueId)
    {
        foreach (var category in Categories.Values)
        {
            foreach (var model in category.Values)
            {
                if (model.UniqueId == uniqueId)
                    return model;
            }
        }
        return null;
    }

    public string GetModelNameByUniqueId(string uniqueId)
    {
        foreach (var category in Categories.Values)
        {
            foreach (var kvp in category)
            {
                if (kvp.Value.UniqueId == uniqueId)
                    return kvp.Key;
            }
        }
        return uniqueId;
    }
}

public class PlayerModelData
{
    [JsonPropertyName("uniqueid")]
    public string UniqueId { get; set; } = "";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("armModel")]
    public string? ArmModel { get; set; }

    [JsonPropertyName("slot")]
    public string Slot { get; set; } = "ALL";

    [JsonPropertyName("disable_leg")]
    public bool DisableLeg { get; set; }
}

// Weapon Models Config (zWeapons.json)
public partial class WeaponModelsConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    [GeneratedRegex(@"//.*|/\*[\s\S]*?\*/", RegexOptions.Compiled)]
    private static partial Regex CommentPattern();

    [JsonPropertyName("version")]
    public string Version { get; set; } = "0.0.1";

    [JsonPropertyName("Weapons")]
    public Dictionary<string, WeaponCollection> Weapons { get; set; } = new();

    public static WeaponModelsConfig Load(string moduleDirectory)
    {
        var configDir = Config.GetConfigDirectory(moduleDirectory);
        var config = zVIPCore.Config;
        var path = Path.Combine(configDir, config?.WeaponsJsonFilename ?? "zWeapons.json");

        if (!File.Exists(path))
            return CreateDefaultModels(path);

        try
        {
            var json = File.ReadAllText(path);
            json = CommentPattern().Replace(json, "");

            using var doc = JsonDocument.Parse(json);
            var result = new WeaponModelsConfig();

            // Parse version
            if (doc.RootElement.TryGetProperty("version", out var versionEl))
                result.Version = versionEl.GetString() ?? "0.0.1";

            if (doc.RootElement.TryGetProperty("Weapons", out var weaponsElement))
            {
                foreach (var collectionProp in weaponsElement.EnumerateObject())
                {
                    var collection = ParseCollection(collectionProp.Value);
                    result.Weapons[collectionProp.Name] = collection;
                }
            }

            return result.Weapons.Count > 0 ? result : new WeaponModelsConfig();
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[zVIPCore] Error loading weapon models: {ex.Message}");
            return new WeaponModelsConfig();
        }
    }

    private static WeaponCollection ParseCollection(JsonElement element)
    {
        var collection = new WeaponCollection();

        foreach (var prop in element.EnumerateObject())
        {
            switch (prop.Name)
            {
                case "image":
                    collection.Image = prop.Value.GetString() ?? "";
                    break;
                case "name":
                    collection.Name = prop.Value.GetString() ?? "";
                    break;
                default:
                    if (prop.Name.StartsWith("weapon_"))
                    {
                        var skinsList = new List<WeaponModelData>();

                        foreach (var skinProp in prop.Value.EnumerateObject())
                        {
                            var weaponData = JsonSerializer.Deserialize<WeaponModelData>(skinProp.Value.GetRawText(), JsonOptions);
                            if (weaponData != null)
                            {
                                weaponData.WeaponType = prop.Name;
                                weaponData.SkinKey = skinProp.Name;
                                skinsList.Add(weaponData);
                            }
                        }

                        if (skinsList.Count > 0)
                            collection.WeaponItems[prop.Name] = skinsList;
                    }
                    break;
            }
        }

        return collection;
    }

    private static WeaponModelsConfig CreateDefaultModels(string path)
    {
        var defaultJson = @"{
  ""version"": ""0.0.1"",
  ""Weapons"": {
    ""Example Collection"": {
      ""image"": ""example.png"",
      ""name"": ""Example Collection"",
      ""weapon_ak47"": {
        ""AK-47 Example"": {
          ""name"": ""AK-47 Example"",
          ""uniqueid"": ""ak47_example"",
          ""subclass"": ""weapon_ak47__example"",
          ""model"": ""weapons/models/example/ak47_example.vmdl"",
          ""image_gun"": ""ak47_example.png""
        }
      }
    }
  }
}";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, defaultJson);
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[zVIPCore] Error creating default weapon models: {ex.Message}");
        }

        return new WeaponModelsConfig();
    }

    public static void CreateDefault(string path) => CreateDefaultModels(path);

    public WeaponModelData? FindModelByUniqueId(string uniqueId)
    {
        foreach (var collection in Weapons.Values)
        {
            foreach (var skins in collection.WeaponItems.Values)
            {
                foreach (var weapon in skins)
                {
                    if (weapon.UniqueId == uniqueId)
                        return weapon;
                }
            }
        }
        return null;
    }

    public string GetModelNameByUniqueId(string uniqueId)
    {
        foreach (var collection in Weapons.Values)
        {
            foreach (var skins in collection.WeaponItems.Values)
            {
                foreach (var weapon in skins)
                {
                    if (weapon.UniqueId == uniqueId)
                        return weapon.Name;
                }
            }
        }
        return uniqueId;
    }

    public string? GetWeaponTypeByUniqueId(string uniqueId)
    {
        foreach (var collection in Weapons.Values)
        {
            foreach (var skins in collection.WeaponItems.Values)
            {
                foreach (var weapon in skins)
                {
                    if (weapon.UniqueId == uniqueId)
                        return weapon.WeaponType;
                }
            }
        }
        return null;
    }

    public int GetTotalSkinsCount()
    {
        return Weapons.Values
            .SelectMany(c => c.WeaponItems.Values)
            .Sum(skins => skins.Count);
    }
}

public class WeaponCollection
{
    [JsonPropertyName("image")]
    public string Image { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonIgnore]
    public Dictionary<string, List<WeaponModelData>> WeaponItems { get; set; } = new();

    public int GetSkinsCount() => WeaponItems.Values.Sum(s => s.Count);
}

public class WeaponModelData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("uniqueid")]
    public string UniqueId { get; set; } = "";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("subclass")]
    public string Subclass { get; set; } = "";

    [JsonPropertyName("image_gun")]
    public string ImageGun { get; set; } = "";



    [JsonIgnore]
    public string WeaponType { get; set; } = "";

    [JsonIgnore]
    public string SkinKey { get; set; } = "";

    public string GetSubclassName()
    {
        if (!string.IsNullOrEmpty(Subclass))
            return Subclass;

        if (string.IsNullOrEmpty(Model))
            return "";

        return Path.GetFileNameWithoutExtension(Model);
    }
}

// MVP Config
public class MvpConfig
{
    [JsonPropertyName("mute_default_mvp_sound")]
    public bool MuteDefaultMvpSound { get; set; } = true;

    [JsonPropertyName("hide_chat")]
    public bool HideChat { get; set; } = false;

    [JsonPropertyName("hide_html")]
    public bool HideHtml { get; set; } = false;

    [JsonPropertyName("center_html_duration")]
    public int CenterHtmlDuration { get; set; } = 6;

    [JsonPropertyName("sound_duration")]
    public float SoundDuration { get; set; } = 8.0f;
}

// KillStreak Config
public class KillStreakConfig
{
    [JsonPropertyName("show_kill_info")]
    public bool ShowKillInfo { get; set; } = true;

    [JsonPropertyName("loop_if_kill_icons_end")]
    public bool LoopIfKillIconsEnd { get; set; } = true;

    [JsonPropertyName("sound_event_path")]
    public string SoundEventPath { get; set; } = "soundevents/killstreak_soundevent.vsndevts";

    [JsonPropertyName("sound_volume")]
    public float SoundVolume { get; set; } = 1.0f;

    [JsonPropertyName("kill_icons")]
    public Dictionary<int, KillStreakIconsSettings> KillIcons { get; set; } = new()
    {
        { 1, new KillStreakIconsSettings { Icon = "<img src='https://cdn.zhw1nq.com/killstreak/kill1.png'>", Sound = "Kill.Sound_01", Duration = 3.0f } },
        { 2, new KillStreakIconsSettings { Icon = "<img src='https://cdn.zhw1nq.com/killstreak/kill2.png'>", Sound = "Kill.Sound_02", Duration = 3.0f } },
        { 3, new KillStreakIconsSettings { Icon = "<img src='https://cdn.zhw1nq.com/killstreak/kill3.png'>", Sound = "Kill.Sound_03", Duration = 3.0f } },
        { 4, new KillStreakIconsSettings { Icon = "<img src='https://cdn.zhw1nq.com/killstreak/kill4.png'>", Sound = "Kill.Sound_04", Duration = 3.0f } },
        { 5, new KillStreakIconsSettings { Icon = "<img src='https://cdn.zhw1nq.com/killstreak/kill5.png'>", Sound = "Kill.Sound_05", Duration = 5.5f, EnableChatNotification = true, BroadcastSoundToAll = true } },
        { 6, new KillStreakIconsSettings { Icon = "<img src='https://cdn.zhw1nq.com/killstreak/kill6.png'>", Sound = "Kill.Sound_06", Duration = 5.5f, EnableChatNotification = true, BroadcastSoundToAll = true } },
    };
}

public class KillStreakIconsSettings
{
    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "";

    [JsonPropertyName("sound")]
    public string Sound { get; set; } = "";

    [JsonPropertyName("duration")]
    public float Duration { get; set; } = 3.0f;

    [JsonPropertyName("enable_chat_notification")]
    public bool EnableChatNotification { get; set; } = false;

    [JsonPropertyName("broadcast_sound_to_all")]
    public bool BroadcastSoundToAll { get; set; } = false;

    [JsonPropertyName("html_kill_all")]
    public string HTMLKillAll { get; set; } = "";
}

// MVP Settings Config (zMVP.json)
public class MvpSettingsConfig
{
    [JsonPropertyName("Version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("MVPSettings")]
    public Dictionary<string, CategorySettings> MVPSettings { get; set; } = new();
}

public class CategorySettings
{
    [JsonPropertyName("CategoryFlags")]
    public List<string> CategoryFlags { get; set; } = new();

    [JsonPropertyName("MVPs")]
    public Dictionary<string, MvpItemSettings> MVPs { get; set; } = new();
}

public class MvpItemSettings
{
    [JsonPropertyName("MVPName")]
    public string MVPName { get; set; } = string.Empty;

    [JsonPropertyName("MVPSound")]
    public string MVPSound { get; set; } = string.Empty;

    [JsonPropertyName("EnablePreview")]
    public bool EnablePreview { get; set; } = true;

    [JsonPropertyName("ShowChatMessage")]
    public bool ShowChatMessage { get; set; } = true;

    [JsonPropertyName("ShowHtmlMessage")]
    public bool ShowHtmlMessage { get; set; } = true;

    [JsonPropertyName("SteamID")]
    public string SteamID { get; set; } = string.Empty;

    [JsonPropertyName("Flags")]
    public List<string> Flags { get; set; } = new();
}

internal static class MvpJsonOptions
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

public static class MvpSettingsLoader
{
    private static readonly HttpClient HttpClient = new();

    static MvpSettingsLoader()
    {
        HttpClient.Timeout = TimeSpan.FromSeconds(10);
        HttpClient.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
        HttpClient.DefaultRequestHeaders.Add("Pragma", "no-cache");
    }

    private static string GetMvpSettingsPath()
    {
        var configDir = Config.GetConfigDirectory(zVIPCore.Instance.ModuleDirectory);
        var config = zVIPCore.Config;
        return Path.Combine(configDir, config?.MvpJsonFilename ?? "zMVP.json");
    }

    public static async Task<MvpSettingsConfig> LoadOrFetchAsync()
    {
        var mvpSettingsPath = GetMvpSettingsPath();
        MvpSettingsConfig? localConfig = null;

        if (File.Exists(mvpSettingsPath))
            localConfig = LoadFromFile(mvpSettingsPath);

        var config = zVIPCore.Config;
        string cdnUrl = config.CdnBaseUrl.TrimEnd('/') + "/" + config.MvpJsonFilename;

        if (!string.IsNullOrWhiteSpace(cdnUrl))
            try
            {
                Console.WriteLine($"[zVIPCore] Checking for MVP settings updates from CDN: {cdnUrl}");
                var cdnConfig = await FetchFromCDNAsync(cdnUrl);

                if (cdnConfig != null)
                {
                    if (localConfig == null || cdnConfig.Version != localConfig.Version)
                    {
                        Console.WriteLine($"[zVIPCore] MVP version changed: {cdnConfig.Version} (local: {localConfig?.Version ?? "none"})");
                        SaveToFile(cdnConfig, mvpSettingsPath);
                        return cdnConfig;
                    }
                    else
                    {
                        Console.WriteLine($"[zVIPCore] MVP settings are up to date (version: {localConfig.Version})");
                        return localConfig;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[zVIPCore] Failed to fetch MVP settings from CDN: {ex.Message}");
            }

        if (localConfig != null)
        {
            Console.WriteLine("[zVIPCore] Using local MVP settings file");
            return localConfig;
        }

        Console.WriteLine("[zVIPCore] Creating default MVP settings file");
        var defaultConfig = CreateDefaultMVPSettings();
        SaveToFile(defaultConfig, mvpSettingsPath);
        return defaultConfig;
    }

    public static MvpSettingsConfig LoadFromLocal()
    {
        var mvpSettingsPath = GetMvpSettingsPath();
        if (File.Exists(mvpSettingsPath))
        {
            var config = LoadFromFile(mvpSettingsPath);
            if (config != null) return config;
        }
        return new MvpSettingsConfig();
    }

    private static async Task<MvpSettingsConfig?> FetchFromCDNAsync(string cdnUrl)
    {
        try
        {
            var bustUrl = cdnUrl + (cdnUrl.Contains('?') ? "&" : "?") + $"t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var response = await HttpClient.GetAsync(bustUrl);
            response.EnsureSuccessStatusCode();
            string jsonContent = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<MvpSettingsConfig>(jsonContent, MvpJsonOptions.Read);
        }
        catch
        {
            return null;
        }
    }

    private static MvpSettingsConfig? LoadFromFile(string path)
    {
        try
        {
            string configText = File.ReadAllText(path);
            return JsonSerializer.Deserialize<MvpSettingsConfig>(configText, MvpJsonOptions.Read);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[zVIPCore] Error loading MVP settings file: {ex.Message}");
            return null;
        }
    }

    private static void SaveToFile(MvpSettingsConfig config, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(config, MvpJsonOptions.Write));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[zVIPCore] Error saving MVP settings file: {ex.Message}");
        }
    }

    private static MvpSettingsConfig CreateDefaultMVPSettings()
    {
        return new MvpSettingsConfig
        {
            Version = "1.0.0",
            MVPSettings = new Dictionary<string, CategorySettings>
            {
                {
                    "PUBLIC MVP", new CategorySettings
                    {
                        CategoryFlags = new List<string>(),
                        MVPs = new Dictionary<string, MvpItemSettings>
                        {
                            {
                                "mvp.1", new MvpItemSettings
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
                                "mvp.2", new MvpItemSettings
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
                        MVPs = new Dictionary<string, MvpItemSettings>
                        {
                            {
                                "mvp.vip.1", new MvpItemSettings
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

public class JoinWelcomeConfig
{
    [JsonPropertyName("sound_event_path")]
    public string SoundEventPath { get; set; } = "soundevents/ui_soundevent.vsndevts";

    [JsonPropertyName("sound_path")]
    public string SoundPath { get; set; } = "MenuUI_exit";
}

public class ParticlesConfig
{
    [JsonPropertyName("default_duration_seconds")]
    public float DefaultDurationSeconds { get; set; } = 5.0f;
}

public class LongJumpConfig
{
    [JsonPropertyName("jump_boost")]
    public float JumpBoost { get; set; } = 1.0f;

    [JsonPropertyName("only_apply_force_in_z_axis")]
    public bool OnlyApplyForceInZAxis { get; set; } = false;

    [JsonPropertyName("admin_flag")]
    public string AdminFlag { get; set; } = "@css/vip";
}

public class DoubleJumpConfig
{
    [JsonPropertyName("jumps_count")]
    public int JumpsCount { get; set; } = 2;

    [JsonPropertyName("velocity")]
    public float Velocity { get; set; } = 250f;

    [JsonPropertyName("allow_instant_jump")]
    public bool AllowInstantJump { get; set; } = true;

    [JsonPropertyName("admin_flag")]
    public string AdminFlag { get; set; } = "@css/tgs-vip";
}
