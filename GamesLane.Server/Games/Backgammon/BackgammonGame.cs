namespace GamesLane.Server.Games.Backgammon;

/// <summary>
/// Backgammon rules for a match. <c>rollDie</c> rolls one die (1 to 6); <c>start</c> replaces the starting position of
/// the first game (for tests).
/// </summary>
public class BackgammonGame : IBackgammonGame
{
    private const int MaxCube = 64;

    private readonly Func<int> _rollDie;
    private readonly int[] _scores = new int[2];
    private readonly List<BackgammonStep> _played = [];
    private readonly int?[] _openingDice = new int?[2];
    private BackgammonPosition _turnStart;
    private IReadOnlySet<string> _finals = new HashSet<string>();
    private int _diceToUse;
    private bool _crawfordUsed;
    private bool _crawfordNext;

    /// <summary>
    /// Starts the match; the first game waits for the opening roll.
    /// </summary>
    public BackgammonGame(BackgammonRules? rules = null, Func<int>? rollDie = null, BackgammonPosition? start = null)
    {
        Rules = rules ?? new BackgammonRules();
        ArgumentOutOfRangeException.ThrowIfLessThan(Rules.MatchLength, 1);
        _rollDie = rollDie ?? (() => Random.Shared.Next(1, 7));
        _turnStart = Position = start ?? BackgammonPosition.Start;
        StartGame(Position);
    }

    /// <inheritdoc />
    public BackgammonRules Rules { get; }

    /// <inheritdoc />
    public BackgammonPosition Position { get; private set; }

    /// <inheritdoc />
    public BackgammonPhase Phase { get; private set; }

    /// <inheritdoc />
    public int Turn { get; private set; }

    /// <inheritdoc />
    public int GameNumber { get; private set; } = 1;

    /// <inheritdoc />
    public IReadOnlyList<int> Scores => _scores;

    /// <inheritdoc />
    public IReadOnlyList<int> Dice { get; private set; } = [];

    /// <inheritdoc />
    public IReadOnlyList<int> DiceLeft { get; private set; } = [];

    /// <inheritdoc />
    public IReadOnlyList<int>? OpeningRoll { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<int?> OpeningDice => _openingDice;

    /// <inheritdoc />
    public int Rolls { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<BackgammonStep> Played => _played;

    /// <inheritdoc />
    public IReadOnlyList<BackgammonStep> LegalMoves { get; private set; } = [];

    /// <inheritdoc />
    public bool CanEndTurn => Phase == BackgammonPhase.Moving && _played.Count == _diceToUse;

    /// <inheritdoc />
    public BackgammonTurn? LastTurn { get; private set; }

    /// <inheritdoc />
    public int CubeValue { get; private set; } = 1;

    /// <inheritdoc />
    public int? CubeOwner { get; private set; }

    /// <inheritdoc />
    public bool IsCrawford { get; private set; }

    /// <inheritdoc />
    public bool CanDouble => Rules.Cube && Rules.MatchLength > 1 && !IsCrawford && Phase == BackgammonPhase.Rolling
        && (CubeOwner is null || CubeOwner == Turn) && CubeValue < MaxCube;

    /// <inheritdoc />
    public BackgammonResult? Result { get; private set; }

    /// <inheritdoc />
    public bool IsMatchOver => Phase == BackgammonPhase.MatchOver;

    /// <inheritdoc />
    public bool TryRoll(int player, out string? error)
    {
        if (Phase == BackgammonPhase.Opening)
        {
            return TryRollOpening(player, out error);
        }

        if (!TryCheckTurn(player, BackgammonPhase.Rolling, out error))
        {
            return false;
        }

        Rolls++;
        StartTurn([_rollDie(), _rollDie()]);
        return true;
    }

    /// <summary>
    /// The opening roll: each player rolls one die. On a tie, both dice stay on view until a player rolls again; then
    /// both roll again. Otherwise the higher die moves first, with both dice.
    /// </summary>
    private bool TryRollOpening(int player, out string? error)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(player, 1);
        if (_openingDice is [{ } tiedWhite, { } tiedBlack] && tiedWhite == tiedBlack)
        {
            Array.Clear(_openingDice);
        }

        if (_openingDice[player] is not null)
        {
            error = "You have rolled your die. Waiting for the other player.";
            return false;
        }

        error = null;
        Rolls++;
        _openingDice[player] = _rollDie();
        if (_openingDice is [{ } white, { } black] && white != black)
        {
            Turn = white > black ? 0 : 1;
            OpeningRoll = [white, black];
            StartTurn([white, black]);
        }

        return true;
    }

    /// <inheritdoc />
    public bool TryMove(int player, int from, int to, out string? error)
    {
        if (!TryCheckTurn(player, BackgammonPhase.Moving, out error))
        {
            return false;
        }

        if (LegalMoves.FirstOrDefault(m => m.From == from && m.To == to) is not { } step)
        {
            error = _played.Count == _diceToUse ? "You have used your dice. End your turn." : "That move is not allowed.";
            return false;
        }

        Play(step);
        if (HasWon(player))
        {
            LastTurn = new BackgammonTurn(player, Dice, [.. _played], false);
            EndGame(player, WinKind(player));
        }
        else
        {
            LegalMoves = FindLegalMoves();
        }

        return true;
    }

    /// <inheritdoc />
    public bool TryUndo(int player, out string? error)
    {
        if (!TryCheckTurn(player, BackgammonPhase.Moving, out error))
        {
            return false;
        }

        if (_played.Count == 0)
        {
            error = "There is nothing to undo.";
            return false;
        }

        var steps = _played.Take(_played.Count - 1).ToList();
        _played.Clear();
        Position = _turnStart;
        DiceLeft = AllDice(Dice);
        steps.ForEach(Play);
        LegalMoves = FindLegalMoves();
        return true;
    }

    /// <inheritdoc />
    public bool TryEndTurn(int player, out string? error)
    {
        if (!TryCheckTurn(player, BackgammonPhase.Moving, out error))
        {
            return false;
        }

        if (!CanEndTurn)
        {
            error = "Move with all the dice you can first.";
            return false;
        }

        LastTurn = new BackgammonTurn(player, Dice, [.. _played], false);
        NextTurn();
        return true;
    }

    /// <inheritdoc />
    public bool TryDouble(int player, out string? error)
    {
        if (!TryCheckTurn(player, BackgammonPhase.Rolling, out error))
        {
            return false;
        }

        if (!CanDouble)
        {
            error = !Rules.Cube || Rules.MatchLength == 1 ? "This match is played without the doubling cube."
                : IsCrawford ? "Nobody may double in the Crawford game."
                : CubeValue >= MaxCube ? "The cube is at its highest value."
                : "The other player owns the cube.";
            return false;
        }

        Phase = BackgammonPhase.Doubled;
        return true;
    }

    /// <inheritdoc />
    public bool TryTake(int player, out string? error)
    {
        if (!TryCheckAnswer(player, out error))
        {
            return false;
        }

        CubeValue *= 2;
        CubeOwner = player;
        Phase = BackgammonPhase.Rolling;
        return true;
    }

    /// <inheritdoc />
    public bool TryDrop(int player, out string? error)
    {
        if (!TryCheckAnswer(player, out error))
        {
            return false;
        }

        EndGame(Turn, BackgammonResultKind.Dropped);
        return true;
    }

    /// <inheritdoc />
    public bool TryNextGame(int game, out string? error)
    {
        error = null;
        if (game != GameNumber)
        {
            return true;
        }

        if (Phase != BackgammonPhase.GameOver)
        {
            error = Phase == BackgammonPhase.MatchOver ? "The match is over." : "This game is not over yet.";
            return false;
        }

        GameNumber++;
        StartGame(BackgammonPosition.Start);
        return true;
    }

    /// <summary>
    /// Starts a game: the cube in the middle, and both players to roll their opening die.
    /// </summary>
    private void StartGame(BackgammonPosition start)
    {
        Position = start;
        CubeValue = 1;
        CubeOwner = null;
        Result = null;
        LastTurn = null;
        IsCrawford = _crawfordNext;
        _crawfordNext = false;
        Phase = BackgammonPhase.Opening;
        Turn = 0;
        Dice = [];
        DiceLeft = [];
        LegalMoves = [];
        OpeningRoll = null;
        Array.Clear(_openingDice);
    }

    /// <summary>
    /// Starts moving with the rolled dice. Without any possible move, the turn passes; with only one way to play, it is
    /// played and the turn ends.
    /// </summary>
    private void StartTurn(int[] dice)
    {
        Phase = BackgammonPhase.Moving;
        Dice = dice;
        DiceLeft = AllDice(dice);
        _turnStart = Position;
        _played.Clear();
        (_finals, _diceToUse) = BackgammonMoves.Turn(Position, Turn, DiceLeft);
        if (_diceToUse == 0)
        {
            LastTurn = new BackgammonTurn(Turn, dice, [], false);
            NextTurn();
            return;
        }

        if (_finals.Count == 1)
        {
            foreach (var step in ForcedPlay(Position, DiceLeft, _diceToUse)!)
            {
                Play(step);
            }

            LastTurn = new BackgammonTurn(Turn, dice, [.. _played], true);
            if (HasWon(Turn))
            {
                EndGame(Turn, WinKind(Turn));
            }
            else
            {
                NextTurn();
            }

            return;
        }

        LegalMoves = FindLegalMoves();
    }

    /// <summary>
    /// Passes the turn to the other player, who rolls next.
    /// </summary>
    private void NextTurn()
    {
        Turn = 1 - Turn;
        Phase = BackgammonPhase.Rolling;
        Dice = [];
        DiceLeft = [];
        LegalMoves = [];
        OpeningRoll = null;
        _played.Clear();
    }

    /// <summary>
    /// Ends the game won by <paramref name="winner"/>: they score the kind's 1, 2 or 3 times the cube (the cube's value
    /// for a dropped double). The next game is the Crawford game if the winner has just come within one point of the
    /// match for the first time and the other player has not.
    /// </summary>
    private void EndGame(int winner, BackgammonResultKind kind)
    {
        var points = CubeValue * kind switch { BackgammonResultKind.Gammon => 2, BackgammonResultKind.Backgammon => 3, _ => 1 };
        _scores[winner] += points;
        Result = new BackgammonResult(winner, points, kind);
        Turn = winner;
        Phase = _scores[winner] >= Rules.MatchLength ? BackgammonPhase.MatchOver : BackgammonPhase.GameOver;
        Dice = [];
        DiceLeft = [];
        LegalMoves = [];
        OpeningRoll = null;
        _played.Clear();
        if (Rules.Cube && !_crawfordUsed && Rules.MatchLength > 1
            && _scores[winner] == Rules.MatchLength - 1 && _scores[1 - winner] < Rules.MatchLength - 1)
        {
            _crawfordUsed = _crawfordNext = true;
        }
    }

    /// <summary>
    /// Checks that the match is waiting for <paramref name="player"/> to act in <paramref name="phase"/>.
    /// </summary>
    private bool TryCheckTurn(int player, BackgammonPhase phase, out string? error)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(player, 1);
        error = Phase is BackgammonPhase.GameOver or BackgammonPhase.MatchOver ? "The game is over."
            : Phase == BackgammonPhase.Opening ? "First roll your die to see who starts."
            : Phase == BackgammonPhase.Doubled ? "Waiting for the answer to the double."
            : player != Turn ? "It is not your turn."
            : Phase == phase ? null
            : phase == BackgammonPhase.Moving ? "Roll the dice first."
            : "You have already rolled.";
        return error is null;
    }

    /// <summary>
    /// Checks that <paramref name="player"/> is the one who must take or drop a double.
    /// </summary>
    private bool TryCheckAnswer(int player, out string? error)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(player, 1);
        error = Phase != BackgammonPhase.Doubled ? "Nobody has offered to double."
            : player == Turn ? "The other player decides whether to take the double."
            : null;
        return error is null;
    }

    /// <summary>
    /// Moves a checker for the player to move and uses its die.
    /// </summary>
    private void Play(BackgammonStep step)
    {
        Position = Position.Apply(Turn, step.From, step.To);
        DiceLeft = BackgammonMoves.Without(DiceLeft, step.Die);
        _played.Add(step);
    }

    private bool HasWon(int player) => Position.Count(player, BackgammonPosition.Off) == BackgammonPosition.Checkers;

    /// <summary>
    /// How <paramref name="winner"/> won by bearing off: a gammon if the loser bore off no checker, a backgammon if the
    /// loser also still has a checker on the bar or in the winner's home board (the loser's points 19 to 24).
    /// </summary>
    private BackgammonResultKind WinKind(int winner)
    {
        var loser = 1 - winner;
        if (Position.Count(loser, BackgammonPosition.Off) > 0)
        {
            return BackgammonResultKind.Single;
        }

        return Enumerable.Range(19, 7).Any(point => Position.Count(loser, point) > 0)
            ? BackgammonResultKind.Backgammon
            : BackgammonResultKind.Gammon;
    }

    /// <summary>
    /// The dice to move with: the two dice, or four of the number with doubles.
    /// </summary>
    private static int[] AllDice(IReadOnlyList<int> dice) => dice[0] == dice[1] ? [.. Enumerable.Repeat(dice[0], 4)] : [.. dice];

    /// <summary>
    /// The moves that keep a complete legal turn possible, one per starting point and destination (with the smallest
    /// die that allows it).
    /// </summary>
    private IReadOnlyList<BackgammonStep> FindLegalMoves()
    {
        var left = _diceToUse - _played.Count;
        if (left == 0)
        {
            return [];
        }

        return [.. DiceLeft.Distinct().Order()
            .SelectMany(die => BackgammonMoves.Steps(Position, Turn, die))
            .Where(step => BackgammonMoves.CanReach(
                Position.Apply(Turn, step.From, step.To), Turn, BackgammonMoves.Without(DiceLeft, step.Die), left - 1, _finals))
            .DistinctBy(step => (step.From, step.To))];
    }

    /// <summary>
    /// The steps of the only legal play (which ends in the single final position), or null if none is found.
    /// </summary>
    private List<BackgammonStep>? ForcedPlay(BackgammonPosition position, IReadOnlyList<int> dice, int steps)
    {
        if (steps == 0 || position.Count(Turn, BackgammonPosition.Off) == BackgammonPosition.Checkers)
        {
            return _finals.Contains(position.Key) ? [] : null;
        }

        foreach (var die in dice.Distinct())
        {
            foreach (var step in BackgammonMoves.Steps(position, Turn, die))
            {
                if (ForcedPlay(position.Apply(Turn, step.From, step.To), BackgammonMoves.Without(dice, die), steps - 1) is { } rest)
                {
                    return [step, .. rest];
                }
            }
        }

        return null;
    }
}
