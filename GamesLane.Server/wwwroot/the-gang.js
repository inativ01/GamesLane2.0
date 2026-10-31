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

// Where the suit symbols go on number cards, as [x%, y%] on the card face, as on classic playing cards.
// Symbols in the lower half are drawn upside down.
const PIP_LAYOUTS = (() => {
    const L = 28, C = 50, R = 72;
    const six = [[L, 16], [R, 16], [L, 50], [R, 50], [L, 84], [R, 84]];
    const fourRows = [16, 39, 61, 84].flatMap((y) => [[L, y], [R, y]]);
    return {
        2: [[C, 16], [C, 84]],
        3: [[C, 16], [C, 50], [C, 84]],
        4: [[L, 16], [R, 16], [L, 84], [R, 84]],
        5: [[L, 16], [R, 16], [C, 50], [L, 84], [R, 84]],
        6: six,
        7: [...six, [C, 33]],
        8: [...six, [C, 33], [C, 67]],
        9: [...fourRows, [C, 50]],
        10: [...fourRows, [C, 27], [C, 73]],
    };
})();

// The figures on jacks, queens and kings (\uFE0E asks for the plain symbol rather than an emoji).
const COURT_FIGURES = { J: "♞\uFE0E", Q: "♛\uFE0E", K: "♚\uFE0E" };

// The middle of a card: suit symbols for 2 to 10, one big symbol for an ace, a framed figure for J, Q and K.
function cardFace(rank, suit) {
    if (COURT_FIGURES[rank]) {
        return el("span", "card-face court",
            el("span", "court-frame", el("span", "court-suit", suit), el("span", "court-figure", COURT_FIGURES[rank]),
                el("span", "court-suit bottom", suit)));
    }
    if (rank === "A") {
        return el("span", "card-face ace", el("span", "ace-pip", suit));
    }
    return el("span", "card-face", ...PIP_LAYOUTS[rank].map(([x, y]) => {
        const pip = el("span", y > 50 ? "pip flipped" : "pip", suit);
        pip.style.left = `${x}%`;
        pip.style.top = `${y}%`;
        return pip;
    }));
}

// A card such as "10♥", drawn like a playing card: rank and suit in two corners and a classic face in the middle.
// Hearts and diamonds are red. Without text, a face-down card.
function cardElement(text) {
    const card = el("span", "card");
    if (!text) {
        card.classList.add("empty");
        return card;
    }
    const rank = text.slice(0, -1);
    const suit = text.slice(-1);
    card.classList.toggle("red", suit === "♥" || suit === "♦");
    card.setAttribute("role", "img");
    card.setAttribute("aria-label", text);
    card.append(
        el("span", "card-corner", el("span", "card-rank", rank), el("span", "card-suit", suit)),
        cardFace(rank, suit),
        el("span", "card-corner bottom", el("span", "card-rank", rank), el("span", "card-suit", suit)));
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

const hasCard = (game, key) => game.cards.some((c) => c.key === key);

// A playing seat's player number (1 to 6), in seat order.
const playerNumber = (game, seat) => game.playingSeats.indexOf(seat) + 1;

// The names of the different users holding these seats, in seat order. Each user votes once, for all their seats.
function userNames(state, seats) {
    const names = new Map();
    for (const s of seats) {
        const seat = state.seats[s];
        if (seat && !names.has(seat.playerId)) {
            names.set(seat.playerId, seat.name);
        }
    }
    return [...names.values()];
}
// Whether a scan's guess must be agreed before the strongest hand can be revealed.
const awaitsScan = (game) => game.decisions.some((d) => d.isOpen && (d.choice === "Rank" || d.choice === "Ranking"));

function gangStatus(game, state, view) {
    switch (game.phase) {
        case "Preparing": {
            const waiting = userNames(state, game.playingSeats.filter((s) => !game.preparedSeats.includes(s)));
            const step = game.preparingCard === "coordinator" ? "Coordinator: pick a card to pass on" : "Con Artist: look at your cards";
            return `${step}. Waiting for: ${waiting.join(", ")}`;
        }
        case "Betting":
            return view.isPlayer
                ? `${STAGES[game.stage]} chips`
                : `Players are taking ${STAGES[game.stage].toLowerCase()} chips…`;
        case "Reveal":
            return awaitsScan(game)
                ? "Before the strongest hand is revealed, the other players agree on the guess."
                : "Revealing hands, lowest red chip first…";
        default:
            if (game.vaults >= TO_FINISH) {
                return "🎉 Three vaults cracked: the gang wins!";
            }
            if (game.alarms >= TO_FINISH) {
                return "🚨 Three alarms: the gang is caught. Game over.";
            }
            return game.succeeded ? "💰 The vault is open!" : "🚨 The alarm went off!";
    }
}

// The challenge and specialist cards in play, drawn as small cards on the table: kind, name, what they do, and what
// they showed everyone. Cards added for this heist are marked "New". Open group decisions are shown to players next to
// their hand instead (see myDecisions).
function heistCards(game, state, view) {
    return game.cards.map((card) => {
        const isNew = game.newCards.includes(card.key);
        const kind = el("span", "heist-card-kind", card.kind === "Challenge" ? "Challenge" : "Specialist");
        const item = el("li", `heist-card ${card.kind.toLowerCase()}${isNew ? " new" : ""}`,
            isNew ? el("span", "heist-card-top", kind, el("span", "heist-card-new", "New")) : kind,
            el("strong", "heist-card-name", card.name),
            el("span", "heist-card-text", card.text));
        if (card.key === "motion-detector" || card.key === "laser-tripwires") {
            if (game.cards.some((c) => c.key === "quick-access")) {
                item.append(el("span", "heist-card-result", "No effect: there are no white chips in this heist (Quick Access)."));
            } else if (game.redrawnSeat !== null) {
                item.append(el("span", "heist-card-result", seatLabel(state, game.redrawnSeat, view), " got new private cards."));
            }
        }
        // Investor and Math Whiz: what they showed about everyone's cards, under the first of them.
        const announcer = game.cards.find((c) => c.key === "investor" || c.key === "math-whiz");
        if (card === announcer && game.announcements.length) {
            item.append(el("ul", "announcements", ...game.announcements.map((a) =>
                el("li", null, seatLabel(state, a.seat, view), el("strong", null, a.text)))));
        }
        if (game.preparingCard === card.key) {
            item.append(el("span", "heist-card-result", card.key === "coordinator"
                ? "Everyone is picking a card to pass on."
                : "Everyone is looking at their cards."));
        }
        const decision = game.decisions.find((d) => d.card === card.key);
        const scanResult = game.results.find((r) => r.card === card.key && r.passed !== null);
        if (scanResult) {
            // A scan's guess: a big PASS or FAIL stamp across the card.
            const stamp = el("span", scanResult.passed ? "scan-stamp pass" : "scan-stamp fail", scanResult.passed ? "PASS" : "FAIL");
            stamp.setAttribute("role", "img");
            stamp.setAttribute("aria-label", scanResult.passed ? "Scan passed" : "Scan failed");
            item.classList.add("stamped");
            item.append(stamp);
        } else if (decision?.isSkipped) {
            item.append(el("span", "heist-card-result optional", "Not needed: the heist already fails."));
        } else if (decision?.isDone) {
            item.append(decisionResult(game, state, view, decision));
        } else if (decision?.isOpen && isUnderWay(decision)) {
            const names = userNames(state, decision.agreed);
            item.append(el("span", "heist-card-result", `Deciding: ${names.length} of ${userNames(state, decision.voters).length} agreed`
                + (names.length ? ` (${names.join(", ")})` : "")));
        } else if (decision && card.kind === "Specialist" && !decision.isDone) {
            item.append(el("span", "heist-card-result optional", "Optional: not used in this heist yet"));
        }
        return item;
    });
}

// Whether someone has started a decision: proposed something or agreed.
const isUnderWay = (decision) => decision.agreed.length > 0 || decision.seat !== null || decision.value !== null;

// The open group decisions this user takes part in, next to their hand. Specialist cards are optional and folded
// away until someone starts using one; the scans must be decided, so they are always open.
function myDecisions(game, state, view) {
    return game.decisions
        .filter((d) => d.isOpen && view.mySeats.some((s) => d.voters.includes(s)))
        .map((d) => {
            const card = game.cards.find((c) => c.key === d.card);
            if (card.kind !== "Specialist") {
                return el("div", "my-decision challenge", el("strong", null, card.name), decisionPanel(game, state, view, d));
            }
            const box = el("details", "my-decision specialist",
                el("summary", null, el("strong", null, card.name), el("span", "hint", " · optional, use it any time")),
                el("p", "hint", card.text),
                decisionPanel(game, state, view, d));
            box.open = isUnderWay(d) || openSpecialists.has(d.card);
            box.addEventListener("toggle", () => {
                box.open ? openSpecialists.add(d.card) : openSpecialists.delete(d.card);
            });
            return box;
        });
}

// Specialist panels this user has opened, kept open across updates.
const openSpecialists = new Set();

const RANKS = ["2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K", "A"];
const RANKINGS = ["High card", "Pair", "Two pair", "Three of a kind", "Straight", "Flush", "Full house", "Four of a kind",
    "Straight flush", "Royal flush"];

// What a carried-out decision did, for everyone to see.
function decisionResult(game, state, view, decision) {
    const who = decision.seat === null ? null : seatLabel(state, decision.seat, view);
    const pending = game.pendingSeat !== null;
    const result = game.results.find((r) => r.card === decision.card);
    const line = (...content) => el("span", "heist-card-result", ...content);
    switch (decision.card) {
        case "informant":
            return game.shownFromSeat === null
                ? line(who, " is choosing a card to show someone.")
                : line(seatLabel(state, game.shownFromSeat, view), " showed a card to ", seatLabel(state, game.shownToSeat, view), ".");
        case "hacker":
            return line(who, pending ? " drew a card and is choosing one to discard." : " drew a card and discarded one.");
        case "jack":
            return line(who, pending ? " got the Jack card and is choosing one to discard." : " got the Jack card and discarded one.");
        case "muscle":
            return line(who, " beats every hand of the same ranking.");
        default:
            return result ? line(seatLabel(state, result.seat, view), el("strong", null, result.text)) : line("Used in this heist.");
    }
}

// The group's choice for a card: any voter proposes (a player and/or a value), everyone sees it, and it takes effect
// once every voter has agreed. Changing the proposal clears the agreement. Each user agrees once, for all their seats.
function decisionPanel(game, state, view, decision) {
    const panel = el("div", "decision");
    if (!decision.isOpen) {
        panel.append(el("span", "hint", decision.choice === "Rank" || decision.choice === "Ranking"
            ? "Decided just before the strongest hand is revealed."
            : "Can be decided during the betting."));
        return panel;
    }

    const mine = view.mySeats.filter((s) => decision.voters.includes(s));
    const needsPlayer = decision.choice === "Player" || decision.choice === "PlayerAndRank";
    const values = decision.choice === "Ranking" ? RANKINGS
        : decision.choice === "Rank" || decision.choice === "PlayerAndRank" ? RANKS
            : [];
    const propose = (seat, value) => call("Propose", view.sessionId, mine[0], decision.card, seat, value).catch(() => {});
    const prompt = {
        Player: "Choose a player together:",
        PlayerAndRank: "Choose a player and a card value together:",
        Rank: "Agree on a card value the strongest hand's player holds:",
        Ranking: "Agree on the strongest hand's ranking:",
    }[decision.choice];
    panel.append(el("span", "decision-prompt", prompt));

    if (needsPlayer) {
        panel.append(el("div", "decision-players", ...game.playingSeats.map((seat) => {
            const pick = el("button", decision.seat === seat ? "secondary small picked" : "secondary small", seatLabel(state, seat, view));
            pick.type = "button";
            pick.disabled = mine.length === 0;
            pick.addEventListener("click", () => propose(seat, decision.value));
            return pick;
        })));
    }
    if (values.length) {
        const select = el("select", "decision-value", el("option", null, "Choose…"), ...values.map((v) => el("option", null, v)));
        select.firstChild.value = "";
        select.value = decision.value ?? "";
        select.disabled = mine.length === 0;
        select.addEventListener("change", () => select.value && propose(decision.seat, select.value));
        panel.append(select);
    }

    // Agreeing is a thumbs-up, like Ready: grey until pressed, green once agreed (tap again to undo).
    const complete = (!needsPlayer || decision.seat !== null) && (!values.length || decision.value !== null);
    const agreedNames = userNames(state, decision.agreed);
    const row = el("div", "decision-agree");
    if (mine.length) {
        const agree = mine.some((s) => !decision.agreed.includes(s));
        const thumb = button("👍", agree ? "ready-thumb" : "ready-thumb is-ready",
            () => call("Agree", view.sessionId, mine[0], decision.card, agree).catch(() => {}));
        thumb.disabled = !complete;
        thumb.title = !complete ? "Complete the choice first" : agree ? "Agree" : "Agreed: tap to undo";
        thumb.setAttribute("aria-label", thumb.title);
        thumb.setAttribute("aria-pressed", String(!agree));
        row.append(thumb);
    }
    row.append(el("span", "hint", `👍 ${agreedNames.length} of ${userNames(state, decision.voters).length}`
        + (agreedNames.length ? ` (${agreedNames.join(", ")})` : "")));
    panel.append(row);
    if (!mine.length) {
        panel.append(el("span", "hint", view.isPlayer
            ? "You can see the strongest hand, so the other players decide this."
            : "The players decide this."));
    }
    return panel;
}

// The seat each Informant chooses to show a card to, kept across updates.
const showTo = {};

// One of this user's hands: big cards with the hand name, then the chip buttons.
// A locked chip (🔒) stays with whoever took it first: it cannot be taken from them, swapped or returned.
function myHand(game, state, view, hand) {
    const seat = hand.seat;
    const stage = game.stage;
    const myChip = game.chips[stage][seat];
    const betting = game.phase === "Betting";
    const isLocked = (number) => game.lockedChips.includes(number);
    const keepsLockedChip = myChip !== null && isLocked(myChip);

    const info = el("div", "my-hand-info", el("span", "my-hand-title", `Player ${playerNumber(game, seat)}`));
    // The chips this seat took in this heist's stages so far.
    const taken = STAGES.map((_, s) => (s <= stage ? game.chips[s][seat] : null))
        .map((number, s) => (number === null ? null : chipElement(s, number)))
        .filter((chip) => chip !== null);
    if (taken.length) {
        info.append(el("span", "stage-pill", ...taken));
    }

    if (hand.shownCard) {
        const shown = cardElement(hand.shownCard.card);
        info.append(el("span", "shown-card", seatLabel(state, hand.shownCard.fromSeat, view), " showed you", shown));
    }

    // Tapping a card: Coordinator picks it to pass on (it can be changed until everyone has picked); Informant shows it;
    // Hacker and Jack discard it.
    const passing = game.phase === "Preparing" && game.preparingCard === "coordinator";
    const step = game.pendingSeat === seat ? game.pendingStep : "None";
    const others = game.playingSeats.filter((s) => s !== seat);
    if (step === "ShowCard" && !others.includes(showTo[seat])) {
        showTo[seat] = others[0];
    }
    const cardAction = passing ? { verb: "Pass", run: (index) => call("PassCard", view.sessionId, seat, index) }
        : step === "ShowCard" ? { verb: "Show", run: (index) => call("ShowCard", view.sessionId, seat, index, showTo[seat]) }
            : step === "Discard" ? { verb: "Discard", run: (index) => call("DiscardCard", view.sessionId, seat, index) }
                : null;
    const box = el("div", "my-hand", el("div", "my-hand-top", el("div", "card-row", ...hand.cards.map((c, index) => {
        const card = cardElement(c);
        card.classList.add("big");
        if (!cardAction) {
            return card;
        }
        const pick = el("button", passing && c === hand.passingCard ? "card-pick picked" : "card-pick", card);
        pick.type = "button";
        pick.title = `${cardAction.verb} ${c}`;
        pick.addEventListener("click", () => cardAction.run(index).catch(() => {}));
        return pick;
    })), info));

    if (step === "ShowCard") {
        const select = el("select", "decision-value", ...others.map((s) => {
            const option = el("option", null, state.seats[s]?.name ?? `Seat ${s + 1}`);
            option.value = String(s);
            return option;
        }));
        select.value = String(showTo[seat]);
        select.addEventListener("change", () => { showTo[seat] = Number(select.value); });
        box.append(el("p", "hint step", "Informant: choose who sees it, then tap the card to show them. ", select));
    } else if (step === "Discard") {
        box.append(el("p", "hint step", "Tap the card to discard. The stage can't end until you do."));
    }

    if (passing) {
        const order = game.playingSeats;
        const next = order[(order.indexOf(seat) + 1) % order.length];
        box.append(el("p", "hint", hand.passingCard
            ? `You pass ${hand.passingCard} to ${state.seats[next]?.name ?? ""}. Tap another card to change it.`
            : `Tap the card you want to pass to ${state.seats[next]?.name ?? ""}.`));
    }

    if (!betting) {
        return box;
    }

    // One button per chip: 1 = weakest hand, N = strongest. The label below shows who holds it.
    const chips = el("div", "my-chips");
    for (let number = 1; number <= game.playingSeats.length; number++) {
        const holder = game.playingSeats.find((s) => game.chips[stage][s] === number);
        const mine = holder === seat;
        const label = holder === undefined ? "" : mine ? "You" : state.seats[holder]?.name ?? "";
        const lockedByOther = isLocked(number) && holder !== undefined && !mine;
        const chip = el("button", mine ? "chip-button mine" : "chip-button",
            chipElement(stage, number), el("span", "holder", isLocked(number) ? `🔒 ${label}`.trim() : label));
        // Tapping your own chip returns it to the middle (unless it is locked).
        chip.type = "button";
        chip.disabled = lockedByOther || keepsLockedChip;
        chip.title = lockedByOther ? `Chip ${number} is locked: ${label} keeps it`
            : keepsLockedChip ? "Your chip is locked: you keep it for this stage"
                : holder === undefined ? `Take chip ${number}` : mine ? "Your chip: tap to return it" : `Take chip ${number} from ${label}`;
        chip.addEventListener("click", () => (mine
            ? call("ReturnChip", view.sessionId, seat)
            : call("TakeChip", view.sessionId, seat, number)).catch(() => {}));
        chips.append(chip);
    }

    box.append(chips);
    if (game.lockedChips.length) {
        box.append(el("p", "hint", "🔒 Locked: whoever takes it first keeps it."));
    }
    return box;
}

// This user's buttons, once for all their hands (each user is ready or confirms once): Ready once everyone holds a
// chip, and with Con Artist, "I've seen my cards". Null if there are none now.
function myActions(game, state, view) {
    const seats = view.mySeats.filter((s) => game.playingSeats.includes(s));
    if (!seats.length) {
        return null;
    }
    if (game.phase === "Preparing" && game.preparingCard === "con-artist") {
        const done = seats.every((s) => game.preparedSeats.includes(s));
        const seen = button(done ? "✓ Seen" : "I've seen my cards", done ? "is-ready" : "",
            () => call("ConfirmSeen", view.sessionId, seats[0]).catch(() => {}));
        seen.disabled = done;
        return el("div", null,
            el("p", "hint", "Remember your cards: once everyone has seen theirs, all private cards are shuffled and dealt again."),
            el("div", "my-hand-actions", seen));
    }
    if (game.phase !== "Betting" || game.middleChips.length > 0) {
        return null;
    }

    // Ready is a thumbs-up: grey until pressed, green once ready (tap again to undo).
    const isReady = seats.every((s) => game.readySeats.includes(s));
    const ready = button("👍", isReady ? "ready-thumb is-ready" : "ready-thumb",
        () => call("SetReady", view.sessionId, seats[0], !isReady).catch(() => {}));
    ready.title = isReady ? "Ready: tap to undo" : "Ready";
    ready.setAttribute("aria-label", ready.title);
    ready.setAttribute("aria-pressed", String(isReady));
    return el("div", "my-hand-actions", ready);
}

// Every playing seat's chips from all stages, with ✓ for players who are ready. Skipped stages are struck out, and
// with Blackout, earlier stages' chips show as "?" because they were removed.
function chipTable(game, state, view) {
    const blackout = game.cards.some((c) => c.key === "blackout");
    const skipped = (stage) => game.skippedStages.includes(stage);
    const header = el("tr", null, el("th", null, "Player"), ...STAGES.map((s, stage) => {
        const th = el("th", skipped(stage) ? "skipped" : null, s);
        th.title = skipped(stage) ? "Skipped in this heist" : "";
        return th;
    }));
    const rows = game.playingSeats.map((seat) => el("tr", null,
        el("td", null, el("span", "player-number", String(playerNumber(game, seat))), seatLabel(state, seat, view)),
        ...STAGES.map((_, stage) => {
            const chip = game.chips[stage][seat];
            const removed = blackout && stage < game.stage && !skipped(stage);
            const empty = skipped(stage) ? "" : removed ? "?" : stage <= game.stage ? "–" : "";
            const cell = el("td", null, chip === null ? empty : chipElement(stage, chip));
            cell.title = removed ? "Removed (Blackout)" : "";
            if (stage === game.stage && game.phase === "Betting" && game.readySeats.includes(seat)) {
                cell.append(el("span", "ready", " 👍"));
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

// Observers see everything without scrolling: the table's font size is the largest (8 to 40 pixels) at which the page
// fits the screen.
function fitTable() {
    const table = document.querySelector(".table-view");
    if (!document.body.classList.contains("observer")) {
        table.style.removeProperty("font-size");
        return;
    }
    const page = document.documentElement;
    const fits = () => page.scrollHeight <= innerHeight && page.scrollWidth <= innerWidth;
    let low = 8;
    let high = 40;
    while (high - low > 0.5) {
        const size = (low + high) / 2;
        table.style.fontSize = `${size}px`;
        if (fits()) low = size; else high = size;
    }
    table.style.fontSize = `${low}px`;
    while (low > 8 && !fits()) {
        low -= 0.5;
        table.style.fontSize = `${low}px`;
    }
}

let gangState = null;
let tableHidden = localStorage.getItem(TABLE_HIDDEN_KEY) === "1";

const gang = startSessionPage("TheGang", (game, state, view) => {
    gangState = game;
    $("status").textContent = gangStatus(game, state, view);

    document.body.classList.toggle("observer", !view.isPlayer);
    $("heist-cards").hidden = game.cards.length === 0;
    $("heist-cards").replaceChildren(...heistCards(game, state, view));
    const decisions = myDecisions(game, state, view);
    $("my-decisions").hidden = decisions.length === 0;
    $("my-decisions").replaceChildren(...decisions);

    const hands = view.private?.hands ?? [];
    const actions = myActions(game, state, view);
    $("my-hands").replaceChildren(...hands.map((hand) => myHand(game, state, view, hand)), ...(actions ? [actions] : []));
    $("reveal-next").hidden = !(view.isPlayer && game.phase === "Reveal" && !awaitsScan(game));
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
    fitTable();
});

addEventListener("resize", () => requestAnimationFrame(fitTable));
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
