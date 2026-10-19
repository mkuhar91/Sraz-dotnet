using System.ComponentModel;
using System.Globalization;

namespace Sraz.UI.Wpf.Localization;

public sealed class Localizer : INotifyPropertyChanged
{
    public const string English = "en";
    public const string Croatian = "hr";

    private static readonly Dictionary<string, string> En = new()
    {
        ["AppSubtitle"] = "Sraz-inspired .NET quiz game",
        ["PlayerName"] = "Player name",
        ["Category"] = "Category",
        ["Difficulty"] = "Difficulty",
        ["Mode"] = "Game mode",
        ["Language"] = "Language",
        ["StartGame"] = "Start game",
        ["Leaderboard"] = "Leaderboard",
        ["Exit"] = "Exit",
        ["Close"] = "Close",

        ["Cat_All"] = "All",
        ["Cat_General"] = "General",
        ["Cat_Science"] = "Science",
        ["Cat_History"] = "History",
        ["Cat_Math"] = "Math",
        ["Cat_Geography"] = "Geography",
        ["Cat_Literature"] = "Literature",
        ["Cat_Technology"] = "Technology",
        ["Cat_Art"] = "Art",
        ["Cat_Sports"] = "Sports",
        ["Cat_Music"] = "Music",

        ["Diff_All"] = "All",
        ["Diff_Easy"] = "Easy",
        ["Diff_Medium"] = "Medium",
        ["Diff_Hard"] = "Hard",

        ["Mode_All"] = "All",
        ["Mode_Classic"] = "Classic",
        ["Mode_Survival"] = "Survival",
        ["Mode_TimeAttack"] = "Time attack",
        ["Mode_Daily"] = "Daily challenge",
        ["ModeHint_Classic"] = "10 questions, 20 seconds each.",
        ["ModeHint_Survival"] = "Keep going until you make 3 mistakes.",
        ["ModeHint_TimeAttack"] = "Answer as many as you can in 60 seconds.",
        ["ModeHint_Daily"] = "The same 10 questions for everyone today. Filters are ignored.",

        ["Period"] = "Period",
        ["Period_AllTime"] = "All time",
        ["Period_Today"] = "Today",
        ["Period_Week"] = "Last 7 days",
        ["Period_Month"] = "Last 30 days",

        ["MsgNameRequired"] = "Please enter a player name before starting.",
        ["MsgNoQuestions"] = "No questions found for the selected filters. Please adjust them.",
        ["MsgError"] = "Something went wrong: {0}",
        ["MsgLeaderboardError"] = "Could not open the leaderboard: {0}",

        ["WindowQuestion"] = "Sraz - Question",
        ["WindowSummary"] = "Sraz - Round Summary",
        ["WindowLeaderboard"] = "Sraz - Leaderboard",
        ["TimeLeft"] = "Time: {0}s",
        ["QuestionHeader"] = "Category: {0} | Difficulty: {1}",
        ["RoundComplete"] = "Round complete",
        ["Correct"] = "Correct!",
        ["Wrong"] = "Wrong!",
        ["TimesUp"] = "Time's up!",
        ["CorrectAnswerIs"] = "Correct answer: {0}",
        ["Submit"] = "Submit",
        ["TypeAnswer"] = "Type your answer",
        ["Lifeline5050"] = "50:50",
        ["LifelineTime"] = "+10 s",
        ["LifelineSkip"] = "Skip",
        ["FooterFixed"] = "Question {0}/{1} | Score: {2} | Streak: {3} (x{4}) | Correct: {5} | Wrong: {6} | Timeouts: {7}",
        ["FooterOpen"] = "Question {0} | Score: {1} | Streak: {2} (x{3}) | Correct: {4} | Wrong: {5} | Timeouts: {6}",
        ["FooterLives"] = " | Lives: {0}",
        ["FooterFinal"] = "Score: {0}",

        ["RoundSummary"] = "Round Summary",
        ["FinalScore"] = "Final Score: {0}",
        ["QuestionsPlayed"] = "Questions played: {0}",
        ["CorrectAnswers"] = "Correct answers: {0}",
        ["WrongAnswers"] = "Wrong answers: {0}",
        ["Timeouts"] = "Timeouts: {0}",
        ["BestStreak"] = "Best streak: {0}",
        ["ModeLabel"] = "Mode: {0}",

        ["ColPlayer"] = "Player",
        ["ColScore"] = "Score",
        ["ColCategory"] = "Category",
        ["ColDifficulty"] = "Difficulty",
        ["ColMode"] = "Mode",
        ["ColCorrect"] = "Correct",
        ["ColWrong"] = "Wrong",
        ["ColTimeouts"] = "Timeouts",
        ["ColStreak"] = "Streak",
        ["ColDate"] = "Date"
    };

    private static readonly Dictionary<string, string> Hr = new()
    {
        ["AppSubtitle"] = "Kviz znanja inspiriran igrom Sraz",
        ["PlayerName"] = "Ime igrača",
        ["Category"] = "Kategorija",
        ["Difficulty"] = "Težina",
        ["Mode"] = "Način igre",
        ["Language"] = "Jezik",
        ["StartGame"] = "Započni igru",
        ["Leaderboard"] = "Ljestvica",
        ["Exit"] = "Izlaz",
        ["Close"] = "Zatvori",

        ["Cat_All"] = "Sve",
        ["Cat_General"] = "Opće znanje",
        ["Cat_Science"] = "Znanost",
        ["Cat_History"] = "Povijest",
        ["Cat_Math"] = "Matematika",
        ["Cat_Geography"] = "Geografija",
        ["Cat_Literature"] = "Književnost",
        ["Cat_Technology"] = "Tehnologija",
        ["Cat_Art"] = "Umjetnost",
        ["Cat_Sports"] = "Sport",
        ["Cat_Music"] = "Glazba",

        ["Diff_All"] = "Sve",
        ["Diff_Easy"] = "Lako",
        ["Diff_Medium"] = "Srednje",
        ["Diff_Hard"] = "Teško",

        ["Mode_All"] = "Svi",
        ["Mode_Classic"] = "Klasični",
        ["Mode_Survival"] = "Preživljavanje",
        ["Mode_TimeAttack"] = "Utrka s vremenom",
        ["Mode_Daily"] = "Dnevni izazov",
        ["ModeHint_Classic"] = "10 pitanja, 20 sekundi za svako.",
        ["ModeHint_Survival"] = "Igraj dok ne pogriješiš 3 puta.",
        ["ModeHint_TimeAttack"] = "Odgovori na što više pitanja u 60 sekundi.",
        ["ModeHint_Daily"] = "Ista 10 pitanja za sve igrače danas. Filteri se ignoriraju.",

        ["Period"] = "Razdoblje",
        ["Period_AllTime"] = "Sve vrijeme",
        ["Period_Today"] = "Danas",
        ["Period_Week"] = "Zadnjih 7 dana",
        ["Period_Month"] = "Zadnjih 30 dana",

        ["MsgNameRequired"] = "Unesi ime igrača prije početka.",
        ["MsgNoQuestions"] = "Nema pitanja za odabrane filtere. Promijeni ih.",
        ["MsgError"] = "Nešto je pošlo po zlu: {0}",
        ["MsgLeaderboardError"] = "Ljestvicu nije moguće otvoriti: {0}",

        ["WindowQuestion"] = "Sraz - Pitanje",
        ["WindowSummary"] = "Sraz - Sažetak runde",
        ["WindowLeaderboard"] = "Sraz - Ljestvica",
        ["TimeLeft"] = "Vrijeme: {0}s",
        ["QuestionHeader"] = "Kategorija: {0} | Težina: {1}",
        ["RoundComplete"] = "Runda završena",
        ["Correct"] = "Točno!",
        ["Wrong"] = "Netočno!",
        ["TimesUp"] = "Vrijeme je isteklo!",
        ["CorrectAnswerIs"] = "Točan odgovor: {0}",
        ["Submit"] = "Potvrdi",
        ["TypeAnswer"] = "Upiši odgovor",
        ["Lifeline5050"] = "50:50",
        ["LifelineTime"] = "+10 s",
        ["LifelineSkip"] = "Preskoči",
        ["FooterFixed"] = "Pitanje {0}/{1} | Bodovi: {2} | Niz: {3} (x{4}) | Točno: {5} | Netočno: {6} | Istek vremena: {7}",
        ["FooterOpen"] = "Pitanje {0} | Bodovi: {1} | Niz: {2} (x{3}) | Točno: {4} | Netočno: {5} | Istek vremena: {6}",
        ["FooterLives"] = " | Životi: {0}",
        ["FooterFinal"] = "Bodovi: {0}",

        ["RoundSummary"] = "Sažetak runde",
        ["FinalScore"] = "Konačni rezultat: {0}",
        ["QuestionsPlayed"] = "Odigrano pitanja: {0}",
        ["CorrectAnswers"] = "Točnih odgovora: {0}",
        ["WrongAnswers"] = "Netočnih odgovora: {0}",
        ["Timeouts"] = "Isteklo vrijeme: {0}",
        ["BestStreak"] = "Najbolji niz: {0}",
        ["ModeLabel"] = "Način: {0}",

        ["ColPlayer"] = "Igrač",
        ["ColScore"] = "Bodovi",
        ["ColCategory"] = "Kategorija",
        ["ColDifficulty"] = "Težina",
        ["ColMode"] = "Način",
        ["ColCorrect"] = "Točno",
        ["ColWrong"] = "Netočno",
        ["ColTimeouts"] = "Istek",
        ["ColStreak"] = "Niz",
        ["ColDate"] = "Datum"
    };

    public static Localizer Instance { get; } = new();

    public static IReadOnlyList<string> SupportedLanguages { get; } = new[] { English, Croatian };

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    public string Language { get; private set; } = English;

    public string this[string key] => Get(key);

    public string Get(string key)
    {
        var table = Language == Croatian ? Hr : En;
        if (table.TryGetValue(key, out var value) || En.TryGetValue(key, out value))
        {
            return value;
        }

        return key;
    }

    public string Format(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, Get(key), args);
    }

    public string CategoryLabel(string category) => Label("Cat_", category);

    public string DifficultyLabel(string difficulty) => Label("Diff_", difficulty);

    public string ModeLabel(string mode) => Label("Mode_", mode);

    public void SetLanguage(string language)
    {
        var normalized = SupportedLanguages.Contains(language) ? language : English;
        if (normalized == Language)
        {
            return;
        }

        Language = normalized;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private string Label(string prefix, string value)
    {
        var key = prefix + value;
        var table = Language == Croatian ? Hr : En;
        return table.TryGetValue(key, out var text) || En.TryGetValue(key, out text) ? text : value;
    }
}
