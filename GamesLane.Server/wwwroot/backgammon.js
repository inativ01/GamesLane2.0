"use strict";

// Backgammon: seat 0 plays white, seat 1 black. Each player sees the board from their own side of the table: black's
// home board is at the bottom right, and for white the whole board is turned around (rotated, not mirrored), so white's
// home board is at the bottom left. Spectators see white's side; a user holding both seats sees the side of the seat that
// must act. Points are numbered from the mover's side: 1 to 24, 25 for the bar and 0 for off the board. The board is an
// SVG drawing that scales to the screen; it is laid out from black's side, and turned for white.

const COLORS = ["White", "Black"];
const SVG_NS = "http://www.w3.org/2000/svg";
const BAR = 25;
const OFF = 0;

// Board geometry, in SVG units.
const PW = 56;                          // width of a point
const FRAME = 16;
const HALF = 6 * PW;
const BAR_W = 56;
const TRAY_W = 64;
const POINT_H = 250;
const GAP = 70;
const R = 25;                           // checker radius
const LEFT = FRAME;
const RIGHT = FRAME + HALF + BAR_W;
const BOARD_W = RIGHT + HALF + FRAME;
const WIDTH = BOARD_W + TRAY_W;
const HEIGHT = FRAME + POINT_H + GAP + POINT_H + FRAME;
const BAR_X = LEFT + HALF + BAR_W / 2;
const MID_Y = HEIGHT / 2;

// Whether the board being drawn is turned around (for white).
let turned = false;

function svg(tag, attributes = {}, ...children) {
    const element = document.createElementNS(SVG_NS, tag);
    Object.entries(attributes).forEach(([name, value]) => element.setAttribute(name, String(value)));
    element.append(...children);
    return element;
}

// Text at (x, y) that stays upright when the board is turned around.
function text(x, y, content, className) {
    const element = svg("text", { x, y, "text-anchor": "middle", class: className }, content);
    if (turned) {
        element.setAttribute("transform", `rotate(180 ${x} ${y - 7})`);
    }
    return element;
}

// The x of the middle of black's point p (1 to 24), before the board is turned around.
function pointX(p) {
    if (p <= 6) return RIGHT + HALF - (p - 0.5) * PW;
    if (p <= 12) return LEFT + HALF - (p - 6.5) * PW;
    if (p <= 18) return LEFT + (p - 12.5) * PW;
    return RIGHT + (p - 18.5) * PW;
}

const isBottom = (p) => p <= 12;

// The y of the middle of the k-th checker (from 0) on point p.
const checkerY = (p, k) => (isBottom(p) ? HEIGHT - FRAME - R - k * 2 * R : FRAME + R + k * 2 * R);

// The seat's point p as black numbers it (the bar and off the board stay the same).
const toBlack = (p, seat) => (seat === 1 || p === BAR || p === OFF ? p : 25 - p);

// Checkers on black's point p: { seat, count }, or null if empty. Points[] is from white's side.
function checkersAt(game, p) {
    const value = game.points[24 - p];
    return value === 0 ? null : { seat: value > 0 ? 0 : 1, count: Math.abs(value) };
}

function checker(seat, x, y, label = "") {
    const group = svg("g", { class: `bg-checker ${seat === 0 ? "white" : "black"}` },
        svg("circle", { cx: x, cy: y, r: R - 1.5 }),
        svg("circle", { cx: x, cy: y, r: R * 0.62, class: "bg-checker-ring" }));
    if (label) {
        group.append(text(x, y + 7, label, "bg-checker-count"));
    }
    return group;
}

// The board's frame, felt, points, bar and tray.
function drawBoard() {
    const parts = [
        svg("rect", { x: 0, y: 0, width: WIDTH, height: HEIGHT, rx: 14, class: "bg-wood" }),
        svg("rect", { x: LEFT, y: FRAME, width: HALF, height: HEIGHT - 2 * FRAME, class: "bg-felt" }),
        svg("rect", { x: RIGHT, y: FRAME, width: HALF, height: HEIGHT - 2 * FRAME, class: "bg-felt" }),
        svg("rect", { x: BOARD_W, y: FRAME, width: TRAY_W - FRAME, height: HEIGHT - 2 * FRAME, rx: 6, class: "bg-tray" }),
    ];
    for (let p = 1; p <= 24; p++) {
        const x = pointX(p);
        const base = isBottom(p) ? HEIGHT - FRAME : FRAME;
        const tip = isBottom(p) ? base - POINT_H : base + POINT_H;
        parts.push(svg("polygon", {
            points: `${x - PW / 2 + 2},${base} ${x + PW / 2 - 2},${base} ${x},${tip}`,
            class: `bg-point ${p % 2 ? "dark" : "light"}`,
            "data-point": p,
        }));
    }
    return parts;
}

// Dots on a die face, on a 3 by 3 grid.
const PIPS = {
    1: [[1, 1]], 2: [[0, 0], [2, 2]], 3: [[0, 0], [1, 1], [2, 2]], 4: [[0, 0], [2, 0], [0, 2], [2, 2]],
    5: [[0, 0], [2, 0], [1, 1], [0, 2], [2, 2]], 6: [[0, 0], [2, 0], [0, 1], [2, 1], [0, 2], [2, 2]],
};

function die(value, x, y, size, seat, used, tilt = 0) {
    const group = svg("g", { class: `bg-die ${seat === 0 ? "white" : "black"}${used ? " used" : ""}` },
        svg("rect", { x: x - size / 2, y: y - size / 2, width: size, height: size, rx: size * 0.18 }));
    PIPS[value].forEach(([col, row]) => group.append(svg("circle", {
        cx: x + (col - 1) * size * 0.28, cy: y + (row - 1) * size * 0.28, r: size * 0.09,
    })));
    if (tilt) {
        group.setAttribute("transform", `rotate(${tilt} ${x} ${y})`);
    }
    return group;
}

// The doubling cube: in the middle of the bar, or at the owner's end of the bar (black's at the bottom). When the viewer
// may double, it is outlined, and tapping it offers to double.
function cube(game, mayDouble) {
    const y = game.cubeOwner === null ? MID_Y : game.cubeOwner === 1 ? HEIGHT - FRAME - 26 : FRAME + 26;
    const group = svg("g", { class: `bg-cube${mayDouble ? " can-double" : ""}`, "data-action": "cube" },
        svg("rect", { x: BAR_X - 22, y: y - 22, width: 44, height: 44, rx: 7 }),
        text(BAR_X, y + 8, String(game.cubeValue === 1 ? 64 : game.cubeValue), "bg-cube-value"));
    return group;
}

// A coloured overlay on black's point p (1 to 24), on the bar (25) or on the tray (0).
function highlight(p, className) {
    if (p === BAR) {
        return svg("rect", { x: BAR_X - BAR_W / 2 + 4, y: FRAME, width: BAR_W - 8, height: HEIGHT - 2 * FRAME, class: className });
    }
    if (p === OFF) {
        return svg("rect", { x: BOARD_W, y: FRAME, width: TRAY_W - FRAME, height: HEIGHT - 2 * FRAME, rx: 6, class: className });
    }
    const x = pointX(p);
    const base = isBottom(p) ? HEIGHT - FRAME : FRAME;
    const tip = isBottom(p) ? base - POINT_H : base + POINT_H;
    return svg("polygon", { points: `${x - PW / 2 + 2},${base} ${x + PW / 2 - 2},${base} ${x},${tip}`, class: className });
}

// The last checker moved (this turn's, or the last turn's) as { seat, move }, or null.
function lastMove(game) {
    if (game.played.length) {
        return { seat: game.turn, move: game.played[game.played.length - 1] };
    }
    const last = game.lastTurn;
    return last?.moves.length ? { seat: last.seat, move: last.moves[last.moves.length - 1] } : null;
}

let selected = null;
let lastArrival = "";

// The dice rolling animation: until when it runs, and the seats whose dice are rolling.
const ROLL_MS = 1000;
let rollingUntil = 0;
let rollingSeats = new Set();
let rollTimer = 0;
let seenRolls = null;
let seenOpening = [null, null];

// Starts the one-second rolling animation when the state shows a new roll (not when the page first opens).
function noticeRoll(game) {
    const opening = game.phase === "Opening" || (game.openingRoll && !game.played.length);
    if (seenRolls !== null && game.rolls !== seenRolls) {
        // After a tie, any opening die shown is a new roll; otherwise, the one that was not there before.
        const wasTie = seenOpening[0] !== null && seenOpening[0] === seenOpening[1];
        rollingSeats = opening
            ? new Set([0, 1].filter((s) => game.openingDice[s] !== null && (wasTie || seenOpening[s] === null)))
            : new Set([game.dice.length ? game.turn : game.lastTurn?.seat]);
        rollingUntil = Date.now() + ROLL_MS;
        clearInterval(rollTimer);
        rollTimer = setInterval(() => {
            backgammon.refresh();
            if (Date.now() >= rollingUntil) clearInterval(rollTimer);
        }, 90);
    }
    seenRolls = game.rolls;
    seenOpening = [...game.openingDice];
}

// The dice on the board: each seat's dice on its own half (black's on the right before the board is turned). During the
// opening roll and its first turn, each player's one die; then the mover's dice, with used ones faded; between turns,
// the last roll, faded. Rolling dice show random faces and tumble.
function drawDice(game) {
    const parts = [];
    const rolling = Date.now() < rollingUntil;
    const sideX = (seat) => (seat === 1 ? RIGHT + HALF / 2 : LEFT + HALF / 2);
    const row = (values, seat, used) => {
        const tumbling = rolling && rollingSeats.has(seat);
        const size = values.length === 4 ? 36 : 46;
        values.forEach((value, i) => parts.push(die(
            tumbling ? 1 + Math.floor(Math.random() * 6) : value,
            sideX(seat) + (i - (values.length - 1) / 2) * (size + 10), MID_Y, size, seat,
            !tumbling && used[i], tumbling ? Math.random() * 50 - 25 : 0)));
    };
    const usedOf = (values) => {
        const left = [...game.diceLeft];
        return values.map((value) => {
            const at = left.indexOf(value);
            if (at >= 0) left.splice(at, 1);
            return at < 0;
        });
    };
    if (game.phase === "Opening" || (game.openingRoll && game.phase === "Moving")) {
        const used = game.openingRoll ? usedOf(game.openingRoll) : [false, false];
        [0, 1].filter((s) => game.openingDice[s] !== null).forEach((s) => row([game.openingDice[s]], s, [used[s]]));
    } else if (game.dice.length) {
        const all = game.dice[0] === game.dice[1] ? [...game.dice, ...game.dice] : game.dice;
        row(all, game.turn, usedOf(all));
    } else if (game.lastTurn) {
        row(game.lastTurn.dice, game.lastTurn.seat, [true, true]);
    }
    return parts;
}

// The whole board for the current state, laid out from black's side and turned around for white. When canMove, the
// viewer is the seat to move: its movable checkers, and the destinations of the selected one, are marked. Points tapped
// are reported in the viewer's numbering. When mayDouble, the viewer may double with the cube.
function drawGame(game, viewer, canMove, mayDouble) {
    turned = viewer === 0;
    const root = svg("svg", { viewBox: `0 0 ${WIDTH} ${HEIGHT}`, class: "bg-svg", role: "img", "aria-label": t("Backgammon board") });
    const board = svg("g", turned ? { transform: `rotate(180 ${WIDTH / 2} ${HEIGHT / 2})` } : {});
    root.append(board);
    board.append(...drawBoard());

    // The last turn's moves (faint) and this turn's moves so far.
    const marked = (seat, moves, className) => moves.forEach((m) =>
        [m.from, m.to].forEach((p) => board.append(highlight(toBlack(p, seat), className))));
    if (game.lastTurn && !game.played.length) {
        marked(game.lastTurn.seat, game.lastTurn.moves, "bg-last");
    }
    marked(game.turn, game.played, "bg-last current");

    const moves = canMove ? game.legalMoves : [];
    const mover = (p) => toBlack(p, game.turn);
    if (selected === null) {
        new Set(moves.map((m) => m.from)).forEach((p) => board.append(highlight(mover(p), "bg-movable")));
    } else {
        board.append(highlight(mover(selected), "bg-selected"));
    }

    // Checkers on the points: up to 5 in a stack, with the count on the top one if there are more.
    const arrival = lastMove(game);
    const arrivalKey = arrival ? JSON.stringify([game.gameNumber, game.played.length, game.lastTurn, arrival]) : "";
    const arrivalPoint = arrival && arrivalKey !== lastArrival ? toBlack(arrival.move.to, arrival.seat) : null;
    lastArrival = arrivalKey;
    for (let p = 1; p <= 24; p++) {
        const stack = checkersAt(game, p);
        if (!stack) continue;
        const shown = Math.min(stack.count, 5);
        for (let k = 0; k < shown; k++) {
            const top = k === shown - 1;
            const piece = checker(stack.seat, pointX(p), checkerY(p, k), top && stack.count > 5 ? String(stack.count) : "");
            if (top && p === arrivalPoint) piece.classList.add("arrived");
            board.append(piece);
        }
    }

    // The bar: black's checkers below the middle, white's above it.
    [0, 1].forEach((seat) => {
        const count = game.bar[seat];
        const direction = seat === 1 ? 1 : -1;
        for (let k = 0; k < Math.min(count, 3); k++) {
            const label = k === Math.min(count, 3) - 1 && count > 3 ? String(count) : "";
            board.append(checker(seat, BAR_X, MID_Y + direction * (58 + k * 2 * R), label));
        }
    });

    // The tray: borne-off checkers as thin slices, black's from the bottom, white's from the top.
    [0, 1].forEach((seat) => {
        for (let k = 0; k < game.off[seat]; k++) {
            const y = seat === 1 ? HEIGHT - FRAME - 16 - k * 13 : FRAME + 6 + k * 13;
            board.append(svg("rect", { x: BOARD_W + 6, y, width: TRAY_W - FRAME - 12, height: 10, rx: 3, class: `bg-off ${seat === 0 ? "white" : "black"}` }));
        }
    });

    // Where the selected checker can go.
    moves.filter((m) => m.from === selected).forEach((m) => {
        if (m.to === OFF) {
            board.append(highlight(OFF, "bg-target-tray"));
        } else {
            const to = mover(m.to);
            const slot = Math.min(checkersAt(game, to)?.count ?? 0, 4);
            board.append(svg("circle", { cx: pointX(to), cy: checkerY(to, slot), r: R * 0.55, class: "bg-target" }));
        }
    });

    board.append(...drawDice(game));

    // Tap areas: each point's half of the board, the bar and the tray; then the cube on top.
    const tapped = (p) => (viewer === 1 || p === BAR || p === OFF ? p : 25 - p);
    for (let p = 1; p <= 24; p++) {
        board.append(svg("rect", {
            x: pointX(p) - PW / 2, y: isBottom(p) ? MID_Y : FRAME, width: PW, height: MID_Y - FRAME, class: "bg-hit", "data-target": tapped(p),
        }));
    }
    board.append(svg("rect", { x: BAR_X - BAR_W / 2, y: FRAME, width: BAR_W, height: HEIGHT - 2 * FRAME, class: "bg-hit", "data-target": BAR }));
    board.append(svg("rect", { x: BOARD_W, y: FRAME, width: TRAY_W - FRAME, height: HEIGHT - 2 * FRAME, class: "bg-hit", "data-target": OFF }));
    if (game.cubeEnabled) {
        board.append(cube(game, mayDouble));
    }
    return root;
}

// Whether both opening dice show the same number, so both players roll again.
const openingTie = (game) => game.openingDice[0] !== null && game.openingDice[0] === game.openingDice[1];

// The seat that must act now: the seat to move, or while a double is offered, the other seat (who takes or drops).
// During the opening roll, white until it has rolled, then black.
function actingSeat(game) {
    if (game.phase === "Opening") {
        return game.openingDice[0] === null || openingTie(game) ? 0 : 1;
    }
    return game.phase === "Doubled" ? 1 - game.turn : game.turn;
}

// During the opening roll, the viewer's seat that may roll its die now, or null.
function openingRoller(game, view) {
    if (game.phase !== "Opening") return null;
    return [0, 1].find((s) => view.isMe(s) && (game.openingDice[s] === null || openingTie(game))) ?? null;
}

// The side the board is seen from: the viewer's seat; with both seats, the one that must act; for spectators, white.
function viewerSeat(game, view) {
    const mine = view.mySeats.filter((s) => s === 0 || s === 1);
    return mine.length === 2 ? actingSeat(game) : mine[0] ?? 0;
}

// A move such as "13/7", "bar/22" or "6/off".
const moveText = (m) => `${m.from === BAR ? t("bar") : m.from}/${m.to === OFF ? t("off") : m.to}`;
const diceText = (dice) => dice.join("-");
const pointsText = (n) => (n === 1 ? t("1 point") : t("{n} points", { n }));
const colorName = (seat) => t(COLORS[seat]);

// The status line and the message under the board. Each text has a form for the viewer ("You rolled 5.") and one for a
// named player ("Dana rolled 5."); a user holding both seats sees the named forms.
function backgammonTexts(game, state, view) {
    const both = view.isMe(0) && view.isMe(1);
    const name = (seat) => state.seats[seat]?.name ?? colorName(seat);
    const about = (seat, you, other, values = {}) =>
        (view.isMe(seat) && !both ? t(you, values) : t(other, { name: name(seat), ...values }));
    const mine = (seat) => view.isMe(seat);
    const result = game.result;
    let status;
    switch (game.phase) {
        case "Opening": {
            const roller = openingRoller(game, view);
            const waiting = [0, 1].filter((s) => game.openingDice[s] === null || openingTie(game)).map(name);
            status = roller !== null
                ? both ? t("{color}: tap the board to roll your die: the higher die starts", { color: colorName(roller) })
                    : t("Tap the board to roll your die: the higher die starts")
                : waiting.length > 1 ? t("Waiting for {a} and {b} to roll their dice to see who starts", { a: waiting[0], b: waiting[1] })
                    : t("Waiting for {name} to roll a die to see who starts", { name: waiting[0] });
            break;
        }
        case "Rolling": {
            const color = colorName(game.turn);
            status = !mine(game.turn) ? t("{name} ({color}) to roll", { name: name(game.turn), color })
                : both && game.canDouble ? t("{color}: tap the board to roll, or the cube to double", { color })
                    : both ? t("{color}: tap the board to roll", { color })
                        : game.canDouble ? t("Your turn: tap the board to roll, or the cube to double")
                            : t("Your turn: tap the board to roll");
            break;
        }
        case "Doubled":
            status = `${about(game.turn, "You double to {n}.", "{name} doubles to {n}.", { n: game.cubeValue * 2 })} `
                + (mine(1 - game.turn) ? t("Take or drop?") : t("Waiting for {name}…", { name: name(1 - game.turn) }));
            break;
        case "Moving": {
            const dice = diceText(game.dice);
            status = !mine(game.turn) ? t("{name} ({color}) is moving: {dice}", { name: name(game.turn), color: colorName(game.turn), dice })
                : !game.legalMoves.length ? t("Press Done to end your turn")
                    : both ? `${colorName(game.turn)}: ${dice}` : t("Your move: {dice}", { dice });
            break;
        }
        case "GameOver":
            status = t("Game {n} is over. Next: game {next}.", { n: game.gameNumber, next: game.gameNumber + 1 });
            break;
        default:
            status = about(result.seat, "You win the match {a}–{b}!", "{name} wins the match {a}–{b}!",
                { a: game.scores[result.seat], b: game.scores[1 - result.seat] });
    }

    let message = "";
    if (game.phase === "Opening") {
        message = openingTie(game) ? t("Both rolled {n}: roll again.", { n: game.openingDice[0] })
            : [0, 1].filter((s) => game.openingDice[s] !== null)
                .map((s) => about(s, "You rolled {n}.", "{name} rolled {n}.", { n: game.openingDice[s] })).join(" ");
    } else if (result) {
        const points = pointsText(result.points);
        const loser = 1 - result.seat;
        const wins = {
            Single: ["You win the game: {points}.", "{name} wins the game: {points}."],
            Gammon: ["You win a gammon: {points}.", "{name} wins a gammon: {points}."],
            Backgammon: ["You win a backgammon: {points}.", "{name} wins a backgammon: {points}."],
        }[result.kind];
        message = result.kind !== "Dropped" ? about(result.seat, ...wins, { points })
            : mine(result.seat) && !both ? t("{name} dropped the double: you win {points}.", { name: name(loser), points })
                : mine(loser) && !both ? t("You dropped the double: {name} wins {points}.", { name: name(result.seat), points })
                    : t("{loser} dropped the double: {winner} wins {points}.", { loser: name(loser), winner: name(result.seat), points });
    } else if (game.openingRoll && !game.played.length && game.phase === "Moving") {
        message = about(game.turn, "Opening roll: White {white}, Black {black}. You start.",
            "Opening roll: White {white}, Black {black}. {name} starts.", { white: game.openingRoll[0], black: game.openingRoll[1] });
    } else if (game.lastTurn && (game.lastTurn.seat !== game.turn || game.phase !== "Moving")) {
        const last = game.lastTurn;
        const values = { dice: diceText(last.dice), moves: last.moves.map(moveText).join(" ") };
        message = !last.moves.length ? about(last.seat, "You rolled {dice} and could not move.", "{name} rolled {dice} and could not move.", values)
            : last.forced ? about(last.seat, "You rolled {dice}: the only possible move, {moves}, was played.",
                "{name} rolled {dice}: the only possible move, {moves}, was played.", values)
                : about(last.seat, "You played {dice}: {moves}.", "{name} played {dice}: {moves}.", values);
    }
    return { status, message };
}

// The two players: colour, name, match score and pip count, with the seat to act marked.
function scoreboard(game, state, view) {
    const acting = game.phase === "GameOver" || game.phase === "MatchOver" ? null : actingSeat(game);
    const header = listItem([t("Match to {points}", { points: pointsText(game.matchLength) }), t("Game {n}", { n: game.gameNumber }),
        game.isCrawford && t("Crawford game")].filter(Boolean).join(" · "));
    header.className = "bg-match";
    return [header, ...[0, 1].map((seat) => {
        const item = listItem(
            Object.assign(document.createElement("span"), { className: `bg-swatch ${seat === 0 ? "white" : "black"}` }),
            seatLabel(state, seat, view),
            Object.assign(document.createElement("strong"), { className: "bg-score", textContent: String(game.scores[seat]) }),
            Object.assign(document.createElement("span"), { className: "bg-pips", textContent: t("{n} pips", { n: game.pips[seat] }) }));
        item.classList.toggle("acting", seat === acting);
        return item;
    })];
}

// The whole page fits the screen without scrolling: the board shrinks by however much the page is too tall (down to
// 160 pixels high).
function fitBoard() {
    const board = document.querySelector("#bg-board .bg-svg");
    if (!board || $("game-screen").hidden) return;
    board.style.maxHeight = "none";
    const overflow = document.documentElement.scrollHeight - innerHeight;
    if (overflow > 0) {
        board.style.maxHeight = `${Math.max(160, board.getBoundingClientRect().height - overflow)}px`;
    }
}

let current = null;

const backgammon = startSessionPage("Backgammon", (game, state, view) => {
    noticeRoll(game);
    const viewer = viewerSeat(game, view);
    const canMove = game.phase === "Moving" && view.isMe(game.turn);
    if (!canMove || !game.legalMoves.some((m) => m.from === selected)) {
        selected = null;
    }
    current = { game, view, viewer, canMove };

    $("bg-players").replaceChildren(...scoreboard(game, state, view));
    $("bg-board").replaceChildren(drawGame(game, viewer, canMove, game.canDouble && view.isMe(game.turn)));
    const { status, message } = backgammonTexts(game, state, view);
    $("status").textContent = status;
    $("bg-message").textContent = message;
    $("bg-message").hidden = !message;

    const rolling = game.phase === "Rolling" && view.isMe(game.turn);
    const answering = game.phase === "Doubled" && view.isMe(1 - game.turn);
    $("bg-roll").hidden = !(rolling || openingRoller(game, view) !== null);
    $("bg-double").hidden = !(rolling && game.canDouble);
    $("bg-double").textContent = t("Double to {n}", { n: game.cubeValue * 2 });
    $("bg-take").hidden = !answering;
    $("bg-drop").hidden = !answering;
    $("bg-undo").hidden = !(canMove && game.played.length);
    $("bg-done").hidden = !(canMove && game.canEndTurn);
    $("next-round").hidden = !(view.isPlayer && game.phase === "GameOver");
    fitBoard();
});

addEventListener("resize", () => requestAnimationFrame(fitBoard));

// Tapping the board: a checker that can go to only one place moves there; otherwise it is selected and its
// destinations are marked. Tapping a marked destination moves the selected checker there.
function pick(p) {
    if (!current?.canMove) return;
    const { game } = current;
    const send = (m) => call("MoveChecker", backgammon.sessionId, game.turn, m.from, m.to).catch(() => {});
    const chosen = selected !== null && game.legalMoves.find((m) => m.from === selected && m.to === p);
    if (chosen) {
        selected = null;
        send(chosen);
        return;
    }
    const from = game.legalMoves.filter((m) => m.from === p);
    if (from.length === 1) {
        selected = null;
        send(from[0]);
        return;
    }
    selected = from.length ? p : null;
    backgammon.refresh();
}

// The seat for a roll now: the viewer's seat that may roll its opening die, or the seat to move if it is the viewer's
// turn to roll; otherwise null.
function rollerSeat() {
    if (!current) return null;
    const { game, view } = current;
    if (game.phase === "Opening") return openingRoller(game, view);
    return game.phase === "Rolling" && view.isMe(game.turn) ? game.turn : null;
}

const roll = () => {
    const seat = rollerSeat();
    if (seat !== null) call("Roll", backgammon.sessionId, seat).catch(() => {});
};

// Tapping the cube offers to double; tapping the board when it is the viewer's roll rolls the dice; otherwise taps on
// the points move checkers.
$("bg-board").addEventListener("click", (event) => {
    if (!current) return;
    const { game, view } = current;
    if (event.target.closest("[data-action='cube']") && game.canDouble && view.isMe(game.turn)) {
        call("OfferDouble", backgammon.sessionId, game.turn).catch(() => {});
        return;
    }
    if (rollerSeat() !== null) {
        roll();
        return;
    }
    const target = event.target.closest("[data-target]");
    if (target) pick(Number(target.dataset.target));
});

const act = (method) => () => {
    if (current) call(method, backgammon.sessionId, actingSeat(current.game)).catch(() => {});
};
$("bg-roll").addEventListener("click", roll);
$("bg-double").addEventListener("click", act("OfferDouble"));
$("bg-take").addEventListener("click", act("TakeDouble"));
$("bg-drop").addEventListener("click", act("DropDouble"));
$("bg-undo").addEventListener("click", act("UndoMove"));
$("bg-done").addEventListener("click", act("EndTurn"));
$("next-round").addEventListener("click", () => {
    if (current) call("NextRound", backgammon.sessionId, current.game.gameNumber).catch(() => {});
});
