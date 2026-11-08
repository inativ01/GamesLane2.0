"use strict";

// Yaniv: on their turn, players discard one card, a series or a straight, then take a card from the closed pile or the
// open pile's top card, to lower their hand score; with 7 or less they can call Yaniv instead. Each player's own cards
// come in view.private. The page shows the piles on top, then the user's own hands, then the players' table (players
// can hide it).

const JOKER = "JOKER";
const TABLE_SHOWN_KEY = "yaniv.tableShown";
const POSITIONS = { A: 1, J: 11, Q: 12, K: 13 };
const narrowScreen = matchMedia("(max-width: 600px)");

const isJoker = (card) => card === JOKER;
const rankOf = (card) => card.slice(0, -1);
const suitOf = (card) => card.slice(-1);
const position = (card) => (isJoker(card) ? 0 : POSITIONS[rankOf(card)] ?? Number(rankOf(card)));
const cardText = (card) => (isJoker(card) ? t("Joker") : card);
const cardsText = (cards) => cards.map(cardText).join(" ");

// The cards in the order they go on the open pile, or null if they cannot be discarded together: one card; 2 to 4 of
// the same value (jokers stand for any card), jokers first; or 3 or more in a row of one suit, from lowest to highest,
// spare jokers going below it (or, above an ace, under it), so a joker is never on top (as the server).
function arrange(cards) {
    const others = cards.filter((c) => !isJoker(c));
    const jokers = cards.length - others.length;
    if (cards.length === 0) return null;
    if (cards.length === 1 || (cards.length <= 4 && new Set(others.map(rankOf)).size <= 1)) {
        return [...Array(jokers).fill(JOKER), ...others];
    }
    if (cards.length < 3 || others.length === 0 || new Set(others.map(suitOf)).size !== 1) return null;
    const positions = others.map(position).sort((a, b) => a - b);
    if (new Set(positions).size !== positions.length) return null;
    const high = positions[positions.length - 1];
    const spare = jokers - (high - positions[0] + 1 - positions.length);
    if (spare < 0) return null;
    const down = Math.min(spare, positions[0] - 1);
    const low = positions[0] - down;
    const above = spare - down;
    if (high + above > 13) return null;
    return [...Array(above).fill(JOKER),
        ...Array.from({ length: high - low + 1 }, (_, i) => others.find((c) => position(c) === low + i) ?? JOKER)];
}

// The cards chosen in a hand: the seat, and the positions of the chosen cards in its hand (which comes sorted).
let chosen = { seat: null, cards: "", picks: [] };
let currentGame = null;

const playerName = (state, seat) => state.seats[seat]?.name ?? t("Seat {n}", { n: seat + 1 });
const playerOf = (game, seat) => game.players.find((p) => p.seat === seat);
const myHands = (game, view) => (view.private?.hands ?? []).filter((h) => playerOf(game, h.seat));

// A text about a seat: its "you" form for the viewer (unless they hold several seats), otherwise with its name.
function about(state, view, seat, you, other, values = {}) {
    return view.isMe(seat) && view.mySeats.length === 1 ? t(you, values) : t(other, { name: playerName(state, seat), ...values });
}

function chosenCards(hand) {
    return chosen.seat === hand.seat && chosen.cards === hand.cards.join(",") ? chosen.picks.map((i) => hand.cards[i]) : [];
}

function pickCard(hand, index) {
    const key = hand.cards.join(",");
    if (chosen.seat !== hand.seat || chosen.cards !== key) {
        chosen = { seat: hand.seat, cards: key, picks: [] };
    }
    chosen.picks = chosen.picks.includes(index) ? chosen.picks.filter((i) => i !== index) : [...chosen.picks, index];
    yaniv.refresh();
}

// The hand whose turn it is, if the user holds it.
function handToPlay(game, view) {
    return game.phase === "Playing" ? myHands(game, view).find((h) => h.seat === game.turn) ?? null : null;
}

function send(method, ...args) {
    chosen.picks = [];
    call(method, yaniv.sessionId, ...args).catch(() => {});
}

// The status line: 👉 when the viewer must act (the status bar then turns gold), ⏳ while they wait, 🏁 after a round
// and 🏆 for the winner.
function statusText(game, state, view) {
    if (game.phase === "GameOver") {
        return `🏆 ${about(state, view, game.winner, "You win!", "{name} wins!")}`;
    }
    if (game.phase === "RoundOver") {
        return `🏁 ${roundText(game, state, view)}`;
    }
    const hand = handToPlay(game, view);
    if (!hand) {
        return `⏳ ${t("Waiting for {name}", { name: playerName(state, game.turn) })}`;
    }
    const action = game.caller !== null ? t("Last turn: choose cards to discard, then take a card from a pile.")
        : hand.canCallYaniv ? t("Choose cards to discard, then take a card from a pile, or call Yaniv.")
            : t("Choose cards to discard, then take a card from a pile.");
    return `👉 ${view.mySeats.length > 1 ? `${playerName(state, hand.seat)}: ` : ""}${action}`;
}

// How the last round ended: the caller won it, or was Assaf and the lowest hand won it.
function roundText(game, state, view) {
    const result = game.lastResult;
    const n = game.roundNumber;
    return result.assaf
        ? `${t("Assaf!")} ${about(state, view, result.winnerSeat, "You have the lowest hand and win round {n}.", "{name} has the lowest hand and wins round {n}.", { n })}`
        : about(state, view, result.callerSeat, "You called Yaniv and win round {n}.", "{name} called Yaniv and wins round {n}.", { n });
}

// What happened in the last turn, and the Yaniv call, in sentences.
function messageText(game, state, view) {
    const turn = game.lastTurn;
    const say = (seat, you, other, values) => about(state, view, seat, you, other, values);
    const parts = [];
    if (turn?.skipped) {
        parts.push(say(turn.seat, "Your turn was skipped.", "{name}'s turn was skipped."));
    } else if (turn?.tookOpen) {
        parts.push(say(turn.seat, "You discarded {cards} and took {card}.", "{name} discarded {cards} and took {card}.",
            { cards: cardsText(turn.cards), card: cardText(turn.taken) }));
    } else if (turn) {
        parts.push(say(turn.seat, "You discarded {cards} and took a card from the closed pile.",
            "{name} discarded {cards} and took a card from the closed pile.", { cards: cardsText(turn.cards) }));
    }
    if (game.caller !== null && game.phase === "Playing") {
        parts.push(say(game.caller, "You called Yaniv!", "{name} called Yaniv!"));
    }
    return parts.join(" ");
}

// The piles: the closed pile (face down, with its count), the open pile's top card, and next to it the cards chosen to
// discard. On the user's turn, once the chosen cards can be discarded together, tapping a pile takes its card.
function renderPiles(game, view) {
    const hand = handToPlay(game, view);
    const picks = hand ? chosenCards(hand) : [];
    const ordered = arrange(picks);
    const pile = (face, label, takeOpen, enabled, title) => {
        const button = el("button", "yaniv-pile", face, el("span", "yaniv-pile-label", label));
        button.type = "button";
        button.disabled = !enabled;
        button.title = title;
        button.setAttribute("aria-label", title);
        button.addEventListener("click", () => send("Discard", hand.seat, ordered, takeOpen));
        return button;
    };
    const closed = pile(cardElement(), String(game.closedPile), false, Boolean(ordered),
        t("Closed pile: {n} cards", { n: game.closedPile }));
    const open = pile(game.openTop ? cardElement(game.openTop) : el("span", "card yaniv-no-card"), t("Open pile"), true,
        Boolean(ordered && game.openTop), game.openTop ? t("Take {card}", { card: cardText(game.openTop) }) : t("The open pile is empty."));
    const staged = el("div", "yaniv-staged", ...(ordered ?? picks).map((c) => cardElement(c)));
    staged.hidden = picks.length === 0;
    staged.classList.toggle("invalid", picks.length > 0 && !ordered);
    staged.title = ordered ? t("To discard") : t("These cards cannot be discarded together.");
    $("yaniv-piles").replaceChildren(closed, open, staged);
}

// After a round: every player's hand, hand score and points, with who called Yaniv, Assaf, the winner, the bonus and
// the players who are out.
function renderResult(game, state, view) {
    const result = game.lastResult;
    const shown = game.phase !== "Playing" && result;
    $("yaniv-result").hidden = !shown;
    if (!shown) {
        $("yaniv-result").replaceChildren();
        return;
    }
    $("yaniv-result").replaceChildren(...result.players.map((p) => {
        const tags = el("span", "yaniv-tags");
        const tag = (text, className) => tags.append(el("span", `rp-tag ${className}`, text));
        if (p.seat === result.callerSeat) tag(result.assaf ? t("Assaf!") : t("Yaniv!"), result.assaf ? "assaf" : "yaniv");
        if (p.seat === result.winnerSeat) tag(`🏆 ${t("Winner")}`, "finished");
        if (p.bonus) tag(t("Bonus −50"), "finished");
        if (p.isOut) tag(t("Out"), "poor");
        const points = el("span", "yaniv-points", t("Hand: {n}", { n: p.handScore }), " · ", `+${p.points}`);
        return el("div", "yaniv-result-hand", el("div", "yaniv-result-head", seatLabel(state, p.seat, view), tags, points),
            el("div", "card-row small", ...p.cards.map((c) => cardElement(c))));
    }));
}

// One of the user's hands: its cards (as buttons to choose on its turn), its hand score centred under them, and on its
// turn with 7 or less, the Yaniv! button next to the score.
function handPanel(game, state, view, hand) {
    const myTurn = game.phase === "Playing" && game.turn === hand.seat;
    const picks = chosenCards(hand);
    const slots = hand.cards.map((card, index) => {
        const face = cardElement(card);
        const picked = picks.length > 0 && chosen.picks.includes(index);
        face.classList.toggle("chosen", picked);
        if (!myTurn) {
            return el("span", "rp-slot", face);
        }
        const button = el("button", "rp-card", face);
        button.type = "button";
        button.setAttribute("aria-pressed", String(picked));
        button.addEventListener("click", () => pickCard(hand, index));
        return el("span", "rp-slot", button);
    });
    const title = myHands(game, view).length > 1 ? playerName(state, hand.seat) : t("Your cards");
    const head = el("div", "my-hand-title", title, ...(myTurn ? [el("span", "rp-your-turn", `👉 ${t("Your turn")}`)] : []));
    const foot = el("div", "yaniv-hand-foot", el("p", "yaniv-score", t("Hand score: {n}", { n: hand.handScore })));
    if (myTurn && hand.canCallYaniv) {
        const call = el("button", "yaniv-call", t("Yaniv!"));
        call.type = "button";
        call.addEventListener("click", () => send("CallYaniv", hand.seat));
        foot.append(call);
    }
    return el("div", `my-hand rp-hand yaniv-hand${myTurn ? " my-turn" : ""}`, head, el("div", "card-row rp-cards", ...slots), foot);
}

// A player in the table: name, Yaniv once called, cards in hand and points; players who are out are faded.
function playerItem(game, state, view, player) {
    const start = el("span", "rp-start", seatLabel(state, player.seat, view));
    const end = el("span", "rp-end");
    const item = el("li", null, start, end);
    item.classList.toggle("turn", game.phase === "Playing" && game.turn === player.seat);
    item.classList.toggle("left", player.isOut);
    if (game.caller === player.seat && game.phase === "Playing") start.append(el("span", "rp-tag yaniv", t("Yaniv!")));
    if (player.isOut) start.append(el("span", "rp-tag poor", t("Out")));
    end.append(el("span", "rp-count", `🂠 ${player.cardCount}`), el("strong", "yaniv-player-score", t("{n} points", { n: player.score })));
    return item;
}

// Confetti for the user's success: winning a round or the game.
function noticeSuccess(game, state, view) {
    const winner = game.phase === "GameOver" ? game.winner : game.phase === "RoundOver" ? game.lastResult?.winnerSeat : null;
    if (winner != null && view.isMe(winner)) {
        celebrate(state, `${game.phase}-${game.roundNumber}`);
    }
}

// The players' table: until a player shows or hides it (remembered on this device), hidden on phones and shown on
// computers. Observers always see it.
const tableShown = () => {
    const saved = localStorage.getItem(TABLE_SHOWN_KEY);
    return saved === null ? !narrowScreen.matches : saved === "1";
};

const yaniv = startSessionPage("Yaniv", (game, state, view) => {
    currentGame = game;
    const scored = game.end === "Score";
    showScores(state, view, scored ? [t("Round {n}", { n: game.roundNumber }), t("To {n} points", { n: game.target })] : [],
        scored ? game.players.map((p) => ({ seat: p.seat, score: p.score, acting: game.phase === "Playing" && p.seat === game.turn })) : []);
    $("status").textContent = statusText(game, state, view);
    noticeSuccess(game, state, view);
    const message = messageText(game, state, view);
    $("yaniv-message").textContent = message;
    $("yaniv-message").hidden = !message;
    renderPiles(game, view);
    renderResult(game, state, view);
    $("yaniv-hands").replaceChildren(...myHands(game, view).map((hand) => handPanel(game, state, view, hand)));
    const shown = !view.isPlayer || tableShown();
    $("yaniv-round").textContent = t("Round {n}", { n: game.roundNumber });
    $("yaniv-toggle-table").hidden = !view.isPlayer;
    $("yaniv-toggle-table").textContent = t(shown ? "Hide table" : "Show table");
    $("yaniv-players").hidden = !shown;
    $("yaniv-players").replaceChildren(...game.players.map((p) => playerItem(game, state, view, p)));
    $("next-round").hidden = !(view.isPlayer && game.phase === "RoundOver");
});

$("next-round").addEventListener("click", () => send("NextRound", currentGame?.roundNumber ?? 0));
$("yaniv-toggle-table").addEventListener("click", () => {
    localStorage.setItem(TABLE_SHOWN_KEY, tableShown() ? "0" : "1");
    yaniv.refresh();
});
narrowScreen.addEventListener("change", () => yaniv.refresh());
