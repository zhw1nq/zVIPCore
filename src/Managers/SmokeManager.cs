using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using System.Collections.Concurrent;
using System.Globalization;

namespace zVIPCore;

public class SmokeManager
{
    private readonly Random _random = new();

    public void OnEntityCreated(CEntityInstance entity)
    {
        if (entity.DesignerName != "smokegrenade_projectile")
            return;

        var grenade = new CSmokeGrenadeProjectile(entity.Handle);
        if (grenade.Handle == IntPtr.Zero)
            return;

        Server.NextFrame(() =>
        {
            if (!grenade.IsValid)
                return;

            var player = grenade.Thrower.Value?.Controller.Value;
            if (player == null)
                return;

            var steamId = player.SteamID;
            var color = zVIPCore.Database.GetSmokeColorCached(steamId);
            if (string.IsNullOrEmpty(color))
                return;

            ApplySmokeColor(grenade, color);
        });
    }

    private void ApplySmokeColor(CSmokeGrenadeProjectile grenade, string color)
    {
        if (color == "random")
        {
            // Random color each time
            grenade.SmokeColor.X = _random.NextSingle() * 255.0f;
            grenade.SmokeColor.Y = _random.NextSingle() * 255.0f;
            grenade.SmokeColor.Z = _random.NextSingle() * 255.0f;
        }
        else
        {
            // Parse "R G B" format
            var parts = color.Split(' ');
            if (parts.Length >= 3)
            {
                if (float.TryParse(parts[0], CultureInfo.InvariantCulture, out var r) &&
                    float.TryParse(parts[1], CultureInfo.InvariantCulture, out var g) &&
                    float.TryParse(parts[2], CultureInfo.InvariantCulture, out var b))
                {
                    grenade.SmokeColor.X = r;
                    grenade.SmokeColor.Y = g;
                    grenade.SmokeColor.Z = b;
                }
            }
        }
    }

    public void SetPlayerSmokeColor(ulong steamId, string color)
    {
        _ = zVIPCore.Database.SavePlayerSmokeColorAsync(steamId, color);
    }

    public void ClearPlayerData(ulong steamId)
    {
        // Handled by Database.ClearPlayerCache
    }

    public string? GetPlayerSmokeColor(ulong steamId)
    {
        return zVIPCore.Database.GetSmokeColorCached(steamId);
    }
}
