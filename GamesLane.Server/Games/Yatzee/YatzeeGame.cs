namespace GamesLane.Server.Games.Yatzee;

/// <summary>
/// A scored turn: the player, the box, the points scored there, whether it earned a Yatzee bonus, and the dice.
/// </summary>
public record YatzeeTurn(int Player, YatzeeCategory Category, int Points, bool Bonus, IReadOnlyList<int> Dice);

/// <summary>
/// Yatzee rules for one game. <c>rollDie</c> rolls one die (1 to 6).
/// </summary>
public class YatzeeGame : IYatzeeGame
{
    /// <summary>The number of dice.</summary>
    public const int DiceCount = 5;

    /// <summary>The number of rolls in a turn.</summary>
    public const int RollsPerTurn = 3;

    private Func<int> _rollDie;
    private readonly YatzeeScorecard[] _scorecards;
    private readonly int[] _dice = new int[DiceCount];
    private readonly bool[] _held = new bool[DiceCount];

    /// <summary>
    /// Starts a game for <paramref name="players"/> players; player 0 rolls first.
    /// </summary>
    public YatzeeGame(int players, Func<int>? rollDie = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(players, 1);
        _rollDie = rollDie ?? (() => Random.Shared.Next(1, 7));
        _scorecards = [.. Enumerable.Range(0, players).Select(_ => new YatzeeScorecard())];
    }

    /// <summary>
    /// Gives a game loaded from a save its die again (see the constructor).
    /// </summary>
    internal void Reattach(Func<int>? rollDie) => _rollDie = rollDie ?? (() => Random.Shared.Next(1, 7));

    /// <inheritdoc />
    public int Players => _scorecards.Length;

    /// <inheritdoc />
    public IReadOnlyList<YatzeeScorecard> Scorecards => _scorecards;

    /// <inheritdoc />
    public int Round { get; private set; } = 1;

    /// <inheritdoc />
    public int Turn { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<int> Dice => _dice;

    /// <inheritdoc />
    public IReadOnlyList<bool> Held => _held;

    /// <inheritdoc />
    public int RollsUsed { get; private set; }

    /// <inheritdoc />
    public int Rolls { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<int?> PossibleScores => RollsUsed == 0 || IsOver
        ? new int?[YatzeeScoring.Categories]
        : YatzeeScoring.Options(_scorecards[Turn], _dice);

    /// <inheritdoc />
    public YatzeeTurn? LastTurn { get; private set; }

    /// <inheritdoc />
    public bool IsOver { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<int> Winners
    {
        get
        {
            if (!IsOver)
            {
                return [];
            }

            var best = _scorecards.Max(c => c.GrandTotal);
            return [.. Enumerable.Range(0, Players).Where(p => _scorecards[p].GrandTotal == best)];
        }
    }

    /// <inheritdoc />
    public bool TryRoll(int player, out string? error)
    {
        if (!TryCheckTurn(player, out error))
        {
            return false;
        }

        if (RollsUsed == RollsPerTurn)
        {
            error = "You have no rolls left. Choose a box to score.";
            return false;
        }

        for (var i = 0; i < DiceCount; i++)
        {
            if (!_held[i])
            {
                _dice[i] = _rollDie();
            }
        }

        RollsUsed++;
        Rolls++;
        return true;
    }

    /// <inheritdoc />
    public bool TryHold(int player, int die, bool held, out string? error)
    {
        if (!TryCheckTurn(player, out error))
        {
            return false;
        }

        error = die is < 0 or >= DiceCount ? "There is no such die."
            : RollsUsed == 0 ? "Roll the dice first."
            : RollsUsed == RollsPerTurn ? "You have no rolls left. Choose a box to score."
            : null;
        if (error is not null)
        {
            return false;
        }

        _held[die] = held;
        return true;
    }

    /// <inheritdoc />
    public bool TryScore(int player, YatzeeCategory category, out string? error)
    {
        if (!TryCheckTurn(player, out error))
        {
            return false;
        }

        var card = _scorecards[player];
        error = RollsUsed == 0 ? "Roll the dice first." : YatzeeScoring.ScoreError(card, _dice, category);
        if (error is not null)
        {
            return false;
        }

        var points = YatzeeScoring.Options(card, _dice)[(int)category]!.Value;
        var bonus = YatzeeScoring.IsJoker(card, _dice) && card[YatzeeCategory.Yatzee] == YatzeeScoring.YatzeePoints;
        card.Fill(category, points, bonus);
        LastTurn = new YatzeeTurn(player, category, points, bonus, [.. _dice]);
        NextTurn();
        return true;
    }

    /// <summary>
    /// Passes the turn to the next player, with the dice cleared; after the last player, the next round starts. The game
    /// ends when every scorecard is full.
    /// </summary>
    private void NextTurn()
    {
        Array.Clear(_dice);
        Array.Clear(_held);
        RollsUsed = 0;
        if (_scorecards.All(c => c.IsComplete))
        {
            IsOver = true;
            return;
        }

        Turn = (Turn + 1) % Players;
        if (Turn == 0)
        {
            Round++;
        }
    }

    /// <summary>
    /// Checks that the game is waiting for <paramref name="player"/>.
    /// </summary>
    private bool TryCheckTurn(int player, out string? error)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, Players);
        error = IsOver ? "The game is over." : player != Turn ? "It is not your turn." : null;
        return error is null;
    }
}
