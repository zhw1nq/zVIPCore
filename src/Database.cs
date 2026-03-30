using MySqlConnector;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API;
using System.Collections.Concurrent;

namespace zModelsCustom;

public class Database : IDisposable
{
    private readonly string _connectionString;
    private readonly ConcurrentDictionary<(ulong, CsTeam), string> _modelCache = new();
    private readonly ConcurrentDictionary<(ulong, string), string> _weaponCache = new();
    private readonly ConcurrentDictionary<ulong, string> _smokeCache = new();
    private readonly ConcurrentDictionary<ulong, bool> _soundEnabledCache = new();
    private bool _disposed;

    public Database(DatabaseConfig config)
    {
        _connectionString = $"Server={config.Host};Port={config.Port};Database={config.Database};" +
                          $"Uid={config.User};Pwd={config.Password};" +
                          $"Pooling=true;MinimumPoolSize={config.MinPoolSize};MaximumPoolSize={config.MaxPoolSize};" +
                          $"ConnectionTimeout={config.ConnectionTimeout};DefaultCommandTimeout={config.CommandTimeout};";

        Task.Run(() => InitializeDatabaseAsync()).GetAwaiter().GetResult();
    }

    private async Task InitializeDatabaseAsync()
    {
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(@"
            CREATE TABLE IF NOT EXISTS zPlayerModels (
                steamid BIGINT UNSIGNED,
                team VARCHAR(2) NOT NULL,
                model_id VARCHAR(64) NOT NULL,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                PRIMARY KEY (steamid, team),
                INDEX idx_steamid (steamid)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

            CREATE TABLE IF NOT EXISTS zWeaponEquipments (
                id INT AUTO_INCREMENT PRIMARY KEY,
                steamid BIGINT UNSIGNED NOT NULL,
                weapon_type VARCHAR(32) NOT NULL,
                uniqueid VARCHAR(64) NOT NULL,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                UNIQUE KEY uk_player_weapon (steamid, weapon_type),
                INDEX idx_steamid (steamid)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

            CREATE TABLE IF NOT EXISTS zSmokeColors (
                steamid BIGINT UNSIGNED PRIMARY KEY,
                color VARCHAR(32) NOT NULL,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

            CREATE TABLE IF NOT EXISTS zSoundSettings (
                steamid BIGINT UNSIGNED PRIMARY KEY,
                enabled TINYINT(1) NOT NULL DEFAULT 1,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;", conn);

        await cmd.ExecuteNonQueryAsync();
    }

    #region Player Models

    public async Task<string?> GetPlayerModelAsync(ulong steamId, CsTeam team)
    {
        if (_modelCache.TryGetValue((steamId, team), out var cached))
            return cached;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "SELECT model_id FROM zPlayerModels WHERE steamid = @steamid AND team = @team LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@team", GetTeamString(team));

        var result = await cmd.ExecuteScalarAsync();
        if (result is string modelId)
        {
            _modelCache[(steamId, team)] = modelId;
            return modelId;
        }

        return null;
    }

    public async Task SavePlayerModelAsync(ulong steamId, CsTeam team, string modelId)
    {
        _modelCache[(steamId, team)] = modelId;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(@"
            INSERT INTO zPlayerModels (steamid, team, model_id)
            VALUES (@steamid, @team, @model_id)
            ON DUPLICATE KEY UPDATE model_id = @model_id", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@team", GetTeamString(team));
        cmd.Parameters.AddWithValue("@model_id", modelId);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemovePlayerModelAsync(ulong steamId, CsTeam team)
    {
        _modelCache.TryRemove((steamId, team), out _);

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "DELETE FROM zPlayerModels WHERE steamid = @steamid AND team = @team", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@team", GetTeamString(team));

        await cmd.ExecuteNonQueryAsync();
    }

    private static string GetTeamString(CsTeam team) =>
        team == CsTeam.CounterTerrorist ? "CT" : "T";

    #endregion

    #region Weapon Equipments

    public async Task<string?> GetPlayerWeaponAsync(ulong steamId, string weaponType)
    {
        if (_weaponCache.TryGetValue((steamId, weaponType), out var cached))
            return cached;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "SELECT uniqueid FROM zWeaponEquipments WHERE steamid = @steamid AND weapon_type = @weapon_type LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@weapon_type", weaponType);

        var result = await cmd.ExecuteScalarAsync();
        if (result is string uniqueId && !string.IsNullOrEmpty(uniqueId))
        {
            _weaponCache[(steamId, weaponType)] = uniqueId;
            return uniqueId;
        }

        return null;
    }

    public async Task<Dictionary<string, string>> GetAllPlayerWeaponsAsync(ulong steamId)
    {
        var weapons = new Dictionary<string, string>();

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "SELECT weapon_type, uniqueid FROM zWeaponEquipments WHERE steamid = @steamid", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var weaponType = reader.GetString(0);
            var uniqueId = reader.GetString(1);
            weapons[weaponType] = uniqueId;
            _weaponCache[(steamId, weaponType)] = uniqueId;
        }

        return weapons;
    }

    public async Task SavePlayerWeaponAsync(ulong steamId, string weaponType, string uniqueId)
    {
        _weaponCache[(steamId, weaponType)] = uniqueId;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(@"
            INSERT INTO zWeaponEquipments (steamid, weapon_type, uniqueid)
            VALUES (@steamid, @weapon_type, @uniqueid)
            ON DUPLICATE KEY UPDATE uniqueid = @uniqueid", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@weapon_type", weaponType);
        cmd.Parameters.AddWithValue("@uniqueid", uniqueId);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemovePlayerWeaponAsync(ulong steamId, string weaponType)
    {
        _weaponCache.TryRemove((steamId, weaponType), out _);

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "DELETE FROM zWeaponEquipments WHERE steamid = @steamid AND weapon_type = @weapon_type", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@weapon_type", weaponType);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemoveAllPlayerWeaponsAsync(ulong steamId)
    {
        var keysToRemove = _weaponCache.Keys.Where(k => k.Item1 == steamId).ToList();
        foreach (var key in keysToRemove)
            _weaponCache.TryRemove(key, out _);

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "DELETE FROM zWeaponEquipments WHERE steamid = @steamid", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);

        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Smoke Colors

    public async Task<string?> GetPlayerSmokeColorAsync(ulong steamId)
    {
        if (_smokeCache.TryGetValue(steamId, out var cached))
            return cached;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "SELECT color FROM zSmokeColors WHERE steamid = @steamid LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);

        var result = await cmd.ExecuteScalarAsync();
        if (result is string color && !string.IsNullOrEmpty(color))
        {
            _smokeCache[steamId] = color;
            return color;
        }

        return null;
    }

    public async Task SavePlayerSmokeColorAsync(ulong steamId, string color)
    {
        _smokeCache[steamId] = color;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(@"
            INSERT INTO zSmokeColors (steamid, color)
            VALUES (@steamid, @color)
            ON DUPLICATE KEY UPDATE color = @color", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@color", color);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemovePlayerSmokeColorAsync(ulong steamId)
    {
        _smokeCache.TryRemove(steamId, out _);

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "DELETE FROM zSmokeColors WHERE steamid = @steamid", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);

        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Sound Settings

    public async Task<bool> GetPlayerSoundEnabledAsync(ulong steamId)
    {
        if (_soundEnabledCache.TryGetValue(steamId, out var cached))
            return cached;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "SELECT enabled FROM zSoundSettings WHERE steamid = @steamid LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);

        var result = await cmd.ExecuteScalarAsync();
        if (result != null && result != DBNull.Value)
        {
            var enabled = Convert.ToBoolean(result);
            _soundEnabledCache[steamId] = enabled;
            return enabled;
        }

        _soundEnabledCache[steamId] = true;
        return true;
    }

    public async Task SavePlayerSoundEnabledAsync(ulong steamId, bool enabled)
    {
        _soundEnabledCache[steamId] = enabled;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        if (enabled)
        {
            await using var delCmd = new MySqlCommand(
                "DELETE FROM zSoundSettings WHERE steamid = @steamid", conn);
            delCmd.Parameters.AddWithValue("@steamid", steamId);
            await delCmd.ExecuteNonQueryAsync();
        }
        else
        {
            await using var cmd = new MySqlCommand(@"
                INSERT INTO zSoundSettings (steamid, enabled) VALUES (@steamid, 0)
                ON DUPLICATE KEY UPDATE enabled = 0", conn);
            cmd.Parameters.AddWithValue("@steamid", steamId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    #endregion

    #region Batch Preload & Common

    /// <summary>
    /// Preloads all player data using a single connection to reduce DB overhead.
    /// </summary>
    public async Task PreloadAllPlayerDataAsync(ulong steamId)
    {
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        // 1. Models (T + CT)
        await using (var cmd = new MySqlCommand(
            "SELECT team, model_id FROM zPlayerModels WHERE steamid = @steamid", conn))
        {
            cmd.Parameters.AddWithValue("@steamid", steamId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var teamStr = reader.GetString(0);
                var modelId = reader.GetString(1);
                var team = teamStr == "CT" ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
                _modelCache[(steamId, team)] = modelId;
            }
        }

        // 2. Weapons
        await using (var cmd = new MySqlCommand(
            "SELECT weapon_type, uniqueid FROM zWeaponEquipments WHERE steamid = @steamid", conn))
        {
            cmd.Parameters.AddWithValue("@steamid", steamId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                _weaponCache[(steamId, reader.GetString(0))] = reader.GetString(1);
            }
        }

        // 3. Smoke color
        await using (var cmd = new MySqlCommand(
            "SELECT color FROM zSmokeColors WHERE steamid = @steamid LIMIT 1", conn))
        {
            cmd.Parameters.AddWithValue("@steamid", steamId);
            var result = await cmd.ExecuteScalarAsync();
            if (result is string color && !string.IsNullOrEmpty(color))
                _smokeCache[steamId] = color;
        }

        // 4. Sound settings
        await using (var cmd = new MySqlCommand(
            "SELECT enabled FROM zSoundSettings WHERE steamid = @steamid LIMIT 1", conn))
        {
            cmd.Parameters.AddWithValue("@steamid", steamId);
            var result = await cmd.ExecuteScalarAsync();
            _soundEnabledCache[steamId] = result != null && result != DBNull.Value
                ? Convert.ToBoolean(result) : true;
        }
    }

    public void ClearPlayerCache(ulong steamId)
    {
        _modelCache.TryRemove((steamId, CsTeam.Terrorist), out _);
        _modelCache.TryRemove((steamId, CsTeam.CounterTerrorist), out _);

        var weaponKeys = _weaponCache.Keys.Where(k => k.Item1 == steamId).ToList();
        foreach (var key in weaponKeys)
            _weaponCache.TryRemove(key, out _);

        _smokeCache.TryRemove(steamId, out _);
        _soundEnabledCache.TryRemove(steamId, out _);
    }

    public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player?.IsBot != false) return HookResult.Continue;

        _ = zModelsCustom.SafeAsync(() => PreloadAllPlayerDataAsync(player.SteamID));

        return HookResult.Continue;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _modelCache.Clear();
        _weaponCache.Clear();
        _smokeCache.Clear();
        _soundEnabledCache.Clear();
        MySqlConnection.ClearAllPools();

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    #endregion
}
