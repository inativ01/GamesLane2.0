"use strict";

// Rich man, poor man: players throw cards of one number, each throw beating the last with as many cards of a higher
// number, and the first to empty their hand is the next game's rich man. Each player's own cards come in view.private.
// The page shows the user's own hands on top, and the table (everything observers see) below. On a shared screen, the
// throws can be read aloud.

const FACE_RANKS = { J: 11, Q: 12, K: 13, A: 14 };
const rankOf = (card) => FACE_RANKS[card.slice(0, -1)] ?? Number(card.slice(0, -1));
const SPEECH_KEY = "richManPoorMan.speech";

// The hint to beat the throw on the table, by its number of cards: its group up to 4 cards, then the number.
const BEAT_HINTS = {
    1: "Throw a single card higher than '{card}', or pass.", 2: "Throw a pair of cards higher than '{card}', or pass.",
    3: "Throw a triple of cards higher than '{card}', or pass.", 4: "Throw a quadruple of cards higher than '{card}', or pass.",
};

// The cards chosen in a hand: the seat, and the positions of the chosen cards in its hand (which comes sorted).
let chosen = { seat: null, cards: "", picks: [] };
let heardThrows = null;

const playerName = (state, seat) => state.seats[seat]?.name ?? t("A player who left");

function placeOf(game, seat) {
    return game.players.find((p) => p.seat === seat);
}

// The user's hands in the game, by seat.
function myHands(view) {
    return view.private?.hands ?? [];
}

// Tapping a card: while throwing, cards of one number (with a throw to beat, as many as it has at once); while giving,
// any cards up to the number to give. Tapping a chosen card puts it back.
function pickCard(game, hand, index) {
    const key = hand.cards.join(",");
    if (chosen.seat !== hand.seat || chosen.cards !== key) {
        chosen = { seat: hand.seat, cards: key, picks: [] };
    }
    const picks = chosen.picks;
    if (picks.includes(index)) {
        chosen.picks = picks.filter((i) => i !== index);
    } else if (game.phase === "Exchanging") {
        const limit = placeOf(game, hand.seat).exchangeCount;
        chosen.picks = [...picks, index].slice(-limit);
    } else {
        const rank = rankOf(hand.cards[index]);
        const same = picks.length && rankOf(hand.cards[picks[0]]) === rank ? picks : [];
        const need = game.currentThrow?.cards.length;
        chosen.picks = need && same.length === 0
            ? hand.cards.flatMap((c, i) => (rankOf(c) === rank ? [i] : [])).slice(0, need)
            : [...same, index];
    }
    rich.refresh();
}

function chosenCards(hand) {
    return chosen.seat === hand.seat && chosen.cards === hand.cards.join(",") ? chosen.picks.map((i) => hand.cards[i]) : [];
}

function cardRow(cards, className = "") {
    return el("div", `card-row ${className}`, ...cards.map((c) => cardElement(c)));
}

// One of the user's hands: its cards (as buttons when they can be chosen), a hint, and its buttons.
function handPanel(game, state, view, hand) {
    const me = placeOf(game, hand.seat);
    const myTurn = game.phase === "Playing" && game.turn === hand.seat;
    const giving = game.phase === "Exchanging" && me.exchangeCount > 0 && !hand.gift;
    const required = giving && hand.requiredGift ? hand.requiredGift : null;
    const canChoose = myTurn || (giving && !required);
    const picks = chosenCards(hand);
    const highlight = required ? [...required] : [...picks];

    // Each card sits in a slot that narrows when the hand is wider than the screen, so the cards overlap to fit.
    const cards = el("div", "card-row rp-cards", ...hand.cards.map((card, index) => {
        const face = cardElement(card);
        const marked = highlight.indexOf(card);
        if (marked >= 0) {
            highlight.splice(marked, 1);
        }
        face.classList.toggle("chosen", marked >= 0);
        if (!canChoose) {
            return el("span", "rp-slot", face);
        }
        const button = el("button", "rp-card", face);
        button.type = "button";
        button.setAttribute("aria-pressed", String(marked >= 0));
        button.addEventListener("click", () => pickCard(game, hand, index));
        return el("span", "rp-slot", button);
    }));

    const title = myHands(view).length > 1 ? playerName(state, hand.seat) : t("Your cards");
    const panel = el("div", "my-hand rp-hand", el("div", "my-hand-title", `${title} · #${me.place}`), cards);
    const hint = el("p", "hint");
    const buttons = el("div", "actions");
    const button = (text, onClick, enabled = true, secondary = false) => {
        const b = el("button", secondary ? "secondary" : "", text);
        b.type = "button";
        b.disabled = !enabled;
        b.addEventListener("click", onClick);
        buttons.append(b);
    };
    const send = (method, ...args) => {
        chosen.picks = [];
        call(method, rich.sessionId, hand.seat, ...args).catch(() => {});
    };
    const partner = hand.partnerSeat !== null ? playerName(state, hand.partnerSeat) : "";

    if (game.phase === "Playing" && me.finished === null && myTurn) {
        const current = game.currentThrow;
        const valid = picks.length > 0 && (!current
            || (picks.length === current.cards.length && rankOf(picks[0]) > rankOf(current.cards[0])));
        hint.textContent = !current ? t("You start this round: tap one or more cards of the same value to place on the table.")
            : t(BEAT_HINTS[current.cards.length] ?? "Throw {n} cards higher than '{card}', or pass.",
                { n: current.cards.length, card: current.cards[0].slice(0, -1) });
        button(t("Select"), () => send("ThrowCards", picks), valid);
        if (current) {
            button(t("Pass"), () => send("Pass"), true, true);
        }
    } else if (game.phase === "Playing" || game.phase === "GameOver") {
        hint.textContent = me.finished !== null ? t("You emptied your hand: place {n} for the next game.", { n: me.finished })
            : game.phase === "Playing" ? t("Waiting for {name}…", { name: playerName(state, game.turn) }) : "";
        if (hand.received && game.phase === "Playing" && !game.lastThrow) {
            panel.append(el("p", "hint", t("You received from {name}:", { name: partner })), cardRow(hand.received, "small"));
        }
    } else if (me.exchangeCount === 0) {
        hint.textContent = t("You do not exchange cards in this game.");
    } else if (hand.gift) {
        hint.textContent = t("Your cards are given. Everyone gets their new cards once all have given.");
        panel.append(cardRow(hand.gift, "small"));
    } else if (required) {
        hint.textContent = t("Give your {n} highest cards to {name}.", { n: required.length, name: partner });
        button(t("Give these cards"), () => send("GiveCards", required));
    } else {
        hint.textContent = t("Choose {n} cards to give to {name}.", { n: me.exchangeCount, name: partner });
        button(t("Give"), () => send("GiveCards", picks), picks.length === me.exchangeCount);
    }

    panel.append(hint, buttons);
    return panel;
}

// A text about a seat: its "you" form for the viewer (unless they hold several seats), otherwise with its name.
function about(state, view, seat, you, other, values = {}) {
    return view.isMe(seat) && view.mySeats.length === 1 ? t(you, values) : t(other, { name: playerName(state, seat), ...values });
}

function statusText(game, state, view) {
    if (game.phase === "Exchanging") {
        const givers = game.players.filter((p) => p.exchangeCount > 0);
        const mine = givers.some((p) => view.isMe(p.seat) && !p.hasGiven);
        return `${mine ? "👉 " : ""}🔄 ${t("Card exchange: {given}/{total} given", { given: givers.filter((p) => p.hasGiven).length, total: givers.length })}`;
    }
    if (game.phase === "GameOver") {
        return `🏁 ${t("Game {n} is over.", { n: game.gameNumber })}`;
    }
    const mine = view.isMe(game.turn) && view.mySeats.length === 1;
    return mine ? `👉 ${t(game.currentThrow ? "Throw higher cards than the ones on the table, or pass" : "You start the round")}`
        : `⏳ ${t("Waiting for {name}", { name: playerName(state, game.turn) })}`;
}

// The middle of the table: the throw to beat, or the last throw (faded) and who won its round. After a game, the ranking.
function pile(game, state, view) {
    if (game.phase === "Exchanging") {
        return [el("p", "hint", t("The pairs are exchanging cards. Nobody gets new cards until all have given."))];
    }
    if (game.phase === "GameOver") {
        const left = game.players.filter((p) => !state.seats[p.seat] || state.seats[p.seat].status === "Away").length;
        const staying = game.players.length - left;
        const ranked = game.ranking.filter((seat) => state.seats[seat]);
        return [
            ...(ranked.length ? [el("p", "rp-pile-who", t("The next game's places:")),
                el("ol", "rp-ranking", ...ranked.map((seat) => el("li", null, seatLabel(state, seat, view))))] : []),
            ...(staying < 6 ? [el("p", "hint", t("Fewer than 6 players are left: Next game goes back to the lobby."))] : []),
        ];
    }
    const current = game.currentThrow;
    if (current) {
        return [
            el("p", "rp-pile-who", about(state, view, current.seat, "You threw:", "{name} threw:")),
            cardRow(current.cards, "rp-pile-cards"),
        ];
    }
    if (game.lastThrow && game.lastRoundWinner !== null) {
        return [
            el("p", "rp-pile-who", about(state, view, game.lastRoundWinner, "You won the round.", "{name} won the round.")),
            cardRow(game.lastThrow.cards, "rp-pile-cards faded"),
        ];
    }
    return [el("p", "hint", t("No cards on the table yet."))];
}

// A player in the list: place, name, cards left (or, once their hand is empty, their finishing place), and their last
// call in this round (Pass, or what they threw, e.g. "2×9"), in the middle of the line. An absent player shows "Away"
// (their seat can be taken from the banner in the status bar).
function playerItem(game, state, view, player) {
    const start = el("span", "rp-start", el("span", "rp-place", `#${player.place}`));
    const end = el("span", "rp-end");
    const item = el("li", null, start);
    item.classList.toggle("turn", game.phase === "Playing" && game.turn === player.seat);
    item.classList.toggle("left", !player.isActive);
    item.classList.toggle("done", player.finished !== null);
    start.append(state.seats[player.seat] ? seatLabel(state, player.seat, view) : el("span", "seat-label", t("A player who left")));
    const tag = (text, className = "", group = end) => group.append(el("span", `rp-tag ${className}`, text));
    if (player.place === 1) tag(t("Rich man"), "rich", start);
    if (player.place === game.players.length) tag(t("Poor man"), "poor", start);
    if (player.finished !== null) {
        tag(`🏁 ${player.finished}`, "finished");
    } else {
        end.append(el("span", "rp-count", `🂠 ${player.cardCount}`));
    }
    if (game.phase === "Playing" && player.passed) {
        tag(t("Pass"), "call pass", item);
    } else if (game.phase === "Playing" && player.roundThrow) {
        tag(`${player.roundThrow.length}×${player.roundThrow[0].slice(0, -1)}`, "call", item);
    }
    item.append(end);
    if (!player.isActive) tag(t("Away"), "away");
    // In the exchange, each player gives to their pair: #1 and the last place, #2 and the one before it, and so on.
    if (game.phase === "Exchanging" && player.exchangeCount > 0) {
        const n = player.exchangeCount;
        const values = { n, place: `#${game.players.length + 1 - player.place}` };
        const text = player.hasGiven
            ? `✓ ${t(n === 1 ? "Gave 1 card to {place}" : "Gave {n} cards to {place}", values)}`
            : t(n === 1 ? "Gives 1 card to {place}" : "Gives {n} cards to {place}", values);
        tag(text, player.hasGiven ? "given" : "giving");
    }
    return item;
}

// During the exchange: players see who asks to join, and accept them at a place of their choice (the last place by
// default) or decline. Others can ask to join, or withdraw their request. Not in public games.
function joinPanel(game, state, view) {
    if (game.phase !== "Exchanging" || state.isPublic) {
        return null;
    }
    const send = (method, ...args) => call(method, rich.sessionId, ...args).catch(() => {});
    const requests = game.joinRequests;
    if (view.isPlayer) {
        if (!requests.length) {
            return null;
        }
        return [el("h3", null, t("Asking to join")), ...requests.map((request) => {
            const places = el("select", null, ...Array.from({ length: game.players.length + 1 }, (_, i) =>
                Object.assign(el("option", null, `#${i + 1}`), { value: String(i + 1) })));
            places.value = String(game.players.length + 1);
            places.setAttribute("aria-label", t("Place"));
            const accept = el("button", "small", t("Accept"));
            accept.addEventListener("click", () => send("AcceptSeat", request.playerId, Number(places.value)));
            const decline = el("button", "secondary small", t("Decline"));
            decline.addEventListener("click", () => send("DeclineSeat", request.playerId));
            return el("div", "rp-request", iconElement(request.icon), el("span", null, request.name), places, accept, decline);
        })];
    }
    if (requests.some((r) => r.playerId === view.playerId)) {
        const cancel = el("button", "secondary small", t("Cancel"));
        cancel.addEventListener("click", () => send("CancelSeatRequest"));
        return [el("p", "hint", t("You asked to join. Waiting for a player to accept you."), " ", cancel)];
    }
    if (game.players.length >= 12) {
        return null;
    }
    const ask = el("button", null, t("Ask to join"));
    ask.addEventListener("click", () => send("RequestSeat"));
    return [el("p", "hint", t("The next game can take new players.")), ask];
}

// Reading the throws aloud, on a shared screen, in the page's language: a single card by its value (e.g. "A single
// queen", a whole text, so translations can match the value's gender), and 2 to 12 cards as their group, then the
// card's value (e.g. "Triplet of ace").
const SPOKEN_SINGLES = {
    2: "A single two", 3: "A single three", 4: "A single four", 5: "A single five", 6: "A single six",
    7: "A single seven", 8: "A single eight", 9: "A single nine", 10: "A single ten", J: "A single jack",
    Q: "A single queen", K: "A single king", A: "A single ace",
};
const SPOKEN_GROUPS = {
    2: "Pair of {card}", 3: "Triplet of {card}", 4: "Quadruplet of {card}", 5: "Quintuplet of {card}",
    6: "Sextuplet of {card}", 7: "Septuplet of {card}", 8: "Octuplet of {card}", 9: "Nonuplet of {card}",
    10: "Decuplet of {card}", 11: "Undecuplet of {card}", 12: "Duodecuplet of {card}",
};
const SPOKEN_VALUES = {
    2: "two", 3: "three", 4: "four", 5: "five", 6: "six", 7: "seven", 8: "eight", 9: "nine", 10: "ten",
    J: "jack", Q: "queen", K: "king", A: "ace",
};

// Many computers have no voice for some languages (e.g. Hebrew), and another language's voice misreads the text, so
// without one the throws are read in English. Until the browser has listed its voices, the page's language is tried
// anyway. A voice for the language's locale (e.g. Mexican Spanish) is preferred over another variant of it.
const voiceLang = (voice) => voice.lang.replace("_", "-").toLowerCase();
const speaks = (lang) => (voice) => [lang.code, ...(lang.code === "he" ? ["iw"] : [])].includes(voiceLang(voice).split("-")[0]);

function missingVoice() {
    const voices = speechSynthesis.getVoices();
    return language !== LANGUAGES[0] && voices.length > 0 && !voices.some(speaks(language));
}

// The varying parts are texts too, read in the same language as the key.
function speak(key, values = {}) {
    speechSynthesis.cancel();
    if (!userSettings().sound) return;
    const lang = missingVoice() ? LANGUAGES[0] : language;
    const say = (text) => (lang === language ? t(text) : text);
    const text = say(key).replace(/\{(\w+)\}/g, (match, name) => (name in values ? say(values[name]) : match));
    const utterance = new SpeechSynthesisUtterance(text);
    utterance.lang = lang.locale;
    const voices = speechSynthesis.getVoices().filter(speaks(lang));
    utterance.voice = voices.find((voice) => voiceLang(voice) === lang.locale.toLowerCase()) ?? voices[0] ?? null;
    speechSynthesis.speak(utterance);
}

// With sound off in the user's profile, the button is greyed out and says why, and nothing is read.
function updateSpeech(game, view) {
    const button = $("rp-speech");
    const soundOn = userSettings().sound;
    const on = soundOn && localStorage.getItem(SPEECH_KEY) === "on";
    button.hidden = view.isPlayer || !("speechSynthesis" in window);
    button.disabled = !soundOn;
    button.textContent = on ? t("🔊 Reading throws aloud") : t("🔇 Read throws aloud");
    button.setAttribute("aria-pressed", String(on));
    button.title = button.hidden ? ""
        : !soundOn ? t("Sound is off in your profile.")
            : missingVoice() ? t("This device has no voice for this language, so the throws are read in English. On Windows, add one in Settings > Time & language > Speech.")
                : "";
    const isNew = heardThrows !== null && game.throws > heardThrows;
    heardThrows = game.throws;
    if (isNew && on && !button.hidden && game.lastThrow) {
        const cards = game.lastThrow.cards;
        const value = cards[0].slice(0, -1);
        speak(cards.length === 1 ? SPOKEN_SINGLES[value] : SPOKEN_GROUPS[cards.length], { card: SPOKEN_VALUES[value] });
    }
}

$("rp-speech").addEventListener("click", () => {
    const on = localStorage.getItem(SPEECH_KEY) !== "on";
    localStorage.setItem(SPEECH_KEY, on ? "on" : "off");
    if (on) {
        speak("The throws will be read aloud.");
    }
    rich.refresh();
});

if ("speechSynthesis" in window) {
    speechSynthesis.addEventListener("voiceschanged", () => rich.refresh());
}
addEventListener(SETTINGS_EVENT, () => {
    if (!userSettings().sound && "speechSynthesis" in window) {
        speechSynthesis.cancel();
    }
    rich.refresh();
});

let currentGame = null;

const rich = startSessionPage("RichManPoorMan", (game, state, view) => {
    currentGame = game;
    document.body.classList.toggle("observer", !view.isPlayer);
    $("status").textContent = statusText(game, state, view);
    $("rp-game").textContent = t("Game {n}", { n: game.gameNumber });
    $("rp-hands").replaceChildren(...myHands(view).filter((h) => placeOf(game, h.seat))
        .map((hand) => handPanel(game, state, view, hand)));
    const join = joinPanel(game, state, view);
    $("rp-join").hidden = !join;
    $("rp-join").replaceChildren(...(join ?? []));
    $("rp-pile").replaceChildren(...pile(game, state, view));
    $("rp-players").replaceChildren(...game.players.map((p) => playerItem(game, state, view, p)));
    $("next-round").hidden = !(view.isPlayer && game.phase === "GameOver");
    updateSpeech(game, view);
}, (seat, state) => {
    const place = state.game && placeOf(state.game, seat)?.place;
    return place ? `#${place}` : String(seat + 1);
});

$("next-round").addEventListener("click", () => call("NextRound", rich.sessionId, currentGame?.gameNumber ?? 0).catch(() => {}));
