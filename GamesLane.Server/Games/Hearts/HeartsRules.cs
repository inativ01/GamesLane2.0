using GamesLane.Server.Games.TheGang;

namespace GamesLane.Server.Games.Hearts;

/// <summary>
/// What a Hearts game is waiting for.
/// </summary>
public enum HeartsPhase
{
    /// <summary>Every player chooses 3 cards to pass.</summary>
    Passing,

    /// <summary>The players play the 13 tricks.</summary>
    Playing,

    /// <summary>A hand has ended; the next one can be dealt.</summary>
    HandOver,

    /// <summary>The game has ended.</summary>
    GameOver,
}

/// <summary>
/// Where the cards are passed in a hand: to the left (the next player), to the right (the previous one), across, or not
/// at all. The hands go round in this order.
/// </summary>
public enum HeartsPass
{
    Left,
    Right,
    Across,
    Hold,
}

/// <summary>
/// A player at the end of a hand: the points they took in it, and their score after it.
/// </summary>
public record HeartsHandPlayer(int Player, int Points, int Score);

/// <summary>
/// The end of a hand: each player's points, and the player who shot the moon (took all 26 points), or null.
/// </summary>
public record HeartsHandResult(HeartsHandPlayer[] Players, int? Moon);

/// <summary>
/// The rules of Hearts (the standard game, without options).
/// </summary>
public static class HeartsRules
{
    /// <summary>The cards each player passes.</summary>
    public const int PassCount = 3;

    /// <summary>The score that ends the game, at the end of a hand.</summary>
    public const int Target = 100;

    /// <summary>The points of all the hearts and the queen of spades.</summary>
    public const int AllPoints = 26;

    /// <summary>The card that leads the first trick.</summary>
    public static Card TwoOfClubs { get; } = new(Card.Two, Suit.Clubs);

    /// <summary>The queen of spades, worth 13 points.</summary>
    public static Card QueenOfSpades { get; } = new(12, Suit.Spades);

    /// <summary>A card's points: a heart 1, the queen of spades 13, any other card 0.</summary>
    public static int Points(Card card) => card.Suit == Suit.Hearts ? 1 : card == QueenOfSpades ? 13 : 0;

    /// <summary>Where the cards are passed in hand <paramref name="hand"/> (from 1): left, right, across, hold, and again.</summary>
    public static HeartsPass PassFor(int hand) => (HeartsPass)((hand - 1) % 4);

    /// <summary>The player who receives <paramref name="player"/>'s cards with <paramref name="pass"/> (themselves with Hold).</summary>
    public static int PassTarget(int player, HeartsPass pass) => (player + pass switch
    {
        HeartsPass.Left => 1,
        HeartsPass.Right => 3,
        HeartsPass.Across => 2,
        _ => 0,
    }) % 4;
}
