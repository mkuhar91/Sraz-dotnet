using Sraz.Application.Services;
using Sraz.Domain.Models;
using Sraz.UI.Wpf.Localization;

namespace Sraz.UI.Wpf.ViewModels;

public sealed class BoardViewModel : BindableBase
{
    private string _statusMessage = string.Empty;

    public BoardViewModel(GameEngine engine)
    {
        Engine = engine;
        Refresh();
    }

    public GameEngine Engine { get; }
    public IReadOnlyList<BoardCell> AvailableMoves { get; private set; } = Array.Empty<BoardCell>();
    public Pawn? SelectedPawn { get; private set; }
    public string CurrentPlayerText => Localizer.Instance.Format("BoardCurrentPlayer", Engine.CurrentPlayer.Name);
    public string RemainingActionsText => Localizer.Instance.Format(
        "BoardRemainingActions", Engine.CurrentPlayer.ActivePawns.Count(pawn => !pawn.HasActedThisTurn),
        Engine.CurrentPlayer.ActivePawns.Count);
    public string WinnerText => Engine.Winner is null
        ? string.Empty
        : Localizer.Instance.Format(Engine.Winner.IsNpc ? "BoardNpcWinner" : "BoardWinner", Engine.Winner.Name);

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsGameFinished => Engine.State == GameState.Finished;

    public void SelectPawn(Pawn? pawn)
    {
        SelectedPawn = pawn;
        AvailableMoves = pawn is null ? Array.Empty<BoardCell>() : Engine.GetAvailableMoves(pawn);
        RaisePropertyChanged(nameof(SelectedPawn));
        RaisePropertyChanged(nameof(AvailableMoves));
    }

    public void Refresh()
    {
        RaisePropertyChanged(nameof(CurrentPlayerText));
        RaisePropertyChanged(nameof(RemainingActionsText));
        RaisePropertyChanged(nameof(IsGameFinished));
        RaisePropertyChanged(nameof(WinnerText));
    }
}
