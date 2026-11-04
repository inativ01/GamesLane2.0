using GamesLane.Server.Games.RichManPoorMan;
using GamesLane.Server.Games.TheGang;

namespace GamesLane.Server.Tests;

public class RichManPoorManGameTests
{
    private static readonly int[] Six = [0, 1, 2, 3, 4, 5];

    // The deck and the deal

    [Fact]
    public void The_deck_has_one_set_of_2_to_A_per_player_with_the_suits_in_turn()
    {
        var deck = RichManPoorManRules.Deck(6);

        Assert.Equal(78, deck.Length);
        Assert.All(deck.GroupBy(c => c.Rank), g => Assert.Equal(6, g.Count()));
        Assert.Equal([Suit.Spades, Suit.Hearts, Suit.Diamonds, Suit.Clubs, Suit.Spades, Suit.Hearts],
            deck.Where(c => c.Rank == 7).Select(c => c.Suit));
    }

    [Fact]
    public void Every_player_gets_13_cards_sorted_and_the_player_with_the_most_2s_starts()
    {
        var game = new RichManPoorManGame([3, 7, 1, 9, 0, 4], RankSorted);

        Assert.All(game.Order, p => Assert.Equal(13, game.Hand(p).Count));
        Assert.Equal(["2♠", "2♠", "2♥", "2♥", "2♦", "2♣", "3♠"], Text(game.Hand(3).Take(7)));
        Assert.Equal((3, RichManPoorManPhase.Playing), (game.Turn, game.Phase));
    }

    // Rounds

    [Fact]
    public void Play_goes_from_1_to_the_last_then_one_place_richer_and_aces_win_the_round_at_once()
    {
        var game = new RichManPoorManGame(Six, RankSorted);

        Throw(game, 0, "2S");
        Assert.Equal(5, game.Turn);
        Throw(game, 5, "AS");
        Assert.Null(game.CurrentThrow);
        Assert.Equal(5, game.Turn);
        Assert.Equal(5, game.LastRoundWinner);
        Throw(game, 5, "KS");
        Assert.Equal(4, game.Turn);
    }

    [Fact]
    public void A_throw_must_be_as_many_cards_of_one_strictly_higher_number_held_in_hand()
    {
        var game = new RichManPoorManGame(Six, RankSorted);
        Throw(game, 0, "2S", "2H");

        Assert.Equal("It is not your turn.", Refused(game, 4, "QS", "QH"));
        Assert.Equal("Throw exactly 2 cards.", Refused(game, 5, "KS"));
        Assert.Equal("Throw cards of one number only.", Refused(game, 5, "KS", "AS"));
        Assert.Equal("You do not have those cards.", Refused(game, 5, "2S", "2D"));
        Throw(game, 5, "KS", "KH");
        Assert.Equal("Throw a higher number than the cards on the table.", Refused(game, 4, "QS", "QH"));
        Assert.Equal(11, game.Hand(5).Count);
    }

    [Fact]
    public void The_round_ends_when_everyone_else_passed_since_the_last_throw_and_its_winner_starts_the_next()
    {
        var game = new RichManPoorManGame(Six, RankSorted);
        Throw(game, 0, "2S");
        Throw(game, 5, "KS");
        foreach (var player in new[] { 4, 3, 2, 1 })
        {
            Assert.True(game.TryPass(player, out var error), error);
            Assert.True(game.HasPassed(player));
        }

        Assert.Equal(0, game.Turn);
        Pass(game, 0);

        Assert.Null(game.CurrentThrow);
        Assert.Equal(5, game.Turn);
        Assert.Equal(5, game.LastRoundWinner);
        Assert.False(game.HasPassed(4));
        Assert.False(game.TryPass(5, out var startError));
        Assert.Equal("You start this round: throw cards of one number.", startError);
    }

    [Fact]
    public void A_player_who_empties_their_hand_is_ranked_and_skipped_and_the_next_player_starts_in_their_place()
    {
        var game = new RichManPoorManGame(Six, RankSorted);
        Throw(game, 0, "2S", "2H", "2D", "2C", "2S", "2H");
        Pass(game, 5, 4, 3, 2, 1);
        Throw(game, 0, "3S", "3H", "3D", "3C", "3S", "3H");
        Pass(game, 5, 4, 3, 2, 1);
        Throw(game, 0, "4S");

        Assert.Equal([0], game.Ranking);
        Assert.Equal(5, game.Turn);
        Pass(game, 5, 4, 3, 2, 1);
        Assert.Equal(0, game.LastRoundWinner);
        Assert.Equal(5, game.Turn);
        Throw(game, 5, "QH");
        Assert.Equal(4, game.Turn);
        Pass(game, 4, 3, 2, 1);
        Assert.Equal(5, game.Turn);
    }

    // Players who leave

    [Fact]
    public void A_player_who_left_passes_automatically_and_if_they_are_to_start_the_next_active_player_starts()
    {
        var game = new RichManPoorManGame(Six, RankSorted);
        Throw(game, 0, "2S");
        game.SetActive(5, false);

        Assert.Equal(4, game.Turn);
        Assert.False(game.HasPassed(5));
        Pass(game, 4, 3, 2, 1);
        Assert.Equal(0, game.Turn);

        game.SetActive(0, false);
        Assert.Equal(4, game.Turn);
        Throw(game, 4, "10S");
        Assert.Equal(3, game.Turn);
        game.SetActive(5, true);
        Pass(game, 3, 2, 1);
        Assert.Equal(5, game.Turn);
    }

    [Fact]
    public void When_everyone_leaves_the_game_waits_and_goes_on_when_a_player_comes_back()
    {
        var game = new RichManPoorManGame(Six, RankSorted);
        Throw(game, 0, "2S");
        foreach (var player in Six)
        {
            game.SetActive(player, false);
        }

        Assert.Equal(RichManPoorManPhase.Playing, game.Phase);
        Assert.Empty(game.Ranking);

        game.SetActive(3, true);
        Assert.Equal(RichManPoorManPhase.Playing, game.Phase);
        Assert.Equal(3, game.Turn);
        Assert.True(game.TryThrow(3, Cards(game.Hand(3)[0].ToString()), out var error), error);
    }

    [Fact]
    public void The_game_ends_once_every_active_player_has_emptied_their_hand_and_leavers_get_no_place()
    {
        var game = FinishedBySeat0();

        Assert.Equal(RichManPoorManPhase.GameOver, game.Phase);
        Assert.Equal([0], game.Ranking);
        Assert.Equal(13, game.Hand(3).Count);
        Assert.Equal([0, 1, 2, 3, 4, 5], game.NextOrder);
        Assert.Equal("The game is over.", Refused(game, 0, "2S"));
    }

    // The exchange

    [Fact]
    public void The_next_game_seats_the_players_in_the_given_order_and_the_pairs_exchange_by_the_table()
    {
        var game = Exchanging([0, 5, 4, 3, 2, 1]);

        Assert.Equal((2, RichManPoorManPhase.Exchanging), (game.GameNumber, game.Phase));
        Assert.Equal([0, 5, 4, 3, 2, 1], game.Order);
        Assert.Equal([3, 2, 1, 1, 2, 3], game.Order.Select(game.ExchangeCount));
        Assert.Equal([1, 2, 3, 4, 5, 0], game.Order.Select(p => game.ExchangePartner(p)!.Value));
        Assert.Equal("Wait until the cards have been exchanged.", Refused(game, 1, "AS"));
    }

    [Fact]
    public void The_poorer_player_must_give_their_highest_cards_and_nobody_gets_cards_until_every_pair_has_given()
    {
        var game = Exchanging([0, 5, 4, 3, 2, 1]);
        Assert.Equal([14, 14, 14], game.RequiredGift(1)!.Select(c => c.Rank));
        Assert.Null(game.RequiredGift(0));

        Assert.False(game.TryGive(1, Cards("QH", "AS", "AH"), out var error));
        Assert.Equal("You must give your highest cards.", error);
        Assert.False(game.TryGive(0, Cards("2S"), out error));
        Assert.Equal("Give exactly 3 cards.", error);
        Give(game, 1, "AS", "AH", "AD");
        Give(game, 0, "2S", "2H", "2D");
        Assert.Equal(13, game.Hand(0).Count(c => c.Rank <= 4));
        Assert.Equal("You have already given your cards.", GiveError(game, 0, "2C", "2S", "2H"));

        foreach (var player in new[] { 5, 4, 3, 2 })
        {
            var gift = game.RequiredGift(player) ?? [.. game.Hand(player).Take(game.ExchangeCount(player))];
            Assert.True(game.TryGive(player, gift, out var giveError), giveError);
        }

        Assert.Equal((RichManPoorManPhase.Playing, 1), (game.Phase, game.Turn));
        Assert.Equal(["2♠", "2♥", "2♦"], Text(game.Received(1)!));
        Assert.Equal(3, game.Hand(0).Count(c => c.Rank == 14));
        Assert.All(game.Order, p => Assert.Equal(13, game.Hand(p).Count));
    }

    [Fact]
    public void A_player_added_or_dropped_in_the_exchange_changes_the_deck_by_one_set_and_every_pair_gives_again()
    {
        var game = Exchanging(Six);
        Give(game, 0, "2S", "2H", "2D");

        Assert.True(game.TryAddPlayer(9, 0, out var error), error);
        Assert.Equal([9, 0, 1, 2, 3, 4, 5], game.Order);
        Assert.All(game.Order.SelectMany(game.Hand).GroupBy(c => c.Rank), g => Assert.Equal(7, g.Count()));
        Assert.Null(game.Gift(0));
        Assert.Equal(0, game.ExchangeCount(2));

        Assert.True(game.TryRemovePlayer(9, out error), error);
        Assert.Equal(78, game.Order.Sum(p => game.Hand(p).Count));
        Assert.False(game.TryRemovePlayer(3, out error));
        Assert.Equal("At least 6 players are needed for the exchange.", error);
        Assert.False(game.TryAddPlayer(0, 0, out error));
        Assert.Equal("That player is already playing.", error);
    }

    [Fact]
    public void Starting_the_next_game_again_or_too_early_changes_nothing()
    {
        var game = new RichManPoorManGame(Six, RankSorted);
        Assert.False(game.TryNextGame(1, Six, out var error));
        Assert.Equal("This game is not over yet.", error);

        var exchanging = Exchanging(Six);
        Assert.True(exchanging.TryNextGame(1, [0, 1, 2, 3, 4, 5], out _));
        Assert.Equal(2, exchanging.GameNumber);
    }

    // Seat 0 (#1, with the lowest cards) is the only active player and empties their hand.
    private static RichManPoorManGame FinishedBySeat0()
    {
        var game = new RichManPoorManGame(Six, RankSorted);
        foreach (var player in Six.Skip(1))
        {
            game.SetActive(player, false);
        }

        Throw(game, 0, "2S", "2H", "2D", "2C", "2S", "2H");
        Throw(game, 0, "3S", "3H", "3D", "3C", "3S", "3H");
        Throw(game, 0, "4S");
        return game;
    }

    private static RichManPoorManGame Exchanging(int[] order)
    {
        var game = FinishedBySeat0();
        Assert.True(game.TryNextGame(1, order, out var error), error);
        return game;
    }

    // Deals the cards from low to high: #1 gets the 13 lowest, #2 the next 13, and so on.
    private static void RankSorted(Card[] deck) => deck.OrderBy(c => c.Rank).ToArray().CopyTo(deck, 0);

    private static Card[] Cards(params string[] cards) => [.. cards.Select(Card.Parse)];

    private static string[] Text(IEnumerable<Card> cards) => [.. cards.Select(c => c.ToString())];

    private static void Throw(RichManPoorManGame game, int player, params string[] cards) =>
        Assert.True(game.TryThrow(player, Cards(cards), out var error), error);

    private static string? Refused(RichManPoorManGame game, int player, params string[] cards)
    {
        Assert.False(game.TryThrow(player, Cards(cards), out var error));
        return error;
    }

    private static void Pass(RichManPoorManGame game, params int[] players)
    {
        foreach (var player in players)
        {
            Assert.True(game.TryPass(player, out var error), error);
        }
    }

    private static void Give(RichManPoorManGame game, int player, params string[] cards) =>
        Assert.True(game.TryGive(player, Cards(cards), out var error), error);

    private static string? GiveError(RichManPoorManGame game, int player, params string[] cards)
    {
        Assert.False(game.TryGive(player, Cards(cards), out var error));
        return error;
    }
}