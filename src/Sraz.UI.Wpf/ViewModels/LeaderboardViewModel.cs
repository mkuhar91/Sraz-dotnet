using Sraz.Application.Services;
using Sraz.Domain.Models;

namespace Sraz.UI.Wpf.ViewModels;

public sealed class LeaderboardViewModel : BindableBase
{
    private readonly LeaderboardService _service;

    public LeaderboardViewModel(LeaderboardService service)
    {
        _service = service;
        Entries = new List<LeaderboardEntry>();
        Reload();
    }

    public List<LeaderboardEntry> Entries { get; private set; }

    public void Reload()
    {
        Entries = _service.GetTopEntries(10);
        RaisePropertyChanged(nameof(Entries));
    }

    public void SaveCurrentGame(string playerName, int score, int correctAnswers, int wrongAnswers, int timeouts, string category, string difficulty)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            playerName = "Player";
        }

        var entry = new LeaderboardEntry
        {
            PlayerName = playerName,
            Score = score,
            CorrectAnswers = correctAnswers,
            WrongAnswers = wrongAnswers,
            Timeouts = timeouts,
            Category = category,
            Difficulty = difficulty,
            PlayedAtUtc = DateTime.UtcNow
        };

        _service.SaveEntry(entry);
        Reload();
    }
}
