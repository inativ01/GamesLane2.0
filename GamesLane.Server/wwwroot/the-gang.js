"use strict";

// The Gang: players rank their hands by taking numbered chips over four stages (white, yellow, orange, red).
// Each player's own two cards come in view.private; the shared screen never sees them before the reveal.

const STAGES = ["White", "Yellow", "Orange", "Red"];
const TO_FINISH = 3;

function el(tag, className, ...content) {
    const element = document.createElement(tag);
    if (className) {
        element.className = className;
    }
    element.append(...content);
    return element;
}

// A card such as "10♥"; hearts and diamonds are red. Without text, a face-down placeholder.
function cardElement(text) {
    const card = el("span", "card", text ?? "");
    card.classList.toggle("red", /[♥♦]$/.test(text ?? ""));
    card.classList.toggle("empty", !text);
    return card;
}

function chipElement(stage, number, content = number) {
    const chip = el("span", `chip stage-${stage}`, String(content));
    chip.title = `${STAGES[stage]} chip ${number}`;
    return chip;
}

function gangStatus(game, state, view) {
    const n = game.playingSeats.length;
    switch (game.phase) {
        case "Betting":
            return view.isPlayer
                ? `${STAGES[game.stage]} chips: take a chip (1 = weakest hand, ${n} = strongest), then press Ready.`
                : `Players are taking ${STAGES[game.stage].toLowerCase()} chips…`;
        case "Reveal":
            return "Revealing hands, from the lowest red chip to the highest…";
        default:
            if (game.vaults >= TO_FINISH) {
                return "🎉 Three vaults cracked: the gang wins!";
            }
            if (game.alarms >= TO_FINISH) {
                return "🚨 Three alarms: the gang is caught. Game over.";
            }
            return game.revealed.every((h) => h.inOrder) ? "💰 The vault is open!" : "🚨 The alarm went off!";
    }
}

// This user's hands, with chip buttons for the current stage.
function myHand(game, state, view, hand) {
    const seat = hand.seat;
    const myChip = game.chips[game.stage][seat];
    const isReady = game.readySeats.includes(seat);
    const box = el("div", "my-hand panel");
    const title = view.mySeats.length > 1 ? `Seat ${seat + 1}: ${state.seats[seat]?.name ?? ""}` : "Your cards";
    box.append(el("h3", null, title), el("div", "card-row", ...hand.cards.map(cardElement)));
    if (hand.bestHand) {
        box.append(el("p", "hint", hand.bestHand));
    }

    if (game.phase === "Betting") {
        const chips = el("div", "chip-row");
        for (let number = 1; number <= game.playingSeats.length; number++) {
            const holder = game.playingSeats.find((s) => game.chips[game.stage][s] === number);
            const button = el("button", "chip-button");
            button.type = "button";
            button.append(chipElement(game.stage, number));
            button.disabled = holder === seat;
            button.title = holder === undefined ? "In the middle" : holder === seat ? "Yours" : `Take from ${state.seats[holder]?.name}`;
            button.addEventListener("click", () => call("TakeChip", view.sessionId, seat, number).catch(() => {}));
            chips.append(button);
        }
        box.append(chips);

        const actions = el("div", "actions center");
        const ready = el("button", isReady ? "secondary" : "", isReady ? "Not ready" : "Ready");
        ready.type = "button";
        ready.disabled = myChip === null;
        ready.addEventListener("click", () => call("SetReady", view.sessionId, seat, !isReady).catch(() => {}));
        actions.append(ready);
        if (myChip !== null) {
            const back = el("button", "secondary", "Return chip");
            back.type = "button";
            back.addEventListener("click", () => call("ReturnChip", view.sessionId, seat).catch(() => {}));
            actions.append(back);
        }
        box.append(actions);
    }
    return box;
}

// Every playing seat's chips from all stages, and whether they are ready.
function chipTable(game, state, view) {
    const header = el("tr", null, el("th", null, "Player"), ...STAGES.map((s) => el("th", null, s)));
    const rows = game.playingSeats.map((seat) => el("tr", null,
        el("td", null, seatLabel(state, seat, view)),
        ...STAGES.map((_, stage) => {
            const chip = game.chips[stage][seat];
            const cell = el("td", null, chip === null ? (stage <= game.stage ? "–" : "") : chipElement(stage, chip));
            if (stage === game.stage && game.phase === "Betting" && game.readySeats.includes(seat)) {
                cell.append(" ✓");
            }
            return cell;
        })));
    return [el("thead", null, header), el("tbody", null, ...rows)];
}

function revealedHand(game, state, view, hand, index) {
    const redChip = game.chips[3][hand.seat];
    const item = el("li", hand.inOrder ? "revealed-hand" : "revealed-hand wrong",
        chipElement(3, redChip),
        seatLabel(state, hand.seat, view),
        el("span", "card-row", ...hand.cards.map(cardElement)),
        el("span", "hand-name", hand.name + (hand.inOrder ? "" : " ✗ weaker than the hand before")));
    item.dataset.index = String(index);
    return item;
}

let gangState = null;

const gang = startSessionPage("TheGang", (game, state, view) => {
    gangState = game;
    $("heist").textContent = String(game.heist);
    $("vaults").textContent = `${game.vaults}/${TO_FINISH}`;
    $("alarms").textContent = `${game.alarms}/${TO_FINISH}`;
    $("status").textContent = gangStatus(game, state, view);

    $("community").replaceChildren(...Array.from({ length: 5 }, (_, i) => cardElement(game.communityCards[i])));

    const hands = view.private?.hands ?? [];
    $("my-hands").replaceChildren(...hands.map((hand) => myHand(game, state, view, hand)));

    const betting = game.phase === "Betting";
    $("middle").hidden = !betting;
    $("middle").replaceChildren(el("span", "hint", "Middle: "), ...game.middleChips.map((n) => chipElement(game.stage, n)));
    $("chip-table").replaceChildren(...chipTable(game, state, view));

    $("reveal-next").hidden = !(view.isPlayer && game.phase === "Reveal");
    $("next-heist").hidden = !(view.isPlayer && game.phase === "HeistOver" && !state.isGameOver);
    $("revealed").replaceChildren(...game.revealed.map((hand, i) => revealedHand(game, state, view, hand, i)));
});

$("reveal-next").addEventListener("click", () => {
    call("RevealNext", gang.sessionId, gangState?.revealed.length ?? 0).catch(() => {});
});
$("next-heist").addEventListener("click", () => {
    call("NextHeist", gang.sessionId, gangState?.heist ?? 1).catch(() => {});
});
