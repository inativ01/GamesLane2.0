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

// Wood grain for the board: noise stretched along the planks (across for the frame, along the points for the playing
// field), drawn as darker streaks on the wood's colour.
function woodDefs() {
    const grain = (id, frequency, strength) => svg("filter", { id, x: 0, y: 0, width: "100%", height: "100%" },
        svg("feTurbulence", { type: "fractalNoise", baseFrequency: frequency, numOctaves: 3, seed: 7, result: "noise" }),
        svg("feColorMatrix", {
            in: "noise", type: "matrix", result: "streaks",
            values: `0 0 0 0 0.18  0 0 0 0 0.09  0 0 0 0 0.02  0 0 0 ${strength} -0.2`,
        }),
        svg("feComposite", { in: "streaks", in2: "SourceGraphic", operator: "atop" }));
    const gradient = (id, from, to) => svg("linearGradient", { id, x1: 0, y1: 0, x2: 0, y2: 1 },
        svg("stop", { offset: 0, "stop-color": from }), svg("stop", { offset: 1, "stop-color": to }));
    return svg("defs", {},
        grain("bg-grain-across", "0.003 0.09", 0.7),
        grain("bg-grain-along", "0.09 0.004", 0.45),
        gradient("bg-walnut", "#6e3d1c", "#4a2610"),
        gradient("bg-maple", "#e2bf8a", "#cfa66d"));
}

// The board's frame, playing field, points, bar and tray, in wood.
function drawBoard() {
    const field = (x) => svg("rect", { x, y: FRAME, width: HALF, height: HEIGHT - 2 * FRAME, class: "bg-field" });
    const parts = [
        woodDefs(),
        svg("rect", { x: 0, y: 0, width: WIDTH, height: HEIGHT, rx: 14, class: "bg-wood" }),
        field(LEFT),
        field(RIGHT),
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
    // A carved groove around each half of the playing field, and along both sides of the bar.
    [LEFT, RIGHT].forEach((x) => parts.push(
        svg("rect", { x: x - 1.5, y: FRAME - 1.5, width: HALF + 3, height: HEIGHT - 2 * FRAME + 3, class: "bg-groove" })));
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

// Where an arrow of the last turn starts or ends on black's point p: at the base of the point, on the bar, or in the
// seat's half of the tray.
function arrowEnd(p, seat) {
    if (p === BAR) return [BAR_X, MID_Y];
    if (p === OFF) return [BOARD_W + (TRAY_W - FRAME) / 2, seat === 1 ? HEIGHT - FRAME - 60 : FRAME + 60];
    return [pointX(p), isBottom(p) ? HEIGHT - FRAME - R : FRAME + R];
}

// The last finished turn drawn on the board (with Last move): an arrow per checker moved, numbered in order.
function lastTurnArrows(game) {
    const last = game.lastTurn;
    const parts = [svg("defs", {}, svg("marker", {
        id: "bg-arrowhead", viewBox: "0 0 10 10", refX: 8, refY: 5, markerWidth: 4, markerHeight: 4, orient: "auto",
    }, svg("path", { d: "M0,0 L10,5 L0,10 z", class: "bg-arrowhead" })))];
    last.moves.forEach((m, i) => {
        const [x1, y1] = arrowEnd(toBlack(m.from, last.seat), last.seat);
        const [x2, y2] = arrowEnd(toBlack(m.to, last.seat), last.seat);
        const bend = (isBottom(toBlack(m.from, last.seat)) ? -1 : 1) * 90;
        const [cx, cy] = [(x1 + x2) / 2, (y1 + y2) / 2 + bend];
        parts.push(
            svg("path", { d: `M${x1},${y1} Q${cx},${cy} ${x2},${y2}`, class: "bg-arrow", "marker-end": "url(#bg-arrowhead)" }),
            svg("circle", { cx: (x1 + 2 * cx + x2) / 4, cy: (y1 + 2 * cy + y2) / 4, r: 15, class: "bg-arrow-step" }),
            text((x1 + 2 * cx + x2) / 4, (y1 + 2 * cy + y2) / 4 + 6, String(i + 1), "bg-arrow-number"));
    });
    return parts;
}

let selected = null;
let lastArrival = "";
// Whether Last move is on: the last finished turn is drawn on the board as arrows.
let showLast = false;

// The dice rolling animation: until when it runs, and the seats whose dice are rolling.
const ROLL_MS = 1000;
let rollingUntil = 0;
let rollingSeats = new Set();
let rollTimer = 0;
let seenRolls = null;
let seenOpening = [null, null];

// Starts the one-second rolling animation when the state shows a new roll (not when the page first opens).
function noticeRoll(game) {
    const opening = game.phase === "Opening" || Boolean(game.openingRoll);
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
// opening roll, until the starting player rolls, each player's one die; then the mover's dice, with used ones faded;
// between turns, the last roll, faded. Rolling dice show random faces and tumble.
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
    if (game.phase === "Opening" || game.openingRoll) {
        [0, 1].filter((s) => game.openingDice[s] !== null).forEach((s) => row([game.openingDice[s]], s, [false]));
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
    if (showLast && game.lastTurn?.moves.length && !game.played.length) {
        board.append(...lastTurnArrows(game));
    }

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
                ? `👉 ${both ? `${colorName(roller)}: ` : ""}${t("The higher die starts")}`
                : `⏳ ${t("Waiting for {name}", { name: waiting.join(t(" and ")) })}`;
            break;
        }
        case "Rolling": {
            const color = colorName(game.turn);
            status = !mine(game.turn) ? `⏳ ${t("Waiting for {name}", { name: `${name(game.turn)} (${color})` })}`
                : `👉 ${both ? color : t("Your turn")}`;
            break;
        }
        case "Doubled":
            status = `${about(game.turn, "You double to {n}.", "{name} doubles to {n}.", { n: game.cubeValue * 2 })} `
                + (mine(1 - game.turn) ? `👉 ${t("Take or drop?")}` : `⏳ ${t("Waiting for {name}", { name: name(1 - game.turn) })}`);
            break;
        case "Moving": {
            const dice = diceText(game.dice);
            status = !mine(game.turn) ? `⏳ ${t("Waiting for {name}", { name: `${name(game.turn)} (${colorName(game.turn)})` })}: ${dice}`
                : !game.legalMoves.length ? `👉 ${t("No more moves")}`
                    : `👉 ${both ? colorName(game.turn) : t("Your move")}: ${dice}`;
            break;
        }
        case "GameOver":
            status = `🏁 ${t("Game {n} is over.", { n: game.gameNumber })}`;
            break;
        default:
            status = `🏆 ${about(result.seat, "You win the match {a}–{b}!", "{name} wins the match {a}–{b}!",
                { a: game.scores[result.seat], b: game.scores[1 - result.seat] })}`;
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
    } else if (game.openingRoll) {
        message = about(game.turn, "Opening roll: White {white}, Black {black}. You start.",
            "Opening roll: White {white}, Black {black}. {name} starts.", { white: game.openingRoll[0], black: game.openingRoll[1] });
    }
    return { status, message };
}

// The score row in the status bar: the match length, the game number and the Crawford game, then each player's colour,
// name, match score and pip count, with the seat to act outlined.
function showScoreboard(game, state, view) {
    const acting = game.phase === "GameOver" || game.phase === "MatchOver" ? null : actingSeat(game);
    showScores(state, view,
        [t("Match to {points}", { points: pointsText(game.matchLength) }), t("Game {n}", { n: game.gameNumber }), game.isCrawford && t("Crawford game")],
        [0, 1].map((seat) => ({
            seat,
            before: el("span", `bg-swatch ${seat === 0 ? "white" : "black"}`),
            score: game.scores[seat],
            extra: t("{n} pips", { n: game.pips[seat] }),
            acting: seat === acting,
        })));
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

    showScoreboard(game, state, view);
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
    $("bg-last-move").hidden = !game.lastTurn?.moves.length || game.played.length > 0;
    $("bg-last-move").setAttribute("aria-pressed", String(showLast));
    fitBoard();
}, (seat) => `${["⚪", "⚫"][seat]} ${colorName(seat)}`);

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
$("bg-last-move").addEventListener("click", () => {
    showLast = !showLast;
    backgammon.refresh();
});
