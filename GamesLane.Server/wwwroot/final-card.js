"use strict";

// Final Card: players take turns playing a card that matches the pile's colour, number or symbol, or a wild card; a
// player who cannot (or does not want to) play draws. Each player's own cards come in view.private. The table (the
// piles, what just happened and the players) is on top, and the user's own hands below it.

const COLORS = ["Red", "Yellow", "Green", "Blue"];
const COLOR_LETTERS = { R: "Red", Y: "Yellow", G: "Green", B: "Blue" };
const VALUE_NAMES = { S: "Skip", R: "Reverse", "+2": "Draw Two" };

// Each colour is drawn as the suit of that colour on the GamesLane cards.
const COLOR_SUITS = { R: "♥", Y: "♦", G: "♣", B: "♠" };

const TABLE_SHOWN_KEY = "finalCard.tableShown";
const narrowScreen = matchMedia("(max-width: 600px)");

// The players' table: until a player shows or hides it (remembered on this device), hidden on phones and shown on
// computers. Observers always see it.
const tableShown = () => {
    const saved = localStorage.getItem(TABLE_SHOWN_KEY);
    return saved === null ? !narrowScreen.matches : saved === "1";
};

// A card's colour ("Wild" for wild cards) and value ("0" to "9", "S", "R", "+2", "W" or "+4") from its short text.
function parseCard(text) {
    if (text === "W" || text === "W+4") {
        return { color: "Wild", value: text === "W" ? "W" : "+4" };
    }
    return { color: COLOR_LETTERS[text[0]], value: text.slice(1) };
}

function cardName(text) {
    const { color, value } = parseCard(text);
    if (color === "Wild") {
        return t(value === "W" ? "Wild" : "Wild Draw Four");
    }
    return t("{color} {value}", { color: t(color), value: VALUE_NAMES[value] ? t(VALUE_NAMES[value]) : value });
}

// A card, always a GamesLane card (the classic cards have no Skip, Reverse or wild cards): "R7" is drawn as 7♥, "W" as
// WILD and "W+4" as +4. A wild card on the pile has a ring of the colour chosen.
function finalCard(text, chosenColor = null) {
    const { color, value } = parseCard(text);
    const card = cardElement(color === "Wild" ? (value === "W" ? "WILD" : "+4") : value + COLOR_SUITS[text[0]], "New");
    if (chosenColor && color === "Wild") {
        card.classList.add(`chose-${chosenColor.toLowerCase()}`);
    }
    card.title = cardName(text);
    card.setAttribute("aria-label", card.title);
    return card;
}

const playerName = (state, seat) => state.seats[seat]?.name ?? t("Seat {n}", { n: seat + 1 });
const playerOf = (game, seat) => game.players.find((p) => p.seat === seat);

// A text about a seat: its "you" form for the viewer (unless they hold several seats), otherwise with its name.
function about(state, view, seat, you, other, values = {}) {
    return view.isMe(seat) && view.mySeats.length === 1 ? t(you, values) : t(other, { name: playerName(state, seat), ...values });
}

// The status line: 👉 when the viewer must act (the status bar then turns gold), ⏳ while they wait, 🏁 after a hand and
// 🏆 for the winner.
function statusText(game, state, view) {
    if (game.phase === "GameOver") {
        return game.end === "Score"
            ? `🏆 ${about(state, view, game.winner, "You win with {n} points!", "{name} wins with {n} points!", { n: playerOf(game, game.winner).score })}`
            : `🏆 ${about(state, view, game.winner, "You win!", "{name} wins!")}`;
    }
    if (game.phase === "HandOver") {
        const result = game.lastResult;
        return `🏁 ${about(state, view, result.seat, "You win hand {n} and score {points} points.", "{name} wins hand {n} and scores {points} points.",
            { n: game.handNumber, points: result.points })}`;
    }
    if (!view.isMe(game.turn)) {
        return `⏳ ${t("Waiting for {name}", { name: playerName(state, game.turn) })}`;
    }
    const action = game.canChallenge ? t("Challenge the Wild Draw Four, or draw {n} cards", { n: game.pendingDraw })
        : game.pendingDraw > 0 ? t("Stack a draw card, or draw {n} cards", { n: game.pendingDraw })
        : game.hasDrawn ? t("Play the card you drew, or keep it")
            : t("Play a card, or draw");
    return `👉 ${view.mySeats.length > 1 ? `${playerName(state, game.turn)}: ` : ""}${action}`;
}

// What happened in the last action, in sentences.
function eventText(game, state, view, e) {
    const say = (you, other, values = {}) => about(state, view, e.seat, you, other, values);
    switch (e.kind) {
        case "TurnedUp":
            return t("{card} is turned up.", { card: cardName(e.card) });
        case "Played":
            return e.color
                ? say("You played {card} and chose {color}.", "{name} played {card} and chose {color}.", { card: cardName(e.card), color: t(e.color) })
                : say("You played {card}.", "{name} played {card}.", { card: cardName(e.card) });
        case "Drew":
            return e.count === 0 ? t("There are no cards left to draw.")
                : e.count === 1 ? say("You drew a card.", "{name} drew a card.")
                    : say("You drew {n} cards.", "{name} drew {n} cards.", { n: e.count });
        case "Skipped":
            return say("You miss your turn.", "{name} misses a turn.");
        case "Passed":
            return say("You kept the card.", "{name} kept the card.");
        case "CalledFinalCard":
            return say("You called Final Card!", "{name} called Final Card!");
        case "Caught": {
            const values = { by: playerName(state, e.bySeat), name: playerName(state, e.seat), n: e.count };
            return view.isMe(e.seat) && view.mySeats.length === 1 ? t("{by} caught you without Final Card: you drew {n} cards.", values)
                : view.isMe(e.bySeat) && view.mySeats.length === 1 ? t("You caught {name} without Final Card: {name} drew {n} cards.", values)
                    : t("{by} caught {name} without Final Card: {name} drew {n} cards.", values);
        }
        case "ChallengeWon":
            return say("You challenged the Wild Draw Four and were right: {by} draws {n} cards.",
                "{name} challenged the Wild Draw Four and was right: {by} draws {n} cards.", { by: playerName(state, e.bySeat), n: e.count });
        case "ChallengeLost":
            return say("You challenged the Wild Draw Four and were wrong: you draw {n} cards.",
                "{name} challenged the Wild Draw Four and was wrong: {name} draws {n} cards.", { n: e.count });
        case "NewCards":
            return say("You played all your cards and got {n} new ones.", "{name} played all their cards and got {n} new ones.", { n: e.count });
        case "Finished":
            return game.end === "Pyramid" ? say("You finished in place {n}!", "{name} finished in place {n}!", { n: e.count }) : "";
        default:
            return "";
    }
}

const send = (method, ...args) => call(method, page.sessionId, ...args).catch(() => {});

// The top of the table, for everyone: the closed pile (face down, with its count; tap it to draw on your turn) and the
// open pile on the left; the colour to play, the direction of play and any cards stacked for the next player on the right.
function renderPiles(game, view) {
    const pile = (face, label, title) => {
        const button = el("button", "final-card-pile", face, el("span", "final-card-pile-label", label));
        button.type = "button";
        button.title = title;
        button.setAttribute("aria-label", title);
        return button;
    };
    const closed = pile(cardElement(), String(game.drawPile), t("Draw pile: {n} cards", { n: game.drawPile }));
    closed.disabled = !(game.phase === "Playing" && view.isMe(game.turn) && !game.hasDrawn);
    closed.addEventListener("click", () => send("DrawCard", game.turn));
    const open = pile(finalCard(game.topCard, game.color), t("Open pile"), cardName(game.topCard));
    open.disabled = true;

    const color = el("span", `final-card-color ${game.color.toLowerCase()}`, t(game.color));
    color.title = t("Colour to play");
    const direction = el("span", "final-card-direction", game.direction === 1 ? "↻" : "↺");
    direction.title = t(game.direction === 1 ? "Play goes in seat order" : "Play goes in reverse seat order");
    const info = el("div", "final-card-pile-info", color, direction);
    if (game.pendingDraw > 0) {
        const pending = el("span", "final-card-pending", `+${game.pendingDraw}`);
        pending.title = t("Cards to draw: {n}", { n: game.pendingDraw });
        info.append(pending);
    }
    $("final-card-piles").replaceChildren(el("div", "final-card-pile-cards", closed, open), info);
}

// A player in the table: name, 🛡 while protected (called Final Card on their final card), their place in a pyramid (or
// the cards they were last dealt), the cards they hold, and their points in a game to a score target.
function playerItem(game, state, view, player) {
    const start = el("span", "rp-start", seatLabel(state, player.seat, view));
    const end = el("span", "rp-end");
    const item = el("li", null, start, end);
    item.classList.toggle("turn", game.phase === "Playing" && game.turn === player.seat);
    item.classList.toggle("left", player.finished !== null);
    if (player.protected) {
        const shield = el("span", "rp-tag protected", "🛡");
        shield.title = t("Protected: called Final Card");
        start.append(shield);
    }
    if (player.finished !== null) {
        start.append(el("span", "rp-tag finished", `🏁 ${player.finished}`));
    } else if (game.end === "Pyramid") {
        const level = el("span", "rp-tag", `▲ ${player.handSize}`);
        level.title = t("Cards last dealt: {n}", { n: player.handSize });
        start.append(level);
    }
    end.append(el("span", "rp-count", `🂠 ${player.cardCount}`));
    if (game.end === "Score") {
        end.append(el("strong", "final-card-score", t("{n} points", { n: player.score })));
    }
    return item;
}

// The wild card waiting for its colour: the seat and the card.
let picking = null;
let currentGame = null;

// One of the user's hands, in a box that glows on its turn: its cards (those that can be played are buttons), the colours
// to choose for a wild card, and under them Draw (always shown, enabled on its turn) and Keep the card on the left, and
// Final Card! on the right (always shown during a hand).
function handPanel(game, state, view, hand) {
    const me = playerOf(game, hand.seat);
    const myTurn = game.phase === "Playing" && game.turn === hand.seat;
    const choosing = picking?.seat === hand.seat && hand.playable.includes(picking.card) ? picking.card : null;
    const drawn = myTurn && game.hasDrawn ? hand.playable[0] : null;

    const slots = hand.cards.map((card) => {
        const face = finalCard(card);
        if (!myTurn) {
            return el("span", "rp-slot", face);
        }
        if (!hand.playable.includes(card)) {
            face.classList.add("unplayable");
            return el("span", "rp-slot", face);
        }
        face.classList.toggle("drawn", card === drawn);
        face.classList.toggle("chosen", card === choosing);
        const button = el("button", "rp-card", face);
        button.type = "button";
        button.setAttribute("aria-label", t("Play {card}", { card: cardName(card) }));
        button.addEventListener("click", () => {
            if (parseCard(card).color === "Wild") {
                picking = { seat: hand.seat, card };
                page.refresh();
                return;
            }
            picking = null;
            send("PlayCard", hand.seat, card, null);
        });
        return el("span", "rp-slot", button);
    });

    const title = view.mySeats.length > 1 ? playerName(state, hand.seat) : t("Your cards");
    const head = el("div", "my-hand-title", title, ...(myTurn ? [el("span", "rp-your-turn", `👉 ${t("Your turn")}`)] : []));
    const panel = el("div", `my-hand rp-hand final-card-hand${myTurn ? " my-turn" : ""}`, head, el("div", "card-row rp-cards", ...slots));
    const buttons = el("div", "actions final-card-actions");
    const button = (text, onClick, className = "") => {
        const b = el("button", className, text);
        b.type = "button";
        b.addEventListener("click", onClick);
        buttons.append(b);
        return b;
    };

    if (choosing) {
        panel.append(el("p", "hint", t("Choose the colour to play:")));
        const colors = el("div", "actions final-card-colors", ...COLORS.map((color) => {
            const b = el("button", `final-card-color-choice ${color.toLowerCase()}`, t(color));
            b.type = "button";
            b.addEventListener("click", () => {
                picking = null;
                send("PlayCard", hand.seat, choosing, color);
            });
            return b;
        }));
        const cancel = el("button", "secondary", t("Cancel"));
        cancel.type = "button";
        cancel.addEventListener("click", () => {
            picking = null;
            page.refresh();
        });
        colors.append(cancel);
        panel.append(colors);
    }

    if (drawn) {
        panel.append(el("p", "hint", t("You drew {card}. Play it, or keep it.", { card: cardName(drawn) })));
    }
    if (me.finished !== null) {
        panel.append(el("p", "hint", t("You finished in place {n}!", { n: me.finished })));
    }
    const draw = button(myTurn && game.pendingDraw > 0 ? t("Draw {n} cards", { n: game.pendingDraw }) : t("Draw a card"),
        () => send("DrawCard", hand.seat), "secondary");
    draw.disabled = !myTurn || game.hasDrawn;
    if (drawn) {
        button(t("Keep the card"), () => send("PassAfterDraw", hand.seat), "secondary");
    }
    if (myTurn && game.canChallenge) {
        const challenge = button(t("Challenge!"), () => send("Challenge", hand.seat), "final-card-challenge");
        challenge.title = t("Did they have a card of the colour? If so, they draw the cards; if not, you draw 2 more.");
    }
    const call = button(t("Final Card!"), () => send("CallFinalCard", hand.seat), "final-card-call");
    call.disabled = game.phase !== "Playing";
    panel.append(buttons);
    return panel;
}

// Confetti for the user's success: winning a hand or the game, or emptying their hand in a pyramid.
function noticeSuccess(game, state, view) {
    const winner = game.phase === "GameOver" ? game.winner : game.phase === "HandOver" ? game.lastResult?.seat : null;
    if (winner != null && view.isMe(winner)) {
        celebrate(state, `${game.phase}-${game.handNumber}`);
    }
    game.players.filter((p) => p.finished !== null && view.isMe(p.seat))
        .forEach((p) => celebrate(state, `finished-${game.handNumber}-${p.seat}`));
}

const page = startSessionPage("FinalCard", (game, state, view) => {
    currentGame = game;
    if (game.phase !== "Playing") {
        picking = null;
    }
    showScores(state, view, game.end === "Score" ? [t("Hand {n}", { n: game.handNumber }), t("To {n} points", { n: game.target })] : [],
        game.end === "Score" ? game.players.map((p) => ({ seat: p.seat, score: p.score, acting: game.phase === "Playing" && p.seat === game.turn })) : []);
    $("status").textContent = statusText(game, state, view);
    noticeSuccess(game, state, view);
    const message = game.events.map((e) => eventText(game, state, view, e)).filter(Boolean).join(" ");
    $("final-card-message").textContent = message;
    $("final-card-message").hidden = !message;
    renderPiles(game, view);
    $("final-card-hands").replaceChildren(...(view.private?.hands ?? []).filter((h) => playerOf(game, h.seat))
        .map((hand) => handPanel(game, state, view, hand)));
    const shown = !view.isPlayer || tableShown();
    $("final-card-hand-number").textContent = t("Hand {n}", { n: game.handNumber });
    $("final-card-toggle-table").hidden = !view.isPlayer;
    $("final-card-toggle-table").textContent = t(shown ? "Hide table" : "Show table");
    $("final-card-players").hidden = !shown;
    $("final-card-players").replaceChildren(...game.players.map((p) => playerItem(game, state, view, p)));
    $("final-card-options").hidden = !shown;
    $("final-card-options").textContent = `${t("Game options:")} ${optionsSummary(state)}`;
    $("next-round").hidden = !(view.isPlayer && game.phase === "HandOver");
});

$("next-round").addEventListener("click", () => send("NextRound", currentGame?.handNumber ?? 0));
$("final-card-toggle-table").addEventListener("click", () => {
    localStorage.setItem(TABLE_SHOWN_KEY, tableShown() ? "0" : "1");
    page.refresh();
});
narrowScreen.addEventListener("change", () => page.refresh());
