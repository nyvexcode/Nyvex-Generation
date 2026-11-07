using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NyvexGeneration.Core;
using NyvexGeneration.Models;

namespace NyvexGeneration.Data;

internal static class SqliteHelpers
{
    public static string ToDb(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    public static DateTimeOffset FromDb(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public static async Task ExecuteAsync(SqliteDatabase db, string sql, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

public sealed class SqliteLookupHistoryRepository : ILookupHistoryRepository
{
    private readonly SqliteDatabase _db;

    public SqliteLookupHistoryRepository(SqliteDatabase db) => _db = db;

    public async Task SaveAsync(IEnumerable<LookupResult> results, CancellationToken cancellationToken)
    {
        await using var connection = await _db.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var r in results)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO lookup_history
                    (username, status, failure_reason, details, checked_at, roblox_user_id, matched_name, http_status)
                VALUES ($u, $s, $f, $d, $c, $id, $m, $h);
                """;
            command.Parameters.AddWithValue("$u", r.Username);
            command.Parameters.AddWithValue("$s", (int)r.Status);
            command.Parameters.AddWithValue("$f", (int)r.FailureReason);
            command.Parameters.AddWithValue("$d", r.Details);
            command.Parameters.AddWithValue("$c", SqliteHelpers.ToDb(r.CheckedAt));
            command.Parameters.AddWithValue("$id", (object?)r.RobloxUserId ?? DBNull.Value);
            command.Parameters.AddWithValue("$m", (object?)r.MatchedName ?? DBNull.Value);
            command.Parameters.AddWithValue("$h", (object?)r.HttpStatusCode ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LookupResult>> GetRecentAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await _db.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT username, status, failure_reason, details, checked_at, roblox_user_id, matched_name, http_status
            FROM lookup_history
            WHERE id IN (SELECT MAX(id) FROM lookup_history GROUP BY username)
            ORDER BY id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Max(1, limit));

        var list = new List<LookupResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new LookupResult(
                reader.GetString(0),
                (LookupStatus)reader.GetInt32(1),
                (LookupFailureReason)reader.GetInt32(2),
                reader.GetString(3),
                SqliteHelpers.FromDb(reader.GetString(4)),
                reader.IsDBNull(5) ? null : reader.GetInt64(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7)));
        }

        return list;
    }

    public Task ClearAsync(CancellationToken cancellationToken) =>
        SqliteHelpers.ExecuteAsync(_db, "DELETE FROM lookup_history;", cancellationToken);
}

public sealed class SqliteGenerationHistoryRepository : IGenerationHistoryRepository
{
    private readonly SqliteDatabase _db;

    public SqliteGenerationHistoryRepository(SqliteDatabase db) => _db = db;

    public async Task SaveAsync(IEnumerable<string> usernames, DateTimeOffset generatedAt, CancellationToken cancellationToken)
    {
        await using var connection = await _db.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var stamp = SqliteHelpers.ToDb(generatedAt);

        foreach (var username in usernames)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO generation_history (username, generated_at) VALUES ($u, $t);";
            command.Parameters.AddWithValue("$u", username);
            command.Parameters.AddWithValue("$t", stamp);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlySet<string>> GetAllUsernamesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _db.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT username FROM generation_history;";

        var set = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            set.Add(reader.GetString(0));
        }

        return set;
    }

    public async Task<IReadOnlyList<GenerationHistoryItem>> GetRecentAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await _db.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT username, generated_at FROM generation_history ORDER BY id DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", Math.Max(1, limit));

        var list = new List<GenerationHistoryItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new GenerationHistoryItem(reader.GetString(0), SqliteHelpers.FromDb(reader.GetString(1))));
        }

        return list;
    }

    public Task ClearAsync(CancellationToken cancellationToken) =>
        SqliteHelpers.ExecuteAsync(_db, "DELETE FROM generation_history;", cancellationToken);
}

public sealed class SqliteFavoritesRepository : IFavoritesRepository
{
    private readonly SqliteDatabase _db;

    public SqliteFavoritesRepository(SqliteDatabase db) => _db = db;

    public async Task AddAsync(string username, CancellationToken cancellationToken)
    {
        await using var connection = await _db.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO favorites (username, added_at) VALUES ($u, $t);";
        command.Parameters.AddWithValue("$u", username);
        command.Parameters.AddWithValue("$t", SqliteHelpers.ToDb(DateTimeOffset.Now));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string username, CancellationToken cancellationToken)
    {
        await using var connection = await _db.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM favorites WHERE username = $u;";
        command.Parameters.AddWithValue("$u", username);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _db.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT username FROM favorites ORDER BY added_at, username;";

        var list = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(reader.GetString(0));
        }

        return list;
    }

    public Task ClearAsync(CancellationToken cancellationToken) =>
        SqliteHelpers.ExecuteAsync(_db, "DELETE FROM favorites;", cancellationToken);
}

public sealed class SqliteSettingsRepository : ISettingsRepository
{
    private const string Key = "app_settings";
    private readonly SqliteDatabase _db;

    public SqliteSettingsRepository(SqliteDatabase db) => _db = db;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _db.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $k;";
        command.Parameters.AddWithValue("$k", Key);

        var raw = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new AppSettings().Normalize();
        }

        try
        {
            return (JsonSerializer.Deserialize<AppSettings>(raw) ?? new AppSettings()).Normalize();
        }
        catch (JsonException)
        {
            return new AppSettings().Normalize();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await using var connection = await _db.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings (key, value) VALUES ($k, $v)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$k", Key);
        command.Parameters.AddWithValue("$v", JsonSerializer.Serialize(settings.Clone().Normalize()));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class SqliteStatsRepository : IStatsRepository
{
    private readonly SqliteDatabase _db;

    public SqliteStatsRepository(SqliteDatabase db) => _db = db;

    public async Task AddAsync(string key, long delta, CancellationToken cancellationToken)
    {
        if (delta == 0)
        {
            return;
        }

        await using var connection = await _db.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO stats (key, value) VALUES ($k, $v)
            ON CONFLICT(key) DO UPDATE SET value = value + excluded.value;
            """;
        command.Parameters.AddWithValue("$k", key);
        command.Parameters.AddWithValue("$v", delta);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, long>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _db.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM stats;";

        var map = new Dictionary<string, long>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            map[reader.GetString(0)] = reader.GetInt64(1);
        }

        return map;
    }

    public Task ClearAsync(CancellationToken cancellationToken) =>
        SqliteHelpers.ExecuteAsync(_db, "DELETE FROM stats;", cancellationToken);
}
