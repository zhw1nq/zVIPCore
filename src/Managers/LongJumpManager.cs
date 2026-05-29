using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;

namespace zVIPCore;

public class LongJumpManager
{
    private static Config Config => zVIPCore.Config;

    public void Load()
    {
        // Execute server command to enable legacy jump mechanics
        Server.ExecuteCommand("sv_legacy_jump 1");
    }

    public HookResult OnPlayerJump(EventPlayerJump @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot) 
            return HookResult.Continue;

        var longJumpConfig = Config.LongJump;
        if (!AdminManager.PlayerHasPermissions(player, longJumpConfig.AdminFlag))
            return HookResult.Continue;

        Server.NextFrame(() =>
        {
            if (player == null || !player.IsValid) return;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;

            if (longJumpConfig.OnlyApplyForceInZAxis)
            {
                pawn.BaseVelocity.Add(new Vector(z: 100 * longJumpConfig.JumpBoost));
            }
            else
            {
                pawn.BaseVelocity.Add(new Vector(
                    pawn.Velocity.X * longJumpConfig.JumpBoost,
                    pawn.Velocity.Y * longJumpConfig.JumpBoost,
                    100 * longJumpConfig.JumpBoost
                ));
            }
        });

        return HookResult.Continue;
    }
}
