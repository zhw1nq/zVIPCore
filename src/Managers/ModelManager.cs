using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Drawing;

namespace zVIPCore;

public class ModelManager
{
    private static readonly Color LegDisabledColor = Color.FromArgb(254, 255, 255, 255);
    private static readonly Color DefaultColor = Color.FromArgb(255, 255, 255, 255);

    private static readonly IReadOnlyDictionary<CsTeam, string> DefaultModels = new Dictionary<CsTeam, string>
    {
        { CsTeam.CounterTerrorist, "characters/models/ctm_sas/ctm_sas.vmdl" },
        { CsTeam.Terrorist, "characters/models/tm_phoenix/tm_phoenix.vmdl" }
    };

    // Cached config reference — updated on reload, avoids file I/O on every spawn
    private PlayerModelsConfig _cachedConfig = new();

    public void UpdateConfig(PlayerModelsConfig config) => _cachedConfig = config;

    public void ApplyModel(CCSPlayerController player, PlayerModelData model)
    {
        if (!IsValidPlayer(player)) return;

        var pawn = player.PlayerPawn.Value;
        if (pawn == null) return;
        RemoveGloves(pawn);

        Server.NextFrame(() =>
        {
            if (!IsValidPlayer(player)) return;
            var currentPawn = player.PlayerPawn.Value;
            if (currentPawn == null) return;

            currentPawn.SetModel(model.Model);
            currentPawn.Render = model.DisableLeg ? LegDisabledColor : DefaultColor;
        });
    }

    public void ResetModel(CCSPlayerController player)
    {
        if (!IsValidPlayer(player)) return;
        if (!DefaultModels.TryGetValue(player.Team, out var defaultModel)) return;

        Server.NextFrame(() =>
        {
            if (!IsValidPlayer(player)) return;
            var currentPawn = player.PlayerPawn.Value;
            if (currentPawn == null) return;

            currentPawn.SetModel(defaultModel);
            currentPawn.Render = DefaultColor;
        });
    }

    private static void RemoveGloves(CCSPlayerPawn pawn)
    {
        if (pawn.EconGloves == null) return;
        pawn.EconGloves.Initialized = false;
        pawn.EconGloves.ItemDefinitionIndex = 0;
    }

    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!IsValidPlayerForModel(player)) return HookResult.Continue;

        var steamId = player!.SteamID;
        var team = player.Team;

        _ = zVIPCore.SafeAsync(() => LoadAndApplyModelAsync(player, steamId, team));

        return HookResult.Continue;
    }

    private async Task LoadAndApplyModelAsync(CCSPlayerController player, ulong steamId, CsTeam team)
    {
        var modelId = await zVIPCore.Database.GetPlayerModelAsync(steamId, team);
        if (modelId == null) return;

        // Use cached config instead of loading from disk every spawn
        var model = _cachedConfig.FindModelByUniqueId(modelId);

        if (model == null)
        {
            await zVIPCore.Database.RemovePlayerModelAsync(steamId, team);
            return;
        }

        Server.NextFrame(() => ProcessModelApplication(player, steamId, team, model));
    }

    private void ProcessModelApplication(CCSPlayerController player, ulong steamId, CsTeam team, PlayerModelData model)
    {
        if (!IsValidPlayer(player)) return;

        // Re-check team at application time (player may have swapped during async)
        var currentTeam = player.Team;

        if (!IsModelSlotValid(model, currentTeam))
        {
            _ = zVIPCore.SafeAsync(async () =>
            {
                await zVIPCore.Database.RemovePlayerModelAsync(steamId, team);
                Server.NextFrame(() =>
                {
                    if (IsValidPlayer(player))
                        ResetModel(player);
                });
            });
            return;
        }

        // Apply directly — already on main thread via NextFrame from caller
        ApplyModel(player, model);
    }

    private static bool IsModelSlotValid(PlayerModelData model, CsTeam team)
    {
        var slot = model.Slot;
        if (string.Equals(slot, "ALL", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(slot, "CT", StringComparison.OrdinalIgnoreCase)) return team == CsTeam.CounterTerrorist;
        if (string.Equals(slot, "T", StringComparison.OrdinalIgnoreCase)) return team == CsTeam.Terrorist;
        return false;
    }

    private static bool IsValidPlayer(CCSPlayerController? player) =>
        player?.IsValid == true && player.PlayerPawn.Value != null;

    private static bool IsValidPlayerForModel(CCSPlayerController? player) =>
        player?.IsBot == false && player.Team >= CsTeam.Terrorist;

    public void PrecacheModels(PlayerModelsConfig models)
    {
        foreach (var model in models.Categories.Values.SelectMany(c => c.Values))
        {
            Server.PrecacheModel(model.Model);
            if (!string.IsNullOrEmpty(model.ArmModel))
                Server.PrecacheModel(model.ArmModel);
        }
    }
}
