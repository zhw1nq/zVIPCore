using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System;

namespace zVIPCore;

public class ParticleManager
{
    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot)
            return HookResult.Continue;

        var teamStr = player.Team == CsTeam.CounterTerrorist ? "CT" : "T";
        var particlePath = zVIPCore.Database.GetPlayerParticleCached(player.SteamID, teamStr);
        if (string.IsNullOrEmpty(particlePath))
            return HookResult.Continue;

        // Use Server.NextFrame instead of a Timer to delay spawn to the very next engine frame.
        // This is extremely optimized and completely avoids using a spawn Timer!
        Server.NextFrame(() =>
        {
            if (player == null || !player.IsValid) return;
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;

            try
            {
                var particle = Utilities.CreateEntityByName<CParticleSystem>("info_particle_system");
                if (particle == null || !particle.IsValid) return;

                particle.EffectName = particlePath;
                particle.Teleport(pawn.AbsOrigin, pawn.AbsRotation, new Vector(0, 0, 0));
                particle.DispatchSpawn();
                particle.AcceptInput("Start");

                // Parent to player pawn so particle follows
                particle.AcceptInput("SetParent", pawn, null, "!activator");

                // Auto-cleanup after configured duration (e.g. 5 seconds) to remove the entity
                float duration = zVIPCore.Config.Particles.DefaultDurationSeconds;
                if (duration > 0.0f)
                {
                    zVIPCore.Instance.AddTimer(duration, () =>
                    {
                        if (particle.IsValid)
                        {
                            particle.AcceptInput("Stop");
                            particle.Remove();
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[zVIPCore] Error spawning spawn particle for {player.PlayerName}: {ex.Message}");
            }
        });

        return HookResult.Continue;
    }
}
