using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Admin;
using Microsoft.Extensions.Logging;
using Menu;

namespace MVPAnthem;

public partial class MVPAnthem : BasePlugin
{
    public override string ModuleAuthor => "T3Marius & zhw1nq";
    public override string ModuleName => "zMVP-Anthem";
    public override string ModuleVersion => "3.1.0";

    public static MVPAnthem Instance { get; set; } = new();
    public PluginConfig Config { get; set; } = new();
    public MVPSettingsConfig MVPSettings { get; set; } = new();
    public KitsuneMenu? Menu { get; set; }
    public IDatabaseProvider? DatabaseProvider { get; set; }
    public PlayerCache PlayerCache { get; set; } = null!;

    public override void Load(bool hotReload)
    {
        Instance = this;
        Menu = new KitsuneMenu(this);
        Config = ConfigLoader.Load();

        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(MVPSettingsLoader).TypeHandle);

        Task.Run(async () =>
        {
            MVPSettings = await MVPSettingsLoader.LoadOrFetchAsync();
            Logger.LogInformation("[MVP-Anthem] MVP settings loaded successfully");
        });

        InitializeDatabase();
        Events.RegisterEvents();
        RegisterCommands();
    }

    public override void Unload(bool hotReload) => Events.Dispose();

    private void InitializeDatabase()
    {
        var db = Config.Database;
        var connStr = $"Server={db.Host};Port={db.Port};Database={db.Database};" +
                      $"User={db.User};Password={db.Password};SslMode={db.SslMode};";

        DatabaseProvider = new MySqlDatabaseProvider(connStr, Logger);
        PlayerCache = new PlayerCache(DatabaseProvider);

        Task.Run(async () =>
        {
            if (await DatabaseProvider.TestConnectionAsync())
            {
                Logger.LogInformation("[MVP-Anthem] Database connection successful");
                await DatabaseProvider.InitializeAsync();
            }
            else
            {
                Logger.LogError("[MVP-Anthem] Failed to connect to database!");
            }
        });
    }

    private void RegisterCommands()
    {
        foreach (var cmd in Config.Commands.MVPCommands)
            AddCommand($"css_{cmd}", "Opens the MVP Menu", OnMVPCommand);

        AddCommand("css_mvp_fetch", "Force fetch MVP settings from CDN", OnMVPFetchCommand);
        AddCommand("css_mvp_reload", "Reload MVP settings from local file", OnMVPReloadCommand);
    }

    private void OnMVPCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid) return;
        MVPMenu.Display(player);
    }

    private void OnMVPFetchCommand(CCSPlayerController? player, CommandInfo info)
    {
        ExecuteSettingsRefresh(player, "Fetching MVP settings from CDN...", "forced MVP settings fetch");
    }

    private void OnMVPReloadCommand(CCSPlayerController? player, CommandInfo info)
    {
        ExecuteSettingsRefresh(player, "Reloading MVP settings from local file...", "reloaded MVP settings");
    }

    private void ExecuteSettingsRefresh(CCSPlayerController? player, string startMsg, string logAction)
    {
        if (player == null || !player.IsValid) return;

        if (!AdminManager.PlayerHasPermissions(player, "@css/root"))
        {
            player.PrintToChat($"{Localizer["prefix"]}You don't have permission to use this command.");
            return;
        }

        player.PrintToChat($"{Localizer["prefix"]}{startMsg}");

        int slot = player.Slot;
        string playerName = player.PlayerName;
        Task.Run(async () =>
        {
            try
            {
                var newSettings = await MVPSettingsLoader.LoadOrFetchAsync();
                MVPSettings = newSettings;
                Server.NextFrame(() =>
                {
                    var p = Utilities.GetPlayerFromSlot(slot);
                    if (p != null && p.IsValid && p.Connected == PlayerConnectedState.PlayerConnected)
                        p.PrintToChat($"{Localizer["prefix"]}MVP settings updated successfully! Version: {newSettings.Version}");
                });
                Logger.LogInformation($"[MVP-Anthem] Admin {playerName} {logAction}");
            }
            catch (Exception ex)
            {
                Server.NextFrame(() =>
                {
                    var p = Utilities.GetPlayerFromSlot(slot);
                    if (p != null && p.IsValid && p.Connected == PlayerConnectedState.PlayerConnected)
                        p.PrintToChat($"{Localizer["prefix"]}Failed: {ex.Message}");
                });
                Logger.LogError($"[MVP-Anthem] Error: {ex.Message}");
            }
        });
    }
}
