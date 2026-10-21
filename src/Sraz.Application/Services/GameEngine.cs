using Sraz.Application.Abstractions;
using Sraz.Domain.Models;

namespace Sraz.Application.Services;

public sealed class GameEngine : IGameEngine
{
    private static readonly string[] PlayerColors = ["#C62828", "#1565C0", "#2E7D32", "#6A1B9A"];
    private static readonly int[] TwoPlayerCorners = [0, 3];
    private readonly IQuestionProvider? _questionProvider;
    private readonly IBoardFactory _boardFactory;
    private Random _random = new();
    private int _currentPlayerIndex;
    private bool _gameStarted;

    public GameEngine(IQuestionProvider? questionProvider = null, IBoardFactory? boardFactory = null)
    {
        _questionProvider = questionProvider;
        _boardFactory = boardFactory ?? new BoardFactory();
    }

    public GameSession? CurrentSession { get; private set; }
    public Board Board { get; private set; } = null!;
    public IReadOnlyList<Player> Players { get; private set; } = Array.Empty<Player>();
    public Player CurrentPlayer => _gameStarted ? Players[_currentPlayerIndex] : throw new InvalidOperationException("A board game has not started.");
    public Player? Winner { get; private set; }
    public GameState State { get; private set; } = GameState.InProgress;

    public void StartNewSession(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            throw new ArgumentException("Player name is required.", nameof(playerName));
        }

        CurrentSession = new GameSession
        {
            PlayerName = playerName,
            TotalQuestions = 0,
            CorrectAnswers = 0,
            WrongAnswers = 0,
            FinalScore = 0
        };
    }

    public async Task<GameSession> StartNewSessionAsync(
        string playerName,
        int questionCount = 10,
        string? category = null,
        string? difficulty = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            throw new ArgumentException("Player name is required.", nameof(playerName));
        }

        if (_questionProvider is null)
        {
            throw new InvalidOperationException("A question provider is required to start a round.");
        }

        var roundService = new QuizRoundService(_questionProvider);
        var round = await roundService.CreateRoundAsync(questionCount, category, difficulty, cancellationToken: cancellationToken);
        var session = new GameSession
        {
            PlayerName = playerName,
            TotalQuestions = round.Questions.Count,
            CorrectAnswers = round.CorrectAnswers,
            WrongAnswers = round.WrongAnswers,
            FinalScore = round.Score,
            EndedAt = DateTimeOffset.UtcNow
        };

        CurrentSession = session;
        return session;
    }

    public void StartGame(IReadOnlyList<string> playerNames, int boardSize = Board.DefaultSize, int? seed = null)
    {
        ArgumentNullException.ThrowIfNull(playerNames);
        if (playerNames.Count is < 1 or > 4 || playerNames.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Enter between one and four player names.", nameof(playerNames));
        }

        _random = seed.HasValue ? new Random(seed.Value) : new Random();
        Board = _boardFactory.Create(boardSize, seed);
        var names = playerNames.Select(name => name.Trim()).ToList();
        var npcAdded = names.Count == 1;
        if (npcAdded)
        {
            names.Add("NPC");
        }

        var cornerIndices = names.Count == 2
            ? TwoPlayerCorners
            : Enumerable.Range(0, names.Count).ToArray();
        var players = new List<Player>(names.Count);

        for (var index = 0; index < names.Count; index++)
        {
            var player = new Player
            {
                Name = names[index],
                Index = index,
                Color = PlayerColors[index],
                IsNpc = npcAdded && index == 1
            };
            foreach (var (row, column) in Board.GetStartingCells(cornerIndices[index]))
            {
                var pawn = new Pawn { OwnerIndex = index };
                player.Pawns.Add(pawn);
                Board.PlacePawn(pawn, row, column);
            }

            players.Add(player);
        }

        Players = players;
        Winner = null;
        State = GameState.InProgress;
        _currentPlayerIndex = 0;
        _gameStarted = true;
        BeginTurn();
    }

    public void BeginTurn()
    {
        EnsureGameInProgress();
        foreach (var pawn in CurrentPlayer.ActivePawns)
        {
            pawn.ResetTurnAction();
        }
    }

    public void EndTurn()
    {
        EnsureGameInProgress();
        if (CurrentPlayer.ActivePawns.Any(pawn => !pawn.HasActedThisTurn))
        {
            throw new InvalidOperationException("The current player still has unused pawn actions.");
        }

        NextTurn();
    }

    public void NextTurn()
    {
        EnsureGameInProgress();
        for (var offset = 1; offset <= Players.Count; offset++)
        {
            var next = (_currentPlayerIndex + offset) % Players.Count;
            if (Players[next].IsEliminated)
            {
                continue;
            }

            _currentPlayerIndex = next;
            BeginTurn();
            return;
        }
    }

    public IReadOnlyList<BoardCell> GetAvailableMoves(Pawn pawn)
    {
        if (!_gameStarted || State != GameState.InProgress || pawn.OwnerIndex != CurrentPlayer.Index ||
            pawn.IsCaptured || pawn.HasActedThisTurn)
        {
            return Array.Empty<BoardCell>();
        }

        return Enum.GetValues<Direction>()
            .Select(direction => Board.GetNeighbor(pawn.Row, pawn.Column, direction))
            .Where(cell => cell is not null && cell.Occupant?.OwnerIndex != pawn.OwnerIndex)
            .Select(cell => cell!)
            .ToList();
    }

    public async Task<MoveRequest> RequestMove(Pawn pawn, Direction direction, CancellationToken cancellationToken = default)
    {
        EnsureCanAct(pawn);
        var target = Board.GetNeighbor(pawn.Row, pawn.Column, direction);
        if (target is null || target.Occupant?.OwnerIndex == pawn.OwnerIndex)
        {
            return new MoveRequest { Target = target ?? Board.GetCell(pawn.Row, pawn.Column), Questions = Array.Empty<Question>(), IsBlocked = true };
        }

        if (_questionProvider is null)
        {
            throw new InvalidOperationException("A question provider is required for board moves.");
        }

        var allQuestions = await _questionProvider.GetQuestionsAsync(cancellationToken);
        var questionsForTheme = allQuestions.Where(question =>
            string.Equals(question.Category, CategoryFor(target.Theme), StringComparison.OrdinalIgnoreCase)).ToList();
        if (target.Occupant is not null)
        {
            var duelQuestions = Shuffle(questionsForTheme.Where(question => question.Type == QuestionType.MultipleChoice)).Take(3).ToList();
            if (duelQuestions.Count != 3)
            {
                throw new InvalidOperationException($"Not enough duel questions are available for {target.Theme}.");
            }

            return new MoveRequest { Target = target, Questions = duelQuestions, IsDuel = true };
        }

        var question = Shuffle(questionsForTheme).FirstOrDefault()
            ?? throw new InvalidOperationException($"No questions are available for {target.Theme}.");
        return new MoveRequest { Target = target, Questions = new[] { question } };
    }

    public MoveResolution ResolveMove(Pawn pawn, Direction direction, bool isCorrect)
    {
        EnsureCanAct(pawn);
        var target = Board.GetNeighbor(pawn.Row, pawn.Column, direction);
        if (target is null || target.Occupant?.OwnerIndex == pawn.OwnerIndex || target.Occupant is not null)
        {
            return new MoveResolution { Outcome = MoveOutcome.Blocked };
        }

        if (isCorrect)
        {
            MovePawn(pawn, target);
        }

        pawn.MarkActedThisTurn();
        return CompleteAction(isCorrect ? MoveOutcome.Moved : MoveOutcome.Stayed);
    }

    public MoveResolution ResolveDuel(Pawn pawn, Direction direction, IReadOnlyList<bool> answersCorrect)
    {
        EnsureCanAct(pawn);
        ArgumentNullException.ThrowIfNull(answersCorrect);
        var target = Board.GetNeighbor(pawn.Row, pawn.Column, direction);
        if (target is null || target.Occupant is null || target.Occupant.OwnerIndex == pawn.OwnerIndex)
        {
            return new MoveResolution { Outcome = MoveOutcome.Blocked };
        }

        var firstIncorrect = -1;
        for (var index = 0; index < answersCorrect.Count; index++)
        {
            if (!answersCorrect[index])
            {
                firstIncorrect = index;
                break;
            }
        }

        if (answersCorrect.Count > 3 ||
            (firstIncorrect < 0 && answersCorrect.Count != 3) ||
            (firstIncorrect >= 0 && firstIncorrect != answersCorrect.Count - 1))
        {
            throw new ArgumentException("A duel requires three correct answers or may stop at the first incorrect answer.", nameof(answersCorrect));
        }

        var opponentPawn = target.Occupant;
        pawn.MarkActedThisTurn();
        if (!answersCorrect.All(correct => correct))
        {
            return CompleteAction(MoveOutcome.Stayed);
        }

        Board.CapturePawn(opponentPawn);
        MovePawn(pawn, target);
        var eliminatedPlayer = Players[opponentPawn.OwnerIndex].IsEliminated ? Players[opponentPawn.OwnerIndex] : null;
        if (Players.Count(player => !player.IsEliminated) == 1)
        {
            Winner = Players.Single(player => !player.IsEliminated);
            State = GameState.Finished;
            return new MoveResolution { Outcome = MoveOutcome.Captured, TurnCompleted = true, EliminatedPlayer = eliminatedPlayer, Winner = Winner };
        }

        return CompleteAction(MoveOutcome.Captured, eliminatedPlayer);
    }

    private MoveResolution CompleteAction(MoveOutcome outcome, Player? eliminatedPlayer = null)
    {
        var remainingPawns = CurrentPlayer.ActivePawns.Where(pawn => !pawn.HasActedThisTurn).ToList();
        if (remainingPawns.All(pawn => GetAvailableMoves(pawn).Count == 0))
        {
            foreach (var pawn in remainingPawns)
            {
                pawn.MarkActedThisTurn();
            }
        }

        var wasTurnCompleted = CurrentPlayer.ActivePawns.All(pawn => pawn.HasActedThisTurn);
        if (wasTurnCompleted)
        {
            NextTurn();
        }

        return new MoveResolution
        {
            Outcome = outcome,
            TurnCompleted = wasTurnCompleted,
            EliminatedPlayer = eliminatedPlayer,
            Winner = Winner
        };
    }

    private void MovePawn(Pawn pawn, BoardCell target)
    {
        Board.MovePawn(pawn, target);
    }

    private IEnumerable<Question> Shuffle(IEnumerable<Question> questions)
    {
        return questions.OrderBy(_ => _random.Next());
    }

    private static string CategoryFor(QuestionTheme theme) => theme switch
    {
        QuestionTheme.Geography => "Geography",
        QuestionTheme.History => "History",
        QuestionTheme.Math => "Math",
        QuestionTheme.Science => "Science",
        QuestionTheme.Sports => "Sports",
        QuestionTheme.Art => "Art",
        QuestionTheme.Literature => "Literature",
        QuestionTheme.Entertainment => "Entertainment",
        _ => throw new ArgumentOutOfRangeException(nameof(theme))
    };

    private void EnsureCanAct(Pawn pawn)
    {
        EnsureGameInProgress();
        ArgumentNullException.ThrowIfNull(pawn);
        if (pawn.IsCaptured || pawn.OwnerIndex != CurrentPlayer.Index || pawn.HasActedThisTurn ||
            !CurrentPlayer.Pawns.Contains(pawn))
        {
            throw new InvalidOperationException("This pawn cannot act now.");
        }
    }

    private void EnsureGameInProgress()
    {
        if (!_gameStarted || State == GameState.Finished)
        {
            throw new InvalidOperationException("The game is not in progress.");
        }
    }
}
