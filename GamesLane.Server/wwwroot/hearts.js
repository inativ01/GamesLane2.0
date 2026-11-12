"use strict";

// Hearts: before each hand's play, every player passes 3 cards (left, right, across, or every fourth hand not at all);
// then 13 tricks, following suit. Each heart taken is 1 point and the Q♠ 13; the lowest score wins once someone reaches
// 100. Each player's own cards come in view.private. On top, the four players around the trick; below, the user's hands,
// then the players' table (players can hide it).

const PASS_COUNT = 3;
const TABLE_SHOWN_KEY = "hearts.tableShown";
const TABLE_IDS = { head: "hearts-hand-number", toggle: "hearts-toggle-table", body: "hearts-table-body", players: "hearts-players" };
const PASS_TEXTS = {
    Left: "Pass 3 cards to the left, to {name}.",
    Right: "Pass 3 cards to the right, to {name}.",
    Across: "Pass 3 cards across, to {name}.",
};

// The cards chosen to pass in a hand: the seat, its cards when chosen (so a new deal clears them), and the cards.
let chosen = { seat: null, cards: "", picks: [] };
let currentGame = null;

const playerOf = (game, seat) => game.players.find((p) => p.seat === seat);
const myHands = (game, view) => (view.private?.hands ?? []).filter((h) => playerOf(game, h.seat));
const picksOf = (hand) => (chosen.seat === hand.seat && chosen.cards === hand.cards.join(",") ? chosen.picks : []);

function pick(hand, card) {
    const picks = picksOf(hand);
    const next = picks.includes(card) ? picks.filter((c) => c !== card) : picks.length < PASS_COUNT ? [...picks, card] : picks;
    chosen = { seat: hand.seat, cards: hand.cards.join(","), picks: next };
    hearts.refresh();
}

function send(method, ...args) {
    chosen.picks = [];
    call(method, hearts.sessionId, ...args).catch(() => {});
}

// The status line: 👉 when the viewer must act, ⏳ while they wait, 🏁 after a hand and 🏆 for the winners.
function statusText(game, state, view) {
    if (game.phase === "GameOver") {
        return `🏆 ${game.winners.map((seat) => aboutSeat(state, view, seat, "You win!", "{name} wins!")).join(" ")}`;
    }
    if (game.phase === "HandOver") {
        return `🏁 ${t("Hand {n} is over.", { n: game.handNumber })}`;
    }
    const hands = myHands(game, view);
    if (game.phase === "Passing") {
        const hand = hands.find((h) => !h.passed);
        return hand
            ? `👉 ${view.mySeats.length > 1 ? `${trickPlayerName(state, hand.seat)}: ` : ""}${t(PASS_TEXTS[game.pass], { name: trickPlayerName(state, hand.passTo) })}`
            : `⏳ ${t("Waiting for the others to pass their cards.")}`;
    }
    const hand = hands.find((h) => h.seat === game.turn);
    return hand
        ? `👉 ${view.mySeats.length > 1 ? `${trickPlayerName(state, hand.seat)}: ` : ""}${t("Play a card.")}`
        : `⏳ ${t("Waiting for {name}", { name: trickPlayerName(state, game.turn) })}`;
}

// Under the table: who won the last trick, or that the cards are not passed in this hand.
function messageText(game, state, view) {
    if (game.phase === "Playing" && game.pass === "Hold" && game.trick.cards.length === 0 && !game.lastTrick) {
        return t("No passing in this hand.");
    }
    return game.phase === "Playing" ? lastTrickText(game, state, view) : "";
}

// After a hand: each player's points in it and their score, with who shot the moon and the winners.
function renderResult(game, state, view) {
    const result = game.lastResult;
    const shown = game.phase !== "Playing" && game.phase !== "Passing" && result;
    $("hearts-result").hidden = !shown;
    $("hearts-result").replaceChildren(...(shown ? trickResult(result.players.map((p) => ({
        who: [seatLabel(state, p.seat, view)],
        tags: [
            ...(result.moonSeat === p.seat ? [[`🌙 ${t("Shot the moon!")}`, "finished"]] : []),
            ...(game.winners.includes(p.seat) ? [[`🏆 ${t("Winner")}`, "finished"]] : []),
        ],
        points: `+${p.points} · ${t("{n} points", { n: p.score })}`,
    }))) : []));
}

// One of the user's hands: while passing, the cards to choose and Pass; while playing, the cards it may play on its turn.
function handPanel(game, state, view, hand) {
    const passing = game.phase === "Passing" && !hand.passed;
    const myTurn = game.phase === "Playing" && game.turn === hand.seat;
    const picks = picksOf(hand);
    const foot = [];
    if (passing) {
        const pass = trickButton(t("Pass {n} cards", { n: PASS_COUNT }), () => send("PassCards", hand.seat, picks));
        pass.disabled = picks.length !== PASS_COUNT;
        foot.push(pass);
    } else if (game.phase === "Passing") {
        foot.push(el("p", "hint", t("You passed {cards}.", { cards: hand.passed.join(" ") })));
    } else if (hand.received.length > 0 && game.phase === "Playing" && hand.cards.length === 13) {
        foot.push(el("p", "hint", t("You received {cards}.", { cards: hand.received.join(" ") })));
    }
    return trickHand(state, view, hand, {
        myTurn: passing || myTurn,
        active: passing || myTurn,
        canPick: (card) => passing || hand.playable.includes(card),
        chosen: passing ? picks : [],
        onPick: (card) => (passing ? pick(hand, card) : send("PlayTrickCard", hand.seat, card)),
        foot,
    });
}

// A player's facts around the trick: while passing, whether they have passed.
function facts(game, seat) {
    return game.phase === "Passing" && playerOf(game, seat).hasPassed ? [el("span", "rp-tag given", t("Passed"))] : [];
}

// A player in the players' table: name, Passed while passing, the hearts and the Q♠ taken in this hand, and the score.
function playerItem(game, state, view, player) {
    const start = el("span", "rp-start", seatLabel(state, player.seat, view));
    if (game.phase === "Passing" && player.hasPassed) start.append(el("span", "rp-tag given", t("Passed")));
    const end = el("span", "rp-end", el("span", "rp-count", `♥ ${player.heartsTaken}`));
    if (player.tookQueen) end.append(el("span", "rp-tag queen", "Q♠"));
    end.append(el("strong", "trick-player-score", t("{n} points", { n: player.score })));
    const item = el("li", null, start, end);
    item.classList.toggle("turn", game.phase === "Playing" && game.turn === player.seat);
    return item;
}

// Confetti for the user's success: winning the game, or shooting the moon.
function noticeSuccess(game, state, view) {
    if (game.phase === "GameOver" && game.winners.some((seat) => view.isMe(seat))) {
        celebrate(state, "won");
    } else if (game.phase === "HandOver" && game.lastResult?.moonSeat != null && view.isMe(game.lastResult.moonSeat)) {
        celebrate(state, `moon-${game.handNumber}`);
    }
}

const hearts = startSessionPage("Hearts", (game, state, view) => {
    currentGame = game;
    $("status").textContent = statusText(game, state, view);
    noticeSuccess(game, state, view);
    $("hearts-table").replaceChildren(trickTable(game, state, view, {
        turn: game.phase === "Playing" ? game.turn : null,
        facts: (seat) => facts(game, seat),
    }));
    const message = messageText(game, state, view);
    $("hearts-message").textContent = message;
    $("hearts-message").hidden = !message;
    renderResult(game, state, view);
    $("hearts-hands").replaceChildren(...myHands(game, view).map((hand) => handPanel(game, state, view, hand)));
    renderTrickTable(view, TABLE_SHOWN_KEY, TABLE_IDS,
        `${t("Hand {n}", { n: game.handNumber })} · ${t("To {n} points", { n: game.target })}`,
        game.players.map((p) => playerItem(game, state, view, p)));
    $("next-round").hidden = !(view.isPlayer && game.phase === "HandOver");
});

$("next-round").addEventListener("click", () => send("NextRound", currentGame?.handNumber ?? 0));
wireTrickTable(TABLE_SHOWN_KEY, TABLE_IDS.toggle, () => hearts.refresh());
