"use strict";

function ticTacToeStatus(state, view, myMark) {
    const nameOf = (mark) => state.players.find((p) => p.mark === mark)?.name ?? mark;
    switch (state.status) {
        case "WaitingForPlayers":
            return `Waiting for players (${state.players.length}/2)…`;
        case "InProgress":
            if (view.isPlayer) {
                return state.currentTurn === myMark ? "Your turn" : `Waiting for ${nameOf(state.currentTurn)}…`;
            }
            return `${nameOf(state.currentTurn)} (${state.currentTurn}) to move`;
        default:
            if (state.isDraw) {
                return "It's a draw!";
            }
            if (view.isPlayer) {
                return state.winner === myMark ? "You win!" : `${nameOf(state.winner)} wins.`;
            }
            return `${nameOf(state.winner)} (${state.winner}) wins!`;
    }
}

const cells = Array.from({ length: 9 }, (_, i) => {
    const cell = document.createElement("button");
    cell.type = "button";
    cell.className = "cell";
    cell.disabled = true;
    cell.setAttribute("aria-label", `Cell ${i + 1}`);
    cell.addEventListener("click", () => call("MakeMove", ticTacToe.gameId, i).catch(() => {}));
    $("board").appendChild(cell);
    return cell;
});

const ticTacToe = startGamePage("TicTacToe", (state, view) => {
    const myMark = view.isPlayer ? state.players[view.player.seat]?.mark : null;
    const myTurn = Boolean(myMark) && state.status === "InProgress" && state.currentTurn === myMark;

    state.board.forEach((mark, i) => {
        cells[i].textContent = mark === "None" ? "" : mark;
        cells[i].dataset.mark = mark;
        cells[i].disabled = !(myTurn && mark === "None");
    });

    $("status").textContent = ticTacToeStatus(state, view, myMark);
    $("players").replaceChildren(...state.players.map((p, seat) => listItem(`${p.mark}: ${playerLabel(p, seat, view)}`)));
});
