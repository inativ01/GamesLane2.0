"use strict";

// Seat 0 plays X, seat 1 plays O. The match goes on round after round, keeping score; the starting player alternates.
const MARKS = ["X", "O"];
const seatOf = (mark) => MARKS.indexOf(mark);
const isRoundOver = (game) => game.isDraw || game.winner !== "None";

function ticTacToeStatus(game, state, view) {
    const nameOf = (mark) => state.seats[seatOf(mark)]?.name ?? mark;
    const both = view.isMe(0) && view.isMe(1);
    if (!isRoundOver(game)) {
        if (view.isMe(seatOf(game.currentTurn))) {
            return both ? `👉 ${t("Your turn")} (${game.currentTurn})` : `👉 ${t("Your turn")}`;
        }
        return `⏳ ${t("Waiting for {name}", { name: `${nameOf(game.currentTurn)} (${game.currentTurn})` })}`;
    }
    if (game.isDraw) {
        return `🤝 ${t("Round {n} is a draw", { n: game.round })}`;
    }
    if (view.isMe(seatOf(game.winner)) && !both) {
        return `🏆 ${t("You win round {n}!", { n: game.round })}`;
    }
    return `🏆 ${t("{name} wins round {n}", { name: `${nameOf(game.winner)} (${game.winner})`, n: game.round })}`;
}

let turnSeat = 0;
let currentRound = 1;
// The board at the last update, so a new mark is set down with a knock and pops in (not when the page opens).
let boardBefore = { round: null, board: [] };

const cells = Array.from({ length: 9 }, (_, i) => {
    const cell = document.createElement("button");
    cell.type = "button";
    cell.className = "cell";
    cell.disabled = true;
    cell.setAttribute("aria-label", t("Cell {n}", { n: i + 1 }));
    cell.addEventListener("click", () => call("MakeMove", ticTacToe.sessionId, turnSeat, i).catch(() => {}));
    $("board").appendChild(cell);
    return cell;
});

const ticTacToe = startSessionPage("TicTacToe", (game, state, view) => {
    currentRound = game.round;
    turnSeat = seatOf(game.currentTurn);
    const myTurn = !isRoundOver(game) && view.isMe(turnSeat);

    const changed = view.changed("board", [game.round, game.board]);
    const before = boardBefore;
    boardBefore = { round: game.round, board: [...game.board] };
    game.board.forEach((mark, i) => {
        cells[i].textContent = mark === "None" ? "" : mark;
        cells[i].dataset.mark = mark;
        cells[i].disabled = !(myTurn && mark === "None");
        if (changed && before.round === game.round && mark !== "None" && before.board[i] === "None") {
            playGameSound("checker");
            if (!reducedMotion()) {
                cells[i].animate([{ transform: "scale(1.5)", opacity: 0.3 }, { transform: "none", opacity: 1 }], { duration: 300, easing: "ease-out" });
            }
        }
    });

    $("status").textContent = ticTacToeStatus(game, state, view);
    if (isRoundOver(game) && !game.isDraw && view.isMe(seatOf(game.winner))) {
        celebrate(state, `round-${game.round}`);
    }
    $("next-round").hidden = !(view.isPlayer && isRoundOver(game));
    showScores(state, view, [t("Round {n}", { n: game.round }), t("Draws: {n}", { n: game.draws })], MARKS.map((mark, seat) => ({
        seat, before: el("span", "score-mark", mark), score: game.scores[seat], acting: !isRoundOver(game) && seat === turnSeat,
    })));
}, (seat) => MARKS[seat]);

$("next-round").addEventListener("click", () => {
    call("NextRound", ticTacToe.sessionId, currentRound).catch(() => {});
});
