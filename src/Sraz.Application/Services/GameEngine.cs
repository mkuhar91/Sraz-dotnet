using Sraz.Application.Abstractions;

namespace Sraz.Application.Services;

public sealed class GameEngine : IGameEngine
{
    public void StartNewSession(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            throw new ArgumentException("Player name is required.", nameof(playerName));
        }

        // TODO: implement round lifecycle, question selection, timer integration, and scoring.
    }
}
