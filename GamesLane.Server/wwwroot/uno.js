"use strict";

// Uno: players take turns playing a card that matches the pile's colour, number or symbol, or a wild card; a player who
// cannot (or does not want to) play draws. Each player's own cards come in view.private. The table (the piles, what just
// happened and the players) is on top, and the user's own hands below it.

const COLORS = ["Red", "Yellow", "Green", "Blue"];
const COLOR_LETTERS = { R: "Red", Y: "Yellow", G: "Green", B: "Blue" };
const VALUE_NAMES = { S: "Skip", R: "Reverse", "+2": "Draw Two" };
const SYMBOLS = { S: "⊘", R: "⇄", "+2": "+2", W: "★", "+4": "+4" };

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

// A card: its colour, the symbol in a white oval and in the corners. A wild card on the pile shows the colour chosen.
function unoCard(text, chosenColor = null) {
    const { color, value } = parseCard(text);
    const symbol = SYMBOLS[value] ?? value;
    const card = el("span", `uno-card ${color.toLowerCase()}`,
        el("span", "uno-corner", symbol),
        el("span", "uno-oval", el("span", "uno-symbol", symbol)),
        el("span", "uno-corner end", symbol));
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
    const action = game.pendingDraw > 0 ? t("Stack a draw card, or draw {n} cards", { n: game.pendingDraw })
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
        case "CalledUno":
            return say("You called UNO!", "{name} called UNO!");
        case "Caught": {
            const values = { by: playerName(state, e.bySeat), name: playerName(state, e.seat), n: e.count };
            return view.isMe(e.seat) && view.mySeats.length === 1 ? t("{by} caught you without UNO: you drew {n} cards.", values)
                : view.isMe(e.bySeat) && view.mySeats.length === 1 ? t("You caught {name} without UNO: {name} drew {n} cards.", values)
                    : t("{by} caught {name} without UNO: {name} drew {n} cards.", values);
        }
        case "NewCards":
            return say("You played all your cards and got {n} new ones.", "{name} played all their cards and got {n} new ones.", { n: e.count });
        case "Finished":
            return game.end === "Pyramid" ? say("You finished in place {n}!", "{name} finished in place {n}!", { n: e.count }) : "";
        default:
            return "";
    }
}

const send = (method, ...args) => call(method, uno.sessionId, ...args).catch(() => {});

// The middle of the table: the draw pile (tap it to draw on your turn), the discard pile, the colour to play, the
// direction of play and any cards stacked for the next player.
function renderPile(game, view) {
    const canDraw = game.phase === "Playing" && view.isMe(game.turn) && !game.hasDrawn;
    const draw = el("button", "uno-draw", el("span", "uno-card back"), el("span", "uno-pile-count", String(game.drawPile)));
    draw.type = "button";
    draw.disabled = !canDraw;
    draw.title = t("Draw pile: {n} cards", { n: game.drawPile });
    draw.setAttribute("aria-label", draw.title);
    draw.addEventListener("click", () => send("DrawCard", game.turn));

    const color = el("span", `uno-color ${game.color.toLowerCase()}`, t(game.color));
    color.title = t("Colour to play");
    const direction = el("span", "uno-direction", game.direction === 1 ? "↻" : "↺");
    direction.title = t(game.direction === 1 ? "Play goes in seat order" : "Play goes in reverse seat order");
    const info = el("div", "uno-pile-info", color, direction);
    if (game.pendingDraw > 0) {
        const pending = el("span", "uno-pending", `+${game.pendingDraw}`);
        pending.title = t("Cards to draw: {n}", { n: game.pendingDraw });
        info.append(pending);
    }
    $("uno-pile").replaceChildren(draw, unoCard(game.topCard, game.color), info);
}

// A player in the list: name, cards left, UNO!, their place in a pyramid (or the cards they were last dealt), and Catch!
// for a player who went down to one card without calling UNO.
function playerItem(game, state, view, player) {
    const item = el("li", null, seatLabel(state, player.seat, view));
    item.classList.toggle("turn", game.phase === "Playing" && game.turn === player.seat);
    item.classList.toggle("done", player.finished !== null);
    const tag = (text, className = "") => item.append(el("span", `uno-tag ${className}`, text));
    if (player.saidUno && player.cardCount === 1) tag("UNO!", "uno-said");
    if (player.finished !== null) {
        tag(`🏁 ${player.finished}`, "finished");
    } else if (game.end === "Pyramid") {
        const level = el("span", "uno-tag", `▲ ${player.handSize}`);
        level.title = t("Cards last dealt: {n}", { n: player.handSize });
        item.append(level);
    }
    item.append(el("span", "uno-count", `🂠 ${player.cardCount}`));
    const catcher = view.mySeats.find((seat) => seat !== player.seat && playerOf(game, seat));
    if (player.canBeCaught && catcher !== undefined) {
        const button = el("button", "small uno-catch", t("Catch!"));
        button.type = "button";
        button.title = t("{name} did not call UNO: make them draw 2 cards", { name: playerName(state, player.seat) });
        button.addEventListener("click", () => send("CatchUno", catcher, player.seat));
        item.append(button);
    }
    return item;
}

// The wild card waiting for its colour: the seat and the card.
let picking = null;
let currentGame = null;

// One of the user's hands: its cards (those that can be played are buttons), the colours to choose for a wild card, and
// the buttons to draw, keep the card drawn and call UNO.
function handPanel(game, state, view, hand) {
    const me = playerOf(game, hand.seat);
    const myTurn = game.phase === "Playing" && game.turn === hand.seat;
    const choosing = picking?.seat === hand.seat && hand.playable.includes(picking.card) ? picking.card : null;
    const drawn = myTurn && game.hasDrawn ? hand.playable[0] : null;

    const cards = el("div", "uno-cards", ...hand.cards.map((card) => {
        const face = unoCard(card);
        if (!myTurn) {
            return face;
        }
        if (!hand.playable.includes(card)) {
            face.classList.add("unplayable");
            return face;
        }
        face.classList.toggle("drawn", card === drawn);
        face.classList.toggle("chosen", card === choosing);
        const button = el("button", "uno-play", face);
        button.type = "button";
        button.setAttribute("aria-label", t("Play {card}", { card: cardName(card) }));
        button.addEventListener("click", () => {
            if (parseCard(card).color === "Wild") {
                picking = { seat: hand.seat, card };
                uno.refresh();
                return;
            }
            picking = null;
            send("PlayCard", hand.seat, card, null);
        });
        return button;
    }));

    const title = view.mySeats.length > 1 ? playerName(state, hand.seat) : t("Your cards");
    const panel = el("div", "my-hand uno-hand", el("div", "my-hand-title", title), cards);
    const buttons = el("div", "actions");
    const button = (text, onClick, className = "") => {
        const b = el("button", className, text);
        b.type = "button";
        b.addEventListener("click", onClick);
        buttons.append(b);
    };

    if (choosing) {
        panel.append(el("p", "hint", t("Choose the colour to play:")));
        const colors = el("div", "actions uno-colors", ...COLORS.map((color) => {
            const b = el("button", `uno-color-choice ${color.toLowerCase()}`, t(color));
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
            uno.refresh();
        });
        colors.append(cancel);
        panel.append(colors);
    }

    if (myTurn && !game.hasDrawn) {
        button(game.pendingDraw > 0 ? t("Draw {n} cards", { n: game.pendingDraw }) : t("Draw a card"),
            () => send("DrawCard", hand.seat), "secondary");
    }
    if (drawn) {
        panel.append(el("p", "hint", t("You drew {card}. Play it, or keep it.", { card: cardName(drawn) })));
        button(t("Keep the card"), () => send("PassAfterDraw", hand.seat), "secondary");
    }
    const aboutToHaveOne = myTurn && hand.cards.length === 2 && hand.playable.length > 0 && !me.saidUno;
    if (aboutToHaveOne || me.canBeCaught) {
        button("UNO!", () => send("CallUno", hand.seat), "uno-call");
    }
    if (me.finished !== null) {
        panel.append(el("p", "hint", t("You finished in place {n}!", { n: me.finished })));
    }
    if (buttons.childElementCount) {
        panel.append(buttons);
    }
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

const uno = startSessionPage("Uno", (game, state, view) => {
    currentGame = game;
    if (game.phase !== "Playing") {
        picking = null;
    }
    showScores(state, view, game.end === "Score" ? [t("Hand {n}", { n: game.handNumber }), t("To {n} points", { n: game.target })] : [],
        game.end === "Score" ? game.players.map((p) => ({ seat: p.seat, score: p.score, acting: game.phase === "Playing" && p.seat === game.turn })) : []);
    $("status").textContent = statusText(game, state, view);
    noticeSuccess(game, state, view);
    const message = game.events.map((e) => eventText(game, state, view, e)).filter(Boolean).join(" ");
    $("uno-message").textContent = message;
    $("uno-message").hidden = !message;
    renderPile(game, view);
    $("uno-players").replaceChildren(...game.players.map((p) => playerItem(game, state, view, p)));
    $("uno-hands").replaceChildren(...(view.private?.hands ?? []).filter((h) => playerOf(game, h.seat))
        .map((hand) => handPanel(game, state, view, hand)));
    $("next-round").hidden = !(view.isPlayer && game.phase === "HandOver");
});

$("next-round").addEventListener("click", () => send("NextRound", currentGame?.handNumber ?? 0));
