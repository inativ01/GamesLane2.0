namespace GamesLane.Server.Games.Dominoes;

/// <summary>
/// A legal play: a tile and where it goes (see <see cref="IDominoesGame.TryPlay"/>).
/// </summary>
public record DominoesMove(Tile Tile, int Target);

/// <summary>
/// Dominoes for 2 to 4 players (Block, Draw, All Fives) or 2 to 8 (Mexican Train), numbered from 0 in turn order, round
/// after round. In the line styles, each player in turn adds a tile matching an open end of the line; the first to play
/// all their tiles scores the pips left in the other hands (by team with partners), and the first side to the score
/// target wins. In Mexican Train, each player builds their own train from the round's engine double, or the Mexican
/// train, or another player's train once it has a marker; the players add up the pips left in their hands each round, and
/// the lowest total after the last round wins.
/// </summary>
public interface IDominoesGame
{
    /// <summary>The rules of this game.</summary>
    DominoesRules Rules { get; }

    /// <summary>The number of players.</summary>
    int Players { get; }

    /// <summary>Whether 4 players play in 2 teams (players 0 and 2, 1 and 3).</summary>
    bool HasTeams { get; }

    /// <summary>The number of sides that score: 2 teams, or each player.</summary>
    int Sides { get; }

    /// <summary>The side of a player.</summary>
    int SideOf(int player);

    /// <summary>The round being played, from 1.</summary>
    int RoundNumber { get; }

    /// <summary>The number of rounds of a Mexican Train game (null for the line styles, which play to a score).</summary>
    int? TotalRounds { get; }

    /// <summary>What the game is waiting for.</summary>
    DominoesPhase Phase { get; }

    /// <summary>The player to play.</summary>
    int Turn { get; }

    /// <summary>Whether the player to play has drawn on this turn.</summary>
    bool HasDrawn { get; }

    /// <summary>The line of tiles, from its left end to its right end (line styles).</summary>
    IReadOnlyList<LaidTile> Line { get; }

    /// <summary>The engine double of this round (Mexican Train), or null.</summary>
    int? Engine { get; }

    /// <summary>A player's train, from the engine (Mexican Train).</summary>
    IReadOnlyList<LaidTile> Train(int player);

    /// <summary>The Mexican train, from the engine (Mexican Train).</summary>
    IReadOnlyList<LaidTile> MexicanTrain { get; }

    /// <summary>Whether a player's train has a marker, so the others may play on it (Mexican Train).</summary>
    bool HasMarker(int player);

    /// <summary>The tiles left in the boneyard.</summary>
    int BoneyardCount { get; }

    /// <summary>A player's tiles, in hand order.</summary>
    IReadOnlyList<Tile> Hand(int player);

    /// <summary>The plays <paramref name="player"/> may make now: none unless it is their turn.</summary>
    IReadOnlyList<DominoesMove> Moves(int player);

    /// <summary>Whether <paramref name="player"/> may draw now: on their turn, with no play, from a boneyard with tiles.</summary>
    bool CanDraw(int player);

    /// <summary>Whether <paramref name="player"/> may pass now: on their turn, with no play, and nothing to draw.</summary>
    bool CanPass(int player);

    /// <summary>Each side's score.</summary>
    IReadOnlyList<int> Scores { get; }

    /// <summary>The last action in this round, or null.</summary>
    DominoesAction? LastAction { get; }

    /// <summary>The number of actions so far, so a page can tell a new one.</summary>
    int Actions { get; }

    /// <summary>How the last round ended, or null during the first.</summary>
    DominoesRoundResult? LastResult { get; }

    /// <summary>Whether the game has ended.</summary>
    bool IsOver { get; }

    /// <summary>The winning sides once the game is over (more than one on a tie), otherwise empty.</summary>
    IReadOnlyList<int> Winners { get; }

    /// <summary>
    /// <paramref name="player"/>, whose turn it is, plays <paramref name="tile"/>: in the line styles at the left end
    /// (<paramref name="target"/> 0) or the right end (1) of the line; in Mexican Train on player <paramref name="target"/>'s
    /// train, or on the Mexican train (-1).
    /// </summary>
    bool TryPlay(int player, Tile tile, int target, out string? error);

    /// <summary><paramref name="player"/>, whose turn it is and who cannot play, draws a tile from the boneyard.</summary>
    bool TryDraw(int player, out string? error);

    /// <summary>
    /// <paramref name="player"/>, whose turn it is and who cannot play or draw, passes (in Mexican Train, their train gets a
    /// marker).
    /// </summary>
    bool TryPass(int player, out string? error);

    /// <summary><paramref name="player"/>'s turn is skipped (they are absent), as if they passed.</summary>
    bool TrySkip(int player, out string? error);

    /// <summary>Deals the round after round <paramref name="round"/>. If it has already been dealt, nothing changes.</summary>
    bool TryNextRound(int round, out string? error);
}
