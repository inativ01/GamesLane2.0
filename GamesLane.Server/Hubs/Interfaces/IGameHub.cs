using GamesLane.Server.Games.FinalCard;
using GamesLane.Server.Games.Wazoo;
using GamesLane.Server.Sessions;

namespace GamesLane.Server.Hubs;

/// <summary>
/// Methods clients can call on the game hub.
/// </summary>
public interface IGameHub
{
    /// <summary>
    /// Opens a session page: subscribes to updates and, for a logged-in user, reconnects them to their seats. A public
    /// game can be opened only by its seated players.
    /// </summary>
    Task<SessionEntry> EnterSession(string sessionId);

    /// <summary>
    /// Logged-in users only. Creates a session and returns its code. The session starts with the creator's saved options
    /// for the game, if they have any. The creator of a private game is not seated; the creator of a public game is.
    /// </summary>
    Task<string> CreateSession(GameType gameType, bool isPublic);

    /// <summary>
    /// Seats the caller in a random public game of <paramref name="gameType"/> that is in its lobby, and returns its code,
    /// or null if there is none. The game starts if this fills its seats.
    /// </summary>
    Task<string?> JoinPublicGame(GameType gameType);

    /// <summary>
    /// The games in progress where the caller holds a seat but is not connected, most recently active first.
    /// </summary>
    Task<IReadOnlyList<WaitingGame>> WaitingGames();

    /// <summary>
    /// Takes the next available seat (in the lobby, the first empty one; during a game, for a user offered a seat, the
    /// first absent seat of the game) as the logged-in user, using their display name. Not in public games, which are
    /// joined from the main screen.
    /// </summary>
    Task TakeSeat(string sessionId);

    /// <summary>
    /// Logged-in users offered a seat when they opened or reconnected to a game in progress (<see cref="SessionEntry.SeatOffer"/>). Takes
    /// the absent game seat <paramref name="seat"/> and continues that player's game.
    /// </summary>
    Task TakeOfferedSeat(string sessionId, int seat);

    /// <summary>
    /// Declines the caller's seat offer: they watch the game until they open it or reconnect again.
    /// </summary>
    Task DeclineSeatOffer(string sessionId);

    /// <summary>
    /// Players in the game only. Skips the turn of <paramref name="seat"/>, an absent seat the game waits for
    /// (<see cref="SessionState.WaitingSeats"/>), in a game that can skip it (<see cref="SessionState.CanSkipWaiting"/>).
    /// </summary>
    Task SkipPlayer(string sessionId, int seat);

    /// <summary>
    /// Gives up one of the caller's seats (used for Leave seat and Stand up).
    /// </summary>
    Task LeaveSeat(string sessionId, int seat);

    /// <summary>
    /// Stops receiving updates and gives up all the caller's seats (used when going back to the main screen).
    /// </summary>
    Task LeaveSession(string sessionId);

    /// <summary>
    /// Seated users only. Starts a new game for everyone seated, replacing game <paramref name="gameNumber"/>
    /// (0 before the first game). If that game has already been replaced, nothing changes.
    /// </summary>
    Task NewGame(string sessionId, int gameNumber);

    /// <summary>
    /// Anyone in the session (in a public game, its creator), in the lobby. Sets the game's options for the next new game;
    /// options left out get their default.
    /// </summary>
    Task SetOptions(string sessionId, Dictionary<string, string>? options);

    /// <summary>
    /// Seated users only. Ends the game in progress and takes everyone back to the session lobby,
    /// so other users can join and a new game can be started.
    /// </summary>
    Task EndGame(string sessionId);

    /// <summary>
    /// Seated users only. After a game has ended, takes everyone back to the session lobby.
    /// </summary>
    Task ReturnToLobby(string sessionId);

    /// <summary>
    /// Logged-in users who have the session open (players and watchers). Sends a chat message of 1 to
    /// <see cref="GameSession.MaxChatLength"/> characters to everyone in the session.
    /// </summary>
    Task SendChatMessage(string sessionId, string text);

    /// <summary>
    /// Tic-tac-toe: places the mark of <paramref name="seat"/>, which the caller must hold, in <paramref name="cell"/> (0-8).
    /// </summary>
    Task MakeMove(string sessionId, int seat, int cell);

    /// <summary>
    /// Tic-tac-toe: starts the round after <paramref name="round"/>; backgammon: the game of the
    /// match after game <paramref name="round"/>; rich man, poor man: the game after game <paramref name="round"/>; Final Card:
    /// the hand after hand <paramref name="round"/>. If it has already started, nothing changes.
    /// </summary>
    Task NextRound(string sessionId, int round);

    /// <summary>
    /// Backgammon: <paramref name="seat"/>, which the caller must hold, rolls the dice.
    /// </summary>
    Task Roll(string sessionId, int seat);

    /// <summary>
    /// Backgammon: <paramref name="seat"/> moves a checker from <paramref name="from"/> to <paramref name="to"/>, in its
    /// own point numbering (1 to 24, 25 for the bar, 0 for off the board).
    /// </summary>
    Task MoveChecker(string sessionId, int seat, int from, int to);

    /// <summary>
    /// Backgammon: <paramref name="seat"/> takes back the last checker it moved in this turn.
    /// </summary>
    Task UndoMove(string sessionId, int seat);

    /// <summary>
    /// Backgammon: <paramref name="seat"/> ends its turn, once it has used as many dice as the rules require.
    /// </summary>
    Task EndTurn(string sessionId, int seat);

    /// <summary>
    /// Backgammon: <paramref name="seat"/> offers to double before rolling.
    /// </summary>
    Task OfferDouble(string sessionId, int seat);

    /// <summary>
    /// Backgammon: <paramref name="seat"/> takes the double; the cube doubles and it owns the cube.
    /// </summary>
    Task TakeDouble(string sessionId, int seat);

    /// <summary>
    /// Backgammon: <paramref name="seat"/> drops the double and loses the game at the cube's value.
    /// </summary>
    Task DropDouble(string sessionId, int seat);

    /// <summary>
    /// Rich man, poor man: <paramref name="seat"/>, which the caller must hold, throws <paramref name="cards"/> (short
    /// text such as "10♥"), all of one number.
    /// </summary>
    Task ThrowCards(string sessionId, int seat, string[]? cards);

    /// <summary>
    /// Rich man, poor man: <paramref name="seat"/>, which the caller must hold, passes its turn.
    /// </summary>
    Task Pass(string sessionId, int seat);

    /// <summary>
    /// Rich man, poor man, in the exchange: <paramref name="seat"/>, which the caller must hold, gives
    /// <paramref name="cards"/> to its exchange partner. The poorer player of a pair must give its highest cards.
    /// </summary>
    Task GiveCards(string sessionId, int seat, string[]? cards);

    /// <summary>
    /// Rich man, poor man, in the exchange: the logged-in caller asks to join the next game.
    /// </summary>
    Task RequestSeat(string sessionId);

    /// <summary>
    /// Rich man, poor man: the caller withdraws their request to join.
    /// </summary>
    Task CancelSeatRequest(string sessionId);

    /// <summary>
    /// Rich man, poor man, in the exchange: a player accepts the request of <paramref name="playerId"/>, who joins at
    /// <paramref name="place"/> (1 for #1); the cards are dealt again for the new number of players.
    /// </summary>
    Task AcceptSeat(string sessionId, string playerId, int place);

    /// <summary>
    /// Rich man, poor man: a player declines the request of <paramref name="playerId"/>.
    /// </summary>
    Task DeclineSeat(string sessionId, string playerId);

    /// <summary>
    /// The Gang: <paramref name="seat"/> takes a chip of the current stage, from the middle or from another player.
    /// </summary>
    Task TakeChip(string sessionId, int seat, int chip);

    /// <summary>
    /// The Gang: <paramref name="seat"/> puts its chip of the current stage back in the middle.
    /// </summary>
    Task ReturnChip(string sessionId, int seat);

    /// <summary>
    /// The Gang: marks <paramref name="seat"/> ready (or not) to end the current stage, together with the caller's other
    /// seats (each user is ready once).
    /// </summary>
    Task SetReady(string sessionId, int seat, bool ready);

    /// <summary>
    /// The Gang, Coordinator: <paramref name="seat"/>, which the caller must hold, picks its card number
    /// <paramref name="cardIndex"/> (from 0) to pass to the next player.
    /// </summary>
    Task PassCard(string sessionId, int seat, int cardIndex);

    /// <summary>
    /// The Gang, Con Artist: <paramref name="seat"/>, which the caller must hold, has seen its cards (and so have the
    /// caller's other seats).
    /// </summary>
    Task ConfirmSeen(string sessionId, int seat);

    /// <summary>
    /// The Gang: <paramref name="seat"/>, which the caller must hold, proposes a choice for the group decision of
    /// <paramref name="card"/>: a seat and/or a value (a card value such as "9", or a hand ranking such as "Pair").
    /// </summary>
    Task Propose(string sessionId, int seat, string card, int? targetSeat, string? value);

    /// <summary>
    /// The Gang: <paramref name="seat"/>, which the caller must hold, agrees (or no longer agrees) to the proposal, for
    /// all the caller's seats that vote (each user votes once).
    /// </summary>
    Task Agree(string sessionId, int seat, string card, bool agree);

    /// <summary>
    /// The Gang, Informant: the chosen <paramref name="seat"/> secretly shows its card number <paramref name="cardIndex"/>
    /// to <paramref name="toSeat"/>.
    /// </summary>
    Task ShowCard(string sessionId, int seat, int cardIndex, int toSeat);

    /// <summary>
    /// The Gang, Hacker and Jack: the chosen <paramref name="seat"/> discards its card number <paramref name="cardIndex"/>.
    /// </summary>
    Task DiscardCard(string sessionId, int seat, int cardIndex);

    /// <summary>
    /// The Gang, with jokers: <paramref name="seat"/>, which the caller must hold, picks card <paramref name="index"/>
    /// (0 or 1) of the two offered for its joker; picking an offered joker swaps it for two more cards.
    /// </summary>
    Task ChooseJokerCard(string sessionId, int seat, int index);

    /// <summary>
    /// The Gang, Motion Detector and Laser Tripwires: <paramref name="seat"/>, which the caller must hold, confirms it
    /// got new pocket cards.
    /// </summary>
    Task ConfirmRedraw(string sessionId, int seat);

    /// <summary>
    /// Wazoo: <paramref name="seat"/>, which the caller must hold, rolls the dice that are not held (all five on the
    /// turn's first roll), up to three times per turn.
    /// </summary>
    Task RollDice(string sessionId, int seat);

    /// <summary>
    /// Wazoo: <paramref name="seat"/>, which the caller must hold, holds (or releases) die <paramref name="die"/>
    /// (0 to 4) between rolls.
    /// </summary>
    Task HoldDie(string sessionId, int seat, int die, bool held);

    /// <summary>
    /// Wazoo: <paramref name="seat"/>, which the caller must hold, scores the dice in <paramref name="category"/>,
    /// which ends its turn.
    /// </summary>
    Task Score(string sessionId, int seat, WazooCategory category);

    /// <summary>
    /// Final Card: <paramref name="seat"/>, which the caller must hold, plays <paramref name="card"/> (short text such as "R7",
    /// "GS", "B+2", "W", "W+4", or in Israeli Style "R+", "RT", "WT", "WC", "W+3" or "W+3X"), choosing
    /// <paramref name="color"/> for a Change Color, a Change Color +4, or a SuperTAKI after a Crown. In Israeli Style, any
    /// seat can play a +3 Breaker on a +3 nobody has answered yet, in or out of turn.
    /// </summary>
    Task PlayCard(string sessionId, int seat, string? card, FinalCardColor? color);

    /// <summary>
    /// Final Card: <paramref name="seat"/>, which the caller must hold, draws its penalty cards (at any time during a hand), or
    /// else on its turn the cards stacked for it, or a card (or, with the option, cards until it can play). If it can play
    /// the card drawn, it plays or keeps it; otherwise its turn ends. In Israeli Style the card drawn is never played on
    /// that turn, and drawing on a +3 gives the seat 3 cards and every other seat but the one of the +3 3 penalty cards.
    /// </summary>
    Task DrawCard(string sessionId, int seat);

    /// <summary>
    /// Final Card, Israeli Style: <paramref name="seat"/>, which the caller must hold and whose turn it is, closes its TAKI:
    /// the action of the TAKI's last card is carried out.
    /// </summary>
    Task CloseTaki(string sessionId, int seat);

    /// <summary>
    /// Final Card: <paramref name="seat"/>, which the caller must hold, keeps the card it drew, which ends its turn.
    /// </summary>
    Task PassAfterDraw(string sessionId, int seat);

    /// <summary>
    /// Final Card, with the challenge option: <paramref name="seat"/>, which the caller must hold and whose turn it is,
    /// challenges the Change Color +4 played on it. If its player held a card of the color that was to play, they have the
    /// cards as penalty cards instead; otherwise <paramref name="seat"/> has them and 2 more, and misses its turn.
    /// </summary>
    Task Challenge(string sessionId, int seat);

    /// <summary>
    /// Final Card: <paramref name="seat"/>, which the caller must hold, calls Final Card, at any time during a hand: it protects
    /// the seat on its final card (or on its turn before playing its second to last), and every other seat holding one card
    /// unprotected has 2 penalty cards to draw (4 in Israeli Style).
    /// </summary>
    Task CallFinalCard(string sessionId, int seat);

    /// <summary>
    /// Yaniv: <paramref name="seat"/>, which the caller must hold, discards <paramref name="cards"/> (short text such as
    /// "10♥" or "JOKER": one card, 2 to 4 of the same value, or 3 or more in a row of one suit) and takes the open pile's
    /// top card (<paramref name="takeOpen"/>) or a card from the closed pile.
    /// </summary>
    Task Discard(string sessionId, int seat, string[]? cards, bool takeOpen);

    /// <summary>
    /// Yaniv: <paramref name="seat"/>, which the caller must hold, calls Yaniv on its turn with a hand score of 7 or less.
    /// </summary>
    Task CallYaniv(string sessionId, int seat);

    /// <summary>
    /// Trick-taking games: <paramref name="seat"/>, which the caller must hold, plays <paramref name="card"/> (short text
    /// such as "10♥") to the trick on its turn, following the suit led if it can.
    /// </summary>
    Task PlayTrickCard(string sessionId, int seat, string? card);

    /// <summary>
    /// Dominoes: <paramref name="seat"/>, which the caller must hold, plays <paramref name="tile"/> (short text such as
    /// "3-5") on its turn: in the line styles at the left end (<paramref name="target"/> 0) or the right end (1); in
    /// Mexican Train on the train of seat <paramref name="target"/>, or on the Mexican train (-1).
    /// </summary>
    Task PlayTile(string sessionId, int seat, string? tile, int target);

    /// <summary>
    /// Dominoes: <paramref name="seat"/>, which the caller must hold and which cannot play, draws a tile from the boneyard.
    /// </summary>
    Task DrawTile(string sessionId, int seat);

    /// <summary>
    /// Dominoes: <paramref name="seat"/>, which the caller must hold and which cannot play or draw, passes (in Mexican
    /// Train, its train gets a marker).
    /// </summary>
    Task PassTurn(string sessionId, int seat);

    /// <summary>
    /// The Gang: reveals the next hand, if <paramref name="revealed"/> hands have been revealed so far.
    /// </summary>
    Task RevealNext(string sessionId, int revealed);

    /// <summary>
    /// The Gang: deals the heist after <paramref name="heist"/>, once its hands are revealed.
    /// </summary>
    Task NextHeist(string sessionId, int heist);
}
