using Microsoft.Extensions.Time.Testing;
using Sraz.Application.Abstractions;
using Sraz.Application.Rooms;
using Sraz.Application.Services;
using Sraz.Domain.Models;
using Xunit;

namespace Sraz.Tests;

internal sealed class TestQuestionProvider : IQuestionProvider
{
    public const int CorrectIndex = 1;

    private static readonly string[] Categories =
        ["Geography", "History", "Math", "Science", "Sports", "Art", "Literature", "Entertainment"];

    public Task<IReadOnlyList<Question>> GetQuestionsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Question> questions = Categories
            .SelectMany(category => Enumerable.Range(0, 5).Select(index => new Question
            {
                Category = category,
                Text = $"{category} question {index}",
                Options = ["A", "B", "C", "D"],
                CorrectOptionIndex = CorrectIndex,
                Explanation = "Because B."
            }))
            .ToList();
        return Task.FromResult(questions);
    }
}

internal sealed class InMemoryRoomStore : IRoomStore
{
    private readonly Dictionary<string, RoomSnapshot> _rooms = new();
    private readonly object _lock = new();

    public bool FailWrites { get; set; }
    public int SaveCount { get; private set; }

    public Task SaveAsync(RoomSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (FailWrites)
        {
            throw new InvalidOperationException("Simulated database failure.");
        }

        lock (_lock)
        {
            SaveCount++;
            _rooms[snapshot.Code] = RoomSnapshotSerializer.Deserialize(RoomSnapshotSerializer.Serialize(snapshot))!;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RoomSnapshot>> LoadActiveAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<RoomSnapshot>>(
                _rooms.Values.Where(room => room.State != RoomState.Finished).ToList());
        }
    }

    public Task DeleteAsync(string code, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _rooms.Remove(code);
        }

        return Task.CompletedTask;
    }

    public RoomSnapshot? Get(string code)
    {
        lock (_lock)
        {
            return _rooms.GetValueOrDefault(code);
        }
    }

    public string? GetJson(string code) => Get(code) is { } snapshot ? RoomSnapshotSerializer.Serialize(snapshot) : null;
}

internal sealed class RoomTestContext
{
    public RoomTestContext(InMemoryRoomStore? store = null, FakeTimeProvider? time = null, RoomManagerOptions? options = null)
    {
        Store = store ?? new InMemoryRoomStore();
        Time = time ?? new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        Options = options ?? new RoomManagerOptions();
        Manager = new RoomManager(Store, new TestQuestionProvider(), new RandomNpcStrategy(1.0, 1), Time, Options);
    }

    public InMemoryRoomStore Store { get; }
    public FakeTimeProvider Time { get; }
    public RoomManagerOptions Options { get; }
    public RoomManager Manager { get; }

    public RoomManager NewManagerOnSameStore() =>
        new(Store, new TestQuestionProvider(), new RandomNpcStrategy(1.0, 1), Time, Options);

    /// <summary>Creates a started two-player room (host P0 moves first).</summary>
    public async Task<(GameRoom Room, Guid P0, Guid P1)> StartTwoPlayerGameAsync(int boardSize = 6)
    {
        var p0 = Guid.NewGuid();
        var p1 = Guid.NewGuid();
        var room = (await Manager.CreateRoomAsync(p0, "Ana", boardSize, seed: 42)).Value!;
        Assert.True((await Manager.JoinAsync(room.Code, p1, "Ben")).Success);
        Assert.True((await Manager.StartGameAsync(room.Code, p0)).Success);
        return (room, p0, p1);
    }

    /// <summary>Stores a room whose P0 pawn at (0,1) faces a P1 pawn at (0,2), then recovers it in a new manager.</summary>
    public async Task<(GameRoom Room, RoomManager Manager, Guid P0, Guid P1, Guid AttackerId, Guid DefenderId)> ArrangeDuelAsync(
        bool lastDefender = false)
    {
        var (room, p0, p1) = await StartTwoPlayerGameAsync();
        var snapshot = await room.GetSnapshotAsync();
        var attacker = snapshot.Game!.Pawns.First(pawn => pawn.OwnerIndex == 0 && pawn.Row == 0 && pawn.Column == 1);
        var defenders = snapshot.Game.Pawns.Where(pawn => pawn.OwnerIndex == 1).ToList();
        defenders[0].Row = 0;
        defenders[0].Column = 2;
        if (lastDefender)
        {
            defenders[1].IsCaptured = true;
            defenders[2].IsCaptured = true;
        }

        room.Close();
        await Store.SaveAsync(snapshot);
        var manager = NewManagerOnSameStore();
        await manager.LoadAsync();
        return (manager.GetRoom(room.Code)!, manager, p0, p1, attacker.Id, defenders[0].Id);
    }

    public static (Guid PawnId, Direction Direction) FindMove(RoomSnapshot snapshot, int ownerIndex)
    {
        var game = snapshot.Game!;
        var occupied = game.Pawns.Where(pawn => !pawn.IsCaptured).Select(pawn => (pawn.Row, pawn.Column)).ToHashSet();
        foreach (var pawn in game.Pawns.Where(pawn => pawn.OwnerIndex == ownerIndex && !pawn.IsCaptured && !pawn.HasActedThisTurn))
        {
            foreach (var direction in Enum.GetValues<Direction>())
            {
                var (row, column) = direction switch
                {
                    Direction.Up => (pawn.Row - 1, pawn.Column),
                    Direction.Down => (pawn.Row + 1, pawn.Column),
                    Direction.Left => (pawn.Row, pawn.Column - 1),
                    _ => (pawn.Row, pawn.Column + 1)
                };
                if (row >= 0 && column >= 0 && row < snapshot.BoardSize && column < snapshot.BoardSize && !occupied.Contains((row, column)))
                {
                    return (pawn.Id, direction);
                }
            }
        }

        throw new InvalidOperationException("No free move found.");
    }

    public static async Task<(Guid PawnId, QuestionDto Question)> BeginAnyMoveAsync(GameRoom room, Guid playerId, int ownerIndex)
    {
        var (pawnId, direction) = FindMove(await room.GetSnapshotAsync(), ownerIndex);
        var result = await room.BeginMoveAsync(playerId, pawnId, direction);
        Assert.True(result.Success, result.Error.ToString());
        return (pawnId, result.Value!);
    }
}
