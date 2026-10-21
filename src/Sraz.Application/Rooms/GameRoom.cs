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
    private const int MaxNpcStepsPerDrive = 5000;

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

    private sealed record RoomPlayer(Guid Id, string Name, bool IsNpc);

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

    public void Close()
    {
        _gate.Wait();
        try
        {
            _pending?.Timer.Dispose();
            _pending = null;
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

    public static async Task<GameRoom> RestoreAsync(RoomSnapshot snapshot, RoomServices services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SchemaVersion != RoomSnapshot.CurrentSchemaVersion || string.IsNullOrWhiteSpace(snapshot.Code))
        {
            throw new InvalidOperationException("Unsupported room snapshot.");
        }

        var room = new GameRoom(snapshot.Code, services);
        room.Restore(snapshot);
        var events = new List<RoomEvent>();
        await room._gate.WaitAsync(cancellationToken);
        try
        {
            if (room.State == RoomState.InProgress)
            {
                if (snapshot.Game?.Pending is { } pending && room.FindPawn(pending.PawnId) is { } pawn)
                {
                    // A question that was open when the server stopped counts as incorrect.
                    var answers = pending.IsDuel ? pending.Answers.Append(false).ToList() : new List<bool> { false };
                    var step = room.Resolve(pawn, pending.Direction, pending.IsDuel, answers);
                    room.Touch();
                    try
                    {
                        await services.Store.SaveAsync(room.Capture(), cancellationToken);
                        events.AddRange(step);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                    }
                }

                await room.DriveNpcAsync(events, cancellationToken);
            }
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
            if (HumanIndex(playerId) >= 0)
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
            return await ResolveAnswerAsync(pending, correct, events, cancellationToken);
        }, cancellationToken);

    private async Task<RoomResult<AnswerResultDto>> ResolveAnswerAsync(
        PendingAction pending, bool correct, List<RoomEvent> events, CancellationToken cancellationToken)
    {
        var pawn = FindPawn(pending.PawnId);
        if (pawn is null)
        {
            return RoomResult<AnswerResultDto>.Fail(RoomError.PawnNotOwned);
        }

        var before = Capture();
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

        Touch();
        if (!await TryPersistAsync(before, pending, cancellationToken))
        {
            return RoomResult<AnswerResultDto>.Fail(RoomError.PersistenceFailed);
        }

        events.AddRange(step);
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

            var result = await ResolveAnswerAsync(pending, false, events, CancellationToken.None);
            if (result.Error == RoomError.PersistenceFailed && ReferenceEquals(_pending?.Timer, timer))
            {
                _pending = NewPending(pending.PawnId, pending.Direction, pending.IsDuel, pending.Questions, pending.Answers);
                timer.Dispose();
            }
        }
        finally
        {
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
            if (State != RoomState.InProgress || _pending is not null || !_engine.CurrentPlayer.IsNpc ||
                !_engine.Players.Any(player => !player.IsNpc && !player.IsEliminated))
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

            Touch();
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
            return _engine.CurrentPlayerIndex != from
                ? new[] { new RoomEvent(RoomEventType.TurnChanged, Code, _engine.CurrentPlayerIndex) }
                : Array.Empty<RoomEvent>();
        }

        var (npcPawn, direction) = choice.Value;
        var request = await _engine.RequestMove(npcPawn, direction, cancellationToken);
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

    private Pawn? FindPawn(Guid pawnId) =>
        _engine.IsStarted ? _engine.Players.SelectMany(player => player.Pawns).FirstOrDefault(pawn => pawn.Id == pawnId) : null;

    private void Touch() => Interlocked.Exchange(ref _lastActivityTicks, _services.Time.GetUtcNow().UtcTicks);

    private GameEngine NewEngine() => new(_services.Questions);

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
            Players = _players.Select(player => new PlayerSnapshot { Id = player.Id, Name = player.Name, IsNpc = player.IsNpc }).ToList()
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
        if (snapshot.Players.Count is < 1 or > MaxPlayers || snapshot.Players.All(player => player.IsNpc))
        {
            throw new InvalidOperationException("The saved room is invalid.");
        }

        _players = snapshot.Players.Select(player => new RoomPlayer(player.Id, player.Name, player.IsNpc)).ToList();
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
