using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;

namespace zVIPCore;

/// <summary>
/// Dev tools for testing particle effects on a dummy bot.
/// All commands require @css/root permission.
/// Commands:
///   css_dev_spawnbot  — Spawn a frozen bot at your position
///   css_dev_joinvpcf {path} — Attach a particle effect to the bot
///   css_dev_clearbot  — Remove the bot and all test particles
/// </summary>
public class DevParticlesManager
{
    private CCSPlayerController? _devBot;
    private readonly List<CParticleSystem> _activeParticles = new();

    #region Commands

    [RequiresPermissions("@css/root")]
    public void Command_SpawnBot(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid)
        {
            Server.PrintToConsole("[zVIPCore] This command must be used in-game.");
            return;
        }

        if (_devBot != null && _devBot.IsValid)
        {
            player.PrintToChat($" {ChatColors.Gold}DevTools {ChatColors.Silver}» {ChatColors.Red}Bot đã tồn tại! Dùng {ChatColors.Yellow}!dev_clearbot {ChatColors.Red}trước.");
            return;
        }

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
        {
            player.PrintToChat($" {ChatColors.Gold}DevTools {ChatColors.Silver}» {ChatColors.Red}Không tìm thấy vị trí player.");
            return;
        }

        // Store spawn position before adding bot
        var pos = pawn.AbsOrigin;
        var ang = pawn.AbsRotation;

        // Add a bot
        Server.ExecuteCommand("bot_quota_mode normal");
        Server.ExecuteCommand("bot_add_ct");

        // Find and freeze the bot on next frame
        zVIPCore.Instance.AddTimer(0.5f, () =>
        {
            var bot = FindNewestBot();
            if (bot == null)
            {
                player.PrintToChat($" {ChatColors.Gold}DevTools {ChatColors.Silver}» {ChatColors.Red}Không thể tạo bot. Kiểm tra bot_quota.");
                return;
            }

            _devBot = bot;

            // Teleport and freeze after spawn
            zVIPCore.Instance.AddTimer(0.3f, () =>
            {
                var botPawn = bot.PlayerPawn.Value;
                if (botPawn == null || !botPawn.IsValid) return;

                // Teleport to player position
                botPawn.Teleport(pos, ang, new Vector(0, 0, 0));

                // Freeze the bot
                botPawn.MoveType = MoveType_t.MOVETYPE_NONE;
                botPawn.ActualMoveType = MoveType_t.MOVETYPE_NONE;

                player.PrintToChat($" {ChatColors.Gold}DevTools {ChatColors.Silver}» {ChatColors.Green}Bot đã spawn tại vị trí của bạn. Dùng {ChatColors.Yellow}!dev_joinvpcf <path> {ChatColors.Green}để test particle.");
            });
        });
    }

    [RequiresPermissions("@css/root")]
    public void Command_JoinVpcf(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid) return;

        if (_devBot == null || !_devBot.IsValid || _devBot.PlayerPawn.Value == null)
        {
            player.PrintToChat($" {ChatColors.Gold}DevTools {ChatColors.Silver}» {ChatColors.Red}Chưa có bot! Dùng {ChatColors.Yellow}!dev_spawnbot {ChatColors.Red}trước.");
            return;
        }

        if (info.ArgCount < 2)
        {
            player.PrintToChat($" {ChatColors.Gold}DevTools {ChatColors.Silver}» {ChatColors.Yellow}Usage: !dev_joinvpcf <vpcf_path>");
            player.PrintToChat($" {ChatColors.Gold}DevTools {ChatColors.Silver}» {ChatColors.Grey}VD: !dev_joinvpcf particles/my_effect.vpcf");
            return;
        }

        var vpcfPath = info.GetArg(1);
        var botPawn = _devBot.PlayerPawn.Value;
        if (botPawn == null || !botPawn.IsValid) return;

        try
        {
            var particle = Utilities.CreateEntityByName<CParticleSystem>("info_particle_system");
            if (particle == null || !particle.IsValid)
            {
                player.PrintToChat($" {ChatColors.Gold}DevTools {ChatColors.Silver}» {ChatColors.Red}Không thể tạo particle entity.");
                return;
            }

            particle.EffectName = vpcfPath;
            particle.Teleport(botPawn.AbsOrigin, botPawn.AbsRotation, new Vector(0, 0, 0));
            particle.DispatchSpawn();
            particle.AcceptInput("Start");

            // Parent to bot pawn so particle follows
            particle.AcceptInput("SetParent", botPawn, null, "!activator");

            _activeParticles.Add(particle);

            player.PrintToChat($" {ChatColors.Gold}DevTools {ChatColors.Silver}» {ChatColors.Green}Particle spawned: {ChatColors.Yellow}{vpcfPath}");
            player.PrintToChat($" {ChatColors.Gold}DevTools {ChatColors.Silver}» {ChatColors.Grey}Tổng particles đang active: {_activeParticles.Count}");
        }
        catch (Exception ex)
        {
            player.PrintToChat($" {ChatColors.Gold}DevTools {ChatColors.Silver}» {ChatColors.Red}Lỗi: {ex.Message}");
        }
    }

    [RequiresPermissions("@css/root")]
    public void Command_ClearBot(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid) return;

        int particlesRemoved = 0;

        // Remove all test particles
        foreach (var particle in _activeParticles)
        {
            if (particle.IsValid)
            {
                particle.AcceptInput("Stop");
                particle.Remove();
                particlesRemoved++;
            }
        }
        _activeParticles.Clear();

        // Kick the bot
        if (_devBot != null && _devBot.IsValid)
        {
            Server.ExecuteCommand($"bot_kick {_devBot.PlayerName}");
            _devBot = null;
        }

        player.PrintToChat($" {ChatColors.Gold}DevTools {ChatColors.Silver}» {ChatColors.Green}Đã xóa bot + {particlesRemoved} particle(s).");
    }

    #endregion

    #region Helpers

    private static CCSPlayerController? FindNewestBot()
    {
        return Utilities.GetPlayers()
            .Where(p => p.IsValid && p.IsBot && !p.IsHLTV)
            .OrderByDescending(p => p.Slot)
            .FirstOrDefault();
    }

    public void Cleanup()
    {
        foreach (var particle in _activeParticles)
        {
            if (particle.IsValid)
            {
                particle.AcceptInput("Stop");
                particle.Remove();
            }
        }
        _activeParticles.Clear();

        if (_devBot != null && _devBot.IsValid)
        {
            Server.ExecuteCommand($"bot_kick {_devBot.PlayerName}");
            _devBot = null;
        }
    }

    #endregion
}
