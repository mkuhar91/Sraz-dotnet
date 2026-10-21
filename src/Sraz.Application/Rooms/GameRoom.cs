using Sraz.Application.Services;
using Sraz.Domain.Models;

namespace Sraz.Application.Rooms;

/// <summary>
/// One game room with its own <see cref="GameEngine"/>. All state changes are serialized by a semaphore,
/// validated against the caller's <c>PlayerId</c>, and persisted as a snapshot before they are kept.
/// </summary>
public sealed class GameRoom : IDisposable
{
    public const int MaxPlayers = 4;
    public const int MaxBoardSize = 20;
    private const int MaxNpcStepsPerDrive = 64;
    private static readonly TimeSpan DisconnectGracePeriod = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly RoomServices _services;
    private readonly object _subscriptionLock = new();
    private readonly List<Subscription> _subscriptions = new();
    private GameEngine _engine;
    private List<RoomPlayer> _players = new();
    private Guid _hostId;
    private int _state;
    private int _boardSize;
    private int _seed;
    private DateTimeOffset _createdAt;
    private long _lastActivityTicks;
    private PendingAction? _pending;
    private Task _lastTimeoutTask = Task.CompletedTask;
    private ITimer? _maintenanceTimer;
    private Guid? _gameId;
    private RoomGameResultSnapshot? _result;
    private bool _closed;

    private sealed record RoomPlayer(Guid Id, string Name, bool IsNpc,
        DateTimeOffset? DisconnectedAtUtc = null, Guid? OriginalPlayerId = null);

    private sealed record PendingAction(
        Guid PawnId,
        Direction Direction,
        bool IsDuel,
        IReadOnlyList<Question> Questions,
        IReadOnlyList<bool> Answers,
        DateTimeOffset Deadline,
        CancellationTokenSource Timer);

    public GameRoom(string code, Guid hostId, string hostName, int boardSize, int seed, RoomServices services)
        : this(code, services)
    {
        _hostId = hostId;
        _players.Add(new RoomPlayer(hostId, NormalizeName(hostName) ?? throw new ArgumentException("Invalid name.", nameof(hostName)), false));
        _boardSize = boardSize;
        _seed = seed;
        _createdAt = services.Time.GetUtcNow();
        Touch();
    }

    private GameRoom(string code, RoomServices services)
    {
        Code = code;
        _services = services;
        _engine = NewEngine();
    }

    public string Code { get; }
    public RoomState State => (RoomState)Volatile.Read(ref _state);
    public DateTimeOffset LastActivityUtc => new(Interlocked.Read(ref _lastActivityTicks), TimeSpan.Zero);

    public static string? NormalizeName(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > 24 ? null : trimmed;
    }

    public IDisposable Subscribe(Action<RoomEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var subscription = new Subscription(this, handler);
        lock (_subscriptionLock)
        {
            _subscriptions.Add(subscription);
        }

        return subscription;
    }

    public Task<RoomSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        RunLockedAsync(_ => Task.FromResult(Capture()), cancellationToken);

    /// <summary>Read-only view for rendering. <paramref name="viewerId"/> only determines which moves are offered.</summary>
    public Task<BoardViewDto> GetViewAsync(Guid? viewerId = null, CancellationToken cancellationToken = default) =>
        RunLockedAsync(_ => Task.FromResult(BuildView(viewerId)), cancellationToken);

    /// <summary>Changes the board size while the room is still in the lobby. Host only.</summary>
    public Task<RoomResult> SetBoardSizeAsync(Guid requesterId, int boardSize, CancellationToken cancellationToken = default) =>
        RunLockedAsync(async events =>
        {
            if (CheckHostInLobby(requesterId) is { } error)
            {
                return RoomResult.Fail(error);
            }

            if (boardSize is <= 5 or > MaxBoardSize)
            {
                return RoomResult.Fail(RoomError.InvalidBoardSize);
            }

            var before = Capture();
            _boardSize = boardSize;
            Touch();
            if (!await TryPersistAsync(before, null, cancellationToken))
            {
                return RoomResult.Fail(RoomError.PersistenceFailed);
            }

            events.Add(new RoomEvent(RoomEventType.SettingsChanged, Code));
            return RoomResult.Ok;
        }, cancellationToken);

    /// <summary>The question currently awaiting an answer from the given player, if any.</summary>
    public Task<QuestionDto?> GetPendingQuestionAsync(Guid playerId, CancellationToken cancellationToken = default) =>
        RunLockedAsync(_ =>
        {
            var index = HumanIndex(playerId);
            return Task.FromResult(index >= 0 && _pending is not null && _engine.CurrentPlayerIndex == index
                ? ToDto(_pending)
                : null);
        }, cancellationToken);

    /// <summary>Completes once any timeout that has fired has been processed (intended for tests).</summary>
    public Task WaitForTimeoutHandlingAsync() => _lastTimeoutTask;

    public Task<RoomResult> SetConnectedAsync(Guid playerId, bool connected, CancellationToken cancellationToken = default) =>
        RunLockedAsync(async events =>
        {
            var index = MemberIndex(playerId);
            if (index < 0)
            {
                return RoomResult.Fail(RoomError.PlayerNotInRoom);
            }

            var player = _players[index];
            if (connected == (player.DisconnectedAtUtc is null))
            {
                return RoomResult.Ok;
            }

            var before = Capture();
            _players[index] = player with { DisconnectedAtUtc = connected ? null : _services.Time.GetUtcNow() };
            if (connected)
            {
                Touch();
            }

            if (!await TryPersistAsync(before, _pending, cancellationToken))
            {
                return RoomResult.Fail(RoomError.PersistenceFailed);
            }

            events.Add(new RoomEvent(connected
                ? player.IsNpc ? RoomEventType.SettingsChanged : RoomEventType.PlayerReturned
                : RoomEventType.PlayerDisconnected, Code, index, playerId));
            await DriveNpcAsync(events, cancellationToken);
            return RoomResult.Ok;
        }, cancellationToken);

    public Task<RoomResult> RestartAsync(Guid requesterId, CancellationToken cancellationToken = default) =>
        RunLockedAsync(async events =>
        {
            if (MemberIndex(requesterId) < 0)
            {
                return RoomResult.Fail(RoomError.PlayerNotInRoom);
            }

            if (requesterId != _hostId)
            {
                return RoomResult.Fail(RoomError.NotHost);
            }

            if (State != RoomState.Finished)
            {
                return RoomResult.Fail(RoomError.NotInLobby);
            }

            var before = Capture();
            _players = _players.Select(player => player.OriginalPlayerId is null ? player :
                player with { IsNpc = false, OriginalPlayerId = null }).ToList();
            _engine = NewEngine();
            _gameId = null;
            _result = null;
            Volatile.Write(ref _state, (int)RoomState.Lobby);
            Touch();
            if (!await TryPersistAsync(before, null, cancellationToken))
            {
                return RoomResult.Fail(RoomError.PersistenceFailed);
            }

            events.Add(new RoomEvent(RoomEventType.SettingsChanged, Code));
            return RoomResult.Ok;
        }, cancellationToken);

    public void Close()
    {
        _gate.Wait();
        try
        {
            _pending?.Timer.Dispose();
            _pending = null;
            _closed = true;
            _maintenanceTimer?.Dispose();
            _maintenanceTimer = null;
        }
        finally
        {
            _gate.Release();
        }

        lock (_subscriptionLock)
        {
            _subscriptions.Clear();
        }
    }

    public void Dispose() => Close();

    public async Task<bool> PersistInitialAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await _services.Store.SaveAsync(Capture(), cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public static async Task<GameRoom> RestoreAsync(RoomSnapshot snapshot, RoomServices services,
        CancellationToken cancellationToken = default, bool disconnectPlayers = false)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SchemaVersion is < 1 or > RoomSnapshot.CurrentSchemaVersion || string.IsNullOrWhiteSpace(snapshot.Code))
        {
            throw new InvalidOperationException("Unsupported room snapshot.");
        }

        var room = new GameRoom(snapshot.Code, services);
        room.Restore(snapshot);
        var events = new List<RoomEvent>();
        await room._gate.WaitAsync(cancellationToken);
        try
        {
            if (disconnectPlayers)
            {
                room._players = room._players.Select(player => !player.IsNpc || player.OriginalPlayerId is not null
                    ? player with { DisconnectedAtUtc = player.DisconnectedAtUtc ?? services.Time.GetUtcNow() }
                    : player).ToList();
                await services.Store.SaveAsync(room.Capture(), cancellationToken);
            }

            if (room.State == RoomState.InProgress)
            {
                if (snapshot.Game?.Pending is { } pending && room.FindPawn(pending.PawnId) is { } pawn)
                {
                    // A question that was open when the server stopped counts as incorrect.
                    var answers = pending.IsDuel ? pending.Answers.Append(false).ToList() : new List<bool> { false };
                    if (room._players[room._engine.CurrentPlayerIndex].DisconnectedAtUtc is not null)
                    {
                        room.TakeOver(room._engine.CurrentPlayerIndex);
                    }
                    var step = room.Resolve(pawn, pending.Direction, pending.IsDuel, answers);
                    try
                    {
                        await services.Store.SaveAsync(room.Capture(), cancellationToken);
                        events.AddRange(step);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                    }
                }

                await room.TakeOverExpiredAsync(events);
                await room.DriveNpcAsync(events, cancellationToken);
            }
            room.ScheduleMaintenance();
        }
        finally
        {
            room._gate.Release();
        }

        room.Publish(events);
        return room;
    }

    public Task<RoomResult> JoinAsync(Guid playerId, string name, CancellationToken cancellationToken = default) =>
        RunLockedAsync(async events =>
        {
            if (MemberIndex(playerId) >= 0)
            {
                return RoomResult.Ok;
            }

            if (State != RoomState.Lobby)
            {
                return RoomResult.Fail(RoomError.NotInLobby);
            }

            if (NormalizeName(name) is not { } playerName)
            {
                return RoomResult.Fail(RoomError.InvalidName);
            }

            if (_players.Count >= MaxPlayers)
            {
                return RoomResult.Fail(RoomError.RoomFull);
            }

            var before = Capture();
            _players.Add(new RoomPlayer(playerId, playerName, false));
            Touch();
            if (!await TryPersistAsync(before, null, cancellationToken))
            {
                return RoomResult.Fail(RoomError.PersistenceFailed);
            }

            events.Add(new RoomEvent(RoomEventType.PlayerJoined, Code, _players.Count - 1, playerId));
            return RoomResult.Ok;
        }, cancellationToken);

    public Task<RoomResult> LeaveAsync(Guid playerId, CancellationToken cancellationToken = default) =>
        RunLockedAsync(async events =>
        {
            var index = HumanIndex(playerId);
            if (index < 0)
            {
                return RoomResult.Fail(RoomError.PlayerNotInRoom);
            }

            if (State != RoomState.Lobby)
            {
                return RoomResult.Fail(RoomError.NotInLobby);
            }

            var before = Capture();
            _players.RemoveAt(index);
            var firstHuman = _players.FirstOrDefault(player => !player.IsNpc);
            if (firstHuman is null)
            {
                Volatile.Write(ref _state, (int)RoomState.Finished);
            }
            else if (_hostId == playerId)
            {
                _hostId = firstHuman.Id;
            }

            Touch();
            if (!await TryPersistAsync(before, null, cancellationToken))
            {
                return RoomResult.Fail(RoomError.PersistenceFailed);
            }

            events.Add(new RoomEvent(RoomEventType.PlayerLeft, Code, index, playerId));
            return RoomResult.Ok;
        }, cancellationToken);

    public Task<RoomResult> AddNpcAsync(Guid requesterId, CancellationToken cancellationToken = default) =>
        RunLockedAsync(async events =>
        {
            if (CheckHostInLobby(requesterId) is { } error)
            {
                return RoomResult.Fail(error);
            }

            if (_players.Count >= MaxPlayers)
            {
                return RoomResult.Fail(RoomError.RoomFull);
            }

            var number = 1;
            while (_players.Any(player => player.Name == $"NPC {number}"))
            {
                number++;
            }

            var before = Capture();
            var npc = new RoomPlayer(Guid.NewGuid(), $"NPC {number}", true);
            _players.Add(npc);
            Touch();
            if (!await TryPersistAsync(before, null, cancellationToken))
            {
                return RoomResult.Fail(RoomError.PersistenceFailed);
            }

            events.Add(new RoomEvent(RoomEventType.PlayerJoined, Code, _players.Count - 1, npc.Id));
            return RoomResult.Ok;
        }, cancellationToken);

    public Task<RoomResult> RemoveNpcAsync(Guid requesterId, Guid npcId, CancellationToken cancellationToken = default) =>
        RunLockedAsync(async events =>
        {
            if (CheckHostInLobby(requesterId) is { } error)
            {
                return RoomResult.Fail(error);
            }

            var index = _players.FindIndex(player => player.IsNpc && player.Id == npcId);
            if (index < 0)
            {
                return RoomResult.Fail(RoomError.PlayerNotInRoom);
            }

            var before = Capture();
            _players.RemoveAt(index);
            Touch();
            if (!await TryPersistAsync(before, null, cancellationToken))
            {
                return RoomResult.Fail(RoomError.PersistenceFailed);
            }

            events.Add(new RoomEvent(RoomEventType.PlayerLeft, Code, index, npcId));
            return RoomResult.Ok;
        }, cancellationToken);

    public Task<RoomResult> StartGameAsync(Guid requesterId, CancellationToken cancellationToken = default) =>
        RunLockedAsync(async events =>
        {
            if (CheckHostInLobby(requesterId) is { } error)
            {
                return RoomResult.Fail(error);
            }

            if (_players.Count < 2)
            {
                return RoomResult.Fail(RoomError.NotEnoughPlayers);
            }

            var before = Capture();
            _gameId = Guid.NewGuid();
            _result = null;
            _engine.StartGame(_players.Select(player => (player.Name, player.IsNpc)).ToList(), _boardSize, _seed);
            Volatile.Write(ref _state, (int)RoomState.InProgress);
            Touch();
            if (!await TryPersistAsync(before, null, cancellationToken))
            {
                return RoomResult.Fail(RoomError.PersistenceFailed);
            }

            events.Add(new RoomEvent(RoomEventType.GameStarted, Code, _engine.CurrentPlayerIndex));
            await DriveNpcAsync(events, cancellationToken);
            return RoomResult.Ok;
        }, cancellationToken);

    /// <summary>Starts a pawn move. The server picks the question(s) and starts the 30 s timer.</summary>
    public Task<RoomResult<QuestionDto>> BeginMoveAsync(
        Guid playerId, Guid pawnId, Direction direction, CancellationToken cancellationToken = default) =>
        RunLockedAsync(async events =>
        {
            if (State != RoomState.InProgress)
            {
                return RoomResult<QuestionDto>.Fail(RoomError.NotInProgress);
            }

            var index = HumanIndex(playerId);
            if (index < 0)
            {
                return RoomResult<QuestionDto>.Fail(RoomError.PlayerNotInRoom);
            }

            if (_engine.CurrentPlayerIndex != index)
            {
                return RoomResult<QuestionDto>.Fail(RoomError.NotYourTurn);
            }

            var pawn = _engine.Players[index].Pawns.FirstOrDefault(candidate => candidate.Id == pawnId);
            if (pawn is null)
            {
                return RoomResult<QuestionDto>.Fail(RoomError.PawnNotOwned);
            }

            if (_pending is not null)
            {
                return RoomResult<QuestionDto>.Fail(RoomError.QuestionPending);
            }

            var target = Enum.IsDefined(direction) ? _engine.Board.GetNeighbor(pawn.Row, pawn.Column, direction) : null;
            if (target is null || !_engine.GetAvailableMoves(pawn).Contains(target))
            {
                return RoomResult<QuestionDto>.Fail(RoomError.InvalidMove);
            }

            MoveRequest request;
            try
            {
                request = await _engine.RequestMove(pawn, direction, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                return RoomResult<QuestionDto>.Fail(RoomError.QuestionsUnavailable);
            }

            if (request.IsBlocked || request.Questions.Count == 0)
            {
                return RoomResult<QuestionDto>.Fail(RoomError.InvalidMove);
            }

            var before = Capture();
            _pending = NewPending(pawn.Id, direction, request.IsDuel, request.Questions.ToList(), Array.Empty<bool>());
            Touch();
            if (!await TryPersistAsync(before, null, cancellationToken))
            {
                return RoomResult<QuestionDto>.Fail(RoomError.PersistenceFailed);
            }

            events.Add(new RoomEvent(RoomEventType.QuestionStarted, Code, index));
            return RoomResult<QuestionDto>.Ok(ToDto(_pending));
        }, cancellationToken);

    /// <summary>Answers the open question. The client sends only the chosen option index.</summary>
    public Task<RoomResult<AnswerResultDto>> AnswerAsync(
        Guid playerId, int selectedIndex, CancellationToken cancellationToken = default) =>
        RunLockedAsync(async events =>
        {
            if (State != RoomState.InProgress)
            {
                return RoomResult<AnswerResultDto>.Fail(RoomError.NotInProgress);
            }

            var index = HumanIndex(playerId);
            if (index < 0)
            {
                return RoomResult<AnswerResultDto>.Fail(RoomError.PlayerNotInRoom);
            }

            if (_engine.CurrentPlayerIndex != index)
            {
                return RoomResult<AnswerResultDto>.Fail(RoomError.NotYourTurn);
            }

            if (_pending is not { } pending)
            {
                return RoomResult<AnswerResultDto>.Fail(RoomError.NoPendingQuestion);
            }

            var question = pending.Questions[pending.Answers.Count];
            if (selectedIndex < 0 || selectedIndex >= question.Options.Count)
            {
                return RoomResult<AnswerResultDto>.Fail(RoomError.InvalidAnswer);
            }

            var correct = selectedIndex == question.CorrectOptionIndex &&
                _services.Time.GetUtcNow() < pending.Deadline;
            return await ResolveAnswerAsync(pending, correct, events, cancellationToken,
                timedOut: _services.Time.GetUtcNow() >= pending.Deadline);
        }, cancellationToken);

    private async Task<RoomResult<AnswerResultDto>> ResolveAnswerAsync(
        PendingAction pending, bool correct, List<RoomEvent> events, CancellationToken cancellationToken, bool timedOut = false)
    {
        var pawn = FindPawn(pending.PawnId);
        if (pawn is null)
        {
            return RoomResult<AnswerResultDto>.Fail(RoomError.PawnNotOwned);
        }

        var before = Capture();
        var takeover = timedOut && _players[_engine.CurrentPlayerIndex].DisconnectedAtUtc is not null
            ? TakeOver(_engine.CurrentPlayerIndex) : null;
        var answers = pending.Answers.Append(correct).ToList();
        IReadOnlyList<RoomEvent> step = Array.Empty<RoomEvent>();
        MoveResolution? resolution = null;
        if (!pending.IsDuel)
        {
            _pending = null;
            step = Resolve(pawn, pending.Direction, false, answers, out resolution);
        }
        else if (!correct || answers.Count >= pending.Questions.Count)
        {
            _pending = null;
            step = Resolve(pawn, pending.Direction, true, answers, out resolution);
        }
        else
        {
            _pending = NewPending(pending.PawnId, pending.Direction, true, pending.Questions, answers);
        }

        if (!timedOut)
        {
            Touch();
        }
        if (!await TryPersistAsync(before, pending, cancellationToken))
        {
            return RoomResult<AnswerResultDto>.Fail(RoomError.PersistenceFailed);
        }

        events.AddRange(step);
        if (takeover is not null)
        {
            events.Add(takeover);
        }
        var dto = new AnswerResultDto(correct, _pending is null, resolution?.Outcome, _pending is null ? null : ToDto(_pending));
        await DriveNpcAsync(events, cancellationToken);
        return RoomResult<AnswerResultDto>.Ok(dto);
    }

    private async Task HandleTimeoutAsync(CancellationTokenSource timer)
    {
        var events = new List<RoomEvent>();
        await _gate.WaitAsync();
        try
        {
            if (_pending is not { } pending || !ReferenceEquals(pending.Timer, timer))
            {
                return;
            }

            var result = await ResolveAnswerAsync(pending, false, events, CancellationToken.None, timedOut: true);
            if (result.Error == RoomError.PersistenceFailed && ReferenceEquals(_pending?.Timer, timer))
            {
                _pending = NewPending(pending.PawnId, pending.Direction, pending.IsDuel, pending.Questions, pending.Answers);
                timer.Dispose();
            }
        }
        finally
        {
            ScheduleMaintenance();
            _gate.Release();
        }

        Publish(events);
    }

    private PendingAction NewPending(
        Guid pawnId, Direction direction, bool isDuel, IReadOnlyList<Question> questions, IReadOnlyList<bool> answers)
    {
        var timer = new CancellationTokenSource(_services.QuestionTimeLimit, _services.Time);
        timer.Token.Register(() => _lastTimeoutTask = HandleTimeoutAsync(timer));
        return new PendingAction(
            pawnId, direction, isDuel, questions, answers, _services.Time.GetUtcNow() + _services.QuestionTimeLimit, timer);
    }

    private QuestionDto ToDto(PendingAction pending)
    {
        var question = pending.Questions[pending.Answers.Count];
        return new QuestionDto(
            question.Id,
            question.Category,
            question.Text,
            question.ImagePath,
            question.Options.ToList(),
            pending.Answers.Count + 1,
            pending.Questions.Count,
            pending.IsDuel,
            pending.Deadline);
    }

    /// <summary>Lets NPC players act until a human is on turn. Each step is persisted and rolled back on failure.</summary>
    private async Task DriveNpcAsync(List<RoomEvent> events, CancellationToken cancellationToken)
    {
        for (var steps = 0; steps < MaxNpcStepsPerDrive; steps++)
        {
            if (!CanDriveNpc())
            {
                return;
            }

            var before = Capture();
            IReadOnlyList<RoomEvent> step;
            try
            {
                step = await PlayNpcStepAsync(cancellationToken);
            }
            catch (InvalidOperationException)
            {
                Restore(before);
                return;
            }

            if (step.Count == 0)
            {
                Restore(before);
                return;
            }

            if (!await TryPersistAsync(before, null, cancellationToken))
            {
                return;
            }

            events.AddRange(step);
        }
    }

    private async Task<IReadOnlyList<RoomEvent>> PlayNpcStepAsync(CancellationToken cancellationToken)
    {
        var from = _engine.CurrentPlayerIndex;
        var choice = _services.Npc.ChooseMove(_engine);
        if (choice is null)
        {
            foreach (var pawn in _engine.CurrentPlayer.ActivePawns)
            {
                pawn.MarkActedThisTurn();
            }

            _engine.NextTurn();
            var returned = ReturnAtTurnStart(from);
            if (returned is not null)
            {
                return new[] { returned, new RoomEvent(RoomEventType.TurnChanged, Code, _engine.CurrentPlayerIndex) };
            }
            return _engine.CurrentPlayerIndex != from
                ? new[] { new RoomEvent(RoomEventType.TurnChanged, Code, _engine.CurrentPlayerIndex) }
                : Array.Empty<RoomEvent>();
        }

        var (npcPawn, direction) = choice.Value;
        var request = await _engine.RequestMove(npcPawn, direction, cancellationToken);
        if (request.IsBlocked || request.Questions.Count == 0)
        {
            return Array.Empty<RoomEvent>();
        }
        var answers = new List<bool>();
        foreach (var _ in request.Questions)
        {
            answers.Add(_services.Npc.AnswerQuestionCorrectly());
            if (!answers[^1])
            {
                break;
            }
        }

        return Resolve(npcPawn, direction, request.IsDuel, answers);
    }

    private IReadOnlyList<RoomEvent> Resolve(Pawn pawn, Direction direction, bool isDuel, IReadOnlyList<bool> answers) =>
        Resolve(pawn, direction, isDuel, answers, out _);

    private IReadOnlyList<RoomEvent> Resolve(
        Pawn pawn, Direction direction, bool isDuel, IReadOnlyList<bool> answers, out MoveResolution resolution)
    {
        var target = _engine.Board.GetNeighbor(pawn.Row, pawn.Column, direction);
        var occupant = target?.Occupant;
        var turnBefore = _engine.CurrentPlayerIndex;
        resolution = isDuel
            ? _engine.ResolveDuel(pawn, direction, answers)
            : _engine.ResolveMove(pawn, direction, answers[0]);
        var finished = _engine.State == GameState.Finished;
        if (finished)
        {
            Volatile.Write(ref _state, (int)RoomState.Finished);
            _result = new RoomGameResultSnapshot
            {
                GameId = _gameId ??= Guid.NewGuid(),
                FinishedAtUtc = _services.Time.GetUtcNow(),
                Players = _players.Where(player => !player.IsNpc || player.OriginalPlayerId is not null).Select(player => new RoomPlayerResultSnapshot
                {
                    PlayerId = player.Id,
                    Name = player.Name,
                    Won = _engine.Winner is { IsNpc: false } winner && _players[winner.Index].Id == player.Id
                }).ToList()
            };
        }

        var events = new List<RoomEvent>
        {
            new(RoomEventType.ActionResolved, Code, pawn.OwnerIndex, null, pawn.Id, Outcome: resolution.Outcome)
        };
        if (resolution.Outcome == MoveOutcome.Captured && occupant is not null && target is not null)
        {
            events.Add(new RoomEvent(RoomEventType.PawnCaptured, Code, occupant.OwnerIndex, null, occupant.Id, target.Row, target.Column));
        }

        if (resolution.Outcome is MoveOutcome.Moved or MoveOutcome.Captured)
        {
            events.Add(new RoomEvent(RoomEventType.PawnMoved, Code, pawn.OwnerIndex, null, pawn.Id, pawn.Row, pawn.Column));
        }

        if (finished)
        {
            events.Add(new RoomEvent(RoomEventType.GameFinished, Code, _engine.Winner?.Index));
        }
        else if (_engine.CurrentPlayerIndex != turnBefore)
        {
            if (ReturnAtTurnStart(turnBefore) is { } returned)
            {
                events.Add(returned);
            }
            events.Add(new RoomEvent(RoomEventType.TurnChanged, Code, _engine.CurrentPlayerIndex));
        }

        return events;
    }

    private RoomError? CheckHostInLobby(Guid requesterId)
    {
        if (HumanIndex(requesterId) < 0)
        {
            return RoomError.PlayerNotInRoom;
        }

        if (requesterId != _hostId)
        {
            return RoomError.NotHost;
        }

        return State == RoomState.Lobby ? null : RoomError.NotInLobby;
    }

    private int HumanIndex(Guid playerId) => _players.FindIndex(player => !player.IsNpc && player.Id == playerId);

    private int MemberIndex(Guid playerId) => _players.FindIndex(player =>
        player.Id == playerId && (!player.IsNpc || player.OriginalPlayerId is not null));

    private Pawn? FindPawn(Guid pawnId) =>
        _engine.IsStarted ? _engine.Players.SelectMany(player => player.Pawns).FirstOrDefault(pawn => pawn.Id == pawnId) : null;

    private void Touch() => Interlocked.Exchange(ref _lastActivityTicks, _services.Time.GetUtcNow().UtcTicks);

    private GameEngine NewEngine() => new(_services.Questions);

    private bool CanDriveNpc() =>
        State == RoomState.InProgress && _pending is null && _engine.CurrentPlayer.IsNpc &&
        _services.Time.GetUtcNow() - LastActivityUtc < (_services.InactivityTimeout ?? TimeSpan.FromHours(2)) &&
        _players.Where((player, index) => !_engine.Players[index].IsEliminated)
            .Any(player => !player.IsNpc || player.OriginalPlayerId is not null);

    private RoomEvent? TakeOver(int index)
    {
        var player = _players[index];
        if (player.IsNpc)
        {
            return null;
        }

        _players[index] = player with { IsNpc = true, OriginalPlayerId = player.Id };
        _engine.Players[index].IsNpc = true;
        return new RoomEvent(RoomEventType.NpcTakeover, Code, index, player.Id);
    }

    private RoomEvent? ReturnAtTurnStart(int previousIndex)
    {
        var index = _engine.CurrentPlayerIndex;
        var player = _players[index];
        if (previousIndex == index || !player.IsNpc || player.OriginalPlayerId is null || player.DisconnectedAtUtc is not null)
        {
            return null;
        }

        _players[index] = player with { IsNpc = false, OriginalPlayerId = null };
        _engine.Players[index].IsNpc = false;
        return new RoomEvent(RoomEventType.PlayerReturned, Code, index, player.Id);
    }

    private void ScheduleMaintenance()
    {
        _maintenanceTimer?.Dispose();
        _maintenanceTimer = null;
        if (_closed || State != RoomState.InProgress)
        {
            return;
        }

        var now = _services.Time.GetUtcNow();
        var deadlines = _players.Where(player => !player.IsNpc && player.DisconnectedAtUtc is not null)
            .Select(player => player.DisconnectedAtUtc!.Value + DisconnectGracePeriod).ToList();
        if (CanDriveNpc())
        {
            deadlines.Add(now + TimeSpan.FromSeconds(1));
        }

        if (deadlines.Count == 0)
        {
            return;
        }

        var delay = deadlines.Min() - now;
        if (delay <= TimeSpan.Zero)
        {
            delay = TimeSpan.FromSeconds(1);
        }

        _maintenanceTimer = _services.Time.CreateTimer(
            _ => _lastTimeoutTask = HandleMaintenanceAsync(), null, delay, Timeout.InfiniteTimeSpan);
    }

    private async Task HandleMaintenanceAsync()
    {
        var events = new List<RoomEvent>();
        await _gate.WaitAsync();
        try
        {
            if (_closed || State != RoomState.InProgress)
            {
                return;
            }

            await TakeOverExpiredAsync(events);
            await DriveNpcAsync(events, CancellationToken.None);
        }
        finally
        {
            ScheduleMaintenance();
            _gate.Release();
        }

        Publish(events);
    }

    private async Task TakeOverExpiredAsync(List<RoomEvent> events)
    {
        for (var index = 0; index < _players.Count; index++)
        {
            var player = _players[index];
            if (player.IsNpc || player.DisconnectedAtUtc is not { } disconnected ||
                _services.Time.GetUtcNow() - disconnected < DisconnectGracePeriod)
            {
                continue;
            }

            var before = Capture();
            var pendingBefore = _pending;
            var step = new List<RoomEvent> { TakeOver(index)! };
            if (_engine.CurrentPlayerIndex == index && _pending is { } pending && FindPawn(pending.PawnId) is { } pawn)
            {
                _pending = null;
                step.AddRange(Resolve(pawn, pending.Direction, pending.IsDuel, pending.Answers.Append(false).ToList()));
            }

            if (!await TryPersistAsync(before, pendingBefore, CancellationToken.None))
            {
                return;
            }

            events.AddRange(step);
        }
    }

    private BoardViewDto BuildView(Guid? viewerId)
    {
        var viewerIndex = viewerId is { } id ? MemberIndex(id) : -1;
        var started = _engine.IsStarted;
        var board = started ? _engine.Board : new BoardFactory().Create(_boardSize, _seed);
        var themes = new List<QuestionTheme>(board.Size * board.Size);
        for (var row = 0; row < board.Size; row++)
        {
            for (var column = 0; column < board.Size; column++)
            {
                themes.Add(board.Cells[row, column].Theme);
            }
        }

        var players = new List<PlayerViewDto>(_players.Count);
        for (var index = 0; index < _players.Count; index++)
        {
            var player = _players[index];
            var engineColor = started ? _engine.Players[index].Color : string.Empty;
            var remaining = started ? _engine.Players[index].ActivePawns.Count : 0;
            players.Add(new PlayerViewDto(index, player.Id, player.Name, engineColor, player.IsNpc, player.Id == _hostId, remaining,
                player.DisconnectedAtUtc is not null, player.OriginalPlayerId));
        }

        var pawns = started
            ? _engine.Players.SelectMany(player => player.Pawns)
                .Select(pawn => new PawnViewDto(pawn.Id, pawn.OwnerIndex, pawn.Row, pawn.Column, pawn.IsCaptured, pawn.HasActedThisTurn))
                .ToList()
            : new List<PawnViewDto>();

        var moves = new List<MoveTargetDto>();
        if (started && State == RoomState.InProgress && _pending is null && viewerIndex >= 0 &&
            !_players[viewerIndex].IsNpc && _engine.CurrentPlayerIndex == viewerIndex)
        {
            foreach (var pawn in _engine.Players[viewerIndex].ActivePawns)
            {
                var available = _engine.GetAvailableMoves(pawn);
                foreach (var direction in Enum.GetValues<Direction>())
                {
                    var target = board.GetNeighbor(pawn.Row, pawn.Column, direction);
                    if (target is not null && available.Contains(target))
                    {
                        moves.Add(new MoveTargetDto(pawn.Id, direction, target.Row, target.Column));
                    }
                }
            }
        }

        return new BoardViewDto(
            Code, State, board.Size, themes, players, pawns,
            started ? _engine.CurrentPlayerIndex : -1,
            _engine.Winner?.Index,
            viewerIndex,
            _pending is not null,
            moves);
    }

    private RoomSnapshot Capture()
    {
        var snapshot = new RoomSnapshot
        {
            Code = Code,
            State = State,
            HostPlayerId = _hostId,
            BoardSize = _boardSize,
            Seed = _seed,
            CreatedAtUtc = _createdAt,
            LastActivityUtc = LastActivityUtc,
            GameId = _gameId,
            Result = _result,
            Players = _players.Select(player => new PlayerSnapshot
            {
                Id = player.Id, Name = player.Name, IsNpc = player.IsNpc,
                DisconnectedAtUtc = player.DisconnectedAtUtc, OriginalPlayerId = player.OriginalPlayerId
            }).ToList()
        };
        if (_engine.IsStarted)
        {
            snapshot.Game = new GameSnapshot
            {
                CurrentPlayerIndex = _engine.CurrentPlayerIndex,
                State = _engine.State,
                WinnerIndex = _engine.Winner?.Index,
                Pawns = _engine.Players.SelectMany(player => player.Pawns).Select(pawn => new PawnSnapshot
                {
                    Id = pawn.Id,
                    OwnerIndex = pawn.OwnerIndex,
                    Row = pawn.Row,
                    Column = pawn.Column,
                    IsCaptured = pawn.IsCaptured,
                    HasActedThisTurn = pawn.HasActedThisTurn
                }).ToList(),
                Pending = _pending is null ? null : new PendingSnapshot
                {
                    PawnId = _pending.PawnId,
                    Direction = _pending.Direction,
                    IsDuel = _pending.IsDuel,
                    Answers = _pending.Answers.ToList(),
                    DeadlineUtc = _pending.Deadline
                }
            };
        }

        return snapshot;
    }

    private void Restore(RoomSnapshot snapshot)
    {
        if (snapshot.Players.Count > MaxPlayers ||
            (snapshot.State != RoomState.Finished && (snapshot.Players.Count < 1 ||
                snapshot.Players.All(player => player.IsNpc && player.OriginalPlayerId is null))))
        {
            throw new InvalidOperationException("The saved room is invalid.");
        }

        _players = snapshot.Players.Select(player => new RoomPlayer(player.Id, player.Name, player.IsNpc,
            player.DisconnectedAtUtc, player.OriginalPlayerId)).ToList();
        _gameId = snapshot.GameId ?? (snapshot.Game is null ? null : Guid.NewGuid());
        _result = snapshot.Result;
        _hostId = snapshot.HostPlayerId;
        _boardSize = snapshot.BoardSize;
        _seed = snapshot.Seed;
        _createdAt = snapshot.CreatedAtUtc;
        Volatile.Write(ref _state, (int)snapshot.State);
        Interlocked.Exchange(ref _lastActivityTicks, snapshot.LastActivityUtc.UtcTicks);
        _engine = NewEngine();
        if (snapshot.Game is { } game)
        {
            _engine.RestoreGame(
                _players.Select(player => (player.Name, player.IsNpc)).ToList(),
                snapshot.BoardSize,
                snapshot.Seed,
                game.Pawns.Select(pawn => new PawnState(pawn.Id, pawn.OwnerIndex, pawn.Row, pawn.Column, pawn.IsCaptured, pawn.HasActedThisTurn)).ToList(),
                game.CurrentPlayerIndex,
                game.State,
                game.WinnerIndex);
        }
    }

    /// <summary>Saves the current state; on failure the in-memory state returns to <paramref name="before"/>.</summary>
    private async Task<bool> TryPersistAsync(RoomSnapshot before, PendingAction? pendingBefore, CancellationToken cancellationToken)
    {
        var current = _pending;
        try
        {
            await _services.Store.SaveAsync(Capture(), cancellationToken);
        }
        catch (Exception ex)
        {
            Restore(before);
            _pending = pendingBefore;
            if (current is not null && !ReferenceEquals(current.Timer, pendingBefore?.Timer))
            {
                current.Timer.Dispose();
            }

            if (ex is OperationCanceledException)
            {
                throw;
            }

            return false;
        }

        if (pendingBefore is not null && !ReferenceEquals(current?.Timer, pendingBefore.Timer))
        {
            pendingBefore.Timer.Dispose();
        }

        return true;
    }

    private async Task<T> RunLockedAsync<T>(Func<List<RoomEvent>, Task<T>> action, CancellationToken cancellationToken)
    {
        var events = new List<RoomEvent>();
        T result;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            result = await action(events);
        }
        finally
        {
            ScheduleMaintenance();
            _gate.Release();
        }

        Publish(events);
        return result;
    }

    private Task<T> RunLockedAsync<T>(Func<List<RoomEvent>, T> action, CancellationToken cancellationToken) =>
        RunLockedAsync(events => Task.FromResult(action(events)), cancellationToken);

    private void Publish(IReadOnlyList<RoomEvent> events)
    {
        if (events.Count == 0)
        {
            return;
        }

        Subscription[] handlers;
        lock (_subscriptionLock)
        {
            handlers = _subscriptions.ToArray();
        }

        foreach (var roomEvent in events)
        {
            foreach (var handler in handlers)
            {
                try
                {
                    handler.Handler(roomEvent);
                }
                catch (Exception)
                {
                    // A faulty subscriber must not break the game.
                }
            }
        }
    }

    private sealed class Subscription(GameRoom room, Action<RoomEvent> handler) : IDisposable
    {
        public Action<RoomEvent> Handler { get; } = handler;

        public void Dispose()
        {
            lock (room._subscriptionLock)
            {
                room._subscriptions.Remove(this);
            }
        }
    }
}
