"use strict";

const SYMBOLS = { Rock: "✊", Paper: "✋", Scissors: "✌️" };

let myPick = null; // { round, choice }: this player's pick, shown on their own screen before the reveal
let currentRound = 1;

function rpsStatus(state, view) {
    if (state.status === "WaitingForPlayers") {
        return `Waiting for players (${state.players.length}/2)…`;
    }

    const round = `Round ${state.round}`;
    if (!state.isRoundComplete) {
        if (view.isPlayer) {
            const other = state.players[1 - view.player.seat];
            return state.players[view.player.seat].hasPicked ? `Waiting for ${other.name}…` : `${round}: make your pick`;
        }
        return `${round}: waiting for picks…`;
    }

    if (state.roundWinner === null) {
        return `${round}: it's a draw!`;
    }
    const winner = state.players[state.roundWinner].name;
    if (view.isPlayer) {
        return view.isMe(state.roundWinner) ? "You win this round!" : `${winner} wins this round.`;
    }
    return `${winner} wins ${round.toLowerCase()}!`;
}

function handCard(state, player, seat, view) {
    let symbol;
    let label;
    if (state.isRoundComplete) {
        symbol = SYMBOLS[player.pick];
        label = player.pick;
    } else if (view.isMe(seat) && myPick) {
        symbol = SYMBOLS[myPick.choice];
        label = `You picked ${myPick.choice}`;
    } else {
        symbol = player.hasPicked ? "✔️" : "…";
        label = player.hasPicked ? "Picked" : "Choosing";
    }

    const card = document.createElement("div");
    card.className = "hand";
    card.classList.toggle("winner", state.isRoundComplete && state.roundWinner === seat);
    const name = document.createElement("div");
    name.textContent = playerLabel(player, seat, view);
    const symbolEl = document.createElement("div");
    symbolEl.className = "hand-symbol";
    symbolEl.textContent = symbol;
    const labelEl = document.createElement("div");
    labelEl.className = "hand-label";
    labelEl.textContent = label;
    card.append(name, symbolEl, labelEl);
    return card;
}

const rps = startGamePage("RockPaperScissors", (state, view) => {
    currentRound = state.round;
    if (myPick && myPick.round !== state.round) {
        myPick = null;
    }

    // Only players see the pick and next-round buttons, and only when they can use them.
    const me = view.isPlayer ? state.players[view.player.seat] : null;
    $("picks").hidden = !(me && state.status === "InProgress" && !state.isRoundComplete && !me.hasPicked && !myPick);
    $("next-round").hidden = !(me && state.isRoundComplete);

    $("status").textContent = rpsStatus(state, view);
    $("hands").replaceChildren(...state.players.map((p, seat) => handCard(state, p, seat, view)));
    $("players").replaceChildren(
        ...state.players.map((p, seat) => listItem(`${playerLabel(p, seat, view)}: ${p.score}`)),
        listItem(`Draws: ${state.draws}`));
});

document.querySelectorAll(".pick").forEach((button) => {
    button.addEventListener("click", () => {
        myPick = { round: currentRound, choice: button.dataset.choice };
        rps.refresh();
        call("Pick", rps.gameId, button.dataset.choice).catch(() => {
            myPick = null;
            rps.refresh();
        });
    });
});

$("next-round").addEventListener("click", () => {
    call("NextRound", rps.gameId, currentRound).catch(() => {});
});
