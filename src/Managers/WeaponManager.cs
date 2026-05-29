using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Collections.Concurrent;

namespace zVIPCore;

public class WeaponManager
{
    private WeaponModelsConfig _modelsConfig = new();
    // Key: (steamId, team) → weaponType → modelId
    private readonly ConcurrentDictionary<(ulong, string), ConcurrentDictionary<string, string>> _playerWeapons = new();
    private static readonly ConcurrentDictionary<nint, string> OldSubclassByHandle = new();

    public void UpdateModelsConfig(WeaponModelsConfig config) => _modelsConfig = config;

    public WeaponModelsConfig GetModelsConfig() => _modelsConfig;

    public void PrecacheModels()
    {
        foreach (var collection in _modelsConfig.Weapons.Values)
        {
            foreach (var skins in collection.WeaponItems.Values)
            {
                foreach (var weapon in skins)
                {
                    if (!string.IsNullOrEmpty(weapon.Model))
                    {
                        try { Server.PrecacheModel(weapon.Model); }
                        catch { /* Already precached by addon */ }
                    }
                }
            }
        }
    }

    public void OnEntityCreated(CEntityInstance entity)
    {
        if (!entity.DesignerName.StartsWith("weapon_"))
            return;

        Server.NextWorldUpdate(() =>
        {
            var weapon = entity.As<CBasePlayerWeapon>();
            if (weapon?.IsValid != true || weapon.OriginalOwnerXuidLow <= 0)
                return;

            var player = FindPlayerFromWeapon(weapon);
            if (player == null || !player.IsValid || player.IsBot)
                return;

            ApplyPlayerWeaponSubclass(player, weapon);
        });
    }

    public HookResult OnItemEquip(EventItemEquip @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot)
            return HookResult.Continue;

        var activeWeapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;
        if (activeWeapon?.IsValid != true)
            return HookResult.Continue;

        ApplyPlayerWeaponSubclass(player, activeWeapon);
        return HookResult.Continue;
    }

    private void ApplyPlayerWeaponSubclass(CCSPlayerController player, CBasePlayerWeapon weapon)
    {
        var steamId = player.SteamID;
        var team = GetTeamString(player.Team);
        var weaponDesignerName = GetDesignerName(weapon);

        if (_playerWeapons.TryGetValue((steamId, team), out var weapons))
        {
            if (weapons.TryGetValue(weaponDesignerName, out var modelId))
            {
                var modelData = _modelsConfig.FindModelByUniqueId(modelId);
                if (modelData != null)
                {
                    var subclass = modelData.GetSubclassName();
                    if (!string.IsNullOrEmpty(subclass) && weaponDesignerName.Equals(modelData.WeaponType, StringComparison.Ordinal))
                    {
                        SetSubclass(weapon, weaponDesignerName, subclass, modelData.Name);
                        return;
                    }
                }
            }
            // Player's weapons are cached but no custom skin for this weapon → reset to default
            ResetSubclass(weapon);
        }
        else
        {
            _ = zVIPCore.SafeAsync(() => LoadAndApplyWeaponSubclassAsync(steamId, team, player, weapon, weaponDesignerName));
        }
    }

    private async Task LoadAndApplyWeaponSubclassAsync(ulong steamId, string team, CCSPlayerController player, CBasePlayerWeapon weapon, string weaponName)
    {
        var modelId = await zVIPCore.Database.GetPlayerWeaponAsync(steamId, weaponName, team);

        // Ensure cache entry exists so we don't re-query DB
        var weapons = _playerWeapons.GetOrAdd((steamId, team), _ => new ConcurrentDictionary<string, string>());

        if (modelId == null)
        {
            // Player has no custom weapon for this type → reset any existing custom subclass
            Server.NextFrame(() =>
            {
                if (player.IsValid && weapon?.IsValid == true)
                    ResetSubclass(weapon);
            });
            return;
        }

        weapons[weaponName] = modelId;

        var modelData = _modelsConfig.FindModelByUniqueId(modelId);
        if (modelData == null)
        {
            await zVIPCore.Database.RemovePlayerWeaponAsync(steamId, weaponName, team);
            Server.NextFrame(() =>
            {
                if (player.IsValid && weapon?.IsValid == true)
                    ResetSubclass(weapon);
            });
            return;
        }

        var subclass = modelData.GetSubclassName();
        if (string.IsNullOrEmpty(subclass) || !weaponName.Equals(modelData.WeaponType, StringComparison.Ordinal))
        {
            Server.NextFrame(() =>
            {
                if (player.IsValid && weapon?.IsValid == true)
                    ResetSubclass(weapon);
            });
            return;
        }

        Server.NextFrame(() =>
        {
            if (player.IsValid && weapon?.IsValid == true)
                SetSubclass(weapon, weaponName, subclass, modelData.Name);
        });
    }

    public void RefreshPlayerWeapons(CCSPlayerController player)
    {
        if (!player.IsValid || player.PlayerPawn.Value?.WeaponServices == null)
            return;

        var activeWeapon = player.PlayerPawn.Value.WeaponServices.ActiveWeapon.Value;
        if (activeWeapon?.IsValid == true)
            ApplyPlayerWeaponSubclass(player, activeWeapon);
    }

    public void ClearPlayerData(ulong steamId)
    {
        _playerWeapons.TryRemove((steamId, "CT"), out _);
        _playerWeapons.TryRemove((steamId, "T"), out _);
    }

    public WeaponModelData? GetEquippedWeaponModel(ulong steamId, string team, string weaponType)
    {
        if (!_playerWeapons.TryGetValue((steamId, team), out var weapons)) return null;
        if (!weapons.TryGetValue(weaponType, out var modelId)) return null;
        return _modelsConfig.FindModelByUniqueId(modelId);
    }

    public void UpdatePlayerWeaponCache(ulong steamId, string team, string weaponName, string? modelId)
    {
        var weapons = _playerWeapons.GetOrAdd((steamId, team), _ => new ConcurrentDictionary<string, string>());
        if (modelId == null)
            weapons.TryRemove(weaponName, out _);
        else
            weapons[weaponName] = modelId;
    }

    #region Weapon Helpers

    public static string GetDesignerName(CBasePlayerWeapon weapon)
    {
        string weaponDesignerName = weapon.DesignerName;
        ushort weaponIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;

        return (weaponDesignerName, weaponIndex) switch
        {
            var (name, _) when name.Contains("bayonet") => "weapon_knife",
            ("weapon_deagle", 64) => "weapon_revolver",
            ("weapon_m4a1", 60) => "weapon_m4a1_silencer",
            ("weapon_hkp2000", 61) => "weapon_usp_silencer",
            ("weapon_mp7", 23) => "weapon_mp5sd",
            _ => weaponDesignerName
        };
    }

    public static string GetTeamString(CsTeam team) =>
        team == CsTeam.CounterTerrorist ? "CT" : "T";

    public static void SetSubclass(CBasePlayerWeapon weapon, string oldSubclass, string newSubclass, string? customName = null)
    {
        if (string.IsNullOrEmpty(newSubclass)) return;

        OldSubclassByHandle[weapon.Handle] = oldSubclass;
        weapon.AcceptInput("ChangeSubclass", weapon, weapon, newSubclass);

        // Set nametag on NextFrame to ensure it persists after subclass change
        if (!string.IsNullOrEmpty(customName))
        {
            Server.NextFrame(() =>
            {
                if (weapon?.IsValid == true)
                    weapon.AttributeManager.Item.CustomName = customName;
            });
        }
    }

    public static void ResetSubclass(CBasePlayerWeapon weapon)
    {
        if (!OldSubclassByHandle.TryGetValue(weapon.Handle, out var oldSubclass) || string.IsNullOrEmpty(oldSubclass))
            return;

        weapon.AcceptInput("ChangeSubclass", weapon, weapon, oldSubclass);
        OldSubclassByHandle.TryRemove(weapon.Handle, out _);

        // Clear nametag set by our plugin
        Server.NextFrame(() =>
        {
            if (weapon?.IsValid == true)
                weapon.AttributeManager.Item.CustomName = "";
        });
    }

    public static void ClearSubclassCache()
    {
        OldSubclassByHandle.Clear();
    }

    private static CCSPlayerController? FindPlayerFromWeapon(CBasePlayerWeapon weapon)
    {
        if (weapon.OwnerEntity.Value == null) return null;
        return weapon.OwnerEntity.Value.As<CCSPlayerPawn>()?.OriginalController.Value;
    }

    #endregion
}
