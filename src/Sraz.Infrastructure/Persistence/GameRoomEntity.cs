namespace Sraz.Infrastructure.Persistence;

public sealed class GameRoomEntity
{
    public string Code { get; set; } = string.Empty;
    public string State { get; set; } = "Lobby";
    public DateTime CreatedAtUtc { get; set; }
}
