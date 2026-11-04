"use strict";

const SYMBOLS = { Rock: "✊", Paper: "✋", Scissors: "✌️" };

// This tab's picks, shown on its own screen before the reveal: seat -> { round, choice }.
let myPicks = {};
let currentRound = 1;
let pickSeat = null; // the seat the pick buttons pick for

const pickedLocally = (seat) => myPicks[seat]?.round === currentRound;

function rpsStatus(game, state, view) {
    if (!game.isRoundComplete) {
        if (pickSeat !== null) {
            return `👉 ${t("Make your pick")}`;
        }
        return `⏳ ${t(view.isPlayer ? "Waiting for the other player" : "Waiting for the picks")}`;
    }

    if (game.roundWinner === null) {
        return `🤝 ${t("Round {n} is a draw", { n: game.round })}`;
    }
    if (view.isMe(game.roundWinner) && !view.isMe(1 - game.roundWinner)) {
        return `🏆 ${t("You win round {n}!", { n: game.round })}`;
    }
    return `🏆 ${t("{name} wins round {n}", { name: state.seats[game.roundWinner]?.name ?? t("Empty seat"), n: game.round })}`;
}

function handCard(game, state, seat, view) {
    const hand = game.hands[seat];
    let symbol;
    let label;
    if (game.isRoundComplete) {
        symbol = SYMBOLS[hand.pick];
        label = t(hand.pick);
    } else if (view.isMe(seat) && pickedLocally(seat)) {
        symbol = SYMBOLS[myPicks[seat].choice];
        label = t("You picked {choice}", { choice: t(myPicks[seat].choice) });
    } else {
        symbol = hand.hasPicked ? "✔️" : "…";
        label = t(hand.hasPicked ? "Picked" : "Choosing");
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
    $("pick-for").textContent = t("Picking for seat {n}", { n: pickSeat + 1 });
    $("next-round").hidden = !(view.isPlayer && game.isRoundComplete);

    $("status").textContent = rpsStatus(game, state, view);
    $("hands").replaceChildren(...game.hands.map((_, seat) => handCard(game, state, seat, view)));
    showScores(state, view, [t("Round {n}", { n: game.round }), t("Draws: {n}", { n: game.draws })], game.hands.map((hand, seat) => ({
        seat, score: hand.score, acting: !game.isRoundComplete && !hand.hasPicked,
    })));
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
