using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sraz.Application.Rooms;
using Sraz.Infrastructure.Persistence;
using Xunit;

namespace Sraz.Tests;

public sealed class RoomLeaderboardPersistenceTests
{
    [Fact]
    public async Task FinishedResultsAreIdempotentAndGroupedByIdentityRatherThanNickname()
    {
        await using var provider = CreateInMemoryProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var store = new EfRoomStore(scopeFactory);
        var leaderboard = new EfRoomLeaderboard(scopeFactory);
        var first = FinishedSnapshot();
        await store.SaveAsync(first);
        await new EfRoomStore(scopeFactory).SaveAsync(first);
        Assert.All(await leaderboard.GetTopAsync(), entry => Assert.Equal(1, entry.GamesPlayed));

        var second = FinishedSnapshot();
        second.Result!.FinishedAtUtc = first.Result!.FinishedAtUtc.AddDays(1);
        second.Result.Players[0].PlayerId = first.Result.Players[0].PlayerId;
        second.Result.Players[0].Name = "Renamed";
        second.Result.Players[0].Won = false;
        second.Result.Players[1].Won = true;
        await store.SaveAsync(second);

        var rows = await leaderboard.GetTopAsync();
        Assert.Equal(3, rows.Count);
        var renamed = Assert.Single(rows, row => row.PlayerId == first.Result.Players[0].PlayerId);
        Assert.Equal(("Renamed", 1, 2, second.Result.FinishedAtUtc),
            (renamed.Nickname, renamed.Wins, renamed.GamesPlayed, renamed.LastPlayedAtUtc));
        Assert.Equal(renamed, Assert.Single(await leaderboard.GetTopAsync(1)));
        Assert.Empty(await leaderboard.GetTopAsync(0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => leaderboard.GetTopAsync(-1));
    }

    [Fact]
    public async Task OnlyFinishedRoomResultsAreCountedAndDeletingRoomsKeepsHistory()
    {
        await using var provider = CreateInMemoryProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var store = new EfRoomStore(scopeFactory);
        var leaderboard = new EfRoomLeaderboard(scopeFactory);
        var snapshot = FinishedSnapshot();
        snapshot.State = RoomState.InProgress;
        await store.SaveAsync(snapshot);
        Assert.Empty(await leaderboard.GetTopAsync());

        snapshot.State = RoomState.Finished;
        await store.SaveAsync(snapshot);
        await store.DeleteAsync(snapshot.Code);
        Assert.Equal(2, (await leaderboard.GetTopAsync()).Count);
        await store.SaveAsync(snapshot);
        Assert.All(await leaderboard.GetTopAsync(), row => Assert.Equal(1, row.GamesPlayed));
    }

    [Fact]
    public async Task LegacyRowsDoNotEnterRoomLeaderboardAndVersionOneSnapshotsLoad()
    {
        await using var provider = CreateInMemoryProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SrazDbContext>();
            db.LeaderboardEntries.Add(new LeaderboardEntryEntity { PlayerName = "Old quiz", Score = 900 });
            await db.SaveChangesAsync();
        }

        var snapshot = FinishedSnapshot();
        snapshot.SchemaVersion = 1;
        snapshot.State = RoomState.InProgress;
        snapshot.GameId = null;
        snapshot.Result = null;
        var store = new EfRoomStore(scopeFactory);
        await store.SaveAsync(snapshot);
        Assert.Equal(1, Assert.Single(await store.LoadActiveAsync()).SchemaVersion);
        Assert.Empty(await new EfRoomLeaderboard(scopeFactory).GetTopAsync());
    }

    [Fact]
    public async Task EmptyOrMismatchedGameIdentityCannotWriteResults()
    {
        await using var provider = CreateInMemoryProvider();
        var store = new EfRoomStore(provider.GetRequiredService<IServiceScopeFactory>());
        var snapshot = FinishedSnapshot();
        snapshot.Result!.GameId = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(snapshot));
        snapshot.GameId = snapshot.Result.GameId = Guid.Empty;
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(snapshot));
        Assert.Empty(await new EfRoomLeaderboard(provider.GetRequiredService<IServiceScopeFactory>()).GetTopAsync());
    }

    internal static ServiceProvider CreateInMemoryProvider()
    {
        var name = Guid.NewGuid().ToString();
        return new ServiceCollection()
            .AddDbContext<SrazDbContext>(options => options.UseInMemoryDatabase(name))
            .BuildServiceProvider();
    }

    internal static RoomSnapshot FinishedSnapshot()
    {
        var gameId = Guid.NewGuid();
        var playedAt = new DateTimeOffset(2026, 10, 21, 20, 0, 0, TimeSpan.Zero);
        return new RoomSnapshot
        {
            Code = "ABCDE",
            State = RoomState.Finished,
            GameId = gameId,
            CreatedAtUtc = playedAt.AddHours(-1),
            LastActivityUtc = playedAt,
            Result = new RoomGameResultSnapshot
            {
                GameId = gameId,
                FinishedAtUtc = playedAt,
                Players =
                [
                    new() { PlayerId = Guid.NewGuid(), Name = "Same nickname", Won = true },
                    new() { PlayerId = Guid.NewGuid(), Name = "Same nickname", Won = false }
                ]
            }
        };
    }
}

public sealed class PostgreSqlLeaderboardTests
{
    [PostgreSqlFact]
    public async Task SameGameSavedUnderDifferentRoomsStillCountsOnceAndUsesLatestNickname()
    {
        await using var database = await PostgreSqlLeaderboardDatabase.CreateAsync();
        var first = RoomLeaderboardPersistenceTests.FinishedSnapshot();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
        {
            var copy = RoomSnapshotSerializer.Deserialize(RoomSnapshotSerializer.Serialize(first))!;
            copy.Code = $"R{index:D4}";
            return new EfRoomStore(database.ScopeFactory).SaveAsync(copy);
        }));

        var leaderboard = new EfRoomLeaderboard(database.ScopeFactory);
        Assert.All(await leaderboard.GetTopAsync(), row => Assert.Equal(1, row.GamesPlayed));
        var next = RoomSnapshotSerializer.Deserialize(RoomSnapshotSerializer.Serialize(first))!;
        next.GameId = next.Result!.GameId = Guid.NewGuid();
        next.Result.FinishedAtUtc = first.Result!.FinishedAtUtc.AddDays(1);
        next.Result.Players[0].Name = "Latest nickname";
        await new EfRoomStore(database.ScopeFactory).SaveAsync(next);

        var rows = await leaderboard.GetTopAsync();
        Assert.All(rows, row => Assert.Equal(2, row.GamesPlayed));
        Assert.Equal("Latest nickname", rows[0].Nickname);
        Assert.Equal(next.Result.FinishedAtUtc, rows[0].LastPlayedAtUtc);
        Assert.Equal(2, rows[0].Wins);
    }

    [PostgreSqlFact]
    public async Task ConcurrentSavesAndRestartsKeepOneResultPerGameAndPlayer()
    {
        await using var database = await PostgreSqlLeaderboardDatabase.CreateAsync();
        var snapshot = RoomLeaderboardPersistenceTests.FinishedSnapshot();
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
            new EfRoomStore(database.ScopeFactory).SaveAsync(snapshot)));

        await using (var scope = database.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SrazDbContext>();
            Assert.Equal(2, await db.LeaderboardEntries.CountAsync());
            Assert.Equal(1, await db.GameSnapshots.CountAsync());
            Assert.Equal("Finished", (await db.GameRooms.SingleAsync()).State);
        }

        var leaderboard = new EfRoomLeaderboard(database.ScopeFactory);
        var rows = await leaderboard.GetTopAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[0].Wins);
        Assert.All(rows, row => Assert.Equal(1, row.GamesPlayed));

        // A new service provider represents a process restart, with no in-memory deduplication.
        await using var restarted = database.NewProvider();
        var restartedStore = new EfRoomStore(restarted.GetRequiredService<IServiceScopeFactory>());
        await restartedStore.SaveAsync(snapshot);
        snapshot.GameId = snapshot.Result!.GameId = Guid.NewGuid();
        await restartedStore.SaveAsync(snapshot);
        Assert.All(await leaderboard.GetTopAsync(), row => Assert.Equal(2, row.GamesPlayed));
    }

    [PostgreSqlFact]
    public async Task DatabaseFailureRollsBackSnapshotRoomAndAllResultsThenRetrySucceeds()
    {
        await using var database = await PostgreSqlLeaderboardDatabase.CreateAsync();
        var store = new EfRoomStore(database.ScopeFactory);
        var snapshot = RoomLeaderboardPersistenceTests.FinishedSnapshot();
        snapshot.State = RoomState.InProgress;
        await store.SaveAsync(snapshot);
        await using (var scope = database.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SrazDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "LeaderboardEntries" ADD CONSTRAINT "TestRejectName" CHECK ("PlayerName" <> 'Reject')""");
        }

        snapshot.State = RoomState.Finished;
        snapshot.Result!.Players[1].Name = "Reject";
        await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveAsync(snapshot));
        await using (var scope = database.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SrazDbContext>();
            Assert.Empty(await db.LeaderboardEntries.ToListAsync());
            Assert.Equal("InProgress", (await db.GameRooms.SingleAsync()).State);
            Assert.Equal(RoomState.InProgress,
                RoomSnapshotSerializer.Deserialize((await db.GameSnapshots.SingleAsync()).StateJson)!.State);
        }

        snapshot.Result.Players[1].Name = "Accepted";
        await store.SaveAsync(snapshot);
        Assert.Equal(2, (await new EfRoomLeaderboard(database.ScopeFactory).GetTopAsync()).Count);
    }

    [PostgreSqlFact]
    public async Task DatabaseUniqueKeyRejectsDuplicateGamePlayerEvenOutsideRoomStore()
    {
        await using var database = await PostgreSqlLeaderboardDatabase.CreateAsync();
        var snapshot = RoomLeaderboardPersistenceTests.FinishedSnapshot();
        await new EfRoomStore(database.ScopeFactory).SaveAsync(snapshot);
        await using var scope = database.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SrazDbContext>();
        db.LeaderboardEntries.Add(new LeaderboardEntryEntity
        {
            GameId = snapshot.GameId,
            PlayerId = snapshot.Result!.Players[0].PlayerId,
            PlayedAtUtc = snapshot.LastActivityUtc.UtcDateTime
        });
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
    }
}

internal sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SRAZ_TEST_POSTGRES")))
        {
            Skip = "Set SRAZ_TEST_POSTGRES to run PostgreSQL transaction and migration tests.";
        }
    }
}

internal sealed class PostgreSqlLeaderboardDatabase : IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly string _schema = "leaderboard_" + Guid.NewGuid().ToString("N");
    public ServiceProvider Provider { get; private set; } = null!;
    public IServiceScopeFactory ScopeFactory => Provider.GetRequiredService<IServiceScopeFactory>();

    private PostgreSqlLeaderboardDatabase(string connectionString) => _connectionString = connectionString;

    public static async Task<PostgreSqlLeaderboardDatabase> CreateAsync()
    {
        var database = new PostgreSqlLeaderboardDatabase(Environment.GetEnvironmentVariable("SRAZ_TEST_POSTGRES")!);
        await using var connection = new NpgsqlConnection(database._connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE SCHEMA \"{database._schema}\"", connection);
        await command.ExecuteNonQueryAsync();
        database.Provider = database.NewProvider();
        try
        {
            await using var scope = database.Provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SrazDbContext>().Database.MigrateAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public ServiceProvider NewProvider()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_connectionString) { SearchPath = _schema }.ConnectionString;
        return new ServiceCollection()
            .AddDbContext<SrazDbContext>(options => options.UseNpgsql(
                connectionString, postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", _schema)))
            .BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        await Provider.DisposeAsync();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA \"{_schema}\" CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }
}
