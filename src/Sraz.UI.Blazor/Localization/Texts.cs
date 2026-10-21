using Sraz.Application.Rooms;
using Sraz.Domain.Models;

namespace Sraz.UI.Blazor.Localization;

/// <summary>Single source of UI strings (Croatian for now; prepared for localization in phase 4).</summary>
public static class Texts
{
    public const string AppName = "Sraz";
    public const string Tagline = "Kviz s pločom za 2 do 4 igrača";

    public const string Nickname = "Nadimak";
    public const string NicknamePlaceholder = "Upiši svoj nadimak";
    public const string CreateRoom = "Stvori sobu";
    public const string JoinRoomHeading = "Pridruži se sobi";
    public const string JoinCode = "Pridruži se kodu";
    public const string JoinCodePlaceholder = "Kod sobe";
    public const string Join = "Pridruži se";
    public const string NicknameInvalid = "Upiši nadimak (1 do 24 znaka).";
    public const string CodeRequired = "Upiši kod sobe.";
    public const string Dismiss = "Zatvori";

    public const string Lobby = "Čekaonica";
    public const string RoomCode = "Kod sobe";
    public const string Copy = "Kopiraj";
    public const string Copied = "Kopirano";
    public const string Players = "Igrači";
    public const string Host = "domaćin";
    public const string Npc = "NPC";
    public const string You = "ti";
    public const string BoardSize = "Veličina ploče";
    public const string AddNpc = "Dodaj NPC";
    public const string RemoveNpc = "Ukloni";
    public const string StartGame = "Pokreni igru";
    public const string LeaveRoom = "Izađi iz sobe";
    public const string WaitingForHost = "Čekamo da domaćin pokrene igru.";
    public const string StartRule = "Za početak treba najmanje 2 igrača (najviše 4, ljudi i NPC zajedno).";

    public const string YourTurn = "Tvoj je red. Odaberi pijuna.";
    public const string Legend = "Legenda tema";
    public const string PawnsLeft = "pijuna";
    public const string OnTurn = "na redu";
    public const string Notices = "Događaji";
    public const string Board = "Ploča";
    public const string AnswersQuestion = "odgovara na pitanje";

    public const string Question = "Pitanje";
    public const string Duel = "Dvoboj";
    public const string TimeUp = "Vrijeme je isteklo.";
    public const string Seconds = "s";
    public const string Correct = "Točan odgovor";
    public const string Wrong = "Netočan odgovor";
    public const string DuelOutcome = "Ishod dvoboja";
    public const string DuelAborted = "Dvoboj je prekinut.";
    public const string DuelWon = "Dvoboj je dobiven.";

    public const string GameOver = "Igra je završena";
    public const string NewGame = "Nova igra";
    public const string Winner = "Pobjednik";
    public const string NoWinner = "Nema pobjednika.";


    public static string OnTurnPlayer(string name) => $"Na redu je {name}";
    public static string PlayerAnswering(string name) => $"Igrač {name} odgovara na pitanje";
    public static string QuestionOf(int number, int total) => $"{number}/{total}";
    public static string RoomBoard(int size) => $"{size} × {size}";
    public static string WinnerIs(string name) => $"Pobjednik je {name}!";
    public static string PawnMoved(string name) => $"{name}: pijun je pomaknut";
    public static string PawnCapturedOf(string name) => $"Pijun igrača {name} je uhvaćen";
    public static string PawnStayed(string name) => $"{name}: netočan odgovor, pijun ostaje";
    public static string PlayerJoined(string name) => $"{name} je ušao u sobu";

    public static string Notice(string? key) => key switch
    {
        "notfound" => "Soba ne postoji ili je istekla.",
        "finished" => "Igra je završena.",
        "notmember" => "Nisi član te sobe. Pridruži se kodom.",
        "left" => "Napustio si sobu.",
        _ => string.Empty
    };

    public static string Theme(QuestionTheme theme) => theme switch
    {
        QuestionTheme.Geography => "Geografija",
        QuestionTheme.History => "Povijest",
        QuestionTheme.Math => "Matematika",
        QuestionTheme.Science => "Znanost",
        QuestionTheme.Sports => "Sport",
        QuestionTheme.Art => "Umjetnost",
        QuestionTheme.Literature => "Književnost",
        QuestionTheme.Entertainment => "Zabava",
        _ => theme.ToString()
    };

    public static string Outcome(MoveOutcome? outcome) => outcome switch
    {
        MoveOutcome.Moved => "Pijun je pomaknut.",
        MoveOutcome.Captured => "Pijun je uhvaćen.",
        MoveOutcome.Stayed => "Pijun ostaje na mjestu.",
        _ => string.Empty
    };

    public static string Error(RoomError error) => error switch
    {
        RoomError.RoomNotFound => "Soba s tim kodom ne postoji.",
        RoomError.RoomFull => "Soba je puna (najviše 4 igrača).",
        RoomError.NotInLobby => "Igra u toj sobi je već počela.",
        RoomError.NotHost => "Samo domaćin to može napraviti.",
        RoomError.NotInProgress => "Igra nije u tijeku.",
        RoomError.NotEnoughPlayers => "Za početak treba najmanje 2 igrača.",
        RoomError.PlayerNotInRoom => "Nisi član ove sobe.",
        RoomError.NotYourTurn => "Nisi na redu.",
        RoomError.PawnNotOwned => "To nije tvoj pijun.",
        RoomError.InvalidMove => "Taj potez nije dopušten.",
        RoomError.InvalidName => "Nadimak mora imati 1 do 24 znaka.",
        RoomError.InvalidBoardSize => "Veličina ploče nije dopuštena.",
        RoomError.InvalidAnswer => "Neispravan odgovor.",
        RoomError.NoPendingQuestion => "Nema otvorenog pitanja.",
        RoomError.QuestionPending => "Pitanje je već otvoreno.",
        RoomError.QuestionsUnavailable => "Pitanja trenutno nisu dostupna.",
        RoomError.TooManyRooms => "Trenutno je previše aktivnih soba. Pokušaj kasnije.",
        RoomError.PersistenceFailed => "Spremanje nije uspjelo. Pokušaj ponovno.",
        _ => "Dogodila se greška."
    };
}
