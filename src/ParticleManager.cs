using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace zModelsCustom;

/// <summary>
/// Manages kill particle effects (testpart feature).
/// Toggle via !testpart — spawns a particle at the victim's death position.
/// </summary>
public class ParticleManager
{
    private readonly HashSet<ulong> _enabled = new();

    private const string ParticleEffect = "particles/fireworks_bomb.vpcf";
    private const float ParticleLifetime = 4.0f;

    #region Command Handler

    public void OnToggleTestPart(CCSPlayerController? player, CommandInfo info)
    {
        if (player?.IsValid != true || player.SteamID == 0)
            return;

        var steamId = player.SteamID;
        var prefix = zModelsCustom.Instance.Localizer["zModelsCustom.prefix"];

        if (_enabled.Contains(steamId))
        {
            _enabled.Remove(steamId);
            player.PrintToChat($"{prefix} {zModelsCustom.Instance.Localizer["zModelsCustom.testpart_disabled"]}");
        }
        else
        {
            _enabled.Add(steamId);
            player.PrintToChat($"{prefix} {zModelsCustom.Instance.Localizer["zModelsCustom.testpart_enabled"]}");
        }
    }

    #endregion

    #region Event Handlers

    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var attacker = @event.Attacker;
        var victim = @event.Userid;

        if (attacker == null || !attacker.IsValid || attacker.SteamID == 0)
            return HookResult.Continue;

        if (victim == null || !victim.IsValid)
            return HookResult.Continue;

        // Don't trigger on suicide
        if (attacker.SteamID == victim.SteamID)
            return HookResult.Continue;

        if (!_enabled.Contains(attacker.SteamID))
            return HookResult.Continue;

        // Get victim's death position
        var victimPawn = victim.PlayerPawn?.Value;
        if (victimPawn?.AbsOrigin == null)
            return HookResult.Continue;

        var deathPos = new Vector(victimPawn.AbsOrigin.X, victimPawn.AbsOrigin.Y, victimPawn.AbsOrigin.Z);

        // Spawn particle at victim's position
        Server.NextFrame(() =>
        {
            var particle = Utilities.CreateEntityByName<CParticleSystem>("info_particle_system");
            if (particle == null)
                return;

            particle.EffectName = ParticleEffect;
            particle.Teleport(deathPos, new QAngle(), new Vector());
            particle.DispatchSpawn();
            particle.AcceptInput("Start");

            // Force stop and remove the particle after lifetime
            zModelsCustom.Instance.AddTimer(ParticleLifetime, () =>
            {
                if (particle?.IsValid == true)
                {
                    particle.AcceptInput("Stop");
                    particle.AcceptInput("Kill");
                    particle.Remove();
                }
            });
        });

        return HookResult.Continue;
    }

    #endregion

    #region Player Data

    public void ClearPlayerData(ulong steamId)
    {
        _enabled.Remove(steamId);
    }

    #endregion
}
