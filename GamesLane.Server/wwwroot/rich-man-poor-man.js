"use strict";

// Rich man, poor man: players throw cards of one number, each throw beating the last with as many cards of a higher
// number, and the first to empty their hand is the next game's rich man. Each player's own cards come in view.private.
// The page shows the cards on the table on top, then the user's own hands, then the players' table (players can hide
// it). On a shared screen, the throws can be read aloud.

const FACE_RANKS = { J: 11, Q: 12, K: 13, A: 14 };
const rankOf = (card) => FACE_RANKS[card.slice(0, -1)] ?? Number(card.slice(0, -1));
const SPEECH_KEY = "richManPoorMan.speech";
const TABLE_SHOWN_KEY = "richManPoorMan.tableShown";
const MIN_PLAYERS = 4;

// Phones: a hand of more than TWO_ROWS_FROM cards takes two rows, and the players' table is hidden until shown.
const narrowScreen = matchMedia("(max-width: 600px)");
const TWO_ROWS_FROM = 8;

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

// Tapping a card: while throwing, cards of one number. The first card of a number picks all the hand's cards of that
// number (with a throw to beat, as many as it has); the starter of a round can then tap some to put them back. While
// giving, any cards up to the number to give. Tapping a chosen card puts it back.
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
        const all = hand.cards.flatMap((c, i) => (rankOf(c) === rank ? [i] : []));
        chosen.picks = same.length === 0 ? all.slice(0, need ?? all.length) : [...same, index];
    }
    rich.refresh();
}

function chosenCards(hand) {
    return chosen.seat === hand.seat && chosen.cards === hand.cards.join(",") ? chosen.picks.map((i) => hand.cards[i]) : [];
}

function cardRow(cards, className = "") {
    return el("div", `card-row ${className}`, ...cards.map((c) => cardElement(c)));
}

// Whether the hand has something to do now: throw or pass on its turn, or give its cards in the exchange.
function mustAct(game, hand) {
    const me = placeOf(game, hand.seat);
    return game.phase === "Playing" ? game.turn === hand.seat && me.finished === null
        : game.phase === "Exchanging" && me.exchangeCount > 0 && !hand.gift;
}

// What the hand does now, shown in the status bar: e.g. "Throw a single card higher than '7', or pass.", "Choose 2
// cards to give to Dana.", or its finishing place.
function handHint(game, state, hand) {
    const me = placeOf(game, hand.seat);
    const partner = hand.partnerSeat !== null ? playerName(state, hand.partnerSeat) : "";
    if (game.phase === "Exchanging") {
        return me.exchangeCount === 0 ? t("You do not exchange cards in this game.")
            : hand.gift ? t("Your cards are given. Everyone gets their new cards once all have given.")
                : hand.requiredGift ? t("Give your {n} highest cards to {name}.", { n: hand.requiredGift.length, name: partner })
                    : t("Choose {n} cards to give to {name}.", { n: me.exchangeCount, name: partner });
    }
    if (me.finished !== null) {
        return t(hand.cards.length ? "You are the last player: place {n} for the next game." : "You emptied your hand: place {n} for the next game.",
            { n: me.finished });
    }
    if (game.phase !== "Playing") {
        return "";
    }
    if (game.turn !== hand.seat) {
        return t("Waiting for {name}", { name: playerName(state, game.turn) });
    }
    const current = game.currentThrow;
    return !current ? t("You start this round: tap one or more cards of the same value to place on the table.")
        : t(BEAT_HINTS[current.cards.length] ?? "Throw {n} cards higher than '{card}', or pass.",
            { n: current.cards.length, card: current.cards[0].slice(0, -1) });
}

// One of the user's hands: its cards (as buttons when they can be chosen) and its buttons. What to do is said in the
// status bar (see handHint).
function handPanel(game, state, view, hand) {
    const me = placeOf(game, hand.seat);
    const myTurn = game.phase === "Playing" && game.turn === hand.seat;
    const giving = game.phase === "Exchanging" && me.exchangeCount > 0 && !hand.gift;
    const required = giving && hand.requiredGift ? hand.requiredGift : null;
    const canChoose = myTurn || (giving && !required);
    const picks = chosenCards(hand);
    const highlight = required ? [...required] : [...picks];
    // Given cards stay in the hand until the exchange; they show only in the row under it.
    const given = [...(game.phase === "Exchanging" ? hand.gift ?? [] : [])];
    const shown = hand.cards.flatMap((card, index) => {
        const at = given.indexOf(card);
        if (at < 0) {
            return [[card, index]];
        }
        given.splice(at, 1);
        return [];
    });

    // Each card sits in a slot that narrows when the hand is wider than the screen, so the cards overlap to fit. On a
    // phone, a long hand is split into two rows.
    const slots = shown.map(([card, index]) => {
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
    });
    const half = narrowScreen.matches && slots.length > TWO_ROWS_FROM ? Math.ceil(slots.length / 2) : slots.length;
    const rows = [slots.slice(0, half), slots.slice(half)].filter((row) => row.length)
        .map((row) => el("div", "card-row rp-cards", ...row));

    const title = myHands(view).length > 1 ? playerName(state, hand.seat) : t("Your cards");
    const turn = myTurn && me.finished === null;
    const head = el("div", "my-hand-title", `${title} · #${me.place}`,
        ...(turn ? [el("span", "rp-your-turn", `👉 ${t("Your turn")}`)] : []));
    const panel = el("div", `my-hand rp-hand${turn ? " my-turn" : ""}`, head, ...rows);
    const buttons = el("div", "actions");
    const button = (text, onClick, enabled = true, secondary = false, className = "") => {
        const b = el("button", `${secondary ? "secondary" : ""} ${className}`.trim(), text);
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
        // Pass and Select at opposite ends of the panel, so one is not tapped instead of the other.
        if (current) {
            button(t("Pass"), () => send("Pass"), true, true, "rp-pass");
        }
        button(t("Select"), () => send("ThrowCards", picks), valid, false, "rp-select");
    } else if (game.phase === "Playing" || game.phase === "GameOver") {
        if (hand.received && game.phase === "Playing" && !game.lastThrow) {
            panel.append(el("p", "hint", t("You received from {name}:", { name: partner })), cardRow(hand.received, "small"));
        }
    } else if (hand.gift) {
        panel.append(cardRow(hand.gift, "small"));
    } else if (required) {
        button(t("Give these cards"), () => send("GiveCards", required));
    } else if (me.exchangeCount > 0) {
        button(t("Give"), () => send("GiveCards", picks), picks.length === me.exchangeCount);
    }

    if (buttons.childElementCount) {
        panel.append(buttons);
    }
    return panel;
}

// A text about a seat: its "you" form for the viewer (unless they hold several seats), otherwise with its name.
function about(state, view, seat, you, other, values = {}) {
    return view.isMe(seat) && view.mySeats.length === 1 ? t(you, values) : t(other, { name: playerName(state, seat), ...values });
}

// The status bar: for a player, what their hand does now (the hand that must act, if they hold several, named); for
// observers, the exchange's progress or whose turn it is. After a game, that it is over.
function statusText(game, state, view) {
    const over = game.phase === "GameOver" ? `🏁 ${t("Game {n} is over.", { n: game.gameNumber })}` : "";
    const hands = myHands(view).filter((h) => placeOf(game, h.seat));
    if (hands.length > 0) {
        const hand = hands.find((h) => mustAct(game, h)) ?? hands[0];
        const name = hands.length > 1 ? `${playerName(state, hand.seat)}: ` : "";
        const hint = handHint(game, state, hand);
        return [over, hint && `${mustAct(game, hand) ? "👉" : "⏳"} ${name}${hint}`].filter(Boolean).join(" ");
    }
    if (game.phase === "Exchanging") {
        const givers = game.players.filter((p) => p.exchangeCount > 0);
        return `🔄 ${t("Card exchange: {given}/{total} given", { given: givers.filter((p) => p.hasGiven).length, total: givers.length })}`;
    }
    return over || `⏳ ${t("Waiting for {name}", { name: playerName(state, game.turn) })}`;
}

// The middle of the table: the throw to beat, or the last throw (faded) and who won its round. After a game, the ranking.
function pile(game, state, view) {
    if (game.phase === "Exchanging") {
        return [el("p", "hint", t("The pairs are exchanging cards. Nobody gets new cards until all have given."))];
    }
    if (game.phase === "GameOver") {
        const left = game.players.filter((p) => !state.seats[p.seat]).length;
        const staying = game.players.length - left;
        const ranked = game.ranking.filter((seat) => state.seats[seat]);
        return [
            ...(ranked.length ? [el("p", "rp-pile-who", t("The next game's places:")),
                el("ol", "rp-ranking", ...ranked.map((seat) => el("li", null, seatLabel(state, seat, view))))] : []),
            ...(staying < MIN_PLAYERS ? [el("p", "hint", t("Fewer than 4 players are left: Next game goes back to the lobby."))] : []),
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
// call in this round (Pass, or what they threw, e.g. "2×9"), in the middle of the line. A disconnected player is faded,
// with a spinner by their name.
function playerItem(game, state, view, player) {
    const start = el("span", "rp-start", el("span", "rp-place", `#${player.place}`));
    const end = el("span", "rp-end");
    const item = el("li", null, start);
    item.dataset.seat = String(player.seat);
    item.classList.toggle("turn", game.phase === "Playing" && game.turn === player.seat);
    item.classList.toggle("left", !player.isActive);
    item.classList.toggle("done", player.finished !== null);
    animateSince(item, "just-finished", finishedAt.get(player.seat) ?? -Infinity, FINISH_GLOW_MS);
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
// anyway. A voice for the language's locale (e.g. Mexican Spanish) is preferred over another variant of it, and for
// Chinese, a Mandarin voice over a Hong Kong (Cantonese) one.
const voiceLang = (voice) => voice.lang.replace("_", "-").toLowerCase();
const speaks = (lang) => (voice) => {
    const code = lang.locale.split("-")[0];
    return [code, ...(code === "he" ? ["iw"] : [])].includes(voiceLang(voice).split("-")[0]);
};

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
    utterance.voice = voices.find((voice) => voiceLang(voice) === lang.locale.toLowerCase())
        ?? voices.find((voice) => !voiceLang(voice).startsWith("zh-hk")) ?? voices[0] ?? null;
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

// When the turn comes to this user: a rising three-note chime made by the browser (unless sound is off in their
// profile) and, on phones that can (not iPhones), a short vibration. Browsers only allow sound after the user has
// tapped the page, so the sound is set up on the first tap and resumed on every tap and whenever the page is shown
// again (iPhones pause it in the background). On iPhones, the sound is played as media, so the silent switch does not
// mute it.
let audio = null;
const resumeSound = () => {
    if (navigator.audioSession) {
        try {
            navigator.audioSession.type = "playback";
        } catch {
            // Not supported on this browser.
        }
    }
    audio ??= new AudioContext();
    return audio.resume();
};
addEventListener("pointerdown", () => resumeSound().catch(() => {}), { capture: true });
document.addEventListener("visibilitychange", () => {
    if (!document.hidden && audio) audio.resume().catch(() => {});
});

function notifyTurn() {
    navigator.vibrate?.([180, 90, 180]);
    if (!audio || !userSettings().sound) return;
    (audio.state === "running" ? Promise.resolve() : audio.resume()).then(playTurnChime).catch(() => {});
}

function playTurnChime() {
    const start = audio.currentTime;
    [[660, 0], [880, 0.13], [1320, 0.26]].forEach(([frequency, delay]) => {
        const tone = audio.createOscillator();
        const volume = audio.createGain();
        tone.type = "triangle";
        tone.frequency.value = frequency;
        volume.gain.setValueAtTime(0.0001, start + delay);
        volume.gain.exponentialRampToValueAtTime(0.35, start + delay + 0.02);
        volume.gain.exponentialRampToValueAtTime(0.0001, start + delay + 0.25);
        tone.connect(volume).connect(audio.destination);
        tone.start(start + delay);
        tone.stop(start + delay + 0.28);
    });
}

// Whether it was this user's turn at the last update (null before the first), to notify them once when it comes.
let wasMyTurn = null;

function noticeTurn(game, view) {
    const mine = game.phase === "Playing" && view.isMe(game.turn) && placeOf(game, game.turn)?.finished === null;
    if (mine && wasMyTurn === false) {
        notifyTurn();
    }
    wasMyTurn = mine;
}

// Confetti for each of the user's hands that has just been emptied (not for the last player, who keeps their cards).
// For everyone, players and observers, a player who empties their hand gets a small banner with their place, a soft
// chime (not for the user's own hand, which gets the fanfare) and their line in the table glows for a moment.
const FINISH_GLOW_MS = 2500;
const finishedAt = new Map();
// The seats that had emptied their hands at the last update, and in which game.
let emptiedBefore = { game: null, seats: [] };

function noticeFinished(game, state, view) {
    myHands(view).filter((hand) => hand.cards.length === 0 && placeOf(game, hand.seat)?.finished)
        .forEach((hand) => celebrate(state, `${game.gameNumber}-${hand.seat}`));
    const emptied = game.players.filter((p) => p.finished !== null && p.cardCount === 0).map((p) => p.seat);
    const changed = view.changed("finished", [game.gameNumber, emptied]);
    const before = emptiedBefore;
    emptiedBefore = { game: game.gameNumber, seats: emptied };
    if (!changed || before.game !== game.gameNumber) return;
    emptied.filter((seat) => !before.seats.includes(seat)).forEach((seat) => {
        finishedAt.set(seat, performance.now());
        const n = placeOf(game, seat).finished;
        showBanner(`🏁 ${about(state, view, seat, "You finished in place {n}!", "{name} finished in place {n}!", { n })}`, "", "small",
            view.isMe(seat) ? null : "finished");
    });
}

// A throw: the card's sound, and the cards fly from their player to the table.
const THROW_MS = 450;
let thrown = { seat: null, at: -Infinity };

function noticeThrow(game, view) {
    // Aces end the round at once, so their throw is already the last one.
    const latest = game.currentThrow ?? game.lastThrow;
    if (view.changed("throws", [game.gameNumber, game.throws]) && latest && game.phase === "Playing") {
        playGameSound("card");
        thrown = { seat: latest.seat, at: performance.now() };
    }
    const age = animationAge(thrown.at, THROW_MS);
    const cards = document.querySelector("#rp-top .rp-pile-cards");
    if (age !== null && cards && latest?.seat === thrown.seat) {
        flyFrom(cards, seatSpot(view, thrown.seat, "rp-hands", "rp-players"), age);
    }
}

let currentGame = null;
// The players' table: until a player shows or hides it (remembered on this device), hidden on phones and shown on
// computers. Observers always see it.
const tableShown = () => {
    const saved = localStorage.getItem(TABLE_SHOWN_KEY);
    return saved === null ? !narrowScreen.matches : saved === "1";
};

// From the top, for everyone: the cards on the table, the user's own hands, then the players' table.
const rich = startSessionPage("RichManPoorMan", (game, state, view) => {
    currentGame = game;
    noticeTurn(game, view);
    noticeShuffles(view, state, game.shuffles);
    noticeFinished(game, state, view);
    const shown = !view.isPlayer || tableShown();
    document.body.classList.toggle("observer", !view.isPlayer);
    $("status").textContent = statusText(game, state, view);
    $("rp-game").textContent = t("Game {n}", { n: game.gameNumber });
    $("rp-top").replaceChildren(...pile(game, state, view));
    noticeThrow(game, view);
    $("rp-hands").replaceChildren(...myHands(view).filter((h) => placeOf(game, h.seat))
        .map((hand) => handPanel(game, state, view, hand)));
    const join = joinPanel(game, state, view);
    $("rp-join").hidden = !join;
    $("rp-join").replaceChildren(...(join ?? []));
    $("rp-toggle-table").hidden = !view.isPlayer;
    $("rp-toggle-table").textContent = t(shown ? "Hide table" : "Show table");
    $("rp-table-body").hidden = !shown;
    $("rp-players").replaceChildren(...game.players.map((p) => playerItem(game, state, view, p)));
    $("next-round").hidden = !(view.isPlayer && game.phase === "GameOver");
    updateSpeech(game, view);
}, (seat, state) => {
    const place = state.game && placeOf(state.game, seat)?.place;
    return place ? `#${place}` : String(seat + 1);
});
// The page plays its own chime when the turn comes (see notifyTurn).
rich.turnSound = false;

$("next-round").addEventListener("click", () => call("NextRound", rich.sessionId, currentGame?.gameNumber ?? 0).catch(() => {}));
$("rp-toggle-table").addEventListener("click", () => {
    localStorage.setItem(TABLE_SHOWN_KEY, tableShown() ? "0" : "1");
    rich.refresh();
});
narrowScreen.addEventListener("change", () => rich.refresh());
