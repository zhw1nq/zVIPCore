using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Collections.Concurrent;

namespace zVIPCore;

public class KillStreakManager
{
    private readonly ConcurrentDictionary<int, int> _killStreaks = new(); // slot → kill count
    private readonly Dictionary<int, (string Message, float Duration, List<int> RecipientSlots, float StartTime)> _centerMessageLines = new();

    #region Event Handlers

    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot || player.IsHLTV)
            return HookResult.Continue;

        _killStreaks[player.Slot] = 0;
        return HookResult.Continue;
    }

    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var victim = @event.Userid;
        var attacker = @event.Attacker;
        var weapon = @event.Weapon;

        if (victim == null || !victim.IsValid) return HookResult.Continue;
        if (attacker == null || !attacker.IsValid || attacker.IsBot || attacker.IsHLTV) return HookResult.Continue;
        if (victim == attacker || victim.TeamNum == attacker.TeamNum) return HookResult.Continue;

        var slot = attacker.Slot;
        _killStreaks.AddOrUpdate(slot, 1, (_, old) => old + 1);

        int killCount = _killStreaks[slot];
        var config = Config.KillStreak;

        if (config.KillIcons.Count == 0) return HookResult.Continue;

        var settings = GetKillStreakSettings(killCount);
        if (settings == null) return HookResult.Continue;

        DisplayKillStreakIcon(attacker, settings);
        DisplayKillInfo(victim, attacker, weapon, settings.Duration);
        PlayKillStreakSound(attacker, settings);
        SendChatNotification(attacker, victim, weapon, killCount, settings);
        DisplayHTMLKillAll(attacker, victim, weapon, settings);

        return HookResult.Continue;
    }

    public void OnPlayerDisconnect(CCSPlayerController player)
    {
        if (player == null || !player.IsValid) return;

        _killStreaks.TryRemove(player.Slot, out _);
        CleanupPlayerFromCenterMessages(player.Slot);
    }

    #endregion

    #region Kill Streak Logic

    private KillStreakIconsSettings? GetKillStreakSettings(int killCount)
    {
        var icons = Config.KillStreak.KillIcons;

        if (icons.TryGetValue(killCount, out var settings))
            return settings;

        if (Config.KillStreak.LoopIfKillIconsEnd && icons.Count > 0)
        {
            int maxKey = icons.Keys.Max();
            if (killCount > maxKey)
                return icons[maxKey];
        }

        return null;
    }

    private void DisplayKillStreakIcon(CCSPlayerController attacker, KillStreakIconsSettings settings)
    {
        if (string.IsNullOrEmpty(settings.Icon)) return;

        var recipientSlots = new List<int> { attacker.Slot };
        UpdateCenterMessageLine(1, settings.Icon, recipientSlots, settings.Duration, true);
    }

    private void DisplayKillInfo(CCSPlayerController victim, CCSPlayerController attacker, string weapon, float duration)
    {
        if (!Config.KillStreak.ShowKillInfo) return;

        var localizer = zVIPCore.Instance.Localizer;
        var weaponClean = RemoveWeaponPrefix(weapon).ToUpper();

        // Use localizer for kill info HTML message
        var msg = localizer["killstreak.kill_info", victim.PlayerName, weaponClean];
        var msgStr = msg.ToString();
        if (string.IsNullOrEmpty(msgStr) || msgStr == $"killstreak.kill_info")
        {
            // Fallback if no localization found
            msgStr = $"<br><font class='fontSize-m' color='red'>Killed</font> <font class='fontSize-m' color='lime'>{victim.PlayerName}</font> <font class='fontSize-m' color='gold'>[{weaponClean}]</font>";
        }

        var recipientSlots = new List<int> { attacker.Slot };
        UpdateCenterMessageLine(2, msgStr, recipientSlots, duration, true);
    }

    private void PlayKillStreakSound(CCSPlayerController attacker, KillStreakIconsSettings settings)
    {
        if (string.IsNullOrEmpty(settings.Sound)) return;

        if (settings.BroadcastSoundToAll)
        {
            foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV))
                p.EmitSound(settings.Sound, p, Config.KillStreak.SoundVolume);
        }
        else
        {
            attacker.EmitSound(settings.Sound, attacker, Config.KillStreak.SoundVolume);
        }
    }

    private void SendChatNotification(CCSPlayerController attacker, CCSPlayerController victim, string weapon, int killCount, KillStreakIconsSettings settings)
    {
        if (!settings.EnableChatNotification) return;

        var localizer = zVIPCore.Instance.Localizer;
        var weaponClean = RemoveWeaponPrefix(weapon).ToUpper();

        // Use localizer: killstreak.kill_chat with {0}=attacker, {1}=victim, {2}=weapon, {3}=killcount
        var msg = localizer["killstreak.kill_chat", attacker.PlayerName, victim.PlayerName, weaponClean, killCount];
        var msgStr = msg.ToString();
        if (!string.IsNullOrEmpty(msgStr) && msgStr != "killstreak.kill_chat")
        {
            var prefix = localizer["zVIPCore.prefix"];
            Server.PrintToChatAll($"{prefix}{msgStr}");
        }
    }

    private void DisplayHTMLKillAll(CCSPlayerController attacker, CCSPlayerController victim, string weapon, KillStreakIconsSettings settings)
    {
        if (string.IsNullOrEmpty(settings.HTMLKillAll)) return;

        string formattedHTML = settings.HTMLKillAll
            .Replace("{0}", attacker.PlayerName)
            .Replace("{1}", victim.PlayerName)
            .Replace("{2}", RemoveWeaponPrefix(weapon).ToUpper());

        var otherPlayerSlots = Utilities.GetPlayers()
            .Where(p => p.IsValid && !p.IsBot && !p.IsHLTV && p.Slot != attacker.Slot)
            .Select(p => p.Slot)
            .ToList();

        if (otherPlayerSlots.Count > 0)
            UpdateCenterMessageLine(3, formattedHTML, otherPlayerSlots, settings.Duration, true);
    }

    #endregion

    #region Center Message System

    public void OnTick()
    {
        if (_centerMessageLines.Count == 0) return;

        var validPlayers = Utilities.GetPlayers()
            .Where(p => p.IsValid && !p.IsBot && !p.IsHLTV)
            .ToList();

        if (validPlayers.Count == 0) return;

        // Clean up expired lines
        var expiredLines = _centerMessageLines
            .Where(line => Server.CurrentTime - line.Value.StartTime >= line.Value.Duration)
            .Select(line => line.Key)
            .ToList();

        foreach (var lineId in expiredLines)
            _centerMessageLines.Remove(lineId);

        if (_centerMessageLines.Count == 0) return;

        // Display messages per player
        var sortedLines = _centerMessageLines.OrderBy(kvp => kvp.Key).ToList();

        foreach (var player in validPlayers)
        {
            try
            {
                var playerMessages = sortedLines
                    .Where(line => line.Value.RecipientSlots.Contains(player.Slot))
                    .Select(line => line.Value.Message)
                    .ToList();

                if (playerMessages.Count > 0)
                    player.PrintToCenterHtml(string.Join("<br>", playerMessages));
            }
            catch { }
        }
    }

    private void UpdateCenterMessageLine(int lineId, string message, List<int>? recipientSlots = null, float duration = 5f, bool resetTimer = false)
    {
        if (lineId <= 0 || string.IsNullOrWhiteSpace(message)) return;

        if (!_centerMessageLines.ContainsKey(lineId))
        {
            _centerMessageLines[lineId] = (message, duration, recipientSlots ?? new List<int>(), Server.CurrentTime);
            return;
        }

        var existing = _centerMessageLines[lineId];
        _centerMessageLines[lineId] = (
            message,
            resetTimer ? duration : Math.Max(0, existing.Duration - (Server.CurrentTime - existing.StartTime)),
            recipientSlots ?? existing.RecipientSlots,
            resetTimer ? Server.CurrentTime : existing.StartTime
        );
    }

    private void ExtendCenterMessageLineContent(int lineId, string additionalMessage, float additionalDuration)
    {
        if (lineId <= 0 || !_centerMessageLines.ContainsKey(lineId)) return;
        if (string.IsNullOrEmpty(additionalMessage)) return;

        var existing = _centerMessageLines[lineId];
        _centerMessageLines[lineId] = (
            existing.Message + additionalMessage,
            additionalDuration > 0 ? existing.Duration + additionalDuration : existing.Duration,
            existing.RecipientSlots,
            additionalDuration > 0 ? Server.CurrentTime : existing.StartTime
        );
    }

    private void CleanupPlayerFromCenterMessages(int playerSlot)
    {
        var keys = _centerMessageLines.Keys.ToList();
        foreach (var lineId in keys)
        {
            var line = _centerMessageLines[lineId];
            if (line.RecipientSlots.Contains(playerSlot))
            {
                line.RecipientSlots.Remove(playerSlot);
                if (line.RecipientSlots.Count == 0)
                    _centerMessageLines.Remove(lineId);
                else
                    _centerMessageLines[lineId] = line;
            }
        }
    }

    #endregion

    #region Helpers

    private static string RemoveWeaponPrefix(string weaponName)
    {
        if (string.IsNullOrEmpty(weaponName)) return weaponName;
        return weaponName.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase)
            ? weaponName.Substring("weapon_".Length)
            : weaponName;
    }

    public void Clear()
    {
        _killStreaks.Clear();
        _centerMessageLines.Clear();
    }

    #endregion

    private static Config Config => zVIPCore.Config;
}
