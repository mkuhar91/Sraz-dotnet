namespace Sraz.Application.Rooms;

public enum RoomError
{
    None,
    RoomNotFound,
    RoomFull,
    TooManyRooms,
    NotHost,
    NotInLobby,
    NotInProgress,
    NotEnoughPlayers,
    PlayerNotInRoom,
    NotYourTurn,
    PawnNotOwned,
    InvalidMove,
    InvalidName,
    InvalidBoardSize,
    InvalidAnswer,
    NoPendingQuestion,
    QuestionPending,
    QuestionsUnavailable,
    PersistenceFailed
}

public sealed record RoomResult(RoomError Error)
{
    public bool Success => Error == RoomError.None;
    public static RoomResult Ok { get; } = new(RoomError.None);
    public static RoomResult Fail(RoomError error) => new(error);
}

public sealed record RoomResult<T>(RoomError Error, T? Value)
{
    public bool Success => Error == RoomError.None;
    public static RoomResult<T> Ok(T value) => new(RoomError.None, value);
    public static RoomResult<T> Fail(RoomError error) => new(error, default);
}
