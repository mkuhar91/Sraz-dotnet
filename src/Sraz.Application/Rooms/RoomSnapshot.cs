using System.Text.Json;
using System.Text.Json.Serialization;
using Sraz.Domain.Models;

namespace Sraz.Application.Rooms;

public sealed class RoomSnapshot
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Code { get; set; } = string.Empty;
    public RoomState State { get; set; }
    public Guid HostPlayerId { get; set; }
    public int BoardSize { get; set; }
    public int Seed { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset LastActivityUtc { get; set; }
    public List<PlayerSnapshot> Players { get; set; } = new();
    public GameSnapshot? Game { get; set; }
}

public sealed class PlayerSnapshot
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsNpc { get; set; }
}

public sealed class GameSnapshot
{
    public int CurrentPlayerIndex { get; set; }
    public GameState State { get; set; }
    public int? WinnerIndex { get; set; }
    public List<PawnSnapshot> Pawns { get; set; } = new();
    public PendingSnapshot? Pending { get; set; }
}

public sealed class PawnSnapshot
{
    public Guid Id { get; set; }
    public int OwnerIndex { get; set; }
    public int Row { get; set; }
    public int Column { get; set; }
    public bool IsCaptured { get; set; }
    public bool HasActedThisTurn { get; set; }
}

public sealed class PendingSnapshot
{
    public Guid PawnId { get; set; }
    public Direction Direction { get; set; }
    public bool IsDuel { get; set; }
    public List<bool> Answers { get; set; } = new();
    public DateTimeOffset DeadlineUtc { get; set; }
}

public static class RoomSnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(RoomSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);

    public static RoomSnapshot? Deserialize(string json) => JsonSerializer.Deserialize<RoomSnapshot>(json, Options);
}
