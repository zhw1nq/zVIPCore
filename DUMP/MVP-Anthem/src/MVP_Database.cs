using MySqlConnector;
using Microsoft.Extensions.Logging;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using System.Collections.Concurrent;

namespace MVPAnthem;

public class PlayerPreference
{
    public ulong SteamId { get; set; }
    public string? MVPName { get; set; }
    public string? MVPSound { get; set; }
}

public class CachedPlayerData
{
    public ulong SteamId { get; set; }
    public string? MVPName { get; set; }
    public string? MVPSound { get; set; }
}

public interface IDatabaseProvider
{
    Task InitializeAsync();
    Task<PlayerPreference?> GetPlayerPreferenceAsync(ulong steamId);
    Task SavePlayerPreferenceAsync(ulong steamId, string? mvpName, string? mvpSound);
    Task<bool> TestConnectionAsync();
}

public class MySqlDatabaseProvider : IDatabaseProvider
{
    private readonly string _connectionString;
    private readonly ILogger? _logger;

    public MySqlDatabaseProvider(string connectionString, ILogger? logger = null)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Database connection test failed: {ex.Message}");
            return false;
        }
    }

    public async Task InitializeAsync()
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string createTableQuery = @"
            CREATE TABLE IF NOT EXISTS mvp_player_preferences (
                steam_id BIGINT UNSIGNED PRIMARY KEY,
                mvp_name VARCHAR(255) NULL,
                mvp_sound VARCHAR(255) NULL,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                INDEX idx_steam_id (steam_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        ";

        await using var command = new MySqlCommand(createTableQuery, connection);
        await command.ExecuteNonQueryAsync();
        _logger?.LogInformation("[MVP-Anthem] Database table created/verified successfully");
    }

    public async Task<PlayerPreference?> GetPlayerPreferenceAsync(ulong steamId)
    {
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            const string query = @"
                SELECT steam_id, mvp_name, mvp_sound
                FROM mvp_player_preferences
                WHERE steam_id = @steamId
            ";

            await using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@steamId", steamId);

            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new PlayerPreference
                {
                    SteamId = reader.GetUInt64("steam_id"),
                    MVPName = reader.IsDBNull(reader.GetOrdinal("mvp_name")) ? null : reader.GetString("mvp_name"),
                    MVPSound = reader.IsDBNull(reader.GetOrdinal("mvp_sound")) ? null : reader.GetString("mvp_sound")
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Failed to get player preference for {steamId}: {ex.Message}");
            return null;
        }
    }

    public async Task SavePlayerPreferenceAsync(ulong steamId, string? mvpName, string? mvpSound)
    {
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            const string query = @"
                INSERT INTO mvp_player_preferences (steam_id, mvp_name, mvp_sound)
                VALUES (@steamId, @mvpName, @mvpSound)
                ON DUPLICATE KEY UPDATE
                    mvp_name = @mvpName,
                    mvp_sound = @mvpSound,
                    updated_at = CURRENT_TIMESTAMP
            ";

            await using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@steamId", steamId);
            command.Parameters.AddWithValue("@mvpName", mvpName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@mvpSound", mvpSound ?? (object)DBNull.Value);

            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Failed to save player preference for {steamId}: {ex.Message}");
        }
    }
}

public class PlayerCache(IDatabaseProvider database)
{
    private readonly ConcurrentDictionary<ulong, CachedPlayerData> _cache = new();
    private readonly ConcurrentDictionary<ulong, bool> _dirty = new();

    public async Task<CachedPlayerData?> GetPlayerDataAsync(CCSPlayerController player)
    {
        if (player == null || !player.IsValid || player.SteamID == 0)
            return null;

        if (_cache.TryGetValue(player.SteamID, out var cached))
            return cached;

        var pref = await database.GetPlayerPreferenceAsync(player.SteamID);
        if (pref == null) return null;

        var data = new CachedPlayerData
        {
            SteamId = pref.SteamId,
            MVPName = pref.MVPName,
            MVPSound = pref.MVPSound
        };
        _cache[player.SteamID] = data;
        return data;
    }

    public (string? mvpName, string? mvpSound) GetMVP(CCSPlayerController player)
    {
        if (player == null || !player.IsValid || player.SteamID == 0)
            return (null, null);

        return _cache.TryGetValue(player.SteamID, out var data)
            ? (data.MVPName, data.MVPSound)
            : (null, null);
    }

    public void SetMVP(CCSPlayerController player, string mvpName, string mvpSound)
    {
        if (player == null || !player.IsValid || player.SteamID == 0) return;

        var data = _cache.GetOrAdd(player.SteamID, _ => new CachedPlayerData { SteamId = player.SteamID });
        data.MVPName = mvpName;
        data.MVPSound = mvpSound;
        _dirty[player.SteamID] = true;
    }

    public void RemoveMVP(CCSPlayerController player)
    {
        if (player == null || !player.IsValid || player.SteamID == 0) return;

        if (_cache.TryGetValue(player.SteamID, out var data))
        {
            data.MVPName = null;
            data.MVPSound = null;
            _dirty[player.SteamID] = true;
        }
    }

    public async Task FlushPlayerAsync(CCSPlayerController player)
    {
        if (player == null || player.SteamID == 0) return;
        await FlushPlayerAsync(player.SteamID);
    }

    public async Task FlushPlayerAsync(ulong steamId)
    {
        if (steamId == 0) return;
        if (_dirty.TryRemove(steamId, out _) && _cache.TryGetValue(steamId, out var data))
            await database.SavePlayerPreferenceAsync(data.SteamId, data.MVPName, data.MVPSound);
    }

    public async Task FlushAllAsync()
    {
        var tasks = _dirty.Keys
            .Where(id => _dirty.TryRemove(id, out _) && _cache.TryGetValue(id, out _))
            .Select(id => database.SavePlayerPreferenceAsync(_cache[id].SteamId, _cache[id].MVPName, _cache[id].MVPSound))
            .ToList();

        await Task.WhenAll(tasks);
    }

    public void RemovePlayer(CCSPlayerController player)
    {
        if (player == null || player.SteamID == 0) return;
        RemovePlayer(player.SteamID);
    }

    public void RemovePlayer(ulong steamId)
    {
        if (steamId == 0) return;
        _cache.TryRemove(steamId, out _);
        _dirty.TryRemove(steamId, out _);
    }

    public void ClearAll()
    {
        _cache.Clear();
        _dirty.Clear();
    }

    public int GetDirtyCount() => _dirty.Count;
}
