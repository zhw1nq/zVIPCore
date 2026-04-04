using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http;

namespace zVIPCore;

public class zVIPCore : BasePlugin
{
    public override string ModuleName => "zVIPCore";
    public override string ModuleVersion => "2.0.0";

    public static zVIPCore Instance { get; private set; } = null!;
    public static Config Config { get; private set; } = null!;
    public static Database Database { get; private set; } = null!;
    public static ModelManager ModelManager { get; private set; } = null!;
    public static WeaponManager WeaponManager { get; private set; } = null!;
    public static SmokeManager SmokeManager { get; private set; } = null!;
    public static SoundManager SoundManager { get; private set; } = null!;
    public static MvpManager MvpManager { get; private set; } = null!;
    public static MvpSettingsConfig MvpSettings { get; set; } = new();


    private readonly ConcurrentDictionary<ulong, ReloadInfo> _reloadTracking = new();
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    // Cached local versions to avoid re-reading files
    private string _cachedModelsVersion = "";
    private string _cachedWeaponsVersion = "";
    private string _cachedMvpVersion = "";
    /// <summary>
    /// Wraps an async action with try-catch to prevent silent exception swallowing.
    /// </summary>
    public static async Task SafeAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            Console.WriteLine($"[zVIPCore] Async error: {ex.Message}");
        }
    }

    public override void Load(bool hotReload)
    {
        Instance = this;
        Config = Config.Load(ModuleDirectory);
        Database = new Database(Config.DatabaseConfig);
        
        if (Config.Modules.PlayerModelsEnabled)
        {
            ModelManager = new ModelManager();
            var playerModels = PlayerModelsConfig.Load(ModuleDirectory);
            ModelManager.UpdateConfig(playerModels);
            RegisterEventHandler<EventPlayerSpawn>(ModelManager.OnPlayerSpawn);
            _cachedModelsVersion = playerModels.Version;
        }
        
        if (Config.Modules.WeaponsEnabled)
        {
            WeaponManager = new WeaponManager();
            var weaponModels = WeaponModelsConfig.Load(ModuleDirectory);
            WeaponManager.UpdateModelsConfig(weaponModels);
            RegisterEventHandler<EventItemEquip>(WeaponManager.OnItemEquip);
            _cachedWeaponsVersion = weaponModels.Version;
        }
        

        if (Config.Modules.SoundsEnabled)
        {
            SoundManager = new SoundManager();
            SoundManager.UpdateModelsConfig();
            RegisterEventHandler<EventPlayerSpawn>(SoundManager.OnPlayerSpawn, HookMode.Post);
            RegisterEventHandler<EventWeaponFire>(SoundManager.OnWeaponFire, HookMode.Pre);
            RegisterListener<Listeners.OnMapStart>(SoundManager.OnMapStart);
            RegisterListener<Listeners.OnClientPutInServer>(SoundManager.OnClientPutInServer);
            RegisterListener<Listeners.OnClientDisconnect>(SoundManager.OnClientDisconnect);
            HookUserMessage(452, SoundManager.OnWeaponFireUserMessage, HookMode.Pre);
            RegisterEventHandler<EventPlayerDisconnect>(SoundManager.OnPlayerDisconnect, HookMode.Post);
        }

        if (Config.Modules.SmokesEnabled)
        {
            SmokeManager = new SmokeManager();
        }

        if (Config.Modules.MvpEnabled)
        {
            MvpManager = new MvpManager();
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(MvpSettingsLoader).TypeHandle);
            _ = SafeAsync(async () =>
            {
                MvpSettings = await MvpSettingsLoader.LoadOrFetchAsync();
                _cachedMvpVersion = MvpSettings.Version;
                Console.WriteLine($"[zVIPCore] MVP settings loaded successfully (version: {MvpSettings.Version})");
            });
            RegisterEventHandler<EventRoundMvp>(MvpManager.OnRoundMvp, HookMode.Pre);
            RegisterEventHandler<EventRoundStart>(MvpManager.OnRoundStart);
            RegisterEventHandler<EventCsWinPanelMatch>(MvpManager.OnMapEnd);
        }

        // Weapon/Smoke events
        if (Config.Modules.WeaponsEnabled || Config.Modules.SmokesEnabled)
            RegisterListener<Listeners.OnEntityCreated>(OnEntityCreated);
        // Common events
        RegisterEventHandler<EventPlayerConnectFull>(Database.OnPlayerConnectFull);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);

        RegisterCommands();

        // Map start config reload
        RegisterListener<Listeners.OnMapStart>(mapName =>
        {
            if (Config.Modules.PlayerModelsEnabled)
            {
                var newPlayerModels = PlayerModelsConfig.Load(ModuleDirectory);
                ModelManager?.PrecacheModels(newPlayerModels);
                ModelManager?.UpdateConfig(newPlayerModels);
                _cachedModelsVersion = newPlayerModels.Version;
            }
            
            if (Config.Modules.WeaponsEnabled)
            {
                var newWeaponModels = WeaponModelsConfig.Load(ModuleDirectory);
                WeaponManager?.PrecacheModels();
                WeaponManager?.UpdateModelsConfig(newWeaponModels);
                WeaponManager.ClearSubclassCache();
                _cachedWeaponsVersion = newWeaponModels.Version;
            }
            

            if (Config.Modules.SoundsEnabled)
                SoundManager?.UpdateModelsConfig();

            if (Config.Modules.MvpEnabled)
            {
                _ = SafeAsync(async () =>
                {
                    MvpSettings = await MvpSettingsLoader.LoadOrFetchAsync();
                    _cachedMvpVersion = MvpSettings.Version;
                    Console.WriteLine($"[zVIPCore] MVP settings reloaded on map start (version: {MvpSettings.Version})");
                });
            }
        });

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
        if (Config.Modules.PlayerModelsEnabled)
            AddCommand("css_zmodels", "Reload player models from DB (Console only)", Command_ZModels);

        // Console-only: reload weapon DB data for a player
        if (Config.Modules.WeaponsEnabled)
            AddCommand("css_zweapons", "Reload player weapons from DB (Console only)", Command_ZWeapons);

        // Console-only: reload MVP DB data / fetch CDN
        if (Config.Modules.MvpEnabled)
            AddCommand("css_zmvp", "Reload MVP data from DB / Fetch CDN (Console only)", Command_ZMvp);

        // Client+Server: reload JSON configs + CDN fetch
        AddCommand("css_reloadmodel", "Reload model/weapon/mvp JSON configs from CDN", Command_ReloadModel);

        // Sound toggle with configurable permission
        if (Config.Modules.SoundsEnabled)
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
            Server.PrintToConsole("[zVIPCore] Usage: css_zmodels reload <steamid>");
            return;
        }

        if (!ulong.TryParse(info.GetArg(2), out var steamId))
        {
            Server.PrintToConsole($"[zVIPCore] Invalid SteamID: {info.GetArg(2)}");
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
                    target.PrintToChat($"{Localizer["zVIPCore.prefix"]} {Localizer["zVIPCore.web_reload_success"]}");
                }
                Server.PrintToConsole($"[zVIPCore] Reloaded model data for {steamId}");
            });
        });
    }

    private void Command_ZWeapons(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null) { PrintConsoleOnly(player); return; }

        if (info.ArgCount < 3 || info.GetArg(1).ToLowerInvariant() != "reload")
        {
            Server.PrintToConsole("[zVIPCore] Usage: css_zweapons reload <steamid>");
            return;
        }

        if (!ulong.TryParse(info.GetArg(2), out var steamId))
        {
            Server.PrintToConsole($"[zVIPCore] Invalid SteamID: {info.GetArg(2)}");
            return;
        }

        WeaponManager?.ClearPlayerData(steamId);
        Database.ClearPlayerCache(steamId);

        _ = SafeAsync(async () =>
        {
            await Database.PreloadAllPlayerDataAsync(steamId);
            Server.NextFrame(() =>
            {
                var target = FindPlayerBySteamId(steamId);
                if (target != null)
                {
                    WeaponManager?.RefreshPlayerWeapons(target);
                    target.PrintToChat($"{Localizer["zVIPCore.prefix"]} {Localizer["zVIPCore.web_reload_success"]}");
                }
                Server.PrintToConsole($"[zVIPCore] Reloaded weapon data for {steamId}");
            });
        });
    }


    #endregion

    #region Console Command: css_zmvp

    private void Command_ZMvp(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null) { PrintConsoleOnly(player); return; }

        if (info.ArgCount < 2)
        {
            Server.PrintToConsole("[zVIPCore] Usage: css_zmvp reload <steamid> | css_zmvp fetch");
            return;
        }

        var action = info.GetArg(1).ToLowerInvariant();

        if (action == "fetch")
        {
            _ = SafeAsync(async () =>
            {
                try
                {
                    var newSettings = await MvpSettingsLoader.LoadOrFetchAsync();
                    MvpSettings = newSettings;
                    _cachedMvpVersion = newSettings.Version;
                    Server.NextFrame(() =>
                        Server.PrintToConsole($"[zVIPCore] MVP settings updated successfully! Version: {newSettings.Version}"));
                }
                catch (Exception ex)
                {
                    Server.NextFrame(() =>
                        Server.PrintToConsole($"[zVIPCore] Failed to fetch MVP settings: {ex.Message}"));
                }
            });
            return;
        }

        if (action == "reload")
        {
            if (info.ArgCount < 3)
            {
                Server.PrintToConsole("[zVIPCore] Usage: css_zmvp reload <steamid>");
                return;
            }

            if (!ulong.TryParse(info.GetArg(2), out var steamId))
            {
                Server.PrintToConsole($"[zVIPCore] Invalid SteamID: {info.GetArg(2)}");
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
                        target.PrintToChat($"{Localizer["zVIPCore.prefix"]} {Localizer["zVIPCore.web_reload_success"]}");
                    }
                    Server.PrintToConsole($"[zVIPCore] Reloaded MVP data for {steamId}");
                });
            });
            return;
        }

        Server.PrintToConsole("[zVIPCore] Usage: css_zmvp reload <steamid> | css_zmvp fetch");
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
            bool mvpUpdated = false;
            if (Config.Modules.MvpEnabled)
                mvpUpdated = await TryFetchCdnJson(Config.MvpJsonFilename);

            Server.NextFrame(() =>
            {
                try
                {
                    int playerCategories = 0, playerTotal = 0, weaponCollections = 0, weaponTotal = 0;

                    if (Config.Modules.PlayerModelsEnabled)
                    {
                        var newPlayerModels = PlayerModelsConfig.Load(ModuleDirectory);
                        ModelManager?.PrecacheModels(newPlayerModels);
                        ModelManager?.UpdateConfig(newPlayerModels);
                        _cachedModelsVersion = newPlayerModels.Version;
                        playerCategories = newPlayerModels.Categories.Count;
                        playerTotal = newPlayerModels.Categories.Values.Sum(c => c.Count);
                    }
                    if (Config.Modules.WeaponsEnabled)
                    {
                        var newWeaponModels = WeaponModelsConfig.Load(ModuleDirectory);
                        WeaponManager?.PrecacheModels();
                        WeaponManager?.UpdateModelsConfig(newWeaponModels);
                        _cachedWeaponsVersion = newWeaponModels.Version;
                        weaponCollections = newWeaponModels.Weapons.Count;
                        weaponTotal = newWeaponModels.GetTotalSkinsCount();
                    }
                    if (Config.Modules.SoundsEnabled)
                        SoundManager?.UpdateModelsConfig();

                    if (Config.Modules.MvpEnabled)
                    {
                        MvpSettings = MvpSettingsLoader.LoadFromLocal();
                        _cachedMvpVersion = MvpSettings.Version;
                    }

                    // Refresh all connected players for weapons
                    var refreshed = 0;
                    if (Config.Modules.WeaponsEnabled)
                    {
                        foreach (var p in Utilities.GetPlayers().Where(p => p?.IsBot == false && p.IsValid))
                        {
                            try { WeaponManager?.RefreshPlayerWeapons(p); refreshed++; }
                            catch { /* skip */ }
                        }
                    }

                    var msg = Localizer["zVIPCore.reload_success", playerCategories, playerTotal, weaponCollections, weaponTotal];

                    if (player?.IsValid == true)
                    {
                        player.PrintToChat($"{Localizer["zVIPCore.prefix"]} {msg}");
                        player.PrintToChat($"{Localizer["zVIPCore.prefix"]} Refreshed {refreshed} players");
                    }

                    var fetchStatus = modelsUpdated || weaponsUpdated || mvpUpdated ? " (CDN updated)" : " (no CDN changes)";
                    Server.PrintToConsole($"[zVIPCore] Reloaded configs{fetchStatus}. Refreshed {refreshed} players.");
                }
                catch (Exception ex)
                {
                    if (player?.IsValid == true)
                        player.PrintToChat($"{Localizer["zVIPCore.prefix"]} {Localizer["zVIPCore.reload_error", ex.Message]}");
                    Server.PrintToConsole($"[zVIPCore] Error reloading: {ex.Message}");
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
            string localVersion = "";
            if (filename == Config.ModelsJsonFilename) localVersion = _cachedModelsVersion;
            else if (filename == Config.WeaponsJsonFilename) localVersion = _cachedWeaponsVersion;
            else if (filename == Config.MvpJsonFilename) localVersion = _cachedMvpVersion;

            if (remoteVersion == localVersion)
                return false;

            // Save to local config directory
            var configDir = Config.GetConfigDirectory(ModuleDirectory);
            var localPath = Path.Combine(configDir, filename);
            await File.WriteAllTextAsync(localPath, response);

            // Update cached version (thread-safe: only read by main thread in NextFrame)
            if (filename == Config.ModelsJsonFilename)
                _cachedModelsVersion = remoteVersion;
            else if (filename == Config.WeaponsJsonFilename)
                _cachedWeaponsVersion = remoteVersion;
            else if (filename == Config.MvpJsonFilename)
                _cachedMvpVersion = remoteVersion;

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
                Server.ExecuteCommand($"kickid {player.UserId} \"{Localizer["zVIPCore.kick_reason_spam"]}\"");
                reloadInfo.CommandHistory.Clear();
                return;
            }

            player.PrintToChat($" {Localizer["zVIPCore.prefix"]}{Localizer["zVIPCore.website_message", Config.WebsiteUrl]}");

            var timeSinceLastReload = currentTime - reloadInfo.LastReloadTime;
            if (timeSinceLastReload < Config.ReloadCooldownSeconds)
            {
                var remaining = (int)(Config.ReloadCooldownSeconds - timeSinceLastReload);
                player.PrintToChat($"{Localizer["zVIPCore.prefix"]} {Localizer["zVIPCore.cooldown_remaining", remaining]}");
                return;
            }

            reloadInfo.LastReloadTime = currentTime;

            Database.ClearPlayerCache(steamId);
            WeaponManager?.ClearPlayerData(steamId);

            _ = SafeAsync(async () =>
            {
                await Database.PreloadAllPlayerDataAsync(steamId);
                Server.NextFrame(() =>
                {
                    if (player.IsValid)
                        player.PrintToChat($"{Localizer["zVIPCore.prefix"]} {Localizer["zVIPCore.web_reload_success"]}");
                });
            });
        }
    }

    #endregion

    #region Core Event Handlers

    private void OnEntityCreated(CEntityInstance entity)
    {
        if (Config.Modules.WeaponsEnabled) WeaponManager?.OnEntityCreated(entity);
        if (Config.Modules.SmokesEnabled) SmokeManager?.OnEntityCreated(entity);
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player?.IsBot != false) return HookResult.Continue;

        var steamId = player.SteamID;
        Database.ClearPlayerCache(steamId);
        if (Config.Modules.WeaponsEnabled) WeaponManager?.ClearPlayerData(steamId);
        if (Config.Modules.SmokesEnabled) SmokeManager?.ClearPlayerData(steamId);
        _reloadTracking.TryRemove(steamId, out _);

        return HookResult.Continue;
    }

    public override void Unload(bool hotReload)
    {
        MvpManager?.Dispose();
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
            player.PrintToChat($"{Localizer["zVIPCore.prefix"]} {Localizer["zVIPCore.console_only"]}");
    }

    private sealed class ReloadInfo
    {
        public float LastReloadTime { get; set; }
        public List<float> CommandHistory { get; } = new();
    }

    #endregion
}


