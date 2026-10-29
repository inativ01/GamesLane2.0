"use strict";

// The Gang: players rank their hands by taking numbered chips over four stages (white, yellow, orange, red).
// Each player's own two cards come in view.private; the shared screen never sees them before the reveal.
// The page shows the player's own hand on top, and the table (everything observers see) below. Players can hide
// the table, e.g. when a second device shows it; the choice is remembered on this device.

const STAGES = ["White", "Yellow", "Orange", "Red"];
const TO_FINISH = 3;
const TABLE_HIDDEN_KEY = "theGang.tableHidden";

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

function button(text, className, onClick) {
    const b = el("button", className, text);
    b.type = "button";
    b.addEventListener("click", onClick);
    return b;
}

function gangStatus(game, state, view) {
    switch (game.phase) {
        case "Betting":
            return view.isPlayer
                ? `${STAGES[game.stage]} chips`
                : `Players are taking ${STAGES[game.stage].toLowerCase()} chips…`;
        case "Reveal":
            return "Revealing hands, lowest red chip first…";
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

// One of this user's hands: two big cards with the hand name, then the chip buttons and Ready.
function myHand(game, state, view, hand) {
    const seat = hand.seat;
    const stage = game.stage;
    const myChip = game.chips[stage][seat];
    const isReady = game.readySeats.includes(seat);
    const betting = game.phase === "Betting";

    const title = view.mySeats.length > 1 ? `Seat ${seat + 1} · ${state.seats[seat]?.name ?? ""}` : "Your hand";
    const info = el("div", "my-hand-info", el("span", "my-hand-title", title));
    info.append(el("span", "best-hand", hand.bestHand ?? "No shared cards yet"));
    if (myChip !== null) {
        info.append(el("span", "stage-pill", chipElement(stage, myChip), `Your ${STAGES[stage].toLowerCase()} chip`));
    } else if (betting) {
        info.append(el("span", "stage-pill", `Take a ${STAGES[stage].toLowerCase()} chip`));
    }

    const box = el("div", "my-hand", el("div", "my-hand-top", el("div", "card-row", ...hand.cards.map((c) => {
        const card = cardElement(c);
        card.classList.add("big");
        return card;
    })), info));

    if (!betting) {
        return box;
    }

    // One button per chip: 1 = weakest hand, N = strongest. The label below shows who holds it.
    const chips = el("div", "my-chips");
    for (let number = 1; number <= game.playingSeats.length; number++) {
        const holder = game.playingSeats.find((s) => game.chips[stage][s] === number);
        const mine = holder === seat;
        const label = holder === undefined ? "" : mine ? "You" : state.seats[holder]?.name ?? "";
        const chip = el("button", mine ? "chip-button mine" : "chip-button", chipElement(stage, number), el("span", "holder", label));
        chip.type = "button";
        chip.disabled = mine;
        chip.title = holder === undefined ? `Take chip ${number}` : mine ? "Your chip" : `Take chip ${number} from ${label}`;
        chip.addEventListener("click", () => call("TakeChip", view.sessionId, seat, number).catch(() => {}));
        chips.append(chip);
    }

    const ready = button(isReady ? "✓ Ready (tap to undo)" : "Ready", isReady ? "is-ready" : "",
        () => call("SetReady", view.sessionId, seat, !isReady).catch(() => {}));
    ready.disabled = myChip === null;
    const actions = el("div", "my-hand-actions", ready);
    if (myChip !== null) {
        actions.append(button("Return", "secondary", () => call("ReturnChip", view.sessionId, seat).catch(() => {})));
    }

    box.append(chips, el("p", "hint", `1 = weakest hand, ${game.playingSeats.length} = strongest.`), actions);
    return box;
}

// Every playing seat's chips from all stages, with ✓ for players who are ready.
function chipTable(game, state, view) {
    const header = el("tr", null, el("th", null, "Player"), ...STAGES.map((s) => el("th", null, s)));
    const rows = game.playingSeats.map((seat) => el("tr", null,
        el("td", null, seatLabel(state, seat, view)),
        ...STAGES.map((_, stage) => {
            const chip = game.chips[stage][seat];
            const cell = el("td", null, chip === null ? (stage <= game.stage ? "–" : "") : chipElement(stage, chip));
            if (stage === game.stage && game.phase === "Betting" && game.readySeats.includes(seat)) {
                cell.append(el("span", "ready", " ✓"));
            }
            return cell;
        })));
    return [el("thead", null, header), el("tbody", null, ...rows)];
}

function revealedHand(game, state, view, hand) {
    return el("li", hand.inOrder ? "revealed-hand" : "revealed-hand wrong",
        chipElement(3, game.chips[3][hand.seat]),
        seatLabel(state, hand.seat, view),
        el("span", "card-row", ...hand.cards.map(cardElement)),
        el("span", "hand-name", hand.name + (hand.inOrder ? "" : " ✗ weaker than the hand before")));
}

let gangState = null;
let tableHidden = localStorage.getItem(TABLE_HIDDEN_KEY) === "1";

const gang = startSessionPage("TheGang", (game, state, view) => {
    gangState = game;
    $("status").textContent = gangStatus(game, state, view);

    const hands = view.private?.hands ?? [];
    $("my-hands").replaceChildren(...hands.map((hand) => myHand(game, state, view, hand)));
    $("reveal-next").hidden = !(view.isPlayer && game.phase === "Reveal");
    $("next-heist").hidden = !(view.isPlayer && game.phase === "HeistOver" && !state.isGameOver);

    // The table. Observers always see it; players may hide it.
    $("heist").textContent = String(game.heist);
    $("vaults").textContent = `${game.vaults}/${TO_FINISH}`;
    $("alarms").textContent = `${game.alarms}/${TO_FINISH}`;
    $("toggle-table").hidden = !view.isPlayer;
    $("toggle-table").textContent = tableHidden ? "Show table" : "Hide table";
    $("table-body").hidden = view.isPlayer && tableHidden;

    $("community").replaceChildren(...Array.from({ length: 5 }, (_, i) => cardElement(game.communityCards[i])));
    $("middle").hidden = game.phase !== "Betting";
    $("middle").replaceChildren(
        "Middle:",
        ...(game.middleChips.length ? game.middleChips.map((n) => chipElement(game.stage, n)) : [" none"]));
    $("chip-table").replaceChildren(...chipTable(game, state, view));
    $("revealed").replaceChildren(...game.revealed.map((hand) => revealedHand(game, state, view, hand)));
});

$("toggle-table").addEventListener("click", () => {
    tableHidden = !tableHidden;
    localStorage.setItem(TABLE_HIDDEN_KEY, tableHidden ? "1" : "0");
    gang.refresh();
});
$("reveal-next").addEventListener("click", () => {
    call("RevealNext", gang.sessionId, gangState?.revealed.length ?? 0).catch(() => {});
});
$("next-heist").addEventListener("click", () => {
    call("NextHeist", gang.sessionId, gangState?.heist ?? 1).catch(() => {});
});
