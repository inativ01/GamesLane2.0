namespace GamesLane.Server.Games.Backgammon;

/// <summary>
/// A backgammon match between player 0 (white) and player 1 (black), to <see cref="BackgammonRules.MatchLength"/>
/// points. Each game starts with an opening roll (one die each, again on a tie); the higher die moves first with both
/// dice. A turn: roll (or first offer to double), move checkers by the dice as the rules require, then end the turn.
/// Points are each player's own numbering (see <see cref="BackgammonPosition"/>).
/// </summary>
public interface IBackgammonGame
{
    /// <summary>
    /// The rules the match is played with.
    /// </summary>
    BackgammonRules Rules { get; }

    /// <summary>
    /// The checkers, as they are now (including the moves made so far in the current turn).
    /// </summary>
    BackgammonPosition Position { get; }

    /// <summary>
    /// What the match is waiting for.
    /// </summary>
    BackgammonPhase Phase { get; }

    /// <summary>
    /// The player to move (or who offered to double). After a game, the winner.
    /// </summary>
    int Turn { get; }

    /// <summary>
    /// The current game of the match, starting at 1.
    /// </summary>
    int GameNumber { get; }

    /// <summary>
    /// Each player's points in the match.
    /// </summary>
    IReadOnlyList<int> Scores { get; }

    /// <summary>
    /// The dice of the current turn (two values), or empty before rolling.
    /// </summary>
    IReadOnlyList<int> Dice { get; }

    /// <summary>
    /// The dice not used yet in this turn (four with doubles at first).
    /// </summary>
    IReadOnlyList<int> DiceLeft { get; }

    /// <summary>
    /// The opening roll of the current game (white's die, black's die) while its first turn is played, or null.
    /// </summary>
    IReadOnlyList<int>? OpeningRoll { get; }

    /// <summary>
    /// The checkers moved so far in the current turn.
    /// </summary>
    IReadOnlyList<BackgammonStep> Played { get; }

    /// <summary>
    /// The moves the player to move can make now: one per starting point and destination.
    /// </summary>
    IReadOnlyList<BackgammonStep> LegalMoves { get; }

    /// <summary>
    /// Whether the player to move has used as many dice as the rules require, so the turn can end.
    /// </summary>
    bool CanEndTurn { get; }

    /// <summary>
    /// The last finished turn of the current game, or null.
    /// </summary>
    BackgammonTurn? LastTurn { get; }

    /// <summary>
    /// The value of the doubling cube (1 until someone doubles).
    /// </summary>
    int CubeValue { get; }

    /// <summary>
    /// The player who owns the cube (who may double next), or null while it is in the middle (either may double).
    /// </summary>
    int? CubeOwner { get; }

    /// <summary>
    /// Whether the current game is the Crawford game, in which nobody may double.
    /// </summary>
    bool IsCrawford { get; }

    /// <summary>
    /// Whether the player to move may offer to double now.
    /// </summary>
    bool CanDouble { get; }

    /// <summary>
    /// How the last game ended, while it is over (<see cref="BackgammonPhase.GameOver"/> or
    /// <see cref="BackgammonPhase.MatchOver"/>), or null.
    /// </summary>
    BackgammonResult? Result { get; }

    /// <summary>
    /// Whether a player has won the match.
    /// </summary>
    bool IsMatchOver { get; }

    /// <summary>
    /// Rolls the dice for <paramref name="player"/>, whose turn it is. If no checker can move, the turn passes; if there
    /// is only one way to play the dice, it is played and the turn ends.
    /// </summary>
    bool TryRoll(int player, out string? error);

    /// <summary>
    /// Moves one of <paramref name="player"/>'s checkers from <paramref name="from"/> to <paramref name="to"/> with one
    /// of the dice. Bearing off the last checker wins the game.
    /// </summary>
    bool TryMove(int player, int from, int to, out string? error);

    /// <summary>
    /// Takes back the last checker moved in this turn.
    /// </summary>
    bool TryUndo(int player, out string? error);

    /// <summary>
    /// Ends <paramref name="player"/>'s turn, once they have used as many dice as the rules require.
    /// </summary>
    bool TryEndTurn(int player, out string? error);

    /// <summary>
    /// <paramref name="player"/>, whose turn it is, offers to double before rolling.
    /// </summary>
    bool TryDouble(int player, out string? error);

    /// <summary>
    /// <paramref name="player"/> accepts the double: the cube's value doubles and they own it.
    /// </summary>
    bool TryTake(int player, out string? error);

    /// <summary>
    /// <paramref name="player"/> refuses the double and loses the game at the cube's current value.
    /// </summary>
    bool TryDrop(int player, out string? error);

    /// <summary>
    /// Starts the game after game <paramref name="game"/>, once it is over. If it has already started, nothing changes.
    /// </summary>
    bool TryNextGame(int game, out string? error);
}
