namespace GamesLane.Server.Games.Yatzee;

/// <summary>
/// A Yatzee game for players 0 to <see cref="Players"/> - 1, who take turns in that order for 13 rounds. A turn: roll the
/// five dice up to three times, keeping (holding) any dice between rolls, then score the dice in one open box of the
/// scorecard (see <see cref="YatzeeScoring"/>). The game ends when every scorecard is full; the highest total wins.
/// </summary>
public interface IYatzeeGame
{
    /// <summary>
    /// The number of players.
    /// </summary>
    int Players { get; }

    /// <summary>
    /// Each player's scorecard.
    /// </summary>
    IReadOnlyList<YatzeeScorecard> Scorecards { get; }

    /// <summary>
    /// The current round, from 1 to 13 (13 once the game is over).
    /// </summary>
    int Round { get; }

    /// <summary>
    /// The player whose turn it is. Once the game is over, the last player.
    /// </summary>
    int Turn { get; }

    /// <summary>
    /// The five dice (1 to 6), or all 0 before the turn's first roll.
    /// </summary>
    IReadOnlyList<int> Dice { get; }

    /// <summary>
    /// Which dice are held, so the next roll leaves them as they are.
    /// </summary>
    IReadOnlyList<bool> Held { get; }

    /// <summary>
    /// How many times the dice have been rolled in this turn (0 to 3).
    /// </summary>
    int RollsUsed { get; }

    /// <summary>
    /// How many times the dice have been rolled in the game, so pages can tell a new roll.
    /// </summary>
    int Rolls { get; }

    /// <summary>
    /// For each box (in <see cref="YatzeeCategory"/> order), what the dice would score there for the player whose turn
    /// it is, or null if they cannot score there (the box is filled, or the joker rule does not allow it). All null
    /// before the turn's first roll and once the game is over.
    /// </summary>
    IReadOnlyList<int?> PossibleScores { get; }

    /// <summary>
    /// The last turn scored, or null at the start of the game.
    /// </summary>
    YatzeeTurn? LastTurn { get; }

    /// <summary>
    /// Whether every scorecard is full.
    /// </summary>
    bool IsOver { get; }

    /// <summary>
    /// Once the game is over, the players with the highest total (more than one on a tie); otherwise empty.
    /// </summary>
    IReadOnlyList<int> Winners { get; }

    /// <summary>
    /// <paramref name="player"/>, whose turn it is, rolls the dice that are not held (all five on the first roll).
    /// </summary>
    bool TryRoll(int player, out string? error);

    /// <summary>
    /// <paramref name="player"/>, whose turn it is, holds (or releases) die <paramref name="die"/> (0 to 4) between rolls.
    /// </summary>
    bool TryHold(int player, int die, bool held, out string? error);

    /// <summary>
    /// <paramref name="player"/>, whose turn it is, scores the dice in <paramref name="category"/>, which ends the turn.
    /// </summary>
    bool TryScore(int player, YatzeeCategory category, out string? error);
}
