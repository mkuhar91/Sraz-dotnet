using Microsoft.Extensions.Localization;
using Sraz.Application.Rooms;
using Sraz.Domain.Models;

namespace Sraz.UI.Blazor.Localization;

public sealed class Texts(IStringLocalizer<Texts> localizer)
{
    public string this[string key] => localizer[key];
    public string AppName => this[nameof(AppName)];
    public string Tagline => this[nameof(Tagline)];
    public string Nickname => this[nameof(Nickname)];
    public string NicknamePlaceholder => this[nameof(NicknamePlaceholder)];
    public string CreateRoom => this[nameof(CreateRoom)];
    public string JoinRoomHeading => this[nameof(JoinRoomHeading)];
    public string JoinCode => this[nameof(JoinCode)];
    public string JoinCodePlaceholder => this[nameof(JoinCodePlaceholder)];
    public string Join => this[nameof(Join)];
    public string NicknameInvalid => this[nameof(NicknameInvalid)];
    public string CodeRequired => this[nameof(CodeRequired)];
    public string Dismiss => this[nameof(Dismiss)];
    public string Lobby => this[nameof(Lobby)];
    public string RoomCode => this[nameof(RoomCode)];
    public string Copy => this[nameof(Copy)];
    public string Copied => this[nameof(Copied)];
    public string Players => this[nameof(Players)];
    public string Host => this[nameof(Host)];
    public string Npc => this[nameof(Npc)];
    public string You => this[nameof(You)];
    public string BoardSize => this[nameof(BoardSize)];
    public string AddNpc => this[nameof(AddNpc)];
    public string RemoveNpc => this[nameof(RemoveNpc)];
    public string StartGame => this[nameof(StartGame)];
    public string LeaveRoom => this[nameof(LeaveRoom)];
    public string WaitingForHost => this[nameof(WaitingForHost)];
    public string StartRule => this[nameof(StartRule)];
    public string YourTurn => this[nameof(YourTurn)];
    public string Legend => this[nameof(Legend)];
    public string PawnsLeft => this[nameof(PawnsLeft)];
    public string OnTurn => this[nameof(OnTurn)];
    public string Notices => this[nameof(Notices)];
    public string Board => this[nameof(Board)];
    public string Question => this[nameof(Question)];
    public string Duel => this[nameof(Duel)];
    public string TimeUp => this[nameof(TimeUp)];
    public string Seconds => this[nameof(Seconds)];
    public string Correct => this[nameof(Correct)];
    public string Wrong => this[nameof(Wrong)];
    public string DuelOutcome => this[nameof(DuelOutcome)];
    public string DuelAborted => this[nameof(DuelAborted)];
    public string DuelWon => this[nameof(DuelWon)];
    public string GameOver => this[nameof(GameOver)];
    public string NewGame => this[nameof(NewGame)];
    public string Winner => this[nameof(Winner)];
    public string NoWinner => this[nameof(NoWinner)];
    public string OnTurnPlayer(string name) => localizer[nameof(OnTurnPlayer), name];
    public string PlayerAnswering(string name) => localizer[nameof(PlayerAnswering), name];
    public string QuestionOf(int number, int total) => localizer[nameof(QuestionOf), number, total];
    public string RoomBoard(int size) => localizer[nameof(RoomBoard), size];
    public string WinnerIs(string name) => localizer[nameof(WinnerIs), name];
    public string PawnMoved(string name) => localizer[nameof(PawnMoved), name];
    public string PawnCapturedOf(string name) => localizer[nameof(PawnCapturedOf), name];
    public string PawnStayed(string name) => localizer[nameof(PawnStayed), name];
    public string PlayerJoined(string name) => localizer[nameof(PlayerJoined), name];
    public string PlayerDisconnected(string name) => localizer[nameof(PlayerDisconnected), name];
    public string NpcTakeover(string name) => localizer[nameof(NpcTakeover), name];
    public string PlayerReturned(string name) => localizer[nameof(PlayerReturned), name];

    public string Notice(string? key) => key is "notfound" or "finished" or "notmember" or "left"
        ? this[$"Notice_{key}"] : string.Empty;
    public string Theme(QuestionTheme theme) => this[$"Theme_{theme}"];
    public string Category(string category) =>
        Enum.TryParse<QuestionTheme>(category, out var theme) ? Theme(theme) : category;
    public string Outcome(MoveOutcome? outcome) => outcome is MoveOutcome.Moved or MoveOutcome.Captured or MoveOutcome.Stayed
        ? this[$"Outcome_{outcome}"] : string.Empty;
    public string Error(RoomError error)
    {
        var value = localizer[$"Error_{error}"];
        return value.ResourceNotFound ? this["Error_Unknown"] : value.Value;
    }
}
