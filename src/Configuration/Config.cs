using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;

namespace zVIPCore;

public partial class Config
{
    [JsonPropertyName("database")]
    public DatabaseConfig DatabaseConfig { get; set; } = new();

    [JsonPropertyName("website_url")]
    public string WebsiteUrl { get; set; } = "https://example.com/models";

    [JsonPropertyName("cdn_base_url")]
    public string CdnBaseUrl { get; set; } = "https://cdn.lunie.dev/zSystems_v2/json/fetch/";

    [JsonPropertyName("models_json_filename")]
    public string ModelsJsonFilename { get; set; } = "zModels.json";

    [JsonPropertyName("weapons_json_filename")]
    public string WeaponsJsonFilename { get; set; } = "zWeapons.json";

    [JsonPropertyName("mvps_json_filename")]
    public string MvpsJsonFilename { get; set; } = "zMVPs.json";

    [JsonPropertyName("center_html_duration")]
    public float CenterHtmlDuration { get; set; } = 4.0f;

    [JsonPropertyName("restrict_permission")]
    public string RestrictPermission { get; set; } = "";

    [JsonPropertyName("reload_cooldown_seconds")]
    public float ReloadCooldownSeconds { get; set; } = 120.0f;

    [JsonPropertyName("anti_spam_threshold")]
    public int AntiSpamThreshold { get; set; } = 3;

    [JsonPropertyName("anti_spam_window_seconds")]
    public float AntiSpamWindowSeconds { get; set; } = 15.0f;

    [JsonPropertyName("sound")]
    public SoundConfig SoundConfig { get; set; } = new();

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

        var mvpsPath = Path.Combine(configDir, config.MvpsJsonFilename);
        if (!File.Exists(mvpsPath))
            MvpModelsConfig.CreateDefault(mvpsPath);

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

    [JsonPropertyName("sound_event")]
    public string SoundEvent { get; set; } = "";

    [JsonPropertyName("sound_event_unsilenced")]
    public string SoundEventUnsilenced { get; set; } = "";

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

// Sound Config
public class SoundConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("force_mute_all_firebullets")]
    public bool ForceMuteAllFireBullets { get; set; } = false;

    [JsonPropertyName("custom_sound_default_enabled")]
    public bool CustomSoundDefaultEnabled { get; set; } = true;

    [JsonPropertyName("official_overrides")]
    public List<OfficialSoundOverride> OfficialOverrides { get; set; } = new();
}

public class OfficialSoundOverride
{
    [JsonPropertyName("item_def_index")]
    public int ItemDefIndex { get; set; }

    [JsonPropertyName("target_event")]
    public string TargetEvent { get; set; } = "";

    [JsonPropertyName("target_event_unsilenced")]
    public string TargetEventUnsilenced { get; set; } = "";
}

// MVP Models Config (zMVPs.json)
public partial class MvpModelsConfig
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
    public Dictionary<string, Dictionary<string, MvpModelData>> Categories { get; set; } = new();

    public static MvpModelsConfig Load(string moduleDirectory)
    {
        var configDir = Config.GetConfigDirectory(moduleDirectory);
        var config = zVIPCore.Config;
        var path = Path.Combine(configDir, config?.MvpsJsonFilename ?? "zMVPs.json");

        if (!File.Exists(path))
            return CreateDefaultModels(path);

        try
        {
            var json = File.ReadAllText(path);
            json = CommentPattern().Replace(json, "");
            var result = JsonSerializer.Deserialize<MvpModelsConfig>(json, JsonOptions);
            return result?.Categories?.Count > 0 ? result : new MvpModelsConfig();
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[zVIPCore] Error loading MVP models: {ex.Message}");
            return new MvpModelsConfig();
        }
    }

    private static MvpModelsConfig CreateDefaultModels(string path)
    {
        var defaultModels = new MvpModelsConfig
        {
            Version = "0.0.1",
            Categories = new()
            {
                ["Example Category"] = new()
                {
                    ["Example MVP"] = new()
                    {
                        MvpName = "Example MVP",
                        MvpSound = "sounds/example_mvp.vsnd",
                        ShowChatMessage = true,
                        ShowHtmlMessage = true
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
            Server.PrintToConsole($"[zVIPCore] Error creating default MVP models: {ex.Message}");
        }

        return defaultModels;
    }

    public static void CreateDefault(string path) => CreateDefaultModels(path);

    public MvpModelData? FindMvpBySoundAndName(string mvpName, string mvpSound)
    {
        foreach (var category in Categories.Values)
        {
            foreach (var mvp in category.Values)
            {
                if (mvp.MvpName == mvpName && mvp.MvpSound == mvpSound)
                    return mvp;
            }
        }
        return null;
    }
}

public class MvpModelData
{
    [JsonPropertyName("mvp_name")]
    public string MvpName { get; set; } = "";

    [JsonPropertyName("mvp_sound")]
    public string MvpSound { get; set; } = "";

    [JsonPropertyName("show_chat_message")]
    public bool ShowChatMessage { get; set; } = true;

    [JsonPropertyName("show_html_message")]
    public bool ShowHtmlMessage { get; set; } = true;
}

