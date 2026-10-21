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
    public int? BoardSeed { get; private set; }
    public int CurrentPlayerIndex => _currentPlayerIndex;
    public bool IsStarted => _gameStarted;

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

        var players = playerNames.Select(name => (Name: name.Trim(), IsNpc: false)).ToList();
        if (players.Count == 1)
        {
            players.Add(("NPC", true));
        }

        StartGame(players, boardSize, seed);
    }

    public void StartGame(IReadOnlyList<(string Name, bool IsNpc)> players, int boardSize = Board.DefaultSize, int? seed = null)
    {
        ArgumentNullException.ThrowIfNull(players);
        if (players.Count is < 1 or > 4 || players.Any(player => string.IsNullOrWhiteSpace(player.Name)))
        {
            throw new ArgumentException("Enter between one and four player names.", nameof(players));
        }

        var effectiveSeed = seed ?? Random.Shared.Next();
        BoardSeed = effectiveSeed;
        _random = new Random(effectiveSeed);
        Board = _boardFactory.Create(boardSize, effectiveSeed);
        var cornerIndices = players.Count == 2
            ? TwoPlayerCorners
            : Enumerable.Range(0, players.Count).ToArray();
        var created = new List<Player>(players.Count);

        for (var index = 0; index < players.Count; index++)
        {
            var player = CreatePlayer(players[index], index);
            foreach (var (row, column) in Board.GetStartingCells(cornerIndices[index]))
            {
                var pawn = new Pawn { OwnerIndex = index };
                player.Pawns.Add(pawn);
                Board.PlacePawn(pawn, row, column);
            }

            created.Add(player);
        }

        Players = created;
        Winner = null;
        State = GameState.InProgress;
        _currentPlayerIndex = 0;
        _gameStarted = true;
        BeginTurn();
    }

    /// <summary>Rebuilds a game from persisted state. The board is regenerated from <paramref name="seed"/>.</summary>
    public void RestoreGame(
        IReadOnlyList<(string Name, bool IsNpc)> players,
        int boardSize,
        int seed,
        IReadOnlyList<PawnState> pawns,
        int currentPlayerIndex,
        GameState state,
        int? winnerIndex)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(pawns);
        if (players.Count is < 1 or > 4 || currentPlayerIndex < 0 || currentPlayerIndex >= players.Count ||
            (winnerIndex.HasValue && (winnerIndex < 0 || winnerIndex >= players.Count)))
        {
            throw new ArgumentException("The saved game state is invalid.", nameof(players));
        }

        var board = _boardFactory.Create(boardSize, seed);
        var restored = players.Select((player, index) => CreatePlayer(player, index)).ToList();
        foreach (var saved in pawns)
        {
            if (saved.OwnerIndex < 0 || saved.OwnerIndex >= restored.Count)
            {
                throw new ArgumentException("The saved game state is invalid.", nameof(pawns));
            }

            var pawn = new Pawn { Id = saved.Id, OwnerIndex = saved.OwnerIndex };
            board.RestorePawn(pawn, saved.Row, saved.Column, saved.IsCaptured);
            if (saved.HasActedThisTurn)
            {
                pawn.MarkActedThisTurn();
            }

            restored[saved.OwnerIndex].Pawns.Add(pawn);
        }

        BoardSeed = seed;
        _random = new Random(seed);
        Board = board;
        Players = restored;
        State = state;
        Winner = winnerIndex.HasValue ? restored[winnerIndex.Value] : null;
        _currentPlayerIndex = currentPlayerIndex;
        _gameStarted = true;
    }

    private static Player CreatePlayer((string Name, bool IsNpc) setup, int index) => new()
    {
        Name = setup.Name,
        Index = index,
        Color = PlayerColors[index],
        IsNpc = setup.IsNpc
    };

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
