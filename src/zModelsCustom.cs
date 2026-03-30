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


