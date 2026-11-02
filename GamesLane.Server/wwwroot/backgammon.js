"use strict";

// Backgammon: seat 0 plays white, seat 1 black. Each player sees the board from their own side: their home board at the
// bottom right, moving counter-clockwise and bearing off into the tray on the right. Spectators see white's side; a user
// holding both seats sees the side of the seat that must act. Points are numbered from the mover's side: 1 to 24, 25 for
// the bar and 0 for off the board. The board is an SVG drawing that scales to the screen.

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
const TRAY_X = BOARD_W + (TRAY_W - FRAME) / 2;
const MID_Y = HEIGHT / 2;

function svg(tag, attributes = {}, ...children) {
    const element = document.createElementNS(SVG_NS, tag);
    Object.entries(attributes).forEach(([name, value]) => element.setAttribute(name, String(value)));
    element.append(...children);
    return element;
}

// The x of the middle of point p (1 to 24, from the viewer's side) and whether it is on the bottom row.
function pointX(p) {
    if (p <= 6) return RIGHT + HALF - (p - 0.5) * PW;
    if (p <= 12) return LEFT + HALF - (p - 6.5) * PW;
    if (p <= 18) return LEFT + (p - 12.5) * PW;
    return RIGHT + (p - 18.5) * PW;
}

const isBottom = (p) => p <= 12;

// The y of the middle of the k-th checker (from 0) on point p.
const checkerY = (p, k) => (isBottom(p) ? HEIGHT - FRAME - R - k * 2 * R : FRAME + R + k * 2 * R);

// The viewer's point p as the given seat numbers it.
const forSeat = (p, seat, viewer) => (seat === viewer || p === BAR || p === OFF ? p : 25 - p);

// Checkers on the viewer's point p: { seat, count }, or null if empty. Points[] is from white's side.
function checkersAt(game, viewer, p) {
    const value = game.points[viewer === 0 ? p - 1 : 24 - p];
    return value === 0 ? null : { seat: value > 0 ? 0 : 1, count: Math.abs(value) };
}

function checker(seat, x, y, label = "") {
    const group = svg("g", { class: `bg-checker ${seat === 0 ? "white" : "black"}` },
        svg("circle", { cx: x, cy: y, r: R - 1.5 }),
        svg("circle", { cx: x, cy: y, r: R * 0.62, class: "bg-checker-ring" }));
    if (label) {
        group.append(svg("text", { x, y: y + 7, "text-anchor": "middle", class: "bg-checker-count" }, label));
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

function die(value, x, y, size, seat, used) {
    const group = svg("g", { class: `bg-die ${seat === 0 ? "white" : "black"}${used ? " used" : ""}` },
        svg("rect", { x: x - size / 2, y: y - size / 2, width: size, height: size, rx: size * 0.18 }));
    PIPS[value].forEach(([col, row]) => group.append(svg("circle", {
        cx: x + (col - 1) * size * 0.28, cy: y + (row - 1) * size * 0.28, r: size * 0.09,
    })));
    return group;
}

// The doubling cube: in the middle of the bar, or at the owner's end of the bar.
function cube(game, viewer) {
    const y = game.cubeOwner === null ? MID_Y : game.cubeOwner === viewer ? HEIGHT - FRAME - 26 : FRAME + 26;
    return svg("g", { class: "bg-cube" },
        svg("rect", { x: BAR_X - 22, y: y - 22, width: 44, height: 44, rx: 7 }),
        svg("text", { x: BAR_X, y: y + 8, "text-anchor": "middle" }, String(game.cubeValue === 1 ? 64 : game.cubeValue)));
}

// A coloured overlay on the viewer's point p (1 to 24), on the bar (25) or on the tray (0).
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

// The whole board for the current state, from the viewer's side. When canMove, the viewer is the seat to move: its
// movable checkers, and the destinations of the selected one, are marked.
function drawGame(game, viewer, canMove) {
    const root = svg("svg", { viewBox: `0 0 ${WIDTH} ${HEIGHT}`, class: "bg-svg", role: "img", "aria-label": "Backgammon board" });
    root.append(...drawBoard());

    // The last turn's moves (faint) and this turn's moves so far.
    const marked = (seat, moves, className) => moves.forEach((m) =>
        [m.from, m.to].forEach((p) => root.append(highlight(forSeat(p, seat, viewer), className))));
    if (game.lastTurn && !game.played.length) {
        marked(game.lastTurn.seat, game.lastTurn.moves, "bg-last");
    }
    marked(game.turn, game.played, "bg-last current");

    const moves = canMove ? game.legalMoves : [];
    if (selected === null) {
        new Set(moves.map((m) => m.from)).forEach((p) => root.append(highlight(p, "bg-movable")));
    } else {
        root.append(highlight(selected, "bg-selected"));
    }

    // Checkers on the points: up to 5 in a stack, with the count on the top one if there are more.
    const arrival = lastMove(game);
    const arrivalKey = arrival ? JSON.stringify([game.gameNumber, game.played.length, game.lastTurn, arrival]) : "";
    const arrivalPoint = arrival && arrivalKey !== lastArrival ? forSeat(arrival.move.to, arrival.seat, viewer) : null;
    lastArrival = arrivalKey;
    for (let p = 1; p <= 24; p++) {
        const stack = checkersAt(game, viewer, p);
        if (!stack) continue;
        const shown = Math.min(stack.count, 5);
        for (let k = 0; k < shown; k++) {
            const top = k === shown - 1;
            const piece = checker(stack.seat, pointX(p), checkerY(p, k), top && stack.count > 5 ? String(stack.count) : "");
            if (top && p === arrivalPoint) piece.classList.add("arrived");
            root.append(piece);
        }
    }

    // The bar: the viewer's checkers below the middle, the other player's above it.
    [viewer, 1 - viewer].forEach((seat) => {
        const count = game.bar[seat];
        const direction = seat === viewer ? 1 : -1;
        for (let k = 0; k < Math.min(count, 3); k++) {
            const label = k === Math.min(count, 3) - 1 && count > 3 ? String(count) : "";
            root.append(checker(seat, BAR_X, MID_Y + direction * (58 + k * 2 * R), label));
        }
    });

    // The tray: borne-off checkers as thin slices, the viewer's from the bottom, the other player's from the top.
    [viewer, 1 - viewer].forEach((seat) => {
        for (let k = 0; k < game.off[seat]; k++) {
            const y = seat === viewer ? HEIGHT - FRAME - 16 - k * 13 : FRAME + 6 + k * 13;
            root.append(svg("rect", { x: BOARD_W + 6, y, width: TRAY_W - FRAME - 12, height: 10, rx: 3, class: `bg-off ${seat === 0 ? "white" : "black"}` }));
        }
    });

    // Where the selected checker can go.
    moves.filter((m) => m.from === selected).forEach((m) => {
        if (m.to === OFF) {
            root.append(highlight(OFF, "bg-target-tray"));
        } else {
            const slot = Math.min(checkersAt(game, viewer, m.to)?.count ?? 0, 4);
            root.append(svg("circle", { cx: pointX(m.to), cy: checkerY(m.to, slot), r: R * 0.55, class: "bg-target" }));
        }
    });

    // The dice, on the mover's side of the board; used dice are faded.
    if (game.dice.length) {
        const x = game.turn === viewer ? RIGHT + HALF / 2 : LEFT + HALF / 2;
        const all = game.dice[0] === game.dice[1] ? [...game.dice, ...game.dice] : game.dice;
        const left = [...game.diceLeft];
        const size = all.length === 4 ? 36 : 46;
        all.forEach((value, i) => {
            const at = left.indexOf(value);
            if (at >= 0) left.splice(at, 1);
            root.append(die(value, x + (i - (all.length - 1) / 2) * (size + 10), MID_Y, size, game.turn, at < 0));
        });
    }

    if (game.cubeEnabled) {
        root.append(cube(game, viewer));
    }

    // Tap areas: each point's half of the board, the bar and the tray.
    for (let p = 1; p <= 24; p++) {
        root.append(svg("rect", {
            x: pointX(p) - PW / 2, y: isBottom(p) ? MID_Y : FRAME, width: PW, height: MID_Y - FRAME, class: "bg-hit", "data-target": p,
        }));
    }
    root.append(svg("rect", { x: BAR_X - BAR_W / 2, y: FRAME, width: BAR_W, height: HEIGHT - 2 * FRAME, class: "bg-hit", "data-target": BAR }));
    root.append(svg("rect", { x: BOARD_W, y: FRAME, width: TRAY_W - FRAME, height: HEIGHT - 2 * FRAME, class: "bg-hit", "data-target": OFF }));
    return root;
}

// The seat that must act now: the seat to move, or while a double is offered, the other seat (who takes or drops).
const actingSeat = (game) => (game.phase === "Doubled" ? 1 - game.turn : game.turn);

// The side the board is seen from: the viewer's seat; with both seats, the one that must act; for spectators, white.
function viewerSeat(game, view) {
    const mine = view.mySeats.filter((s) => s === 0 || s === 1);
    return mine.length === 2 ? actingSeat(game) : mine[0] ?? 0;
}

// A move such as "13/7", "bar/22" or "6/off".
const moveText = (m) => `${m.from === BAR ? "bar" : m.from}/${m.to === OFF ? "off" : m.to}`;
const diceText = (dice) => dice.join("-");
const pointsText = (n) => (n === 1 ? "1 point" : `${n} points`);

function backgammonTexts(game, state, view) {
    const both = view.isMe(0) && view.isMe(1);
    const name = (seat) => (view.isMe(seat) && !both ? "You" : state.seats[seat]?.name ?? COLORS[seat]);
    const mine = (seat) => view.isMe(seat);
    const result = game.result;
    const verb = (seat, you, other) => (name(seat) === "You" ? you : other);
    let status;
    switch (game.phase) {
        case "Rolling":
            status = mine(game.turn)
                ? `${both ? `${COLORS[game.turn]}: r` : "Your turn: r"}oll the dice${game.canDouble ? " or double" : ""}`
                : `${name(game.turn)} (${COLORS[game.turn]}) to roll`;
            break;
        case "Doubled":
            status = `${name(game.turn)} ${verb(game.turn, "double", "doubles")} to ${game.cubeValue * 2}. `
                + (mine(1 - game.turn) ? "Take or drop?" : `Waiting for ${name(1 - game.turn)}…`);
            break;
        case "Moving":
            status = !mine(game.turn) ? `${name(game.turn)} (${COLORS[game.turn]}) is moving: ${diceText(game.dice)}`
                : !game.legalMoves.length ? "Press Done to end your turn"
                    : `${both ? COLORS[game.turn] : "Your move"}: ${diceText(game.dice)}`;
            break;
        case "GameOver":
            status = `Game ${game.gameNumber} is over. Next: game ${game.gameNumber + 1}.`;
            break;
        default:
            status = `${name(result.seat)} ${verb(result.seat, "win", "wins")} the match ${game.scores[result.seat]}–${game.scores[1 - result.seat]}!`;
    }

    let message = "";
    if (result) {
        const kind = { Single: "the game", Gammon: "a gammon", Backgammon: "a backgammon" }[result.kind];
        message = result.kind === "Dropped"
            ? `${name(1 - result.seat)} dropped the double: ${name(result.seat)} ${verb(result.seat, "win", "wins")} ${pointsText(result.points)}.`
            : `${name(result.seat)} ${verb(result.seat, "win", "wins")} ${kind}: ${pointsText(result.points)}.`;
    } else if (game.openingRoll && !game.played.length && game.phase === "Moving") {
        message = `Opening roll: White ${game.openingRoll[0]}, Black ${game.openingRoll[1]}. ${name(game.turn)} ${verb(game.turn, "start", "starts")}.`;
    } else if (game.lastTurn && (game.lastTurn.seat !== game.turn || game.phase !== "Moving")) {
        const last = game.lastTurn;
        message = !last.moves.length ? `${name(last.seat)} rolled ${diceText(last.dice)} and could not move.`
            : last.forced ? `${name(last.seat)} rolled ${diceText(last.dice)}: the only possible move, ${last.moves.map(moveText).join(" ")}, was played.`
                : `${name(last.seat)} played ${diceText(last.dice)}: ${last.moves.map(moveText).join(" ")}.`;
    }
    return { status, message };
}

// The two players: colour, name, match score and pip count, with the seat to act marked.
function scoreboard(game, state, view) {
    const acting = game.phase === "GameOver" || game.phase === "MatchOver" ? null : actingSeat(game);
    const header = listItem(`Match to ${pointsText(game.matchLength)} · Game ${game.gameNumber}${game.isCrawford ? " · Crawford game" : ""}`);
    header.className = "bg-match";
    return [header, ...[0, 1].map((seat) => {
        const item = listItem(
            Object.assign(document.createElement("span"), { className: `bg-swatch ${seat === 0 ? "white" : "black"}` }),
            seatLabel(state, seat, view),
            Object.assign(document.createElement("strong"), { className: "bg-score", textContent: String(game.scores[seat]) }),
            Object.assign(document.createElement("span"), { className: "bg-pips", textContent: `${game.pips[seat]} pips` }));
        item.classList.toggle("acting", seat === acting);
        return item;
    })];
}

let current = null;

const backgammon = startSessionPage("Backgammon", (game, state, view) => {
    const viewer = viewerSeat(game, view);
    const canMove = game.phase === "Moving" && view.isMe(game.turn);
    if (!canMove || !game.legalMoves.some((m) => m.from === selected)) {
        selected = null;
    }
    current = { game, viewer, canMove };

    $("bg-players").replaceChildren(...scoreboard(game, state, view));
    $("bg-board").replaceChildren(drawGame(game, viewer, canMove));
    const { status, message } = backgammonTexts(game, state, view);
    $("status").textContent = status;
    $("bg-message").textContent = message;
    $("bg-message").hidden = !message;

    const rolling = game.phase === "Rolling" && view.isMe(game.turn);
    const answering = game.phase === "Doubled" && view.isMe(1 - game.turn);
    $("bg-roll").hidden = !rolling;
    $("bg-double").hidden = !(rolling && game.canDouble);
    $("bg-double").textContent = `Double to ${game.cubeValue * 2}`;
    $("bg-take").hidden = !answering;
    $("bg-drop").hidden = !answering;
    $("bg-undo").hidden = !(canMove && game.played.length);
    $("bg-done").hidden = !(canMove && game.canEndTurn);
    $("next-round").hidden = !(view.isPlayer && game.phase === "GameOver");
});

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

$("bg-board").addEventListener("click", (event) => {
    const target = event.target.closest("[data-target]");
    if (target) pick(Number(target.dataset.target));
});

const act = (method) => () => {
    if (current) call(method, backgammon.sessionId, actingSeat(current.game)).catch(() => {});
};
$("bg-roll").addEventListener("click", act("Roll"));
$("bg-double").addEventListener("click", act("OfferDouble"));
$("bg-take").addEventListener("click", act("TakeDouble"));
$("bg-drop").addEventListener("click", act("DropDouble"));
$("bg-undo").addEventListener("click", act("UndoMove"));
$("bg-done").addEventListener("click", act("EndTurn"));
$("next-round").addEventListener("click", () => {
    if (current) call("NextRound", backgammon.sessionId, current.game.gameNumber).catch(() => {});
});
