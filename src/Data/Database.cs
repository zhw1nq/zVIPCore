using MySqlConnector;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API;
using System.Collections.Concurrent;

namespace zVIPCore;

public class Database : IDisposable
{
    private readonly string _connectionString;
    private readonly ConcurrentDictionary<(ulong, CsTeam), string> _modelCache = new();
    private readonly ConcurrentDictionary<(ulong SteamId, string WeaponType, string Team), string> _weaponCache = new();
    private readonly ConcurrentDictionary<(ulong SteamId, string Team), string> _smokeCache = new();
    private readonly ConcurrentDictionary<(ulong, string), MvpPlayerData> _mvpCache = new();
    private readonly ConcurrentDictionary<(ulong, string), bool> _mvpDirty = new();
    private readonly ConcurrentDictionary<(ulong SteamId, string Team), string> _particleCache = new();
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
                team VARCHAR(2) NOT NULL DEFAULT 'CT',
                uniqueid VARCHAR(64) NOT NULL,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                UNIQUE KEY uk_player_weapon_team (steamid, weapon_type, team),
                INDEX idx_steamid (steamid)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

            CREATE TABLE IF NOT EXISTS zSmokeColors (
                steamid BIGINT UNSIGNED NOT NULL,
                team VARCHAR(2) NOT NULL DEFAULT 'CT',
                color VARCHAR(32) NOT NULL,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                PRIMARY KEY (steamid, team),
                INDEX idx_steamid (steamid)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

            CREATE TABLE IF NOT EXISTS zMVP (
                steam_id BIGINT UNSIGNED NOT NULL,
                team VARCHAR(2) NOT NULL DEFAULT 'CT',
                mvp_name VARCHAR(255) NULL,
                mvp_sound VARCHAR(255) NULL,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                PRIMARY KEY (steam_id, team),
                INDEX idx_steam_id (steam_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

            CREATE TABLE IF NOT EXISTS zPlayerParticles (
                steamid BIGINT UNSIGNED NOT NULL,
                team VARCHAR(2) NOT NULL DEFAULT 'CT',
                particle_path VARCHAR(255) NOT NULL,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                PRIMARY KEY (steamid, team),
                INDEX idx_steamid (steamid)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            ", conn);

        await cmd.ExecuteNonQueryAsync();

        // Auto-migration: add team column if table already exists without it
        await MigrateWeaponEquipmentsTeamAsync(conn);
        await MigrateSmokeColorsTeamAsync(conn);
    }

    private static async Task MigrateWeaponEquipmentsTeamAsync(MySqlConnection conn)
    {
        try
        {
            // Check if team column exists
            await using var checkCmd = new MySqlCommand(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'zWeaponEquipments' AND COLUMN_NAME = 'team'", conn);
            var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;
            if (exists) return;

            Console.WriteLine("[zVIPCore] Migrating zWeaponEquipments: adding team column...");

            // Add team column
            await using var alterCmd = new MySqlCommand(
                "ALTER TABLE zWeaponEquipments ADD COLUMN team VARCHAR(2) NOT NULL DEFAULT 'CT' AFTER weapon_type", conn);
            await alterCmd.ExecuteNonQueryAsync();

            // Duplicate existing CT rows as T rows so players keep same skins for both teams
            await using var dupCmd = new MySqlCommand(
                "INSERT IGNORE INTO zWeaponEquipments (steamid, weapon_type, team, uniqueid) SELECT steamid, weapon_type, 'T', uniqueid FROM zWeaponEquipments WHERE team = 'CT'", conn);
            var duplicated = await dupCmd.ExecuteNonQueryAsync();

            // Drop old unique key and add new one with team
            try
            {
                await using var dropCmd = new MySqlCommand(
                    "ALTER TABLE zWeaponEquipments DROP INDEX uk_player_weapon, ADD UNIQUE KEY uk_player_weapon_team (steamid, weapon_type, team)", conn);
                await dropCmd.ExecuteNonQueryAsync();
            }
            catch { /* Key may already be correct */ }

            Console.WriteLine($"[zVIPCore] Migration complete: duplicated {duplicated} weapon entries for T team");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[zVIPCore] Warning: weapon team migration skipped: {ex.Message}");
        }
    }

    private static async Task MigrateSmokeColorsTeamAsync(MySqlConnection conn)
    {
        try
        {
            await using var checkCmd = new MySqlCommand(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'zSmokeColors' AND COLUMN_NAME = 'team'", conn);
            var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;
            if (exists) return;

            Console.WriteLine("[zVIPCore] Migrating zSmokeColors: adding team column...");

            // Add team column
            await using var alterCmd = new MySqlCommand(
                "ALTER TABLE zSmokeColors ADD COLUMN team VARCHAR(2) NOT NULL DEFAULT 'CT' AFTER steamid", conn);
            await alterCmd.ExecuteNonQueryAsync();

            // Drop old PK and add new composite PK
            try
            {
                await using var pkCmd = new MySqlCommand(
                    "ALTER TABLE zSmokeColors DROP PRIMARY KEY, ADD PRIMARY KEY (steamid, team)", conn);
                await pkCmd.ExecuteNonQueryAsync();
            }
            catch { /* PK may already be correct */ }

            // Duplicate CT rows as T rows
            await using var dupCmd = new MySqlCommand(
                "INSERT IGNORE INTO zSmokeColors (steamid, team, color) SELECT steamid, 'T', color FROM zSmokeColors WHERE team = 'CT'", conn);
            var duplicated = await dupCmd.ExecuteNonQueryAsync();

            Console.WriteLine($"[zVIPCore] Migration complete: duplicated {duplicated} smoke entries for T team");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[zVIPCore] Warning: smoke team migration skipped: {ex.Message}");
        }
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

    public async Task<string?> GetPlayerWeaponAsync(ulong steamId, string weaponType, string team)
    {
        if (_weaponCache.TryGetValue((steamId, weaponType, team), out var cached))
            return cached;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "SELECT uniqueid FROM zWeaponEquipments WHERE steamid = @steamid AND weapon_type = @weapon_type AND team = @team LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@weapon_type", weaponType);
        cmd.Parameters.AddWithValue("@team", team);

        var result = await cmd.ExecuteScalarAsync();
        if (result is string uniqueId && !string.IsNullOrEmpty(uniqueId))
        {
            _weaponCache[(steamId, weaponType, team)] = uniqueId;
            return uniqueId;
        }

        return null;
    }

    public async Task<Dictionary<string, string>> GetAllPlayerWeaponsAsync(ulong steamId, string team)
    {
        var weapons = new Dictionary<string, string>();

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "SELECT weapon_type, uniqueid FROM zWeaponEquipments WHERE steamid = @steamid AND team = @team", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@team", team);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var weaponType = reader.GetString(0);
            var uniqueId = reader.GetString(1);
            weapons[weaponType] = uniqueId;
            _weaponCache[(steamId, weaponType, team)] = uniqueId;
        }

        return weapons;
    }

    public async Task SavePlayerWeaponAsync(ulong steamId, string weaponType, string team, string uniqueId)
    {
        _weaponCache[(steamId, weaponType, team)] = uniqueId;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(@"
            INSERT INTO zWeaponEquipments (steamid, weapon_type, team, uniqueid)
            VALUES (@steamid, @weapon_type, @team, @uniqueid)
            ON DUPLICATE KEY UPDATE uniqueid = @uniqueid", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@weapon_type", weaponType);
        cmd.Parameters.AddWithValue("@team", team);
        cmd.Parameters.AddWithValue("@uniqueid", uniqueId);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemovePlayerWeaponAsync(ulong steamId, string weaponType, string team)
    {
        _weaponCache.TryRemove((steamId, weaponType, team), out _);

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "DELETE FROM zWeaponEquipments WHERE steamid = @steamid AND weapon_type = @weapon_type AND team = @team", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@weapon_type", weaponType);
        cmd.Parameters.AddWithValue("@team", team);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemoveAllPlayerWeaponsAsync(ulong steamId)
    {
        var keysToRemove = _weaponCache.Keys.Where(k => k.SteamId == steamId).ToList();
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

    public async Task<string?> GetPlayerSmokeColorAsync(ulong steamId, string team)
    {
        if (_smokeCache.TryGetValue((steamId, team), out var cached))
            return cached;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "SELECT color FROM zSmokeColors WHERE steamid = @steamid AND team = @team LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@team", team);

        var result = await cmd.ExecuteScalarAsync();
        if (result is string color && !string.IsNullOrEmpty(color))
        {
            _smokeCache[(steamId, team)] = color;
            return color;
        }

        return null;
    }

    public string? GetSmokeColorCached(ulong steamId, string team)
    {
        return _smokeCache.TryGetValue((steamId, team), out var cached) ? cached : null;
    }

    public async Task SavePlayerSmokeColorAsync(ulong steamId, string team, string color)
    {
        _smokeCache[(steamId, team)] = color;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(@"
            INSERT INTO zSmokeColors (steamid, team, color)
            VALUES (@steamid, @team, @color)
            ON DUPLICATE KEY UPDATE color = @color", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@team", team);
        cmd.Parameters.AddWithValue("@color", color);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemovePlayerSmokeColorAsync(ulong steamId, string team)
    {
        _smokeCache.TryRemove((steamId, team), out _);

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "DELETE FROM zSmokeColors WHERE steamid = @steamid AND team = @team", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@team", team);

        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Player Particles

    public async Task<string?> GetPlayerParticleAsync(ulong steamId, CsTeam team)
    {
        var teamStr = GetTeamString(team);
        if (_particleCache.TryGetValue((steamId, teamStr), out var cached))
            return cached;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "SELECT particle_path FROM zPlayerParticles WHERE steamid = @steamid AND team = @team LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@team", teamStr);

        var result = await cmd.ExecuteScalarAsync();
        if (result is string path)
        {
            _particleCache[(steamId, teamStr)] = path;
            return path;
        }

        return null;
    }

    public string? GetPlayerParticleCached(ulong steamId, string team)
    {
        return _particleCache.TryGetValue((steamId, team), out var cached) ? cached : null;
    }

    public async Task SavePlayerParticleAsync(ulong steamId, string team, string path)
    {
        _particleCache[(steamId, team)] = path;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(@"
            INSERT INTO zPlayerParticles (steamid, team, particle_path)
            VALUES (@steamid, @team, @particle_path)
            ON DUPLICATE KEY UPDATE particle_path = @particle_path", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@team", team);
        cmd.Parameters.AddWithValue("@particle_path", path);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemovePlayerParticleAsync(ulong steamId, string team)
    {
        _particleCache.TryRemove((steamId, team), out _);

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "DELETE FROM zPlayerParticles WHERE steamid = @steamid AND team = @team", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@team", team);

        await cmd.ExecuteNonQueryAsync();
    }

    #endregion


    #region MVP

    public async Task<MvpPlayerData?> GetPlayerMvpAsync(ulong steamId, string team)
    {
        var key = (steamId, team);
        if (_mvpCache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            await using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = new MySqlCommand(
                "SELECT mvp_name, mvp_sound FROM zMVP WHERE steam_id = @steamId AND team = @team", conn);
            cmd.Parameters.AddWithValue("@steamId", steamId);
            cmd.Parameters.AddWithValue("@team", team);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var data = new MvpPlayerData
                {
                    MvpName = reader.IsDBNull(reader.GetOrdinal("mvp_name")) ? null : reader.GetString("mvp_name"),
                    MvpSound = reader.IsDBNull(reader.GetOrdinal("mvp_sound")) ? null : reader.GetString("mvp_sound")
                };
                _mvpCache[key] = data;
                return data;
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[zVIPCore] Failed to get MVP preference for {steamId}/{team}: {ex.Message}");
            return null;
        }
    }

    public async Task SavePlayerMvpAsync(ulong steamId, string team, string? mvpName, string? mvpSound)
    {
        try
        {
            await using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = new MySqlCommand(@"
                INSERT INTO zMVP (steam_id, team, mvp_name, mvp_sound)
                VALUES (@steamId, @team, @mvpName, @mvpSound)
                ON DUPLICATE KEY UPDATE
                    mvp_name = @mvpName,
                    mvp_sound = @mvpSound,
                    updated_at = CURRENT_TIMESTAMP
            ", conn);
            cmd.Parameters.AddWithValue("@steamId", steamId);
            cmd.Parameters.AddWithValue("@team", team);
            cmd.Parameters.AddWithValue("@mvpName", mvpName ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@mvpSound", mvpSound ?? (object)DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[zVIPCore] Failed to save MVP preference for {steamId}/{team}: {ex.Message}");
        }
    }

    public (string? mvpName, string? mvpSound) GetMvpFromCache(ulong steamId, string team)
    {
        return _mvpCache.TryGetValue((steamId, team), out var data)
            ? (data.MvpName, data.MvpSound)
            : (null, null);
    }

    public void SetMvpCache(ulong steamId, string team, string mvpName, string mvpSound)
    {
        var key = (steamId, team);
        var data = _mvpCache.GetOrAdd(key, _ => new MvpPlayerData());
        data.MvpName = mvpName;
        data.MvpSound = mvpSound;
        _mvpDirty[key] = true;
    }

    public void RemoveMvpCache(ulong steamId, string team)
    {
        var key = (steamId, team);
        if (_mvpCache.TryGetValue(key, out var data))
        {
            data.MvpName = null;
            data.MvpSound = null;
            _mvpDirty[key] = true;
        }
    }

    public void ClearMvpPlayer(ulong steamId)
    {
        foreach (var team in new[] { "CT", "T" })
        {
            _mvpCache.TryRemove((steamId, team), out _);
            _mvpDirty.TryRemove((steamId, team), out _);
        }
    }

    public async Task FlushMvpAsync(ulong steamId)
    {
        if (steamId == 0) return;
        foreach (var team in new[] { "CT", "T" })
        {
            var key = (steamId, team);
            if (_mvpDirty.TryRemove(key, out _) && _mvpCache.TryGetValue(key, out var data))
                await SavePlayerMvpAsync(steamId, team, data.MvpName, data.MvpSound);
        }
    }

    public async Task FlushAllMvpAsync()
    {
        var tasks = _mvpDirty.Keys
            .Where(key => _mvpDirty.TryRemove(key, out _) && _mvpCache.TryGetValue(key, out _))
            .Select(key => SavePlayerMvpAsync(key.Item1, key.Item2, _mvpCache[key].MvpName, _mvpCache[key].MvpSound))
            .ToList();

        await Task.WhenAll(tasks);
    }

    public int GetMvpDirtyCount() => _mvpDirty.Count;

    public void ClearMvpAll()
    {
        _mvpCache.Clear();
        _mvpDirty.Clear();
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

        // 2. Weapons (CT + T)
        await using (var cmd = new MySqlCommand(
            "SELECT team, weapon_type, uniqueid FROM zWeaponEquipments WHERE steamid = @steamid", conn))
        {
            cmd.Parameters.AddWithValue("@steamid", steamId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var team = reader.GetString(0);
                var weaponType = reader.GetString(1);
                var uniqueId = reader.GetString(2);
                _weaponCache[(steamId, weaponType, team)] = uniqueId;
            }
        }

        // 3. Smoke colors (CT + T)
        await using (var cmd = new MySqlCommand(
            "SELECT team, color FROM zSmokeColors WHERE steamid = @steamid", conn))
        {
            cmd.Parameters.AddWithValue("@steamid", steamId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var team = reader.GetString(0);
                var color = reader.GetString(1);
                if (!string.IsNullOrEmpty(color))
                    _smokeCache[(steamId, team)] = color;
            }
        }

        // 4. MVP preferences (CT + T)
        await using (var cmd = new MySqlCommand(
            "SELECT team, mvp_name, mvp_sound FROM zMVP WHERE steam_id = @steamid", conn))
        {
            cmd.Parameters.AddWithValue("@steamid", steamId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var team = reader.GetString("team");
                _mvpCache[(steamId, team)] = new MvpPlayerData
                {
                    MvpName = reader.IsDBNull(reader.GetOrdinal("mvp_name")) ? null : reader.GetString("mvp_name"),
                    MvpSound = reader.IsDBNull(reader.GetOrdinal("mvp_sound")) ? null : reader.GetString("mvp_sound")
                };
            }
        }

        // 5. Particles (CT + T)
        await using (var cmd = new MySqlCommand(
            "SELECT team, particle_path FROM zPlayerParticles WHERE steamid = @steamid", conn))
        {
            cmd.Parameters.AddWithValue("@steamid", steamId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var team = reader.GetString(0);
                var path = reader.GetString(1);
                _particleCache[(steamId, team)] = path;
            }
        }

    }

    public void ClearPlayerCache(ulong steamId)
    {
        _modelCache.TryRemove((steamId, CsTeam.Terrorist), out _);
        _modelCache.TryRemove((steamId, CsTeam.CounterTerrorist), out _);

        var weaponKeys = _weaponCache.Keys.Where(k => k.SteamId == steamId).ToList();
        foreach (var key in weaponKeys)
            _weaponCache.TryRemove(key, out _);

        var smokeKeys = _smokeCache.Keys.Where(k => k.SteamId == steamId).ToList();
        foreach (var key in smokeKeys)
            _smokeCache.TryRemove(key, out _);
        foreach (var t in new[] { "CT", "T" })
        {
            _mvpCache.TryRemove((steamId, t), out _);
            _mvpDirty.TryRemove((steamId, t), out _);
            _particleCache.TryRemove((steamId, t), out _);
        }
    }

    public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player?.IsBot != false) return HookResult.Continue;

        _ = zVIPCore.SafeAsync(() => PreloadAllPlayerDataAsync(player.SteamID));

        return HookResult.Continue;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _modelCache.Clear();
        _weaponCache.Clear();
        _smokeCache.Clear();
        _mvpCache.Clear();
        _mvpDirty.Clear();
        _particleCache.Clear();
        MySqlConnection.ClearAllPools();

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    #endregion
}

public class MvpPlayerData
{
    public string? MvpName { get; set; }
    public string? MvpSound { get; set; }
}
