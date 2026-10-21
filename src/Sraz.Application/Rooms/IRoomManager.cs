namespace Sraz.Application.Rooms;

public interface IRoomManager
{
    int RoomCount { get; }
    GameRoom? GetRoom(string code);
    IReadOnlyList<GameRoom> GetRooms();
    Task<RoomResult<GameRoom>> CreateRoomAsync(Guid hostId, string hostName, int boardSize = 10, int? seed = null, CancellationToken cancellationToken = default);
    Task<RoomResult<GameRoom>> JoinAsync(string code, Guid playerId, string name, CancellationToken cancellationToken = default);
    Task<RoomResult> LeaveAsync(string code, Guid playerId, CancellationToken cancellationToken = default);
    Task<RoomResult> AddNpcAsync(string code, Guid requesterId, CancellationToken cancellationToken = default);
    Task<RoomResult> RemoveNpcAsync(string code, Guid requesterId, Guid npcId, CancellationToken cancellationToken = default);
    Task<RoomResult> StartGameAsync(string code, Guid requesterId, CancellationToken cancellationToken = default);
    Task<int> LoadAsync(CancellationToken cancellationToken = default, bool disconnectPlayers = false);
    Task<int> RemoveInactiveAsync(CancellationToken cancellationToken = default);
}
