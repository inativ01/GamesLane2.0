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
            return both ? `👉 Your turn (${game.currentTurn})` : "👉 Your turn";
        }
        return `⏳ ${nameOf(game.currentTurn)} (${game.currentTurn})`;
    }
    if (game.isDraw) {
        return `🤝 Round ${game.round} is a draw`;
    }
    if (view.isMe(seatOf(game.winner)) && !both) {
        return `🏆 You win round ${game.round}!`;
    }
    return `🏆 ${nameOf(game.winner)} (${game.winner}) wins round ${game.round}`;
}

let turnSeat = 0;
let currentRound = 1;

const cells = Array.from({ length: 9 }, (_, i) => {
    const cell = document.createElement("button");
    cell.type = "button";
    cell.className = "cell";
    cell.disabled = true;
    cell.setAttribute("aria-label", `Cell ${i + 1}`);
    cell.addEventListener("click", () => call("MakeMove", ticTacToe.sessionId, turnSeat, i).catch(() => {}));
    $("board").appendChild(cell);
    return cell;
});

const ticTacToe = startSessionPage("TicTacToe", (game, state, view) => {
    currentRound = game.round;
    turnSeat = seatOf(game.currentTurn);
    const myTurn = !isRoundOver(game) && view.isMe(turnSeat);

    game.board.forEach((mark, i) => {
        cells[i].textContent = mark === "None" ? "" : mark;
        cells[i].dataset.mark = mark;
        cells[i].disabled = !(myTurn && mark === "None");
    });

    $("status").textContent = ticTacToeStatus(game, state, view);
    $("next-round").hidden = !(view.isPlayer && isRoundOver(game));
    showScores(state, view, [`Round ${game.round}`, `Draws: ${game.draws}`], MARKS.map((mark, seat) => ({
        seat, before: el("span", "score-mark", mark), score: game.scores[seat], acting: !isRoundOver(game) && seat === turnSeat,
    })));
}, (seat) => MARKS[seat]);

$("next-round").addEventListener("click", () => {
    call("NextRound", ticTacToe.sessionId, currentRound).catch(() => {});
});
