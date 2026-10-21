namespace Sraz.Infrastructure.Persistence;

public sealed class GameSnapshotEntity
{
    public long Id { get; set; }
    public string RoomCode { get; set; } = string.Empty;
    public string StateJson { get; set; } = "{}";
    public int SchemaVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
