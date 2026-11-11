"use strict";

// Dominoes: in Block, Draw and All Fives, each player in turn adds a tile matching an end of the line (a double-six
// set); in Mexican Train (double-twelve), each builds their own train from the round's engine, or the Mexican train, or
// a train with a marker. Each player's own tiles come in view.private. On top, the players and the board; below, the
// user's hands. Tiles are text such as "3-5" (in a line or train, in the direction laid). Texts about seats and the
// result lines come from tricks.js.

const MEXICAN = -1;
// Which of the 3 × 3 spots of a tile's half have a pip, for 0 to 6; higher values (double-twelve) show their number.
const PIP_SPOTS = { 0: [], 1: [4], 2: [0, 8], 3: [0, 4, 8], 4: [0, 2, 6, 8], 5: [0, 2, 4, 6, 8], 6: [0, 2, 3, 5, 6, 8] };

// The tile chosen in a hand that can go to more than one place: its seat and tile.
let chosen = { seat: null, tile: null };
let currentGame = null;

const ends = (text) => text.split("-").map(Number);
const playerOf = (game, seat) => game.players.find((p) => p.seat === seat);
const myHands = (game, view) => (view.private?.hands ?? []).filter((h) => playerOf(game, h.seat));
const sideSeats = (game, side) => game.sideScores[side].seats;
const sideNames = (state, game, side) => sideSeats(game, side).map((seat) => trickPlayerName(state, seat)).join(" & ");

function send(method, ...args) {
    chosen = { seat: null, tile: null };
    call(method, dominoes.sessionId, ...args).catch(() => {});
}

// A tile, its two halves side by side, each with its pips (or its number above 6).
function dominoTile(text) {
    const tile = el("span", "domino", ...ends(text).map((value) => {
        if (!PIP_SPOTS[value]) {
            return el("span", "domino-half number", String(value));
        }
        return el("span", "domino-half", ...Array.from({ length: 9 }, (_, i) => el("span", PIP_SPOTS[value].includes(i) ? "pip on" : "pip")));
    }));
    tile.classList.toggle("double", ends(text)[0] === ends(text)[1]);
    tile.setAttribute("role", "img");
    tile.setAttribute("aria-label", text);
    return tile;
}

// The places the chosen tile of a hand can go, if it is the one chosen.
const chosenTargets = (hand) => (chosen.seat === hand.seat ? hand.moves.filter((m) => m.tile === chosen.tile).map((m) => m.target) : []);

function pickTile(hand, tile) {
    const targets = [...new Set(hand.moves.filter((m) => m.tile === tile).map((m) => m.target))];
    if (targets.length === 1) {
        send("PlayTile", hand.seat, tile, targets[0]);
        return;
    }
    chosen = chosen.seat === hand.seat && chosen.tile === tile ? { seat: null, tile: null } : { seat: hand.seat, tile };
    dominoes.refresh();
}

// The hand to play now, if the user holds it.
const handToPlay = (game, view) => (game.phase === "Playing" ? myHands(game, view).find((h) => h.seat === game.turn) ?? null : null);

// A target button for the chosen tile: "Left end", "Right end" or "Play here".
function targetButton(hand, target, text) {
    return trickButton(text, () => send("PlayTile", hand.seat, chosen.tile, target), "small domino-target");
}

// The status line: 👉 when the viewer must act, ⏳ while they wait, 🏁 after a round and 🏆 for the winners.
function statusText(game, state, view) {
    if (game.phase === "GameOver") {
        const mine = game.winners.some((side) => sideSeats(game, side).some((seat) => view.isMe(seat)));
        return `🏆 ${game.teams ? (mine ? t("Your team wins the game!") : t("{names} win the game!", { names: game.winners.map((s) => sideNames(state, game, s)).join(", ") }))
            : game.winners.map((side) => aboutSeat(state, view, sideSeats(game, side)[0], "You win!", "{name} wins!")).join(" ")}`;
    }
    if (game.phase === "RoundOver") {
        return `🏁 ${t("Round {n} is over.", { n: game.roundNumber })}`;
    }
    const hand = handToPlay(game, view);
    if (!hand) {
        return `⏳ ${t("Waiting for {name}", { name: trickPlayerName(state, game.turn) })}`;
    }
    const prefix = view.mySeats.length > 1 ? `${trickPlayerName(state, hand.seat)}: ` : "";
    const action = hand.moves.length > 0 ? (chosenTargets(hand).length > 0 ? t("Choose where to play {tile}.", { tile: chosen.tile }) : t("Play a tile."))
        : hand.canDraw ? t("You cannot play: draw a tile.") : t("You cannot play: pass.");
    return `👉 ${prefix}${action}`;
}

// What happened in the last action, in a sentence.
function messageText(game, state, view) {
    const action = game.lastAction;
    if (!action || game.phase !== "Playing") return "";
    const say = (you, other, values) => aboutSeat(state, view, action.seat, you, other, values);
    switch (action.kind) {
        case "Played":
            return action.points > 0
                ? say("You played {tile} and scored {n}.", "{name} played {tile} and scored {n}.", { tile: action.tile, n: action.points })
                : say("You played {tile}.", "{name} played {tile}.", { tile: action.tile });
        case "Drew": return say("You drew a tile.", "{name} drew a tile.");
        case "Passed": return say("You passed.", "{name} passed.");
        default: return say("Your turn was skipped.", "{name}'s turn was skipped.");
    }
}

// The line (line styles), with the ends to choose for the chosen tile; or the engine and every train (Mexican Train),
// each with Play here for the chosen tile.
function renderBoard(game, state, view) {
    const hand = handToPlay(game, view);
    const targets = hand ? chosenTargets(hand) : [];
    if (game.style !== "MexicanTrain") {
        const line = el("div", "domino-line", ...game.line.map(dominoTile));
        const endButtons = el("div", "domino-ends",
            ...(targets.includes(0) ? [targetButton(hand, 0, `◀ ${t("Left end")}`)] : [el("span")]),
            ...(targets.includes(1) ? [targetButton(hand, 1, `${t("Right end")} ▶`)] : []));
        $("dominoes-board").replaceChildren(line, endButtons);
        return;
    }
    const engine = el("div", "domino-engine", t("Engine"), dominoTile(`${game.engine}-${game.engine}`));
    $("dominoes-board").replaceChildren(engine, ...game.trains.map((train) => {
        const target = train.seat ?? MEXICAN;
        const owner = train.seat === null ? el("strong", null, t("Mexican train")) : seatLabel(state, train.seat, view);
        const row = el("div", "domino-train", owner, ...(train.marker ? [el("span", "rp-tag", `🚂 ${t("Open")}`)] : []),
            el("span", "domino-train-tiles", ...train.tiles.slice(-8).map(dominoTile)));
        row.classList.toggle("marker", train.marker);
        if (targets.includes(target)) row.append(targetButton(hand, target, t("Play here")));
        return row;
    }));
}

// The players: name, tiles in hand, and whose turn it is.
function renderPlayers(game, state, view) {
    $("dominoes-players").replaceChildren(...game.players.map((p) => {
        const item = el("div", "trick-player", seatLabel(state, p.seat, view), el("span", "rp-tag", `🁢 ${p.tileCount}`));
        item.classList.toggle("turn", game.phase === "Playing" && game.turn === p.seat);
        return item;
    }));
}

// After a round: for each side, its players' tiles left with their pips, who played their last tile (Domino!) or had the
// fewest pips after a block, the points gained and the score.
function renderResult(game, state, view) {
    const result = game.lastResult;
    const shown = game.phase !== "Playing" && result && result.round === game.roundNumber;
    $("dominoes-result").hidden = !shown;
    $("dominoes-result").replaceChildren(...(shown ? trickResult(game.sideScores.map((side) => {
        const players = result.players.filter((p) => side.seats.includes(p.seat));
        const domino = players.some((p) => p.seat === result.dominoSeat);
        return {
            who: players.flatMap((p) => [seatLabel(state, p.seat, view), el("span", "domino-row small", ...p.tiles.map(dominoTile)),
                el("span", "rp-tag", t("Pips: {n}", { n: p.pips }))]),
            tags: [
                ...(domino ? [[t("Domino!"), "finished"]] : []),
                ...(result.blocked && game.style !== "MexicanTrain" && result.points[side.side] > 0 ? [[t("Fewest pips"), "finished"]] : []),
                ...(game.winners.includes(side.side) ? [[`🏆 ${t("Winner")}`, "finished"]] : []),
            ],
            points: `+${result.points[side.side]} · ${t("{n} points", { n: result.scores[side.side] })}`,
        };
    })) : []));
}

// One of the user's hands: its tiles (on its turn, those it can play are buttons; the chosen one is raised), and under
// them Draw or Pass when it cannot play.
function handPanel(game, state, view, hand) {
    const myTurn = game.phase === "Playing" && game.turn === hand.seat;
    const playable = new Set(hand.moves.map((m) => m.tile));
    const tiles = hand.tiles.map((tile) => {
        const face = dominoTile(tile);
        face.classList.toggle("chosen", chosen.seat === hand.seat && chosen.tile === tile);
        if (!myTurn) return face;
        if (!playable.has(tile)) {
            face.classList.add("unplayable");
            return face;
        }
        const button = el("button", "domino-button", face);
        button.type = "button";
        button.setAttribute("aria-label", t("Play {card}", { card: tile }));
        button.addEventListener("click", () => pickTile(hand, tile));
        return button;
    });
    const title = myHands(game, view).length > 1 ? trickPlayerName(state, hand.seat) : t("Your tiles");
    const head = el("div", "my-hand-title", title, ...(myTurn ? [el("span", "rp-your-turn", `👉 ${t("Your turn")}`)] : []));
    const panel = el("div", `my-hand domino-hand${myTurn ? " my-turn" : ""}`, head, el("div", "domino-row", ...tiles));
    const foot = [];
    if (myTurn && hand.canDraw) foot.push(trickButton(t("Draw a tile"), () => send("DrawTile", hand.seat)));
    if (myTurn && hand.canPass) foot.push(trickButton(t("Pass"), () => send("PassTurn", hand.seat), "secondary"));
    if (foot.length > 0) panel.append(el("div", "trick-hand-foot", ...foot));
    return panel;
}

// The score row: the round (of the rounds in Mexican Train), the target, the boneyard, and each side's score (a team's
// on one line).
function renderScores(game, state, view) {
    const facts = [
        game.totalRounds ? t("Round {n} of {total}", { n: game.roundNumber, total: game.totalRounds }) : t("Round {n}", { n: game.roundNumber }),
        ...(game.totalRounds ? [] : [t("To {n} points", { n: game.target })]),
        t("Boneyard: {n}", { n: game.boneyard }),
        ...(game.teams ? game.sideScores.map((side) => `${sideNames(state, game, side.side)}: ${side.score}`) : []),
    ];
    showScores(state, view, facts, game.teams ? [] : game.sideScores.map((side) => ({
        seat: side.seats[0], score: side.score, acting: game.phase === "Playing" && side.seats[0] === game.turn,
    })));
}

// Confetti for the user's success: winning a round (playing out) or the game.
function noticeSuccess(game, state, view) {
    const mine = (sides) => sides.some((side) => sideSeats(game, side).some((seat) => view.isMe(seat)));
    if (game.phase === "GameOver" && mine(game.winners)) {
        celebrate(state, "won");
    } else if (game.phase === "RoundOver" && game.lastResult?.dominoSeat != null && view.isMe(game.lastResult.dominoSeat)) {
        celebrate(state, `round-${game.roundNumber}`);
    }
}

const dominoes = startSessionPage("Dominoes", (game, state, view) => {
    currentGame = game;
    const hand = handToPlay(game, view);
    if (!hand || chosenTargets(hand).length === 0) chosen = { seat: null, tile: null };
    renderScores(game, state, view);
    $("status").textContent = statusText(game, state, view);
    noticeSuccess(game, state, view);
    renderPlayers(game, state, view);
    renderBoard(game, state, view);
    const message = messageText(game, state, view);
    $("dominoes-message").textContent = message;
    $("dominoes-message").hidden = !message;
    renderResult(game, state, view);
    $("dominoes-hands").replaceChildren(...myHands(game, view).map((h) => handPanel(game, state, view, h)));
    $("next-round").hidden = !(view.isPlayer && game.phase === "RoundOver");
});

$("next-round").addEventListener("click", () => send("NextRound", currentGame?.roundNumber ?? 0));
