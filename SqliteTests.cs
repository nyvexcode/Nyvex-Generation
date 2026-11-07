using NyvexGeneration.Core;
using NyvexGeneration.Data;
using NyvexGeneration.Models;
using Xunit;

namespace NyvexGeneration.Tests;

public class SqliteTests
{
    private static async Task<(SqliteDatabase Db, TempDirectory Temp)> CreateAsync()
    {
        var temp = new TempDirectory();
        var db = new SqliteDatabase(temp.File("nested/nyvex.db"));
        await db.InitializeAsync(CancellationToken.None);
        return (db, temp);
    }

    private static LookupResult R(string name, LookupStatus status, DateTimeOffset at, long? id = null) =>
        new(name, status, LookupFailureReason.None, "detalhe", at, id, id is null ? null : name, 200);

    [Fact]
    public async Task Initialize_IsIdempotentAndCreatesFile()
    {
        var (db, temp) = await CreateAsync();
        using (temp)
        {
            await db.InitializeAsync(CancellationToken.None);

            Assert.True(File.Exists(temp.File("nested/nyvex.db")));
        }
    }

    [Fact]
    public async Task LookupHistory_RoundTripsAllFields()
    {
        var (db, temp) = await CreateAsync();
        using (temp)
        {
            var repo = new SqliteLookupHistoryRepository(db);
            var at = new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.FromHours(-3));

            await repo.SaveAsync(new[]
            {
                R("abcd", LookupStatus.AccountFound, at, 123),
                new LookupResult("zzzz", LookupStatus.Inconclusive, LookupFailureReason.RateLimited, "429", at, HttpStatusCode: 429)
            }, CancellationToken.None);

            var all = await repo.GetRecentAsync(10, CancellationToken.None);

            Assert.Equal(2, all.Count);
            var found = all.Single(r => r.Username == "abcd");
            Assert.Equal(LookupStatus.AccountFound, found.Status);
            Assert.Equal(123, found.RobloxUserId);
            Assert.Equal("abcd", found.MatchedName);
            Assert.Equal(200, found.HttpStatusCode);
            Assert.Equal(at, found.CheckedAt);

            var limited = all.Single(r => r.Username == "zzzz");
            Assert.Equal(LookupFailureReason.RateLimited, limited.FailureReason);
            Assert.Null(limited.RobloxUserId);
            Assert.Null(limited.MatchedName);
        }
    }

    [Fact]
    public async Task LookupHistory_ReturnsOnlyLatestPerUsernameNewestFirstAndHonorsLimit()
    {
        var (db, temp) = await CreateAsync();
        using (temp)
        {
            var repo = new SqliteLookupHistoryRepository(db);
            var t = DateTimeOffset.Now;

            await repo.SaveAsync(new[] { R("aaa", LookupStatus.NoPublicMatch, t) }, CancellationToken.None);
            await repo.SaveAsync(new[] { R("bbb", LookupStatus.NoPublicMatch, t) }, CancellationToken.None);
            await repo.SaveAsync(new[] { R("aaa", LookupStatus.AccountFound, t.AddMinutes(1), 9) }, CancellationToken.None);

            var all = await repo.GetRecentAsync(10, CancellationToken.None);
            Assert.Equal(new[] { "aaa", "bbb" }, all.Select(r => r.Username));
            Assert.Equal(LookupStatus.AccountFound, all[0].Status);

            var limited = await repo.GetRecentAsync(1, CancellationToken.None);
            Assert.Single(limited);
        }
    }

    [Fact]
    public async Task LookupHistory_ClearRemovesEverything()
    {
        var (db, temp) = await CreateAsync();
        using (temp)
        {
            var repo = new SqliteLookupHistoryRepository(db);
            await repo.SaveAsync(new[] { R("aaa", LookupStatus.NoPublicMatch, DateTimeOffset.Now) }, CancellationToken.None);

            await repo.ClearAsync(CancellationToken.None);

            Assert.Empty(await repo.GetRecentAsync(10, CancellationToken.None));
        }
    }

    [Fact]
    public async Task GenerationHistory_SavesListsRecentAndClears()
    {
        var (db, temp) = await CreateAsync();
        using (temp)
        {
            var repo = new SqliteGenerationHistoryRepository(db);
            var t = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

            await repo.SaveAsync(new[] { "aaa", "bbb" }, t, CancellationToken.None);
            await repo.SaveAsync(new[] { "ccc", "aaa" }, t.AddHours(1), CancellationToken.None);

            var names = await repo.GetAllUsernamesAsync(CancellationToken.None);
            Assert.Equal(3, names.Count);
            Assert.Contains("aaa", names);

            var recent = await repo.GetRecentAsync(2, CancellationToken.None);
            Assert.Equal(new[] { "aaa", "ccc" }, recent.Select(i => i.Username));
            Assert.Equal(t.AddHours(1), recent[0].GeneratedAt);

            await repo.ClearAsync(CancellationToken.None);
            Assert.Empty(await repo.GetAllUsernamesAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task Favorites_AddIsIdempotentRemoveAndClear()
    {
        var (db, temp) = await CreateAsync();
        using (temp)
        {
            var repo = new SqliteFavoritesRepository(db);

            await repo.AddAsync("aaa", CancellationToken.None);
            await repo.AddAsync("aaa", CancellationToken.None);
            await repo.AddAsync("bbb", CancellationToken.None);
            Assert.Equal(2, (await repo.GetAllAsync(CancellationToken.None)).Count);

            await repo.RemoveAsync("aaa", CancellationToken.None);
            Assert.Equal(new[] { "bbb" }, await repo.GetAllAsync(CancellationToken.None));

            await repo.ClearAsync(CancellationToken.None);
            Assert.Empty(await repo.GetAllAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task Settings_DefaultsWhenEmptyThenRoundTripAndOverwrite()
    {
        var (db, temp) = await CreateAsync();
        using (temp)
        {
            var repo = new SqliteSettingsRepository(db);

            var defaults = await repo.LoadAsync(CancellationToken.None);
            Assert.Equal(new AppSettings().BatchSize, defaults.BatchSize);

            await repo.SaveAsync(new AppSettings
            {
                Theme = "Magenta", DefaultQuantity = 250, DefaultLength = 5, BatchSize = 20,
                DelayBetweenRequestsMs = 3000, CsvDelimiter = ',', AutoCheckAfterGeneration = false
            }, CancellationToken.None);
            await repo.SaveAsync(new AppSettings { Theme = "Indigo", CsvDelimiter = ',', BatchSize = 30 }, CancellationToken.None);

            var loaded = await repo.LoadAsync(CancellationToken.None);
            Assert.Equal("Indigo", loaded.Theme);
            Assert.Equal(30, loaded.BatchSize);
            Assert.Equal(',', loaded.CsvDelimiter);
        }
    }

    [Fact]
    public async Task Settings_FullRoundTripPreservesEveryField()
    {
        var (db, temp) = await CreateAsync();
        using (temp)
        {
            var repo = new SqliteSettingsRepository(db);
            var original = new AppSettings
            {
                Theme = "Violet", DefaultQuantity = 250, DefaultLength = 5, AutoCheckAfterGeneration = false,
                AvoidPreviouslyGenerated = false, DelayBetweenRequestsMs = 3000, BatchSize = 20,
                RequestTimeoutSeconds = 30, MaxRetriesOnRateLimit = 5, CsvDelimiter = ','
            };

            await repo.SaveAsync(original, CancellationToken.None);
            var loaded = await repo.LoadAsync(CancellationToken.None);

            Assert.Equal("Violet", loaded.Theme);
            Assert.Equal(250, loaded.DefaultQuantity);
            Assert.Equal(5, loaded.DefaultLength);
            Assert.False(loaded.AutoCheckAfterGeneration);
            Assert.False(loaded.AvoidPreviouslyGenerated);
            Assert.Equal(3000, loaded.DelayBetweenRequestsMs);
            Assert.Equal(20, loaded.BatchSize);
            Assert.Equal(30, loaded.RequestTimeoutSeconds);
            Assert.Equal(5, loaded.MaxRetriesOnRateLimit);
            Assert.Equal(',', loaded.CsvDelimiter);
        }
    }

    [Fact]
    public async Task Settings_CorruptedJsonFallsBackToDefaults()
    {
        var (db, temp) = await CreateAsync();
        using (temp)
        {
            await using (var connection = await db.OpenAsync(CancellationToken.None))
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "INSERT INTO settings (key, value) VALUES ('app_settings', '{quebrado');";
                await command.ExecuteNonQueryAsync();
            }

            var loaded = await new SqliteSettingsRepository(db).LoadAsync(CancellationToken.None);

            Assert.Equal(new AppSettings().Theme, loaded.Theme);
        }
    }

    [Fact]
    public async Task Stats_AccumulateAndClear()
    {
        var (db, temp) = await CreateAsync();
        using (temp)
        {
            var repo = new SqliteStatsRepository(db);

            await repo.AddAsync(StatKeys.Generated, 10, CancellationToken.None);
            await repo.AddAsync(StatKeys.Generated, 5, CancellationToken.None);
            await repo.AddAsync(StatKeys.Errors, 0, CancellationToken.None);

            var all = await repo.GetAllAsync(CancellationToken.None);
            Assert.Equal(15, all[StatKeys.Generated]);
            Assert.False(all.ContainsKey(StatKeys.Errors));

            await repo.ClearAsync(CancellationToken.None);
            Assert.Empty(await repo.GetAllAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task Backup_CopiesDataAndOverwritesExistingFile()
    {
        var (db, temp) = await CreateAsync();
        using (temp)
        {
            await new SqliteFavoritesRepository(db).AddAsync("aaa", CancellationToken.None);
            var backupPath = temp.File("backups/copia.db");
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            await File.WriteAllTextAsync(backupPath, "lixo");

            await db.BackupAsync(backupPath, CancellationToken.None);

            var restored = new SqliteDatabase(backupPath);
            var favorites = await new SqliteFavoritesRepository(restored).GetAllAsync(CancellationToken.None);
            Assert.Equal(new[] { "aaa" }, favorites);
        }
    }

    [Fact]
    public async Task Backup_RejectsEmptyPath()
    {
        var (db, temp) = await CreateAsync();
        using (temp)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => db.BackupAsync(" ", CancellationToken.None));
        }
    }

    [Fact]
    public void Constructor_RejectsEmptyPath()
    {
        Assert.Throws<ArgumentException>(() => new SqliteDatabase(" "));
    }
}
