using System.Collections.Concurrent;
using System.Security.Cryptography;
using Sraz.Application.Abstractions;

namespace Sraz.Application.Rooms;

public sealed class RoomManager : IRoomManager
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 5;

    private readonly ConcurrentDictionary<string, GameRoom> _rooms = new();
    private readonly RoomServices _services;
    private readonly RoomManagerOptions _options;

    public RoomManager(
        IRoomStore store,
        IQuestionProvider questions,
        INpcStrategy npcStrategy,
        TimeProvider? timeProvider = null,
        RoomManagerOptions? options = null)
    {
        _options = options ?? new RoomManagerOptions();
        _services = new RoomServices(store, questions, npcStrategy, timeProvider ?? TimeProvider.System, _options.QuestionTimeLimit, _options.InactivityTimeout);
    }

    public int RoomCount => _rooms.Count;

    public IReadOnlyList<GameRoom> GetRooms() => _rooms.Values.ToList();

    public GameRoom? GetRoom(string code) =>
        string.IsNullOrWhiteSpace(code) ? null : _rooms.GetValueOrDefault(code.Trim().ToUpperInvariant());

    public async Task<RoomResult<GameRoom>> CreateRoomAsync(
        Guid hostId, string hostName, int boardSize = 10, int? seed = null, CancellationToken cancellationToken = default)
    {
        if (GameRoom.NormalizeName(hostName) is null)
        {
            return RoomResult<GameRoom>.Fail(RoomError.InvalidName);
        }

        if (boardSize is <= 5 or > GameRoom.MaxBoardSize)
        {
            return RoomResult<GameRoom>.Fail(RoomError.InvalidBoardSize);
        }

        if (_rooms.Count >= _options.MaxRooms)
        {
            return RoomResult<GameRoom>.Fail(RoomError.TooManyRooms);
        }

        GameRoom room;
        do
        {
            room = new GameRoom(GenerateCode(), hostId, hostName, boardSize, seed ?? Random.Shared.Next(), _services);
        }
        while (!_rooms.TryAdd(room.Code, room));

        if (!await room.PersistInitialAsync(cancellationToken))
        {
            _rooms.TryRemove(room.Code, out _);
            room.Close();
            return RoomResult<GameRoom>.Fail(RoomError.PersistenceFailed);
        }

        return RoomResult<GameRoom>.Ok(room);
    }

    public async Task<RoomResult<GameRoom>> JoinAsync(string code, Guid playerId, string name, CancellationToken cancellationToken = default)
    {
        var room = GetRoom(code);
        if (room is null)
        {
            return RoomResult<GameRoom>.Fail(RoomError.RoomNotFound);
        }

        var result = await room.JoinAsync(playerId, name, cancellationToken);
        return result.Success ? RoomResult<GameRoom>.Ok(room) : RoomResult<GameRoom>.Fail(result.Error);
    }

    public async Task<RoomResult> LeaveAsync(string code, Guid playerId, CancellationToken cancellationToken = default)
    {
        var room = GetRoom(code);
        if (room is null)
        {
            return RoomResult.Fail(RoomError.RoomNotFound);
        }

        var result = await room.LeaveAsync(playerId, cancellationToken);
        if (result.Success && room.State == RoomState.Finished)
        {
            await RemoveRoomAsync(room, cancellationToken);
        }

        return result;
    }

    public Task<RoomResult> AddNpcAsync(string code, Guid requesterId, CancellationToken cancellationToken = default) =>
        WithRoomAsync(code, room => room.AddNpcAsync(requesterId, cancellationToken));

    public Task<RoomResult> RemoveNpcAsync(string code, Guid requesterId, Guid npcId, CancellationToken cancellationToken = default) =>
        WithRoomAsync(code, room => room.RemoveNpcAsync(requesterId, npcId, cancellationToken));

    public Task<RoomResult> StartGameAsync(string code, Guid requesterId, CancellationToken cancellationToken = default) =>
        WithRoomAsync(code, room => room.StartGameAsync(requesterId, cancellationToken));

    /// <summary>Loads Lobby and InProgress rooms from the store. Unreadable snapshots are skipped.</summary>
    public async Task<int> LoadAsync(CancellationToken cancellationToken = default, bool disconnectPlayers = false)
    {
        var loaded = 0;
        foreach (var snapshot in await _services.Store.LoadActiveAsync(cancellationToken))
        {
            try
            {
                var room = await GameRoom.RestoreAsync(snapshot, _services, cancellationToken, disconnectPlayers);
                if (_rooms.TryAdd(room.Code, room))
                {
                    loaded++;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
            }
        }

        return loaded;
    }

    public async Task<int> RemoveInactiveAsync(CancellationToken cancellationToken = default)
    {
        var threshold = _services.Time.GetUtcNow() - _options.InactivityTimeout;
        var removed = 0;
        foreach (var room in _rooms.Values.Where(room => room.LastActivityUtc < threshold).ToList())
        {
            if (await RemoveRoomAsync(room, cancellationToken))
            {
                removed++;
            }
        }

        return removed;
    }

    private async Task<RoomResult> WithRoomAsync(string code, Func<GameRoom, Task<RoomResult>> action)
    {
        var room = GetRoom(code);
        return room is null ? RoomResult.Fail(RoomError.RoomNotFound) : await action(room);
    }

    private async Task<bool> RemoveRoomAsync(GameRoom room, CancellationToken cancellationToken)
    {
        if (!_rooms.TryRemove(room.Code, out _))
        {
            return false;
        }

        room.Close();
        try
        {
            await _services.Store.DeleteAsync(room.Code, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }

        return true;
    }

    private static string GenerateCode()
    {
        Span<char> chars = stackalloc char[CodeLength];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        }

        return new string(chars);
    }
}
