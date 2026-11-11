"use strict";

// Shared by the trick-taking games (loaded after common.js): the table with the four players around the trick, the
// user's hand, and texts about seats. A game's state has `trick` (the trick in progress: { cards: [{ seat, card }] }) and
// `lastTrick` (the last trick completed in this hand, with its `winnerSeat`, or null).

const trickPlayerName = (state, seat) => state.seats[seat]?.name ?? t("Seat {n}", { n: seat + 1 });

// A text about a seat: its "you" form for the viewer (unless they hold several seats), otherwise with its name.
function aboutSeat(state, view, seat, you, other, values = {}) {
    return view.isMe(seat) && view.mySeats.length === 1 ? t(you, values) : t(other, { name: trickPlayerName(state, seat), ...values });
}

// The seats around the table from the viewer's place: their own (first) seat at the bottom (the first seat for
// watchers), then in turn order, clockwise: on the left, at the top and on the right.
function tableSeats(view) {
    const me = view.mySeats[0] ?? 0;
    return [0, 1, 2, 3].map((i) => (me + i) % 4);
}

// The trick on the table: the one being played; between tricks, the last one completed (its winner's card outlined),
// until the next card is played.
function shownTrick(game) {
    return game.trick.cards.length > 0 || !game.lastTrick ? game.trick : game.lastTrick;
}

// Between tricks, who won the last one; otherwise "".
function lastTrickText(game, state, view) {
    return game.trick.cards.length === 0 && game.lastTrick
        ? aboutSeat(state, view, game.lastTrick.winnerSeat, "You won the trick.", "{name} won the trick.")
        : "";
}

// The table, on felt: each player at their place (bottom, left, top, right) with their name, what `facts(seat)` gives
// (e.g. tricks won) and the card they played to the trick shown; the player whose turn it is (`turn`, or null) is
// outlined.
function trickTable(game, state, view, { turn = null, facts = () => [] } = {}) {
    const trick = shownTrick(game);
    const places = ["bottom", "left", "top", "right"];
    return el("div", "trick-table", ...tableSeats(view).map((seat, i) => {
        const played = trick.cards.find((c) => c.seat === seat);
        const card = played ? cardElement(played.card) : el("span", "card trick-no-card");
        card.classList.toggle("won", played !== undefined && trick.winnerSeat === seat);
        const player = el("div", "trick-player", seatLabel(state, seat, view), ...facts(seat));
        player.classList.toggle("turn", turn === seat);
        return el("div", `trick-place ${places[i]}`, player, card);
    }));
}

// One of the user's hands, in a box as in Rich man, poor man that glows while `myTurn`: its title, its cards (sorted by
// the server; while `active`, those `canPick(card)` allows are buttons calling `onPick(card)`, and the others are
// greyed; the `chosen` ones are raised), and under them `foot` (e.g. buttons).
function trickHand(state, view, hand, { title, myTurn = false, active = false, canPick = () => false, chosen = [], onPick = () => {}, foot = [] }) {
    const slots = hand.cards.map((card) => {
        const face = cardElement(card);
        face.classList.toggle("chosen", chosen.includes(card));
        if (!active) {
            return el("span", "rp-slot", face);
        }
        if (!canPick(card)) {
            face.classList.add("unplayable");
            return el("span", "rp-slot", face);
        }
        const button = el("button", "rp-card", face);
        button.type = "button";
        button.setAttribute("aria-pressed", String(chosen.includes(card)));
        button.addEventListener("click", () => onPick(card));
        return el("span", "rp-slot", button);
    });
    const head = el("div", "my-hand-title", title ?? (view.mySeats.length > 1 ? trickPlayerName(state, hand.seat) : t("Your cards")),
        ...(myTurn ? [el("span", "rp-your-turn", `👉 ${t("Your turn")}`)] : []));
    const panel = el("div", `my-hand rp-hand trick-hand${myTurn ? " my-turn" : ""}`, head, el("div", "card-row rp-cards", ...slots));
    if (foot.length > 0) {
        panel.append(el("div", "trick-hand-foot", ...foot));
    }
    return panel;
}

// After a hand: a line per player (or team) with `who` (e.g. seat labels), `tags` ([text, class] pairs such as
// ["🏆 Winner", "finished"]) and `points` (e.g. "+5 · 32").
function trickResult(rows) {
    return rows.map((row) => el("div", "trick-result-row",
        el("span", "trick-result-who", ...row.who),
        el("span", "trick-result-tags", ...(row.tags ?? []).map(([text, className]) => el("span", `rp-tag ${className}`, text))),
        el("span", "trick-result-points", row.points)));
}

// A button for a hand's foot.
function trickButton(text, onClick, className = "") {
    const button = el("button", className, text);
    button.type = "button";
    button.addEventListener("click", onClick);
    return button;
}
