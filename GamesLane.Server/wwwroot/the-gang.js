"use strict";

// The Gang: players rank their hands by taking numbered chips over four stages (white, yellow, orange, red).
// Each player's own two cards come in view.private; the shared screen never sees them before the reveal.
// The page shows the player's own hand on top, and the table (everything observers see) below. Players can hide
// the table, e.g. when a second device shows it; the choice is remembered on this device.

const STAGES = ["White", "Yellow", "Orange", "Red"];
const TO_FINISH = 3;
const TABLE_HIDDEN_KEY = "theGang.tableHidden";

function chipElement(stage, number, content = number) {
    const chip = el("span", `chip stage-${stage}`, String(content));
    chip.title = `${STAGES[stage]} chip ${number}`;
    return chip;
}

// Motion Detector and Laser Tripwires: the white chip of the player who got new pocket cards no longer says anything
// about their hand, so it is crossed out wherever it is shown.
const isVoidChip = (game, stage, seat) => stage === 0 && seat === game.redrawnSeat;

function voidChip(chip) {
    chip.classList.add("void");
    chip.title = "No longer counts: this player got new pocket cards";
    return chip;
}

// The card that gave a player new pocket cards: Motion Detector if the first 3 shared cards include a J, Q or K,
// otherwise Laser Tripwires.
function redrawCardName(game) {
    const hasFace = game.communityCards.slice(0, 3).flat().some((c) => /^[JQK]/.test(c));
    return hasFace ? "Motion Detector" : "Laser Tripwires";
}

// The crossed-out white chip of the player who got new pocket cards, followed by a space, or nothing if it is not
// shown (Blackout).
function voidWhiteChip(game) {
    const number = game.chips[0][game.redrawnSeat];
    return number === null || number === undefined ? [] : [voidChip(chipElement(0, number)), " "];
}

// Whether the player who got new pocket cards has yet to confirm it (the stage can't end until they do).
const awaitsRedraw = (game) => game.redrawnSeat !== null && !game.redrawConfirmed;

function button(text, className, onClick) {
    const b = el("button", className, text);
    b.type = "button";
    b.addEventListener("click", onClick);
    return b;
}

// A shared slot: one card, or with jokers, the 2 or 3 cards that replaced a joker, side by side (each hand uses one).
function slotElement(cards) {
    if (!cards || cards.length === 1) {
        return cardElement(cards?.[0]);
    }
    const slot = el("span", "card-slot", ...cards.map(cardElement));
    slot.title = "These cards replaced a joker: each hand uses one of them.";
    return slot;
}

// A playing seat's player number (1 to 8), in seat order.
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
            const step = { jokers: "Jokers: swap the jokers you were dealt", coordinator: "Coordinator: pick a card to pass on" }[game.preparingCard]
                ?? "Con Artist: look at your cards";
            return `${step}. Waiting for: ${waiting.join(", ")}`;
        }
        case "Betting": {
            const status = view.isPlayer
                ? `${STAGES[game.stage]} chips`
                : `Players are taking ${STAGES[game.stage].toLowerCase()} chips…`;
            return awaitsRedraw(game)
                ? `${status} · Waiting for ${state.seats[game.redrawnSeat]?.name ?? ""} to confirm their new cards`
                : status;
        }
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

// The challenge and specialist cards in play, drawn as small cards on the table: a picture, the kind and the name.
// Tapping a card shows what it does. Cards added for this heist are marked "New", and cards not used (yet) in this heist
// are faded. What the cards did is shown in the chip table (see chipTable), and open group decisions are shown to
// players next to their hand (see myDecisions).
function heistCards(game) {
    return game.cards.map((card) => {
        const isNew = game.newCards.includes(card.key);
        const kind = el("span", "heist-card-kind", card.kind === "Challenge" ? "Challenge" : "Specialist");
        const art = el("img", "heist-card-art");
        art.src = `/images/the-gang/${card.key}.svg`;
        art.alt = "";
        const item = el("li", `heist-card ${card.kind.toLowerCase()}${isNew ? " new" : ""}`,
            art,
            el("span", "heist-card-body",
                isNew ? el("span", "heist-card-top", kind, el("span", "heist-card-new", "New")) : kind,
                el("strong", "heist-card-name", card.name)));
        const unused = isUnused(game, card);
        item.classList.toggle("unused", unused);
        item.tabIndex = 0;
        item.setAttribute("role", "button");
        item.title = unused ? `${card.name} (not used in this heist): tap for details` : `${card.name}: tap for details`;
        item.addEventListener("click", () => showCardText(card));
        item.addEventListener("keydown", (event) => {
            if (event.key === "Enter" || event.key === " ") {
                event.preventDefault();
                showCardText(card);
            }
        });
        return item;
    });
}

// A popup with a challenge or specialist card's picture, kind, name and what it does.
function showCardText(card) {
    const dialog = createDialog(`
        <img class="heist-card-art" alt="">
        <span class="heist-card-kind"></span>
        <h2></h2>
        <p class="card-popup-text"></p>
        <div class="actions"><button type="button" data-close>Close</button></div>`);
    dialog.classList.add("card-popup", card.kind.toLowerCase());
    dialog.querySelector(".heist-card-art").src = `/images/the-gang/${card.key}.svg`;
    dialog.querySelector(".heist-card-kind").textContent = card.kind;
    dialog.querySelector("h2").textContent = card.name;
    dialog.querySelector(".card-popup-text").textContent = card.text;
    // Tapping outside the popup closes it too.
    dialog.addEventListener("click", (event) => {
        if (event.target === dialog) {
            dialog.close();
        }
    });
    dialog.addEventListener("close", () => dialog.remove());
    dialog.showModal();
}

// Whether a card has not been used (yet) in this heist: Motion Detector or Laser Tripwires that did not go off (known
// once the first 3 shared cards appear, or at once with Quick Access, which has no white chips), an optional specialist
// card the group has not used yet, or a scan that is not needed.
function isUnused(game, card) {
    if (card.key === "motion-detector" || card.key === "laser-tripwires") {
        const decided = game.communityCards.length >= 3 || game.cards.some((c) => c.key === "quick-access");
        return decided && !(game.redrawnSeat !== null && redrawCardName(game) === card.name);
    }
    const decision = game.decisions.find((d) => d.card === card.key);
    if (!decision) {
        return false;
    }
    return card.kind === "Specialist" ? !decision.isDone : decision.isSkipped;
}

const cardName = (game, key) => game.cards.find((c) => c.key === key)?.name ?? key;

// What the heist's cards did to or showed about one player, one line each starting with the card's name, for the
// player's row in the chip table: Motion Detector or Laser Tripwires, Investor and Math Whiz, and the carried-out group
// decisions (the scans with a PASS or FAIL badge).
function playerFacts(game, state, seat) {
    const facts = [];
    const fact = (...content) => facts.push(el("span", "player-facts", ...content));
    const nameOf = (s) => state.seats[s]?.name ?? `Player ${playerNumber(game, s)}`;
    if (game.redrawnSeat === seat) {
        fact(`${redrawCardName(game)}: new pocket cards were dealt`);
    }
    for (const announcement of game.announcements.filter((a) => a.seat === seat)) {
        fact(`${cardName(game, announcement.card)}: ${announcement.text}`);
    }
    const discarding = game.pendingSeat === seat && game.pendingStep === "Discard";
    for (const decision of game.decisions.filter((d) => d.isDone)) {
        const name = cardName(game, decision.card);
        const result = game.results.find((r) => r.card === decision.card);
        switch (decision.card) {
            case "informant":
                if (decision.seat === seat) {
                    fact(`${name}: `, game.shownFromSeat === null ? "choosing a card to show" : `showed a card to ${nameOf(game.shownToSeat)}`);
                }
                break;
            case "hacker":
                if (decision.seat === seat) {
                    fact(`${name}: drew a card`, discarding ? ", choosing one to discard" : " and discarded one");
                }
                break;
            case "jack":
                if (decision.seat === seat) {
                    fact(`${name}: got the Jack card`, discarding ? ", choosing one to discard" : " and discarded one");
                }
                break;
            case "muscle":
                if (decision.seat === seat) {
                    fact(`${name}: beats every hand of the same ranking`);
                }
                break;
            default:
                if (result?.seat !== seat) {
                    break;
                }
                if (result.passed === null) {
                    fact(`${name}: ${result.text}`);
                } else {
                    fact(`${name}: guessed ${decision.value} `,
                        el("span", result.passed ? "fact-badge pass" : "fact-badge fail", result.passed ? "PASS" : "FAIL"));
                }
        }
    }
    return facts;
}

// Lines under the chip table about the whole heist: group decisions under way.
function tableNotes(game, state) {
    return game.decisions
        .filter((decision) => decision.isOpen && isUnderWay(decision))
        .map((decision) => {
            const names = userNames(state, decision.agreed);
            return `${cardName(game, decision.card)}: deciding, ${names.length} of ${userNames(state, decision.voters).length} agreed`
                + (names.length ? ` (${names.join(", ")})` : "");
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
        .map((number, s) => (number === null ? null : isVoidChip(game, s, seat) ? voidChip(chipElement(s, number)) : chipElement(s, number)))
        .filter((chip) => chip !== null);
    if (taken.length) {
        info.append(el("span", "stage-pill", ...taken));
    }

    if (hasMuscle(game, seat)) {
        info.append(muscleBadge(game));
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

    // A joker in the hand: a panel offering two smaller cards; tap the one to keep instead of the joker. Tapping an
    // offered joker swaps it for two more cards.
    if (hand.jokerOffer) {
        box.querySelector(".my-hand-top .card.joker")?.classList.add("replacing");
        const offer = el("div", "joker-choices", ...hand.jokerOffer.map((c, index) => {
            const again = c === "JOKER";
            const pick = el("button", "joker-choice", cardElement(c), el("span", "joker-choice-label", again ? "Swap again" : "Keep"));
            pick.type = "button";
            pick.title = again ? "Swap this joker for two more cards" : `Keep ${c} instead of the joker`;
            pick.addEventListener("click", () => call("ChooseJokerCard", view.sessionId, seat, index).catch(() => {}));
            return pick;
        }));
        const hasJoker = hand.jokerOffer.includes("JOKER");
        box.append(el("div", "joker-panel",
            el("strong", "joker-panel-title", "🃏 You have a joker: replace it"),
            el("p", "joker-panel-text", hasJoker
                ? "Keep the other card, or swap this joker for two more cards."
                : "Choose one of these two cards to take the joker's place. The other is discarded."),
            offer));
    }

    // Motion Detector, Laser Tripwires: this seat got new pocket cards. It confirms with a button.
    if (game.redrawnSeat === seat && !game.redrawConfirmed) {
        box.append(el("div", "redraw-panel",
            el("strong", "redraw-panel-title", `🔄 ${redrawCardName(game)}: you have new pocket cards`),
            el("p", "redraw-panel-text",
                "Your old cards are gone. Your white chip ", ...voidWhiteChip(game),
                "no longer says anything about your hand. The stage can't end until you confirm."),
            button("Got it", "", () => call("ConfirmRedraw", view.sessionId, seat).catch(() => {}))));
    }

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
            el("p", "hint", "Remember your cards: once everyone has seen theirs, all pocket cards are shuffled and dealt again."),
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
// with Blackout, earlier stages' chips show as chips of their colour with "?" because they were removed.
function chipTable(game, state, view) {
    const blackout = game.cards.some((c) => c.key === "blackout");
    const skipped = (stage) => game.skippedStages.includes(stage);
    const header = el("tr", null, el("th", null, "Player"), ...STAGES.map((s, stage) => {
        const th = el("th", skipped(stage) ? "skipped" : null, s);
        th.title = skipped(stage) ? "Skipped in this heist" : "";
        return th;
    }));
    // With jokers: 🃏 for each joker a player swapped (nobody sees which cards), and "…" while they are choosing.
    const jokerMark = (seat) => {
        const swaps = game.jokerSwaps.filter((s) => s === seat).length;
        const choosing = game.jokerSeats.includes(seat);
        if (!swaps && !choosing) {
            return [];
        }
        const mark = el("span", "joker-mark", "🃏".repeat(swaps) + (choosing ? "…" : ""));
        mark.title = choosing ? "Swapping a joker" : swaps === 1 ? "Swapped a joker" : `Swapped ${swaps} jokers`;
        return [mark];
    };
    const rows = game.playingSeats.map((seat) => el("tr", null,
        el("td", null, el("span", "player-number", String(playerNumber(game, seat))), seatLabel(state, seat, view), ...jokerMark(seat),
            ...playerFacts(game, state, seat)),
        ...STAGES.map((_, stage) => {
            const chip = game.chips[stage][seat];
            const removed = blackout && stage < game.stage && !skipped(stage);
            const voided = isVoidChip(game, stage, seat);
            // With Blackout, a removed chip is shown in its stage's colour with "?" instead of its number.
            const content = chip !== null ? chipElement(stage, chip)
                : removed ? Object.assign(chipElement(stage, "?"), { title: `${STAGES[stage]} chip removed (Blackout)` })
                    : skipped(stage) ? "" : stage <= game.stage ? "–" : "";
            const cell = el("td", null, voided && content instanceof Element ? voidChip(content) : content);
            cell.title = voided ? "No longer counts: this player got new pocket cards" : removed ? "Removed (Blackout)" : "";
            if (stage === game.stage && game.phase === "Betting" && game.readySeats.includes(seat)) {
                cell.append(el("span", "ready", " 👍"));
            }
            return cell;
        })));
    const notes = tableNotes(game, state);
    const caption = notes.length ? [el("caption", "chip-note", ...notes.map((note) => el("div", null, note)))] : [];
    return [...caption, el("thead", null, header), el("tbody", null, ...rows)];
}

// Muscle: whether the group chose this seat, whose hand then beats every hand of the same ranking.
const hasMuscle = (game, seat) => game.decisions.some((d) => d.card === "muscle" && d.isDone && d.seat === seat);

function muscleBadge(game) {
    const badge = el("span", "muscle-badge", `💪 ${cardName(game, "muscle")}`);
    badge.title = "Beats every hand of the same ranking";
    return badge;
}

function revealedHand(game, state, view, hand) {
    const name = handName(hand);
    if (hasMuscle(game, hand.seat)) {
        name.append(" ", muscleBadge(game));
    }
    const item = el("li", hand.inOrder ? "revealed-hand right" : "revealed-hand wrong",
        chipElement(3, game.chips[3][hand.seat]),
        seatLabel(state, hand.seat, view),
        el("span", "card-row", ...hand.cards.map(cardElement)),
        name);
    if (!hand.inOrder) {
        item.title = "Weaker than the hand before";
    }
    return item;
}

// A revealed hand's ranking and five card values, e.g. "Two pair: 9, 9, 5, 5, K". If the hand is weaker than the one
// before it, the first reason is in red: the ranking if it is lower, otherwise the first lower card values.
function handName(hand) {
    const wrong = (text) => el("span", "wrong-part", text);
    const values = hand.values.flatMap((value, i) => [i ? ", " : "", hand.wrongValues.includes(i) ? wrong(value) : value]);
    return el("strong", "hand-name", hand.wrongRanking ? wrong(hand.ranking) : hand.ranking, ": ", ...values);
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
// Players on a phone or tablet (a touch screen without a mouse) start with the table hidden, as a shared screen usually
// shows it; on a computer it is shown. Once a player hides or shows it, their choice is remembered on this device.
const savedTableHidden = localStorage.getItem(TABLE_HIDDEN_KEY);
let tableHidden = savedTableHidden === null
    ? matchMedia("(hover: none) and (pointer: coarse)").matches
    : savedTableHidden === "1";

const gang = startSessionPage("TheGang", (game, state, view) => {
    gangState = game;
    $("status").textContent = gangStatus(game, state, view);

    document.body.classList.toggle("observer", !view.isPlayer);
    $("heist-cards").hidden = game.cards.length === 0;
    $("heist-cards").replaceChildren(...heistCards(game));
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

    $("community").replaceChildren(...Array.from({ length: 5 }, (_, i) => slotElement(game.communityCards[i])));
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
