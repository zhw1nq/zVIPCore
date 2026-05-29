using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;
using System.Collections.Generic;
using System.Linq;

namespace zVIPCore;

public class DoubleJumpManager
{
    private class PlayerJumpInfo
    {
        public PlayerButtons PrevButtons { get; set; }
        public PlayerFlags PrevFlags { get; set; }
        public int JumpsCount { get; set; }
    }

    private readonly Dictionary<int, PlayerJumpInfo> _playerJumpStates = new();
    private static Config Config => zVIPCore.Config;

    public void OnMapStart()
    {
        _playerJumpStates.Clear();
    }

    public void OnPlayerDisconnect(CCSPlayerController player)
    {
        if (player == null || !player.IsValid) return;
        _playerJumpStates.Remove(player.Slot);
    }

    public void OnTick()
    {
        var doubleJumpConfig = Config.DoubleJump;
        if (doubleJumpConfig.JumpsCount <= 1) return;

        var players = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV).ToList();

        foreach (var player in players)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) continue;

            if (!string.IsNullOrWhiteSpace(doubleJumpConfig.AdminFlag) && 
                !AdminManager.PlayerHasPermissions(player, doubleJumpConfig.AdminFlag))
            {
                continue;
            }

            var slot = player.Slot;
            if (!_playerJumpStates.TryGetValue(slot, out var info))
            {
                info = new PlayerJumpInfo();
                _playerJumpStates[slot] = info;
            }

            var currentFlags = (PlayerFlags)pawn.Flags;
            var currentButtons = player.Buttons;

            var wasGrounded = (info.PrevFlags & PlayerFlags.FL_ONGROUND) != 0;
            var isGrounded = (currentFlags & PlayerFlags.FL_ONGROUND) != 0;

            var jumpWasPressed = (info.PrevButtons & PlayerButtons.Jump) != 0;
            var jumpIsPressed = (currentButtons & PlayerButtons.Jump) != 0;

            // Reset or increment jump counts
            if (isGrounded)
            {
                info.JumpsCount = 0;
            }
            else if (info.JumpsCount < 1)
            {
                info.JumpsCount = 1;
            }

            // Perform double jump
            if (!jumpWasPressed && jumpIsPressed && !wasGrounded && !isGrounded && info.JumpsCount < doubleJumpConfig.JumpsCount)
            {
                info.JumpsCount++;

                if (doubleJumpConfig.AllowInstantJump || pawn.AbsVelocity.Z < 0)
                {
                    pawn.AbsVelocity.Z = doubleJumpConfig.Velocity;
                }
            }

            info.PrevFlags = currentFlags;
            info.PrevButtons = currentButtons;
        }
    }
}
