namespace Sraz.Application.Rooms;

public interface IRoomStore
{
    /// <summary>Atomically stores the room row and its latest snapshot. Throws if the write fails.</summary>
    Task SaveAsync(RoomSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoomSnapshot>> LoadActiveAsync(CancellationToken cancellationToken = default);

    Task DeleteAsync(string code, CancellationToken cancellationToken = default);
}
