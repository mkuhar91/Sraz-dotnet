using Sraz.Domain.Models;

namespace Sraz.Application.Rooms;

public enum RoomEventType
{
    PlayerJoined,
    PlayerLeft,
    GameStarted,
    PawnMoved,
    PawnCaptured,
    TurnChanged,
    GameFinished,
    ActionResolved,
    SettingsChanged,
    QuestionStarted
}

/// <summary>Notification published by a room. Carries no question or answer data.</summary>
public sealed record RoomEvent(
    RoomEventType Type,
    string RoomCode,
    int? PlayerIndex = null,
    Guid? PlayerId = null,
    Guid? PawnId = null,
    int? Row = null,
    int? Column = null,
    MoveOutcome? Outcome = null);
