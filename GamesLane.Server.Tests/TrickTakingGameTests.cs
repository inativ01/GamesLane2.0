using GamesLane.Server.Games.TheGang;
using GamesLane.Server.Games.Tricks;

namespace GamesLane.Server.Tests;

public class TrickTakingGameTests
{
    [Fact]
    public void Each_player_gets_13_cards_sorted_by_suit_then_rank()
    {
        var game = new PlainGame(null, Shuffled(new Random(3)));

        Assert.All(Enumerable.Range(0, 4), p => Assert.Equal(13, game.Hand(p).Count));
        Assert.Equal(52, Enumerable.Range(0, 4).SelectMany(game.Hand).Distinct().Count());
        Assert.All(Enumerable.Range(0, 4), p => Assert.Equal(TrickCards.Sorted(game.Hand(p)), game.Hand(p)));
        Assert.Equal(Cards("2♣ A♣ 3♦ 5♠ K♠ 4♥"), TrickCards.Sorted(Cards("K♠ 4♥ A♣ 5♠ 3♦ 2♣")));
    }

    [Fact]
    public void Players_must_follow_suit_when_they_can_and_play_in_turn()
    {
        var game = new PlainGame(null, Stacked("2♣ 3♣ 4♣ 5♥", "6♣ 2♦ 7♣", "8♣ 9♣", "10♣"));

        AssertRefused(game.TryPlay(1, Card("6♣"), out var error), error, "It is not your turn.");
        AssertRefused(game.TryPlay(0, Card("6♣"), out error), error, "You do not have that card.");
        Assert.True(game.TryPlay(0, Card("2♣"), out error), error);
        Assert.Equal(Cards("6♣ 7♣"), game.Playable(1));
        AssertRefused(game.TryPlay(1, Card("2♦"), out error), error, "You must follow suit.");
        Assert.Empty(game.Playable(0));
    }

    [Theory]
    [InlineData(null, "2♣ 9♣ 2♠ 4♣", 1)]
    [InlineData(null, "2♣ 9♥ A♥ 4♣", 3)]
    [InlineData(Suit.Spades, "2♣ 9♣ 2♠ 4♣", 2)]
    [InlineData(Suit.Spades, "2♣ 3♠ K♠ A♣", 2)]
    public void The_highest_trump_or_else_the_highest_card_of_the_suit_led_wins(Suit? trump, string cards, int winner)
    {
        var trick = Cards(cards).Select((card, player) => new TrickCard(player, card)).ToList();

        Assert.Equal(winner, TrickCards.Winner(trick, trump));
    }

    [Fact]
    public void The_winner_of_a_trick_leads_the_next_and_the_hand_ends_after_13_tricks()
    {
        var game = new PlainGame(Suit.Spades, null);
        Assert.Equal(1, game.Shuffles);
        // Unshuffled: player 0 holds the clubs, 1 the diamonds, 2 the spades and 3 the hearts.
        Assert.True(game.TryPlay(0, Card("A♣"), out var error), error);
        Assert.True(game.TryPlay(1, Card("2♦"), out error), error);
        Assert.True(game.TryPlay(2, Card("2♠"), out error), error);
        Assert.True(game.TryPlay(3, Card("2♥"), out error), error);

        Assert.Equal((2, 1), (game.Turn, game.TricksWon[2]));
        Assert.Equal(2, game.LastTrick!.Winner);
        Assert.Equal(Cards("A♣ 2♦ 2♠ 2♥"), game.LastTrick.Cards.Select(c => c.Card));
        Assert.Equal(Cards("A♣ 2♦ 2♠ 2♥"), game.Won(2));
        Assert.Empty(game.Trick);
        AssertRefused(game.TryNextHand(1, out error), error, "This hand is not finished yet.");

        PlayOut(game);
        Assert.False(game.IsPlaying);
        Assert.Equal(1, game.HandsPlayed);
        Assert.Equal(13, game.TricksWon.Sum());
        Assert.True(game.TryNextHand(1, out error), error);
        Assert.True(game.TryNextHand(1, out error), error);
        Assert.Equal((2, 13, 2), (game.HandNumber, game.Hand(0).Count, game.Shuffles));
    }

    // Plays the first card each player may play until the hand is over.
    private static void PlayOut(PlainGame game)
    {
        while (game.IsPlaying)
        {
            Assert.True(game.TryPlay(game.Turn, game.Playable(game.Turn)[0], out var error), error);
        }
    }

    // A game with no rules of its own: player 0 leads each hand, and the next can be dealt once the hand is played.
    private sealed class PlainGame : TrickTakingGame
    {
        private readonly Suit? _trump;

        public PlainGame(Suit? trump, Action<Card[]>? shuffle) : base(shuffle ?? (_ => { }))
        {
            _trump = trump;
            StartHand();
        }

        public int HandsPlayed { get; private set; }

        public override bool IsOver => false;

        protected override Suit? Trump => _trump;

        protected override bool IsHandOver => !IsPlaying;

        protected override void OnHandPlayed() => HandsPlayed++;

        protected override void StartHand()
        {
            Deal();
            StartPlay(0);
        }
    }

    private static Action<Card[]> Shuffled(Random random) => random.Shuffle;

    // Puts the given cards (short text, separated by spaces) at the start of each player's 13, in order.
    private static Action<Card[]> Stacked(params string[] hands) => deck =>
    {
        var rest = deck.ToList();
        var chosen = hands.Select(Cards).ToArray();
        foreach (var card in chosen.SelectMany(c => c))
        {
            Assert.True(rest.Remove(card), $"The deck has no more {card}.");
        }

        var dealt = Enumerable.Range(0, 4).SelectMany(p =>
        {
            var own = p < chosen.Length ? chosen[p].ToList() : [];
            while (own.Count < 13)
            {
                own.Add(rest[0]);
                rest.RemoveAt(0);
            }

            return own;
        });
        dealt.ToArray().CopyTo(deck, 0);
    };

    private static Card[] Cards(string cards) => [.. cards.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Card)];

    private static Card Card(string text) => GamesLane.Server.Games.TheGang.Card.Parse(text);

    private static void AssertRefused(bool done, string? error, string expected)
    {
        Assert.False(done);
        Assert.Equal(expected, error);
    }
}
