"use strict";

// Spades: 4 players in 2 teams of partners sitting across. Each hand, starting on the dealer's left, every player bids
// the tricks they expect to take (or Nil, or Blind Nil before looking at their cards, with the options); then 13 tricks,
// following suit, spades trumps. A team that makes its bid scores 10 a trick bid and 1 a bag; 10 bags cost 100. The first
// team to 500 wins. Each player's own cards come in view.private. On top, the four players around the trick (the
// partner at the top); below, the user's hands with the bid, then the teams' table (players can hide it).

const TABLE_SHOWN_KEY = "spades.tableShown";
const TABLE_IDS = { head: "spades-hand-number", toggle: "spades-toggle-table", body: "spades-table-body", players: "spades-teams" };

// The bid chosen in a hand's box before pressing Bid, by seat.
const chosenBids = {};
let currentGame = null;

const playerOf = (game, seat) => game.players.find((p) => p.seat === seat);
const myHands = (game, view) => (view.private?.hands ?? []).filter((h) => playerOf(game, h.seat));
const teamNames = (state, team) => team.seats.map((seat) => trickPlayerName(state, seat)).join(" & ");
const myTeam = (game, view) => (view.mySeats.length > 0 ? playerOf(game, view.mySeats[0])?.team ?? null : null);

function send(method, ...args) {
    call(method, spades.sessionId, ...args).catch(() => {});
}

// A bid as text: "Nil", "Blind Nil" or the number of tricks.
const bidText = (player) => (player.bid === 0 ? t(player.blind ? "Blind Nil" : "Nil") : String(player.bid));

// The status line: 👉 when the viewer must act, ⏳ while they wait, 🏁 after a hand and 🏆 for the winners.
function statusText(game, state, view) {
    if (game.phase === "GameOver") {
        return `🏆 ${myTeam(game, view) === game.winner ? t("Your team wins!") : t("{name} win!", { name: teamNames(state, game.teams[game.winner]) })}`;
    }
    if (game.phase === "HandOver") {
        return `🏁 ${t("Hand {n} is over.", { n: game.handNumber })}`;
    }
    const prefix = (hand) => (view.mySeats.length > 1 ? `${trickPlayerName(state, hand.seat)}: ` : "");
    if (game.phase === "Bidding") {
        const hand = myHands(game, view).find((h) => h.seat === game.bidder);
        return hand
            ? `👉 ${prefix(hand)}${t(hand.looked ? "Bid the tricks you expect to take." : "Bid Blind Nil, or look at your cards.")}`
            : `⏳ ${t("Waiting for {name} to bid.", { name: trickPlayerName(state, game.bidder) })}`;
    }
    const hand = myHands(game, view).find((h) => h.seat === game.turn);
    return hand ? `👉 ${prefix(hand)}${t("Play a card.")}` : `⏳ ${t("Waiting for {name}", { name: trickPlayerName(state, game.turn) })}`;
}

// After a hand: each team's bid and tricks, the points it gained, its score and bags, with its Nil bids and any bag penalty.
function renderResult(game, state) {
    const result = game.lastResult;
    const shown = (game.phase === "HandOver" || game.phase === "GameOver") && result;
    $("spades-result").hidden = !shown;
    $("spades-result").replaceChildren(...(shown ? trickResult(result.teams.map((team) => ({
        who: [teamNames(state, game.teams[team.team])],
        tags: [
            [t(team.made ? "Bid made" : "Bid missed"), team.made ? "finished" : "poor"],
            ...result.nils.filter((nil) => playerOf(game, nil.seat).team === team.team).map((nil) => [
                t(nil.blind ? (nil.made ? "{name}: Blind Nil made" : "{name}: Blind Nil missed") : (nil.made ? "{name}: Nil made" : "{name}: Nil missed"),
                    { name: trickPlayerName(state, nil.seat) }), nil.made ? "finished" : "poor"]),
            ...(team.bagPenalty ? [[t("10 bags: −100"), "poor"]] : []),
            ...(game.winner === team.team ? [[`🏆 ${t("Winner")}`, "finished"]] : []),
        ],
        points: `${t("Bid {bid}, took {tricks}", { bid: team.bid, tricks: team.tricks })} · ${team.points >= 0 ? "+" : ""}${team.points} · ${t("{n} points", { n: team.score })}`,
    }))) : []));
}

// The bid under a hand, on its turn to bid: the number of tricks (and Nil with the option) and Bid; before looking at the
// cards with Blind Nil, Blind Nil and Show my cards instead.
function bidControls(game, hand) {
    if (!hand.looked) {
        return [
            trickButton(t("Blind Nil"), () => send("Bid", hand.seat, 0, true), "secondary"),
            trickButton(t("Show my cards"), () => send("LookAtCards", hand.seat)),
        ];
    }
    const select = el("select", "spades-bid-select");
    select.setAttribute("aria-label", t("Your bid"));
    const values = [...(game.nil ? [0] : []), ...Array.from({ length: 13 }, (_, i) => i + 1)];
    select.append(...values.map((value) => Object.assign(document.createElement("option"), {
        value: String(value), textContent: value === 0 ? t("Nil") : String(value),
    })));
    select.value = String(chosenBids[hand.seat] ?? (values.includes(3) ? 3 : values[0]));
    select.addEventListener("change", () => { chosenBids[hand.seat] = Number(select.value); });
    return [el("label", "spades-bid", t("Your bid"), select), trickButton(t("Bid"), () => send("Bid", hand.seat, Number(select.value), false))];
}

// One of the user's hands: face down until it looks at them (with Blind Nil); the bid on its turn to bid; the cards it
// may play on its turn, and Claim all when its cards win every remaining trick (it can still play them one by one).
function handPanel(game, state, view, hand) {
    const bidding = game.phase === "Bidding" && game.bidder === hand.seat;
    const myTurn = game.phase === "Playing" && game.turn === hand.seat;
    const shown = hand.looked ? hand : { ...hand, cards: Array(playerOf(game, hand.seat).cardCount).fill(null) };
    return trickHand(state, view, shown, {
        myTurn: bidding || myTurn,
        active: myTurn,
        canPick: (card) => hand.playable.includes(card),
        onPick: (card) => send("PlayTrickCard", hand.seat, card),
        foot: bidding ? bidControls(game, hand)
            : game.phase === "Bidding" && !hand.looked ? [trickButton(t("Show my cards"), () => send("LookAtCards", hand.seat), "secondary")]
                : myTurn && hand.canClaim ? [trickButton(t("Claim all"), () => send("ClaimTricks", hand.seat))]
                    : [],
    });
}

// A team in the teams' table: its players, its bid (the sum of its non-Nil bids, … until both have bid), the tricks
// it has taken in this hand, its score and its bags; the team to bid or play is outlined.
function teamItem(game, state, team) {
    const players = team.seats.map((seat) => playerOf(game, seat));
    const bid = players.some((p) => p.bid === null) ? "…" : String(players.reduce((sum, p) => sum + p.bid, 0));
    const start = el("span", "rp-start", el("span", "seat-label", teamNames(state, team)));
    const end = el("span", "rp-end",
        el("span", "rp-tag call", t("Bid: {bid}", { bid })),
        ...(game.phase === "Bidding" ? [] : [el("span", "rp-count", t("Tricks: {n}", { n: players.reduce((sum, p) => sum + p.tricks, 0) }))]),
        el("span", "rp-count", t("Bags: {n}", { n: team.bags })),
        el("strong", "trick-player-score", t("{n} points", { n: team.score })));
    const acting = game.phase === "Playing" ? game.turn : game.phase === "Bidding" ? game.bidder : null;
    const item = el("li", null, start, end);
    item.classList.toggle("turn", team.seats.includes(acting));
    return item;
}

// A player's facts at the table: dealer, bid, and the tricks taken.
function facts(game, seat) {
    const player = playerOf(game, seat);
    const items = [];
    if (game.dealer === seat && game.phase === "Bidding") items.push(el("span", "rp-tag", t("Dealer")));
    items.push(el("span", "rp-tag call", player.bid === null ? "…" : t("Bid: {bid}", { bid: bidText(player) })));
    if (game.phase !== "Bidding") items.push(el("span", "rp-tag", t("Tricks: {n}", { n: player.tricks })));
    return items;
}

// Confetti for the user's success: their team winning the game.
function noticeSuccess(game, state, view) {
    if (game.phase === "GameOver" && myTeam(game, view) === game.winner) {
        celebrate(state, "won");
    }
}

const spades = startSessionPage("Spades", (game, state, view) => {
    currentGame = game;
    $("status").textContent = statusText(game, state, view);
    noticeSuccess(game, state, view);
    $("spades-table").replaceChildren(trickTable(game, state, view, {
        turn: game.phase === "Playing" ? game.turn : game.phase === "Bidding" ? game.bidder : null,
        facts: (seat) => facts(game, seat),
    }));
    const message = game.claim ? claimText(game, state, view) : game.phase === "Playing" ? lastTrickText(game, state, view) : "";
    $("spades-message").textContent = message;
    $("spades-message").hidden = !message;
    $("spades-claim").replaceChildren(...claimHands(game, state, view));
    $("spades-claim").hidden = !game.claim;
    renderResult(game, state);
    $("spades-hands").replaceChildren(...myHands(game, view).map((hand) => handPanel(game, state, view, hand)));
    renderTrickTable(view, TABLE_SHOWN_KEY, TABLE_IDS,
        `${t("Hand {n}", { n: game.handNumber })} · ${t("To {n} points", { n: game.target })}`,
        game.teams.map((team) => teamItem(game, state, team)));
    $("next-round").hidden = !(view.isPlayer && game.phase === "HandOver");
});

$("next-round").addEventListener("click", () => send("NextRound", currentGame?.handNumber ?? 0));
wireTrickTable(TABLE_SHOWN_KEY, TABLE_IDS.toggle, () => spades.refresh());
