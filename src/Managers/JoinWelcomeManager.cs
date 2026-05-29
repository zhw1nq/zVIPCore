using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;
using System.Collections.Generic;
using System.Linq;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace zVIPCore;

public class JoinWelcomeManager
{
    private readonly HashSet<ulong> _welcomedPlayers = new();
    private readonly List<(string Message, float StartTime, float Duration)> _activeMessages = new();
    private Timer? _centerHtmlTickTimer = null;

    public void OnTick()
    {
        // Check for newly joined VIP players that should be welcomed
        foreach (var player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid || player.IsBot) continue;

            // Wait until the player is fully authorized and permissions are loaded
            if (player.AuthorizedSteamID == null) continue;

            var steamId = player.SteamID;
            if (_welcomedPlayers.Contains(steamId)) continue;

            // Check if player has joined active game round (either Terrorist or Counter-Terrorist)
            if (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist) continue;

            // Ensure pawn is valid (i.e. they are fully spawned in a round)
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) continue;

            string? welcomeMessage = null;

            if (AdminManager.PlayerHasPermissions(player, "@css/tgs-owner"))
            {
                welcomeMessage = zVIPCore.Instance.Localizer["join_welcome.owner_join", player.PlayerName];
            }
            else if (AdminManager.PlayerHasPermissions(player, "@css/tgs-staff"))
            {
                welcomeMessage = zVIPCore.Instance.Localizer["join_welcome.staff_join", player.PlayerName];
            }
            else if (AdminManager.PlayerHasPermissions(player, "@css/tgs-svip"))
            {
                welcomeMessage = zVIPCore.Instance.Localizer["join_welcome.svip_join", player.PlayerName];
            }
            else if (AdminManager.PlayerHasPermissions(player, "@css/tgs-vip"))
            {
                welcomeMessage = zVIPCore.Instance.Localizer["join_welcome.vip_join", player.PlayerName];
            }

            if (welcomeMessage != null)
            {
                _welcomedPlayers.Add(steamId);
                
                // Add to active repeating rendering queue
                float duration = 2.0f;
                _activeMessages.Add((welcomeMessage, Server.CurrentTime, duration));

                StartHtmlTimerIfNeeded();

                var soundPath = zVIPCore.Config.JoinWelcome.SoundPath;
                if (!string.IsNullOrEmpty(soundPath))
                {
                    PlaySoundOnAllPlayers(soundPath);
                }
            }
        }
    }

    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot)
            return HookResult.Continue;

        var steamId = player.SteamID;
        if (_welcomedPlayers.Remove(steamId))
        {
            string? leaveMessage = null;

            if (AdminManager.PlayerHasPermissions(player, "@css/tgs-owner"))
            {
                leaveMessage = zVIPCore.Instance.Localizer["join_welcome.owner_leave", player.PlayerName];
            }
            else if (AdminManager.PlayerHasPermissions(player, "@css/tgs-staff"))
            {
                leaveMessage = zVIPCore.Instance.Localizer["join_welcome.staff_leave", player.PlayerName];
            }
            else if (AdminManager.PlayerHasPermissions(player, "@css/tgs-svip"))
            {
                leaveMessage = zVIPCore.Instance.Localizer["join_welcome.svip_leave", player.PlayerName];
            }
            else if (AdminManager.PlayerHasPermissions(player, "@css/tgs-vip"))
            {
                leaveMessage = zVIPCore.Instance.Localizer["join_welcome.vip_leave", player.PlayerName];
            }

            if (leaveMessage != null)
            {
                float duration = 2.0f;
                _activeMessages.Add((leaveMessage, Server.CurrentTime, duration));

                StartHtmlTimerIfNeeded();

                var soundPath = zVIPCore.Config.JoinWelcome.SoundPath;
                if (!string.IsNullOrEmpty(soundPath))
                {
                    PlaySoundOnAllPlayers(soundPath);
                }
            }
        }

        return HookResult.Continue;
    }

    private void StartHtmlTimerIfNeeded()
    {
        if (_centerHtmlTickTimer == null)
        {
            _centerHtmlTickTimer = zVIPCore.Instance.AddTimer(0.1f, OnTickWelcomeHtml, TimerFlags.REPEAT);
        }
    }

    private void OnTickWelcomeHtml()
    {
        float currentTime = Server.CurrentTime;
        _activeMessages.RemoveAll(msg => currentTime - msg.StartTime >= msg.Duration);

        if (_activeMessages.Count > 0)
        {
            var combinedMessage = string.Join("<br>", _activeMessages.Select(m => m.Message));
            foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV))
            {
                p.PrintToCenterHtml(combinedMessage);
            }
        }
        else
        {
            foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV))
            {
                p.PrintToCenterHtml("");
            }
            _centerHtmlTickTimer?.Kill();
            _centerHtmlTickTimer = null;
        }
    }

    private static void PlaySoundOnAllPlayers(string soundName)
    {
        foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV))
        {
            player.EmitSound(soundName, player, 1.0f);
        }
    }
}
