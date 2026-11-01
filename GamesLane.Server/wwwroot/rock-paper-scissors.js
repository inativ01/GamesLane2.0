"use strict";

const SYMBOLS = { Rock: "✊", Paper: "✋", Scissors: "✌️" };

// This tab's picks, shown on its own screen before the reveal: seat -> { round, choice }.
let myPicks = {};
let currentRound = 1;
let pickSeat = null; // the seat the pick buttons pick for

const pickedLocally = (seat) => myPicks[seat]?.round === currentRound;

function rpsStatus(game, state, view) {
    const round = `Round ${game.round}`;
    if (!game.isRoundComplete) {
        if (pickSeat !== null) {
            return `${round}: make your pick`;
        }
        return view.isPlayer ? "Waiting for the other player…" : `${round}: waiting for picks…`;
    }

    if (game.roundWinner === null) {
        return `${round}: it's a draw!`;
    }
    if (view.isMe(game.roundWinner) && !view.isMe(1 - game.roundWinner)) {
        return "You win this round!";
    }
    return `${state.seats[game.roundWinner]?.name ?? "Empty seat"} wins ${round.toLowerCase()}!`;
}

function handCard(game, state, seat, view) {
    const hand = game.hands[seat];
    let symbol;
    let label;
    if (game.isRoundComplete) {
        symbol = SYMBOLS[hand.pick];
        label = hand.pick;
    } else if (view.isMe(seat) && pickedLocally(seat)) {
        symbol = SYMBOLS[myPicks[seat].choice];
        label = `You picked ${myPicks[seat].choice}`;
    } else {
        symbol = hand.hasPicked ? "✔️" : "…";
        label = hand.hasPicked ? "Picked" : "Choosing";
    }

    const card = document.createElement("div");
    card.className = "hand";
    card.classList.toggle("winner", game.isRoundComplete && game.roundWinner === seat);
    const name = document.createElement("div");
    name.appendChild(seatLabel(state, seat, view));
    const symbolEl = document.createElement("div");
    symbolEl.className = "hand-symbol";
    symbolEl.textContent = symbol;
    const labelEl = document.createElement("div");
    labelEl.className = "hand-label";
    labelEl.textContent = label;
    card.append(name, symbolEl, labelEl);
    return card;
}

const rps = startSessionPage("RockPaperScissors", (game, state, view) => {
    currentRound = game.round;

    // A user holding both seats picks for one seat, then the other.
    pickSeat = game.isRoundComplete
        ? null
        : view.mySeats.find((seat) => !game.hands[seat].hasPicked && !pickedLocally(seat)) ?? null;
    $("picks").hidden = pickSeat === null;
    $("pick-for").hidden = !(pickSeat !== null && view.mySeats.length > 1);
    $("pick-for").textContent = `Picking for seat ${pickSeat + 1}`;
    $("next-round").hidden = !(view.isPlayer && game.isRoundComplete);

    $("status").textContent = rpsStatus(game, state, view);
    $("hands").replaceChildren(...game.hands.map((_, seat) => handCard(game, state, seat, view)));
    $("players").replaceChildren(
        ...game.hands.map((hand, seat) => listItem(seatLabel(state, seat, view), `: ${hand.score}`)),
        listItem(`Draws: ${game.draws}`));
});

document.querySelectorAll(".pick").forEach((button) => {
    button.addEventListener("click", () => {
        const seat = pickSeat;
        if (seat === null) {
            return;
        }
        myPicks[seat] = { round: currentRound, choice: button.dataset.choice };
        rps.refresh();
        call("Pick", rps.sessionId, seat, button.dataset.choice).catch(() => {
            delete myPicks[seat];
            rps.refresh();
        });
    });
});

$("next-round").addEventListener("click", () => {
    call("NextRound", rps.sessionId, currentRound).catch(() => {});
});
