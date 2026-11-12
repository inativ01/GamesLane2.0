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

// Under the table: who won the last trick, that the cards are not passed in this hand, or who claimed the remaining
// tricks.
function messageText(game, state, view) {
    if (game.claim) {
        return claimText(game, state, view);
    }
    if (game.phase === "Playing" && game.pass === "Hold" && game.trick.cards.length === 0 && !game.lastTrick) {
        return t("No passing in this hand.");
    }
    return game.phase === "Playing" ? lastTrickText(game, state, view) : "";
}

// A breaking glass when the first heart of a hand is played: a sharp crack of noise, then shards ringing high.
function playGlassBreak() {
    playSound((audio, start) => {
        const length = 0.3;
        const buffer = audio.createBuffer(1, Math.floor(audio.sampleRate * length), audio.sampleRate);
        const data = buffer.getChannelData(0);
        for (let i = 0; i < data.length; i++) {
            data[i] = (Math.random() * 2 - 1) * (1 - i / data.length) ** 4;
        }
        const crack = audio.createBufferSource();
        crack.buffer = buffer;
        const filter = audio.createBiquadFilter();
        filter.type = "highpass";
        filter.frequency.value = 2200;
        const volume = audio.createGain();
        volume.gain.value = 0.6;
        crack.connect(filter).connect(volume).connect(audio.destination);
        crack.start(start);
        for (let i = 0; i < 10; i++) {
            const at = start + 0.02 + Math.random() * 0.5;
            const ring = 0.08 + Math.random() * 0.2;
            const shard = audio.createOscillator();
            const gain = audio.createGain();
            shard.frequency.value = 2500 + Math.random() * 5000;
            gain.gain.setValueAtTime(0.0001, at);
            gain.gain.exponentialRampToValueAtTime(0.1, at + 0.004);
            gain.gain.exponentialRampToValueAtTime(0.0001, at + ring);
            shard.connect(gain).connect(audio.destination);
            shard.start(at);
            shard.stop(at + ring + 0.02);
        }
    });
}

// A regal doom when the queen of spades is taken: a slow minor brass call, then a low held chord over a timpani roll.
function playRegalDoom() {
    playSound((audio, start) => {
        const brass = (frequency, delay, length, peak) => {
            const at = start + delay;
            const tone = audio.createOscillator();
            const filter = audio.createBiquadFilter();
            const gain = audio.createGain();
            tone.type = "sawtooth";
            tone.frequency.value = frequency;
            filter.type = "lowpass";
            filter.frequency.value = 1400;
            gain.gain.setValueAtTime(0.0001, at);
            gain.gain.exponentialRampToValueAtTime(peak, at + 0.05);
            gain.gain.setValueAtTime(peak, at + length * 0.7);
            gain.gain.exponentialRampToValueAtTime(0.0001, at + length);
            tone.connect(filter).connect(gain).connect(audio.destination);
            tone.start(at);
            tone.stop(at + length + 0.02);
        };
        // D, D, F, then A falling to a long D minor chord.
        [[293.7, 0, 0.32], [293.7, 0.36, 0.14], [349.2, 0.54, 0.32], [220, 0.92, 0.3]].forEach(([f, d, l]) => brass(f, d, l, 0.12));
        [73.4, 146.8, 174.6, 220].forEach((f) => brass(f, 1.25, 1.8, 0.09));
        for (let i = 0; i < 8; i++) {
            const at = start + 1.25 + i * 0.07;
            const drum = audio.createOscillator();
            const gain = audio.createGain();
            drum.frequency.setValueAtTime(90, at);
            drum.frequency.exponentialRampToValueAtTime(50, at + 0.3);
            gain.gain.setValueAtTime(0.0001, at);
            gain.gain.exponentialRampToValueAtTime(i === 0 ? 0.5 : 0.18, at + 0.01);
            gain.gain.exponentialRampToValueAtTime(0.0001, at + 0.35);
            drum.connect(gain).connect(audio.destination);
            drum.start(at);
            drum.stop(at + 0.4);
        }
    });
}

// For everyone with the page open, as soon as the card is played: the glass when the first heart breaks hearts, the
// doom when the Q♠ is played (or taken by a claim). Only changes seen while the page is open count; a claim does not
// break hearts.
let heard = null;
function playSounds(game, state) {
    const played = [...game.trick.cards, ...(game.lastTrick?.cards ?? [])].some((c) => c.card === "Q♠");
    const now = {
        hand: `${state.gameNumber}:${game.handNumber}`,
        broken: game.heartsBroken,
        queen: played || game.players.some((p) => p.tookQueen),
    };
    if (heard?.hand === now.hand) {
        const broke = now.broken && !heard.broken;
        if (broke) playGlassBreak();
        if (now.queen && !heard.queen) setTimeout(playRegalDoom, broke ? 700 : 0);
    }
    heard = now;
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

// One of the user's hands: while passing, the cards to choose and Pass; while playing, the cards it may play on its
// turn, and Claim all when its cards win every remaining trick (it can still play them one by one).
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
    if (myTurn && hand.canClaim) {
        foot.push(trickButton(t("Claim all"), () => send("ClaimTricks", hand.seat)));
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
    playSounds(game, state);
    $("hearts-table").replaceChildren(trickTable(game, state, view, {
        turn: game.phase === "Playing" ? game.turn : null,
        facts: (seat) => facts(game, seat),
    }));
    const message = messageText(game, state, view);
    $("hearts-message").textContent = message;
    $("hearts-message").hidden = !message;
    $("hearts-claim").replaceChildren(...claimCards(game));
    $("hearts-claim").hidden = !game.claim;
    renderResult(game, state, view);
    $("hearts-hands").replaceChildren(...myHands(game, view).map((hand) => handPanel(game, state, view, hand)));
    renderTrickTable(view, TABLE_SHOWN_KEY, TABLE_IDS,
        `${t("Hand {n}", { n: game.handNumber })} · ${t("To {n} points", { n: game.target })}`,
        game.players.map((p) => playerItem(game, state, view, p)));
    $("next-round").hidden = !(view.isPlayer && game.phase === "HandOver");
});

$("next-round").addEventListener("click", () => send("NextRound", currentGame?.handNumber ?? 0));
wireTrickTable(TABLE_SHOWN_KEY, TABLE_IDS.toggle, () => hearts.refresh());
