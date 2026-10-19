using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Sraz.Application.Services;
using Sraz.Domain.Models;
using Sraz.UI.Wpf.Localization;
using Sraz.UI.Wpf.ViewModels;

namespace Sraz.UI.Wpf;

public partial class BoardWindow : Window
{
    private static readonly Geometry PawnGeometry = Geometry.Parse(
        "M 20,2 C 15,2 12,6 12,10 C 12,14 15,17 20,17 C 25,17 28,14 28,10 C 28,6 25,2 20,2 Z M 13,20 C 9,22 8,25 8,29 L 6,32 L 34,32 L 32,29 C 32,25 31,22 27,20 C 25,24 15,24 13,20 Z");
    private readonly Func<GameEngine> _createGame;
    private readonly RandomNpcStrategy _npcStrategy = new();
    private GameEngine _engine;
    private BoardViewModel _viewModel;

    public BoardWindow(GameEngine engine, Func<GameEngine> createGame)
    {
        InitializeComponent();
        _engine = engine;
        _createGame = createGame;
        _viewModel = new BoardViewModel(engine);
        Title = Localizer.Instance.Get("BoardTitle");
        LegendTitle.Text = Localizer.Instance.Get("BoardLegend");
        NewGameButton.Content = Localizer.Instance.Get("BoardNewGame");
        ReturnButton.Content = Localizer.Instance.Get("BoardReturn");
        RenderLegend();
        RenderBoard();
        UpdateDisplay();
        Loaded += async (_, _) => await RunNpcTurnsAsync();
    }

    private void RenderLegend()
    {
        ThemeLegend.Items.Clear();
        foreach (var (theme, brush) in ThemeColors.All)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new Border { Width = 18, Height = 18, Background = brush, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) });
            row.Children.Add(new TextBlock { Text = ThemeColors.Label(theme), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            ThemeLegend.Items.Add(row);
        }
    }

    private void RenderBoard()
    {
        BoardGrid.Rows = _engine.Board.Size;
        BoardGrid.Columns = _engine.Board.Size;
        BoardGrid.Children.Clear();
        for (var row = 0; row < _engine.Board.Size; row++)
        {
            for (var column = 0; column < _engine.Board.Size; column++)
            {
                var cell = _engine.Board.GetCell(row, column);
                var button = new Button
                {
                    Tag = cell,
                    Margin = new Thickness(1),
                    Padding = new Thickness(1),
                    MinWidth = 38,
                    MinHeight = 38,
                    ToolTip = ThemeColors.Label(cell.Theme)
                };
                button.Click += Cell_Click;
                BoardGrid.Children.Add(button);
            }
        }

        UpdateCells();
    }

    private void UpdateCells()
    {
        foreach (var element in BoardGrid.Children)
        {
            if (element is not Button button || button.Tag is not BoardCell cell)
            {
                continue;
            }

            button.Background = ThemeColors.Color(cell.Theme);
            button.BorderThickness = new Thickness(2);
            button.BorderBrush = Brushes.Transparent;
            button.IsEnabled = !_viewModel.IsGameFinished && !_engine.CurrentPlayer.IsNpc;
            if (_viewModel.SelectedPawn is { } selected && selected.Row == cell.Row && selected.Column == cell.Column)
            {
                button.BorderBrush = Brushes.Gold;
            }
            else if (_viewModel.AvailableMoves.Contains(cell))
            {
                button.BorderBrush = cell.Occupant is null ? Brushes.LimeGreen : Brushes.Crimson;
            }

            if (cell.Occupant is { } pawn)
            {
                var owner = _engine.Players[pawn.OwnerIndex];
                button.Content = new Path
                {
                    Data = PawnGeometry,
                    Fill = new BrushConverter().ConvertFromString(owner.Color) as Brush ?? Brushes.Black,
                    Stretch = Stretch.Uniform,
                    Width = 32,
                    Height = 32,
                    Opacity = pawn.HasActedThisTurn ? 0.45 : 1
                };
                button.ToolTip = $"{ThemeColors.Label(cell.Theme)} — {owner.Name}";
            }
            else
            {
                button.Content = null;
            }
        }
    }

    private void UpdateDisplay()
    {
        _viewModel.Refresh();
        CurrentPlayerText.Text = _viewModel.CurrentPlayerText;
        RemainingActionsText.Text = _viewModel.RemainingActionsText;
        StatusText.Text = _viewModel.StatusMessage;
        StatusBar.Text = _viewModel.StatusMessage;
        PlayerList.Text = string.Join(Environment.NewLine, _engine.Players.Select(player =>
            Localizer.Instance.Format(player.IsNpc ? "BoardNpcPlayer" : "BoardPlayer", player.Name, player.ActivePawns.Count)));
        if (_viewModel.IsGameFinished)
        {
            WinnerText.Text = _viewModel.WinnerText;
            GameOverPanel.Visibility = Visibility.Visible;
            StatusBar.Text = Localizer.Instance.Get("BoardGameFinished");
        }
        else
        {
            GameOverPanel.Visibility = Visibility.Collapsed;
        }

        UpdateCells();
    }

    private async void Cell_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BoardCell cell } || _engine.CurrentPlayer.IsNpc ||
            _engine.State == GameState.Finished)
        {
            return;
        }

        if (cell.Occupant is { } pawn && pawn.OwnerIndex == _engine.CurrentPlayer.Index)
        {
            _viewModel.SelectPawn(pawn);
            _viewModel.StatusMessage = string.Empty;
            UpdateDisplay();
            return;
        }

        if (_viewModel.SelectedPawn is not { } selected || !_viewModel.AvailableMoves.Contains(cell))
        {
            return;
        }

        var direction = DirectionTo(selected, cell);
        try
        {
            var request = await _engine.RequestMove(selected, direction);
            if (request.IsBlocked)
            {
                _viewModel.StatusMessage = Localizer.Instance.Get("BoardBlocked");
                UpdateDisplay();
                return;
            }

            MoveResolution? result = null;
            if (request.IsDuel)
            {
                var duel = new DuelWindow(request.Questions) { Owner = this };
                duel.ShowDialog();
                if (duel.Results.Count > 0)
                {
                    result = _engine.ResolveDuel(selected, direction, duel.Results);
                }
            }
            else
            {
                var question = new MoveQuestionWindow(request.Questions[0]) { Owner = this };
                question.ShowDialog();
                result = _engine.ResolveMove(selected, direction, question.IsAnswerCorrect);
            }

            if (result is not null)
            {
                SetResultMessage(result);
            }

            _viewModel.SelectPawn(null);
            UpdateDisplay();
            await RunNpcTurnsAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, Localizer.Instance.Format("MsgError", exception.Message), "Sraz", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetResultMessage(MoveResolution result)
    {
        if (result.Outcome == MoveOutcome.Captured)
        {
            _viewModel.StatusMessage = Localizer.Instance.Format("BoardPawnCaptured", result.EliminatedPlayer?.Name ?? string.Empty);
        }
        else
        {
            _viewModel.StatusMessage = Localizer.Instance.Get(
                result.Outcome == MoveOutcome.Moved ? "BoardMoveCorrect" : "BoardMoveWrong");
        }

        if (result.EliminatedPlayer is not null)
        {
            _viewModel.StatusMessage += " " + Localizer.Instance.Format("BoardPlayerEliminated", result.EliminatedPlayer.Name);
        }
    }

    private async Task RunNpcTurnsAsync()
    {
        while (_engine.State == GameState.InProgress && _engine.CurrentPlayer.IsNpc)
        {
            await Task.Delay(450);
            var choice = _npcStrategy.ChooseMove(_engine);
            if (choice is null)
            {
                _viewModel.StatusMessage = Localizer.Instance.Get("BoardNpcNoMoves");
                UpdateDisplay();
                return;
            }

            var request = await _engine.RequestMove(choice.Value.Pawn, choice.Value.Direction);
            MoveResolution result;
            if (request.IsDuel)
            {
                var answers = new List<bool>();
                foreach (var _ in request.Questions)
                {
                    var correct = _npcStrategy.AnswerQuestionCorrectly();
                    answers.Add(correct);
                    if (!correct) break;
                }

                result = _engine.ResolveDuel(choice.Value.Pawn, choice.Value.Direction, answers);
            }
            else
            {
                result = _engine.ResolveMove(choice.Value.Pawn, choice.Value.Direction, _npcStrategy.AnswerQuestionCorrectly());
            }

            SetResultMessage(result);
            _viewModel.SelectPawn(null);
            UpdateDisplay();
        }
    }

    private static Direction DirectionTo(Pawn pawn, BoardCell cell)
    {
        if (cell.Row < pawn.Row) return Direction.Up;
        if (cell.Row > pawn.Row) return Direction.Down;
        return cell.Column < pawn.Column ? Direction.Left : Direction.Right;
    }

    private async void NewGame_Click(object sender, RoutedEventArgs e)
    {
        _engine = _createGame();
        _viewModel = new BoardViewModel(_engine);
        RenderBoard();
        UpdateDisplay();
        await RunNpcTurnsAsync();
    }

    private void Return_Click(object sender, RoutedEventArgs e) => Close();
}
