"use strict";

// Seat 0 plays X, seat 1 plays O.
const MARKS = ["X", "O"];
const seatOf = (mark) => MARKS.indexOf(mark);

function ticTacToeStatus(game, state, view) {
    const nameOf = (mark) => state.seats[seatOf(mark)]?.name ?? mark;
    const both = view.isMe(0) && view.isMe(1);
    if (!state.isGameOver) {
        if (view.isMe(seatOf(game.currentTurn))) {
            return both ? `Your turn (${game.currentTurn})` : "Your turn";
        }
        return view.isPlayer
            ? `Waiting for ${nameOf(game.currentTurn)}…`
            : `${nameOf(game.currentTurn)} (${game.currentTurn}) to move`;
    }
    if (game.isDraw) {
        return "It's a draw!";
    }
    if (view.isMe(seatOf(game.winner)) && !both) {
        return "You win!";
    }
    return `${nameOf(game.winner)} (${game.winner}) wins!`;
}

let turnSeat = 0;

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
    turnSeat = seatOf(game.currentTurn);
    const myTurn = !state.isGameOver && view.isMe(turnSeat);

    game.board.forEach((mark, i) => {
        cells[i].textContent = mark === "None" ? "" : mark;
        cells[i].dataset.mark = mark;
        cells[i].disabled = !(myTurn && mark === "None");
    });

    $("status").textContent = ticTacToeStatus(game, state, view);
    $("players").replaceChildren(...MARKS.map((mark, seat) => listItem(`${mark}: ${seatName(state, seat, view)}`)));
});
