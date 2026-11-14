"use strict";

// Wazoo: 1 to 8 players take turns in seat order for 13 rounds. On their turn, a player rolls the five dice up to three
// times, tapping dice to keep them between rolls, then taps an open box of their scorecard to score the dice there.
// Everyone sees the dice and every scorecard; the open boxes of the player to play show what the dice would score.

const SVG_NS = "http://www.w3.org/2000/svg";
const ROUNDS = 13;
const ROLLS = 3;

// The boxes, in the server's order, each with its name and a short note on how it scores. The first 6 are the upper section.
const BOXES = [
    { key: "Ones", label: "Ones" }, { key: "Twos", label: "Twos" }, { key: "Threes", label: "Threes" },
    { key: "Fours", label: "Fours" }, { key: "Fives", label: "Fives" }, { key: "Sixes", label: "Sixes" },
    { key: "ThreeOfAKind", label: "Three of a kind", note: "Total of all dice" },
    { key: "FourOfAKind", label: "Four of a kind", note: "Total of all dice" },
    { key: "FullHouse", label: "Full house", note: "25" },
    { key: "SmallStraight", label: "Small straight", note: "30" },
    { key: "LargeStraight", label: "Large straight", note: "40" },
    { key: "Wazoo", label: "Wazoo", note: "50" },
    { key: "Chance", label: "Chance", note: "Total of all dice" },
];
const UPPER = 6;
const boxName = (key) => t(BOXES.find((box) => box.key === key).label);

function svg(tag, attributes = {}) {
    const element = document.createElementNS(SVG_NS, tag);
    Object.entries(attributes).forEach(([name, value]) => element.setAttribute(name, String(value)));
    return element;
}

// Dots on a die face, on a 3 by 3 grid.
const PIPS = {
    1: [[1, 1]], 2: [[0, 0], [2, 2]], 3: [[0, 0], [1, 1], [2, 2]], 4: [[0, 0], [2, 0], [0, 2], [2, 2]],
    5: [[0, 0], [2, 0], [1, 1], [0, 2], [2, 2]], 6: [[0, 0], [2, 0], [0, 1], [2, 1], [0, 2], [2, 2]],
};

// A die face showing value (1 to 6), or a blank face for 0 (not rolled yet).
function dieFace(value) {
    const face = svg("svg", { viewBox: "0 0 100 100", "aria-hidden": "true" });
    face.append(svg("rect", { x: 5, y: 5, width: 90, height: 90, rx: 18 }));
    (PIPS[value] ?? []).forEach(([col, row]) => face.append(svg("circle", { cx: 50 + (col - 1) * 27, cy: 50 + (row - 1) * 27, r: 9 })));
    return face;
}

// The rolling animation: the dice that were not kept tumble and bounce for a moment, with the sound of the dice (of a
// single die when only one was rolled), when the state shows a new roll (not when the page first opens). The open boxes
// show what the dice score only once the dice have stopped.
const ROLL_MS = 800;
let seenRolls = null;
let rollingUntil = 0;
let rollTimer = 0;

function noticeRoll(game) {
    if (seenRolls !== null && game.rolls > seenRolls) {
        playGameSound(game.held.filter((held) => !held).length === 1 ? "die" : "dice");
        rollingUntil = Date.now() + ROLL_MS;
        clearInterval(rollTimer);
        rollTimer = setInterval(() => {
            wazoo.refresh();
            if (Date.now() >= rollingUntil) clearInterval(rollTimer);
        }, 90);
    }
    seenRolls = game.rolls;
}

// The status line and the message about the last turn. The status starts with 👉 when the viewer must act (the status bar
// then turns gold), ⏳ while they wait, and 🏆 or 🤝 for the result. Each message has a form for the viewer ("You
// scored…") and one for a named player ("Dana scored…"); a user holding several seats sees the named forms.
function wazooTexts(game, state, view) {
    const name = (seat) => state.seats[seat]?.name ?? t("Seat {n}", { n: seat + 1 });
    const several = view.mySeats.length > 1;
    const about = (seat, you, other, values = {}) =>
        (view.isMe(seat) && !several ? t(you, values) : t(other, { name: name(seat), ...values }));
    let status;
    if (game.isOver) {
        const best = game.players.find((p) => p.seat === game.winners[0]).total;
        status = game.players.length === 1 ? `🏆 ${t("Game over: {n} points.", { n: best })}`
            : game.winners.length === 1 ? `🏆 ${about(game.winners[0], "You win with {n} points!", "{name} wins with {n} points!", { n: best })}`
                : `🤝 ${t("A tie at {n} points: {names}.", { n: best, names: game.winners.map(name).join(", ") })}`;
    } else if (!view.isMe(game.turn)) {
        status = `⏳ ${t("Waiting for {name}", { name: name(game.turn) })}`;
    } else {
        const action = game.rollsLeft === ROLLS ? t("Roll the dice")
            : game.rollsLeft > 0 ? t("Keep dice and roll again ({n} left), or choose a box", { n: game.rollsLeft })
                : t("Choose a box to score");
        status = `👉 ${several ? `${name(game.turn)}: ` : ""}${action}`;
    }

    let message = "";
    const last = game.lastTurn;
    if (last) {
        message = about(last.seat, "You scored {points} in {box}.", "{name} scored {points} in {box}.",
            { points: last.points, box: boxName(last.category) });
        if (last.bonus) {
            message += ` ${t("Wazoo bonus: +100!")}`;
        }
    }
    return { status, message };
}

// The five dice. On the viewer's turn, between rolls, tapping a die keeps it (or lets it go again).
function renderDice(game, canAct, rolling) {
    const canHold = canAct && game.rollsLeft > 0 && game.rollsLeft < ROLLS;
    $("yz-dice").replaceChildren(...game.dice.map((value, i) => {
        const held = game.held[i];
        const tumbling = rolling && !held && value > 0;
        const die = el("button", "yz-die", dieFace(tumbling ? 1 + Math.floor(Math.random() * 6) : value));
        die.type = "button";
        die.disabled = !canHold;
        die.classList.toggle("held", held);
        die.classList.toggle("blank", value === 0);
        die.classList.toggle("tumbling", tumbling);
        die.setAttribute("aria-pressed", String(held));
        die.setAttribute("aria-label", value ? t("Die showing {n}", { n: value }) : t("Not rolled yet"));
        if (held) {
            die.append(el("span", "yz-die-label", t("Kept")));
        }
        die.addEventListener("click", () => call("HoldDie", wazoo.sessionId, game.turn, i, !held).catch(() => {}));
        return die;
    }));
}

// Scoring 0 in a box asks first, since it is easy to tap by mistake.
function scoreBox(game, box, points) {
    if (points === 0 && !confirm(t("Score 0 in {box}?", { box: t(box.label) }))) return;
    call("Score", wazoo.sessionId, game.turn, box.key).catch(() => {});
}

// The scorecard: a column per player (the player to play is highlighted), a row per box, and the totals. The open boxes
// of the player to play show what the dice would score there; on the viewer's turn, tapping one scores it.
function renderCard(game, state, view, canAct, rolling) {
    const turnClass = (player) => (!game.isOver && player.seat === game.turn ? "turn" : null);
    const rowHead = (text, note) => {
        const th = el("th", null, text);
        th.scope = "row";
        if (note) th.append(el("small", null, note));
        return th;
    };
    const head = el("tr", null, el("th"), ...game.players.map((player) => {
        const th = el("th", turnClass(player), seatLabel(state, player.seat, view));
        th.scope = "col";
        th.classList.toggle("winner", game.winners.includes(player.seat));
        return th;
    }));
    const boxRow = (box, i) => el("tr", null, rowHead(t(box.label), box.note && t(box.note)), ...game.players.map((player) => {
        const td = el("td", turnClass(player));
        const score = player.scores[i];
        const option = player.seat === game.turn && !rolling ? game.possibleScores[i] : null;
        if (score !== null) {
            td.textContent = String(score);
        } else if (option !== null && canAct) {
            const button = el("button", option === 0 ? "yz-option zero" : "yz-option", String(option));
            button.type = "button";
            button.title = t("Score {points} in {box}", { points: option, box: t(box.label) });
            button.addEventListener("click", () => scoreBox(game, box, option));
            td.append(button);
        } else if (option !== null) {
            td.append(el("span", option === 0 ? "yz-option preview zero" : "yz-option preview", String(option)));
        }
        return td;
    }));
    const sumRow = (label, note, value, className) => el("tr", className,
        rowHead(label, note), ...game.players.map((player) => el("td", turnClass(player), String(value(player)))));
    const plus = (points) => (points ? `+${points}` : "–");
    $("yz-card").replaceChildren(
        el("thead", null, head),
        el("tbody", null,
            ...BOXES.slice(0, UPPER).map((box, i) => boxRow(box, i)),
            sumRow(t("Upper total"), null, (player) => player.upperTotal, "yz-sum"),
            sumRow(t("Bonus"), t("35 for 63 or more"), (player) => plus(player.upperBonus), "yz-sum"),
            ...BOXES.slice(UPPER).map((box, i) => boxRow(box, UPPER + i)),
            sumRow(t("Wazoo bonus"), t("100 for each extra Wazoo"), (player) => plus(player.wazooBonus), "yz-sum"),
            sumRow(t("Total"), null, (player) => player.total, "yz-sum yz-total")));
}

let current = null;

// A box scored: the chip's clink, and the new score drops into its box.
function noticeScore(game, view) {
    const last = game.lastTurn;
    if (!view.changed("score", [game.round, last?.seat, last?.category]) || !last) return;
    playGameSound("chip");
    const column = game.players.findIndex((p) => p.seat === last.seat);
    const row = BOXES.findIndex((box) => box.key === last.category);
    const rows = $("yz-card").tBodies[0]?.rows;
    const index = row < UPPER ? row : row + 2;
    rows?.[index]?.cells[column + 1]?.classList.add("scored");
}

const wazoo = startSessionPage("Wazoo", (game, state, view) => {
    noticeRoll(game);
    const rolling = Date.now() < rollingUntil;
    const canAct = !game.isOver && view.isMe(game.turn);
    current = { game, canAct };

    showScores(state, view, [game.isOver ? t("Final scores") : t("Round {n} of {total}", { n: game.round, total: ROUNDS })],
        game.players.map((player) => ({ seat: player.seat, score: player.total, acting: !game.isOver && player.seat === game.turn })));
    const { status, message } = wazooTexts(game, state, view);
    $("status").textContent = status;
    // Confetti for the winner, and for a player alone at the end of their game.
    if (game.isOver && game.winners.some((seat) => view.isMe(seat))) {
        celebrate(state, "over");
    }
    $("yz-message").textContent = message;
    $("yz-message").hidden = !message;
    renderDice(game, canAct, rolling);
    const roll = $("yz-roll");
    roll.hidden = !(canAct && game.rollsLeft > 0);
    roll.title = game.rollsLeft === ROLLS ? t("Roll") : t("Roll again ({n} left)", { n: game.rollsLeft });
    roll.setAttribute("aria-label", roll.title);
    renderCard(game, state, view, canAct, rolling);
    noticeScore(game, view);
});

$("yz-roll").addEventListener("click", () => {
    if (current?.canAct) call("RollDice", wazoo.sessionId, current.game.turn).catch(() => {});
});
