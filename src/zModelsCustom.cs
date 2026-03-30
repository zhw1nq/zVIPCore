using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http;

namespace zModelsCustom;

public class zModelsCustom : BasePlugin
{
    public override string ModuleName => "zModelsCustom";
    public override string ModuleVersion => "2.0.0";

    public static zModelsCustom Instance { get; private set; } = null!;
    public static Config Config { get; private set; } = null!;
    public static Database Database { get; private set; } = null!;
    public static ModelManager ModelManager { get; private set; } = null!;
    public static WeaponManager WeaponManager { get; private set; } = null!;
    public static SmokeManager SmokeManager { get; private set; } = null!;
    public static SoundManager SoundManager { get; private set; } = null!;

    private readonly ConcurrentDictionary<ulong, ReloadInfo> _reloadTracking = new();
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    // Cached local versions to avoid re-reading files
    private string _cachedModelsVersion = "";
    private string _cachedWeaponsVersion = "";

    /// <summary>
    /// Wraps an async action with try-catch to prevent silent exception swallowing.
    /// </summary>
    public static async Task SafeAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[zModelsCustom] Async error: {ex.Message}");
        }
    }

    public override void Load(bool hotReload)
    {
        Instance = this;
        Config = Config.Load(ModuleDirectory);
        Database = new Database(Config.DatabaseConfig);
        ModelManager = new ModelManager();
        WeaponManager = new WeaponManager();
        SmokeManager = new SmokeManager();
        SoundManager = new SoundManager();

        // Load configs and cache
        var playerModels = PlayerModelsConfig.Load(ModuleDirectory);
        var weaponModels = WeaponModelsConfig.Load(ModuleDirectory);
        ModelManager.UpdateConfig(playerModels);
        WeaponManager.UpdateModelsConfig(weaponModels);
        SoundManager.UpdateModelsConfig();
        _cachedModelsVersion = playerModels.Version;
        _cachedWeaponsVersion = weaponModels.Version;

        // Player model events
        RegisterEventHandler<EventPlayerSpawn>(ModelManager.OnPlayerSpawn);
        RegisterEventHandler<EventPlayerSpawn>(SoundManager.OnPlayerSpawn, HookMode.Post);

        // Weapon events
        RegisterListener<Listeners.OnEntityCreated>(OnEntityCreated);
        RegisterEventHandler<EventItemEquip>(WeaponManager.OnItemEquip);
        RegisterEventHandler<EventWeaponFire>(SoundManager.OnWeaponFire, HookMode.Pre);

        // Common events
        RegisterEventHandler<EventPlayerConnectFull>(Database.OnPlayerConnectFull);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        RegisterEventHandler<EventPlayerDisconnect>(SoundManager.OnPlayerDisconnect, HookMode.Post);

        // Sound events
        RegisterListener<Listeners.OnMapStart>(SoundManager.OnMapStart);
        RegisterListener<Listeners.OnClientPutInServer>(SoundManager.OnClientPutInServer);
        RegisterListener<Listeners.OnClientDisconnect>(SoundManager.OnClientDisconnect);
        HookUserMessage(452, SoundManager.OnWeaponFireUserMessage, HookMode.Pre);

        // Map start config reload
        RegisterListener<Listeners.OnMapStart>(mapName =>
        {
            var newPlayerModels = PlayerModelsConfig.Load(ModuleDirectory);
            var newWeaponModels = WeaponModelsConfig.Load(ModuleDirectory);
            ModelManager.PrecacheModels(newPlayerModels);
            ModelManager.UpdateConfig(newPlayerModels);
            WeaponManager.PrecacheModels();
            WeaponManager.UpdateModelsConfig(newWeaponModels);
            SoundManager.UpdateModelsConfig();
            WeaponManager.ClearSubclassCache();
            _cachedModelsVersion = newPlayerModels.Version;
            _cachedWeaponsVersion = newWeaponModels.Version;
        });

        RegisterCommands();

        if (hotReload)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player?.IsBot == false && player.AuthorizedSteamID != null)
                    _ = SafeAsync(() => Database.PreloadAllPlayerDataAsync(player.SteamID));
            }
        }
    }

    private void RegisterCommands()
    {
        // Console-only: reload model DB data for a player
        AddCommand("css_zmodels", "Reload player models from DB (Console only)", Command_ZModels);

        // Console-only: reload weapon DB data for a player
        AddCommand("css_zweapons", "Reload player weapons from DB (Console only)", Command_ZWeapons);

        // Client+Server: reload JSON configs + CDN fetch
        AddCommand("css_reloadmodel", "Reload model/weapon JSON configs from CDN", Command_ReloadModel);

        // Sound toggle with configurable permission
        AddCommand("css_zrestrict", "Toggle custom weapon sounds", SoundManager.OnToggleCustomSound);

        // Website commands
        foreach (var cmd in new[] { "svip", "vip", "md", "mds" })
        {
            AddCommand($"css_{cmd}", "Open models website", Command_ModelsWebsite);
        }

        // Web API commands (Console only) - kept for backend integration
        AddCommand("css_webquery", "Apply model/weapon to player via web (Console only)", Command_WebQuery);
        AddCommand("css_weblogin", "Display web login notification (Console only)", Command_WebLogin);
        AddCommand("css_webdelete", "Remove player model/weapon via web (Console only)", Command_WebDelete);
    }

    #region Console Commands: css_zmodels / css_zweapons

    private void Command_ZModels(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null) { PrintConsoleOnly(player); return; }

        if (info.ArgCount < 3 || info.GetArg(1).ToLowerInvariant() != "reload")
        {
            Server.PrintToConsole("[zModelsCustom] Usage: css_zmodels reload <steamid>");
            return;
        }

        if (!ulong.TryParse(info.GetArg(2), out var steamId))
        {
            Server.PrintToConsole($"[zModelsCustom] Invalid SteamID: {info.GetArg(2)}");
            return;
        }

        Database.ClearPlayerCache(steamId);
        _ = SafeAsync(async () =>
        {
            await Database.PreloadAllPlayerDataAsync(steamId);
            Server.NextFrame(() =>
            {
                var target = FindPlayerBySteamId(steamId);
                if (target != null)
                {
                    target.PrintToChat($"{Localizer["zModelsCustom.prefix"]} {Localizer["zModelsCustom.web_reload_success"]}");
                }
                Server.PrintToConsole($"[zModelsCustom] Reloaded model data for {steamId}");
            });
        });
    }

    private void Command_ZWeapons(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null) { PrintConsoleOnly(player); return; }

        if (info.ArgCount < 3 || info.GetArg(1).ToLowerInvariant() != "reload")
        {
            Server.PrintToConsole("[zModelsCustom] Usage: css_zweapons reload <steamid>");
            return;
        }

        if (!ulong.TryParse(info.GetArg(2), out var steamId))
        {
            Server.PrintToConsole($"[zModelsCustom] Invalid SteamID: {info.GetArg(2)}");
            return;
        }

        WeaponManager.ClearPlayerData(steamId);
        Database.ClearPlayerCache(steamId);

        _ = SafeAsync(async () =>
        {
            await Database.PreloadAllPlayerDataAsync(steamId);
            Server.NextFrame(() =>
            {
                var target = FindPlayerBySteamId(steamId);
                if (target != null)
                {
                    WeaponManager.RefreshPlayerWeapons(target);
                    target.PrintToChat($"{Localizer["zModelsCustom.prefix"]} {Localizer["zModelsCustom.web_reload_success"]}");
                }
                Server.PrintToConsole($"[zModelsCustom] Reloaded weapon data for {steamId}");
            });
        });
    }

    #endregion

    #region Client+Server Command: css_reloadmodel

    [RequiresPermissions("@css/root")]
    private void Command_ReloadModel(CCSPlayerController? player, CommandInfo info)
    {
        _ = SafeAsync(async () =>
        {
            bool modelsUpdated = await TryFetchCdnJson(Config.ModelsJsonFilename);
            bool weaponsUpdated = await TryFetchCdnJson(Config.WeaponsJsonFilename);

            Server.NextFrame(() =>
            {
                try
                {
                    var newPlayerModels = PlayerModelsConfig.Load(ModuleDirectory);
                    var newWeaponModels = WeaponModelsConfig.Load(ModuleDirectory);

                    ModelManager.PrecacheModels(newPlayerModels);
                    ModelManager.UpdateConfig(newPlayerModels);
                    WeaponManager.PrecacheModels();
                    WeaponManager.UpdateModelsConfig(newWeaponModels);
                    SoundManager.UpdateModelsConfig();
                    _cachedModelsVersion = newPlayerModels.Version;
                    _cachedWeaponsVersion = newWeaponModels.Version;

                    var playerCategories = newPlayerModels.Categories.Count;
                    var playerTotal = newPlayerModels.Categories.Values.Sum(c => c.Count);
                    var weaponCollections = newWeaponModels.Weapons.Count;
                    var weaponTotal = newWeaponModels.GetTotalSkinsCount();

                    // Refresh all connected players
                    var refreshed = 0;
                    foreach (var p in Utilities.GetPlayers().Where(p => p?.IsBot == false && p.IsValid))
                    {
                        try { WeaponManager.RefreshPlayerWeapons(p); refreshed++; }
                        catch { /* skip */ }
                    }

                    var msg = Localizer["zModelsCustom.reload_success", playerCategories, playerTotal, weaponCollections, weaponTotal];

                    if (player?.IsValid == true)
                    {
                        player.PrintToChat($"{Localizer["zModelsCustom.prefix"]} {msg}");
                        player.PrintToChat($"{Localizer["zModelsCustom.prefix"]} Refreshed {refreshed} players");
                    }

                    var fetchStatus = modelsUpdated || weaponsUpdated ? " (CDN updated)" : " (no CDN changes)";
                    Server.PrintToConsole($"[zModelsCustom] Reloaded configs{fetchStatus}. Refreshed {refreshed} players.");
                }
                catch (Exception ex)
                {
                    if (player?.IsValid == true)
                        player.PrintToChat($"{Localizer["zModelsCustom.prefix"]} {Localizer["zModelsCustom.reload_error", ex.Message]}");
                    Server.PrintToConsole($"[zModelsCustom] Error reloading: {ex.Message}");
                }
            });
        });
    }

    #endregion

    #region CDN Version-Based Fetch

    /// <summary>
    /// Fetches a JSON file from CDN, compares version, and saves locally if newer.
    /// Returns true if the local file was updated.
    /// </summary>
    private async Task<bool> TryFetchCdnJson(string filename)
    {
        try
        {
            var cdnUrl = Config.CdnBaseUrl.TrimEnd('/') + "/" + filename;
            var response = await _httpClient.GetStringAsync(cdnUrl);

            if (string.IsNullOrWhiteSpace(response))
                return false;

            // Parse remote version
            string remoteVersion;
            try
            {
                using var doc = JsonDocument.Parse(response);
                remoteVersion = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrEmpty(remoteVersion))
                return false;

            // Compare with cached local version
            var localVersion = filename == Config.ModelsJsonFilename ? _cachedModelsVersion : _cachedWeaponsVersion;

            if (remoteVersion == localVersion)
                return false;

            // Save to local config directory
            var configDir = Config.GetConfigDirectory(ModuleDirectory);
            var localPath = Path.Combine(configDir, filename);
            await File.WriteAllTextAsync(localPath, response);

            // Update cached version (thread-safe: only read by main thread in NextFrame)
            if (filename == Config.ModelsJsonFilename)
                _cachedModelsVersion = remoteVersion;
            else
                _cachedWeaponsVersion = remoteVersion;

            // NOTE: Do NOT call Server.PrintToConsole here — we're on a background thread.
            // Logging happens in the Server.NextFrame callback in Command_ReloadModel.
            return true;
        }
        catch
        {
            // Silently fail — CDN may be unreachable. Main thread logs the fetch status.
            return false;
        }
    }

    #endregion

    #region Web API Commands (Console Only)

    private void Command_WebQuery(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null) { PrintConsoleOnly(player); return; }

        if (info.ArgCount < 4)
        {
            Server.PrintToConsole("[zModelsCustom] Usage: css_webquery <type> <steamid> <uniqueid> [target]");
            return;
        }

        var type = info.GetArg(1).ToLowerInvariant();
        if (!ulong.TryParse(info.GetArg(2), out var steamId))
        {
            Server.PrintToConsole($"[zModelsCustom] Invalid SteamID: {info.GetArg(2)}");
            return;
        }

        var uniqueId = info.GetArg(3);
        var target = info.ArgCount >= 5 ? info.GetArg(4).ToLowerInvariant() : "";

        switch (type)
        {
            case "model":
                if (target != "t" && target != "ct" && target != "all")
                {
                    Server.PrintToConsole($"[zModelsCustom] Invalid site: {target}");
                    return;
                }
                _ = SafeAsync(() => ProcessModelWebQuery(steamId, uniqueId, target));
                break;
            case "weapon":
                _ = SafeAsync(() => ProcessWeaponWebQuery(steamId, uniqueId));
                break;
            case "smoke":
                _ = SafeAsync(() => ProcessSmokeWebQuery(steamId, uniqueId));
                break;
            default:
                Server.PrintToConsole($"[zModelsCustom] Invalid type: {type}");
                break;
        }
    }

    private async Task ProcessModelWebQuery(ulong steamId, string uniqueId, string site)
    {
        try
        {
            var modelsConfig = PlayerModelsConfig.Load(ModuleDirectory);
            var model = modelsConfig.FindModelByUniqueId(uniqueId);
            if (model == null) return;

            var teams = new List<CsTeam>();
            if (site is "all" or "t") teams.Add(CsTeam.Terrorist);
            if (site is "all" or "ct") teams.Add(CsTeam.CounterTerrorist);

            foreach (var team in teams)
                await Database.SavePlayerModelAsync(steamId, team, uniqueId);

            Server.NextFrame(() =>
            {
                var target = FindPlayerBySteamId(steamId);
                if (target == null) return;

                var prefix = Localizer["zModelsCustom.prefix"];
                var modelName = modelsConfig.GetModelNameByUniqueId(uniqueId);
                var siteDisplay = site.ToUpperInvariant();

                if (target.PlayerPawn.Value != null && teams.Contains(target.Team))
                    ModelManager.ApplyModel(target, model);

                target.PrintToChat($" {prefix} {Localizer["zModelsCustom.web_model_applied_site", modelName, siteDisplay]}");
            });
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[zModelsCustom] Error in model webquery: {ex.Message}");
        }
    }

    private async Task ProcessWeaponWebQuery(ulong steamId, string uniqueId)
    {
        try
        {
            var modelsConfig = WeaponModelsConfig.Load(ModuleDirectory);
            var model = modelsConfig.FindModelByUniqueId(uniqueId);
            if (model == null || string.IsNullOrEmpty(model.WeaponType)) return;

            await Database.SavePlayerWeaponAsync(steamId, model.WeaponType, uniqueId);
            WeaponManager.UpdatePlayerWeaponCache(steamId, model.WeaponType, uniqueId);

            Server.NextFrame(() =>
            {
                var target = FindPlayerBySteamId(steamId);
                if (target == null) return;

                var prefix = Localizer["zModelsCustom.prefix"];
                var modelName = modelsConfig.GetModelNameByUniqueId(uniqueId);
                var weaponDisplay = model.WeaponType.ToUpperInvariant().Replace("WEAPON_", "");

                if (target.PlayerPawn.Value != null)
                    WeaponManager.RefreshPlayerWeapons(target);

                target.PrintToChat($" {prefix} {Localizer["zModelsCustom.web_weapon_applied", modelName, weaponDisplay]}");
            });
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[zModelsCustom] Error in weapon webquery: {ex.Message}");
        }
    }

    private async Task ProcessSmokeWebQuery(ulong steamId, string color)
    {
        try
        {
            await Database.SavePlayerSmokeColorAsync(steamId, color);
            SmokeManager.SetPlayerSmokeColor(steamId, color);

            Server.NextFrame(() =>
            {
                var target = FindPlayerBySteamId(steamId);
                if (target == null) return;

                var colorDisplay = color == "random" ? "Rainbow" : color;
                target.PrintToChat($" {Localizer["zModelsCustom.prefix"]} {Localizer["zModelsCustom.web_smoke_applied", colorDisplay]}");
            });
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[zModelsCustom] Error in smoke webquery: {ex.Message}");
        }
    }

    private void Command_WebLogin(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null) { PrintConsoleOnly(player); return; }

        if (info.ArgCount < 3)
        {
            Server.PrintToConsole("[zModelsCustom] Usage: css_weblogin <steamid> <json_oneline>");
            return;
        }

        if (!ulong.TryParse(info.GetArg(1), out var steamId)) return;

        var fullCommand = info.GetCommandString;
        var parts = fullCommand.Split(new[] { ' ' }, 3);
        if (parts.Length < 3) return;

        try
        {
            var loginData = JsonSerializer.Deserialize<WebLoginResponse>(parts[2].Trim(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (loginData?.Success != true || loginData.Info == null) return;

            var target = FindPlayerBySteamId(steamId);
            if (target == null) return;

            var prefix = Localizer["zModelsCustom.prefix"];
            target.PrintToChat($" {prefix} {Localizer["zModelsCustom.web_login_success"]}");
            target.PrintToChat($" {Localizer["zModelsCustom.web_login_time", loginData.Info.Time]}");
            target.PrintToChat($" {Localizer["zModelsCustom.web_login_location", loginData.Info.Country, loginData.Info.City]}");
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[zModelsCustom] Error processing web login: {ex.Message}");
        }
    }

    private void Command_WebDelete(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null) { PrintConsoleOnly(player); return; }

        if (info.ArgCount < 4)
        {
            Server.PrintToConsole("[zModelsCustom] Usage: css_webdelete <type> <steamid> <site/weapon>");
            return;
        }

        var type = info.GetArg(1).ToLowerInvariant();
        if (!ulong.TryParse(info.GetArg(2), out var steamId)) return;
        var target = info.GetArg(3).ToLowerInvariant();

        switch (type)
        {
            case "model":
                _ = SafeAsync(() => ProcessModelWebDelete(steamId, target));
                break;
            case "weapon":
                _ = SafeAsync(() => ProcessWeaponWebDelete(steamId, target));
                break;
            case "smoke":
                _ = SafeAsync(() => ProcessSmokeWebDelete(steamId));
                break;
            default:
                Server.PrintToConsole($"[zModelsCustom] Invalid type: {type}");
                break;
        }
    }

    private async Task ProcessModelWebDelete(ulong steamId, string site)
    {
        try
        {
            if (site == "all")
            {
                await Database.RemovePlayerModelAsync(steamId, CsTeam.Terrorist);
                await Database.RemovePlayerModelAsync(steamId, CsTeam.CounterTerrorist);
            }
            else
            {
                var team = site == "t" ? CsTeam.Terrorist : CsTeam.CounterTerrorist;
                await Database.RemovePlayerModelAsync(steamId, team);
            }

            Server.NextFrame(() =>
            {
                var target = FindPlayerBySteamId(steamId);
                if (target?.PlayerPawn.Value != null)
                    ModelManager.ResetModel(target);

                target?.PrintToChat($" {Localizer["zModelsCustom.prefix"]} {Localizer["zModelsCustom.web_model_removed_all"]}");
            });
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[zModelsCustom] Error in model webdelete: {ex.Message}");
        }
    }

    private async Task ProcessWeaponWebDelete(ulong steamId, string weapon)
    {
        try
        {
            if (weapon == "all")
                await Database.RemoveAllPlayerWeaponsAsync(steamId);
            else
                await Database.RemovePlayerWeaponAsync(steamId, weapon);

            Server.NextFrame(() =>
            {
                var target = FindPlayerBySteamId(steamId);
                if (target?.PlayerPawn.Value != null)
                    WeaponManager.RefreshPlayerWeapons(target);

                var msg = weapon == "all"
                    ? Localizer["zModelsCustom.web_weapon_removed_all"]
                    : Localizer["zModelsCustom.web_weapon_removed", weapon.ToUpperInvariant().Replace("WEAPON_", "")];
                target?.PrintToChat($" {Localizer["zModelsCustom.prefix"]} {msg}");
            });
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[zModelsCustom] Error in weapon webdelete: {ex.Message}");
        }
    }

    private async Task ProcessSmokeWebDelete(ulong steamId)
    {
        try
        {
            await Database.RemovePlayerSmokeColorAsync(steamId);
            SmokeManager.ClearPlayerData(steamId);

            Server.NextFrame(() =>
            {
                var target = FindPlayerBySteamId(steamId);
                target?.PrintToChat($" {Localizer["zModelsCustom.prefix"]} {Localizer["zModelsCustom.web_smoke_removed"]}");
            });
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[zModelsCustom] Error in smoke webdelete: {ex.Message}");
        }
    }

    #endregion

    #region Website Commands

    private void Command_ModelsWebsite(CCSPlayerController? player, CommandInfo info)
    {
        if (player?.IsValid != true) return;

        var steamId = player.SteamID;
        var currentTime = Server.CurrentTime;
        var reloadInfo = _reloadTracking.GetOrAdd(steamId, _ => new ReloadInfo());

        lock (reloadInfo)
        {
            reloadInfo.CommandHistory.Add(currentTime);
            reloadInfo.CommandHistory.RemoveAll(t => currentTime - t > Config.AntiSpamWindowSeconds);

            if (reloadInfo.CommandHistory.Count >= Config.AntiSpamThreshold)
            {
                Server.ExecuteCommand($"kickid {player.UserId} \"{Localizer["zModelsCustom.kick_reason_spam"]}\"");
                reloadInfo.CommandHistory.Clear();
                return;
            }

            player.PrintToChat($" {Localizer["zModelsCustom.prefix"]}{Localizer["zModelsCustom.website_message", Config.WebsiteUrl]}");

            var timeSinceLastReload = currentTime - reloadInfo.LastReloadTime;
            if (timeSinceLastReload < Config.ReloadCooldownSeconds)
            {
                var remaining = (int)(Config.ReloadCooldownSeconds - timeSinceLastReload);
                player.PrintToChat($"{Localizer["zModelsCustom.prefix"]} {Localizer["zModelsCustom.cooldown_remaining", remaining]}");
                return;
            }

            reloadInfo.LastReloadTime = currentTime;

            Database.ClearPlayerCache(steamId);
            WeaponManager.ClearPlayerData(steamId);

            _ = SafeAsync(async () =>
            {
                await Database.PreloadAllPlayerDataAsync(steamId);
                Server.NextFrame(() =>
                {
                    if (player.IsValid)
                        player.PrintToChat($"{Localizer["zModelsCustom.prefix"]} {Localizer["zModelsCustom.web_reload_success"]}");
                });
            });
        }
    }

    #endregion

    #region Core Event Handlers

    private void OnEntityCreated(CEntityInstance entity)
    {
        WeaponManager.OnEntityCreated(entity);
        SmokeManager.OnEntityCreated(entity);
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player?.IsBot != false) return HookResult.Continue;

        var steamId = player.SteamID;
        Database.ClearPlayerCache(steamId);
        WeaponManager.ClearPlayerData(steamId);
        SmokeManager.ClearPlayerData(steamId);
        _reloadTracking.TryRemove(steamId, out _);

        return HookResult.Continue;
    }

    public override void Unload(bool hotReload)
    {
        Database?.Dispose();
        WeaponManager.ClearSubclassCache();
        _reloadTracking.Clear();
    }

    #endregion

    #region Helpers

    private static CCSPlayerController? FindPlayerBySteamId(ulong steamId) =>
        Utilities.GetPlayers().FirstOrDefault(p => p?.IsValid == true && p.SteamID == steamId);

    private void PrintConsoleOnly(CCSPlayerController player)
    {
        if (player.IsValid)
            player.PrintToChat($"{Localizer["zModelsCustom.prefix"]} {Localizer["zModelsCustom.console_only"]}");
    }

    private sealed class ReloadInfo
    {
        public float LastReloadTime { get; set; }
        public List<float> CommandHistory { get; } = new();
    }

    #endregion
}

// JSON models for web login
public class WebLoginResponse
{
    [JsonPropertyName("sc")]
    public bool Success { get; set; }

    [JsonPropertyName("i")]
    public WebLoginInfo? Info { get; set; }
}

public class WebLoginInfo
{
    [JsonPropertyName("ct")]
    public string Country { get; set; } = "";

    [JsonPropertyName("cty")]
    public string City { get; set; } = "";

    [JsonPropertyName("t")]
    public string Time { get; set; } = "";
}
