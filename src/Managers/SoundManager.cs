using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using zVIPCore.Data;
using zVIPCore.Utils;

namespace zVIPCore;

public class SoundManager
{
    private readonly PlayerPawnCache _pawnCache = new();
    private readonly FireSoundCache _fireSoundCache = new(MaxPlayerSlots);
    private readonly bool[] _customSoundEnabledBySlot = new bool[MaxPlayerSlots];
    private readonly Dictionary<ulong, bool> _customSoundEnabledBySteamId = new();
    private readonly object _customSoundLock = new();
    private readonly Dictionary<int, OfficialSoundOverride> _overrideByItemDefIndex = new();
    private readonly Dictionary<nint, string> _subclassByWeaponHandle = new();

    private const int MaxPlayerSlots = 65;
    private const int EntityIndexMask = 0x3FFF;
    private const long FireCacheTtlMs = 1500;

    public SoundManager()
    {
        RebuildOfficialOverrides();
    }

    public void UpdateModelsConfig()
    {
        RebuildOfficialOverrides();
    }

    private void RebuildOfficialOverrides()
    {
        _overrideByItemDefIndex.Clear();
        var soundConfig = zVIPCore.Config?.SoundConfig;
        if (soundConfig?.OfficialOverrides == null) return;

        foreach (var entry in soundConfig.OfficialOverrides)
        {
            if (entry.ItemDefIndex <= 0 || string.IsNullOrWhiteSpace(entry.TargetEvent))
                continue;
            _overrideByItemDefIndex[entry.ItemDefIndex] = entry;
        }
    }

    #region Event Handlers

    public HookResult OnWeaponFire(EventWeaponFire @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || !player.PawnIsAlive)
            return HookResult.Continue;

        var weapon = player.PlayerPawn?.Value?.WeaponServices?.ActiveWeapon?.Value;
        if (weapon == null || !weapon.IsValid)
            return HookResult.Continue;

        _pawnCache.Update(player);

        if (!TryResolveFireEvents(player, weapon, @event, out var customEvent, out var officialEvent, out var itemDefIndex))
        {
            _fireSoundCache.Clear(player.Slot);
            return HookResult.Continue;
        }

        _fireSoundCache.Update(player.Slot, itemDefIndex, customEvent, officialEvent, Environment.TickCount64);
        return HookResult.Continue;
    }

    public HookResult OnWeaponFireUserMessage(UserMessage userMessage)
    {
        var soundConfig = zVIPCore.Config?.SoundConfig;
        if (soundConfig == null || !soundConfig.Enabled)
            return HookResult.Continue;

        var forceMute = soundConfig.ForceMuteAllFireBullets;

        var playerHandle = (int)userMessage.ReadUInt("player");
        if (playerHandle <= 0) return HookResult.Continue;

        var playerEntityIndex = playerHandle & EntityIndexMask;
        var shooter = _pawnCache.Find(playerEntityIndex);
        if (shooter == null || !shooter.IsValid) return HookResult.Continue;

        var lastFire = _fireSoundCache.Get(shooter.Slot);

        var nowMs = Environment.TickCount64;
        if (lastFire != null && nowMs - lastFire.UpdatedAtMs > FireCacheTtlMs)
        {
            _fireSoundCache.Clear(shooter.Slot);
            lastFire = null;
        }

        var itemDefIndex = (int)userMessage.ReadUInt("item_def_index");
        if (lastFire != null && itemDefIndex > 0 && lastFire.ItemDefIndex > 0 && itemDefIndex != lastFire.ItemDefIndex)
        {
            _fireSoundCache.Clear(shooter.Slot);
            lastFire = null;
        }

        if (lastFire == null)
        {
            var weapon = shooter.PlayerPawn?.Value?.WeaponServices?.ActiveWeapon?.Value;
            if (weapon == null || !weapon.IsValid) return HookResult.Continue;

            if (!TryResolveFireEvents(shooter, weapon, null, out var customEvent, out var officialEvent, out var resolvedDefIndex))
            {
                _fireSoundCache.Clear(shooter.Slot);
                return HookResult.Continue;
            }

            lastFire = new FireSoundEntry(resolvedDefIndex, customEvent, officialEvent, nowMs);
            _fireSoundCache.Set(shooter.Slot, lastFire);
        }

        var hasCustom = !string.IsNullOrWhiteSpace(lastFire.CustomEvent);
        var hasOfficial = !string.IsNullOrWhiteSpace(lastFire.OfficialEvent);
        if (!hasCustom && !hasOfficial)
        {
            if (forceMute) userMessage.Recipients.Clear();
            return HookResult.Continue;
        }

        RecipientFilter? customRecipients = null;
        RecipientFilter? officialRecipients = null;

        if (userMessage.Recipients != null)
        {
            foreach (var recipient in userMessage.Recipients)
            {
                if (recipient == null || !recipient.IsValid) continue;

                if (hasCustom && IsCustomSoundEnabled(recipient))
                {
                    customRecipients ??= new RecipientFilter();
                    customRecipients.Add(recipient);
                    continue;
                }

                if (hasOfficial)
                {
                    officialRecipients ??= new RecipientFilter();
                    officialRecipients.Add(recipient);
                }
            }
        }

        var shooterWantsCustom = hasCustom && IsCustomSoundEnabled(shooter);
        if (shooterWantsCustom)
        {
            customRecipients ??= new RecipientFilter();
            customRecipients.Add(shooter);
        }
        else if (hasOfficial)
        {
            officialRecipients ??= new RecipientFilter();
            officialRecipients.Add(shooter);
        }

        if (customRecipients != null && hasCustom)
            shooter.EmitSound(lastFire.CustomEvent!, customRecipients);

        if (officialRecipients != null && hasOfficial)
            shooter.EmitSound(lastFire.OfficialEvent!, officialRecipients);

        if (forceMute || customRecipients != null || officialRecipients != null)
            userMessage.Recipients?.Clear();

        return HookResult.Continue;
    }

    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;

        _pawnCache.Update(player);
        return HookResult.Continue;
    }

    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        return HookResult.Continue;
    }

    public void OnMapStart(string mapName)
    {
        _subclassByWeaponHandle.Clear();
        _pawnCache.Clear();
        _fireSoundCache.ClearAll();
        RebuildOfficialOverrides();
        RefreshCustomSoundCacheForAllPlayers();
    }

    public void OnClientPutInServer(int playerSlot)
    {
        var player = Utilities.GetPlayerFromSlot(playerSlot);
        if (player == null || !player.IsValid || player.IsBot || player.SteamID == 0)
            return;

        var soundConfig = zVIPCore.Config?.SoundConfig;
        SetCustomSoundEnabledForPlayer(player, soundConfig?.CustomSoundDefaultEnabled ?? true);
        _pawnCache.Update(player);

        _ = zVIPCore.SafeAsync(() => LoadCustomSoundSettingAsync(player.SteamID));
    }

    public void OnClientDisconnect(int playerSlot)
    {
        var player = Utilities.GetPlayerFromSlot(playerSlot);
        if (player == null || !player.IsValid) return;

        _fireSoundCache.Clear(playerSlot);
        _pawnCache.Remove(player);
        ClearCustomSoundEnabledForPlayer(player);
        RemoveCustomSoundEnabled(player.SteamID);
    }

    #endregion

    #region Sound Resolution

    private bool TryResolveFireEvents(CCSPlayerController player, CBasePlayerWeapon weapon, EventWeaponFire? @event,
        out string? customEvent, out string? officialEvent, out int itemDefIndex)
    {
        customEvent = null;
        officialEvent = null;
        itemDefIndex = (int)weapon.AttributeManager.Item.ItemDefinitionIndex;

        var weaponType = WeaponManager.GetDesignerName(weapon);

        // Use cached config from WeaponManager instead of loading from disk
        WeaponModelData? modelData = null;
        var trackedModelId = zVIPCore.WeaponManager.GetWeaponTrackedModelId(weapon);
        if (!string.IsNullOrEmpty(trackedModelId))
        {
            modelData = zVIPCore.WeaponManager.GetModelsConfig().FindModelByUniqueId(trackedModelId);
        }

        modelData ??= zVIPCore.WeaponManager.GetEquippedWeaponModel(player.SteamID, weaponType);

        if (modelData != null && !string.IsNullOrWhiteSpace(modelData.SoundEvent))
        {
            customEvent = @event != null
                ? WeaponSoundUtils.ResolveTargetEvent(@event, weapon, modelData.SoundEvent, modelData.SoundEventUnsilenced)
                : WeaponSoundUtils.ResolveTargetEvent(weapon, modelData.SoundEvent, modelData.SoundEventUnsilenced);
        }

        _overrideByItemDefIndex.TryGetValue(itemDefIndex, out var officialOverride);

        if (officialOverride == null)
        {
            if (@event != null)
            {
                if (WeaponSoundUtils.TryResolveFallbackItemDefIndex(@event, weapon, out var fallbackIndex))
                    _overrideByItemDefIndex.TryGetValue(fallbackIndex, out officialOverride);
            }
            else
            {
                if (WeaponSoundUtils.TryResolveFallbackItemDefIndex(weapon, out var fallbackIndex))
                    _overrideByItemDefIndex.TryGetValue(fallbackIndex, out officialOverride);
            }
        }

        if (officialOverride != null)
        {
            officialEvent = @event != null
                ? WeaponSoundUtils.ResolveTargetEvent(@event, weapon, officialOverride.TargetEvent, officialOverride.TargetEventUnsilenced)
                : WeaponSoundUtils.ResolveTargetEvent(weapon, officialOverride.TargetEvent, officialOverride.TargetEventUnsilenced);
        }

        return !string.IsNullOrWhiteSpace(customEvent) || !string.IsNullOrWhiteSpace(officialEvent);
    }

    public void TrackWeaponSubclass(CBasePlayerWeapon weapon, string subclass)
    {
        if (weapon == null || !weapon.IsValid || string.IsNullOrWhiteSpace(subclass)) return;
        _subclassByWeaponHandle[weapon.Handle] = subclass.Trim();
    }

    public void UntrackWeaponSubclass(CBasePlayerWeapon weapon)
    {
        if (weapon == null) return;
        _subclassByWeaponHandle.Remove(weapon.Handle);
    }

    #endregion

    #region Custom Sound Toggle

    public void OnToggleCustomSound(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid || player.SteamID == 0) return;

        // Check configurable permission
        var restrictPerm = zVIPCore.Config?.RestrictPermission ?? "";
        if (!string.IsNullOrEmpty(restrictPerm) && !AdminManager.PlayerHasPermissions(player, restrictPerm))
        {
            player.PrintToChat($"{zVIPCore.Instance.Localizer["zVIPCore.prefix"]} {zVIPCore.Instance.Localizer["zVIPCore.console_only"]}");
            return;
        }

        var enabled = !IsCustomSoundEnabled(player);
        SetCustomSoundEnabledForPlayer(player, enabled);
        _ = zVIPCore.SafeAsync(() => SaveCustomSoundSettingAsync(player.SteamID, enabled));

        var prefix = zVIPCore.Instance.Localizer["zVIPCore.prefix"];
        var message = enabled
            ? zVIPCore.Instance.Localizer["zVIPCore.sound_enabled"]
            : zVIPCore.Instance.Localizer["zVIPCore.sound_disabled"];
        player.PrintToChat($"{prefix} {message}");
    }

    private void SetCustomSoundEnabledForPlayer(CCSPlayerController player, bool enabled)
    {
        if (player == null || !player.IsValid) return;
        SetCustomSoundEnabled(player.SteamID, enabled);
        if (IsValidSlot(player.Slot))
            _customSoundEnabledBySlot[player.Slot] = enabled;
    }

    private void ClearCustomSoundEnabledForPlayer(CCSPlayerController player)
    {
        if (player == null) return;
        var soundConfig = zVIPCore.Config?.SoundConfig;
        if (IsValidSlot(player.Slot))
            _customSoundEnabledBySlot[player.Slot] = soundConfig?.CustomSoundDefaultEnabled ?? true;
    }

    private bool IsCustomSoundEnabled(CCSPlayerController player)
    {
        if (player == null || !player.IsValid) return false;
        if (IsValidSlot(player.Slot)) return _customSoundEnabledBySlot[player.Slot];
        return GetCustomSoundEnabled(player.SteamID);
    }

    private bool GetCustomSoundEnabled(ulong steamId)
    {
        lock (_customSoundLock)
        {
            if (_customSoundEnabledBySteamId.TryGetValue(steamId, out var enabled))
                return enabled;
        }
        return zVIPCore.Config?.SoundConfig?.CustomSoundDefaultEnabled ?? true;
    }

    private void SetCustomSoundEnabled(ulong steamId, bool enabled)
    {
        lock (_customSoundLock)
        {
            _customSoundEnabledBySteamId[steamId] = enabled;
        }
    }

    private void RemoveCustomSoundEnabled(ulong steamId)
    {
        lock (_customSoundLock)
        {
            _customSoundEnabledBySteamId.Remove(steamId);
        }
    }

    private void SetCustomSoundEnabledFromSteamId(ulong steamId, bool enabled)
    {
        SetCustomSoundEnabled(steamId, enabled);
        foreach (var candidate in Utilities.GetPlayers())
        {
            if (!candidate.IsValid || candidate.SteamID != steamId) continue;
            if (IsValidSlot(candidate.Slot))
                _customSoundEnabledBySlot[candidate.Slot] = enabled;
            break;
        }
    }

    private void RefreshCustomSoundCacheForAllPlayers()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.IsBot || player.SteamID == 0) continue;

            _pawnCache.Update(player);
            if (IsValidSlot(player.Slot))
                _customSoundEnabledBySlot[player.Slot] = GetCustomSoundEnabled(player.SteamID);

            _ = zVIPCore.SafeAsync(() => LoadCustomSoundSettingAsync(player.SteamID));
        }
    }

    private static bool IsValidSlot(int slot) => slot >= 0 && slot < MaxPlayerSlots;

    #endregion

    #region Database Persistence

    private async Task LoadCustomSoundSettingAsync(ulong steamId)
    {
        if (steamId == 0) return;
        var enabled = await zVIPCore.Database.GetPlayerSoundEnabledAsync(steamId);
        Server.NextFrame(() => SetCustomSoundEnabledFromSteamId(steamId, enabled));
    }

    private Task SaveCustomSoundSettingAsync(ulong steamId, bool enabled)
    {
        if (steamId == 0) return Task.CompletedTask;
        return zVIPCore.Database.SavePlayerSoundEnabledAsync(steamId, enabled);
    }

    #endregion
}
