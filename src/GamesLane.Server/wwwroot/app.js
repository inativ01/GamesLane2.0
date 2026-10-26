"use strict";

const $ = (id) => document.getElementById(id);
const params = new URLSearchParams(location.search);

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/game")
    .build();

const TITLES = { TicTacToe: "Tic-tac-toe", RockPaperScissors: "Rock-paper-scissors" };
const SYMBOLS = { Rock: "✊", Paper: "✋", Scissors: "✌️" };

let role = null; // "player" or "spectator"
let gameId = null;
let mySeat = null;
let myPick = null; // { round, choice }: this player's rock-paper-scissors pick, shown before the reveal
let lastState = null;

connection.on("StateUpdated", render);
connection.onclose(() => showError("Connection lost. Reload the page to reconnect."));

const ready = connection.start().catch(() => showError("Could not connect to the server."));

const cells = [];
for (let i = 0; i < 9; i++) {
    const cell = document.createElement("button");
    cell.type = "button";
    cell.className = "cell";
    cell.setAttribute("aria-label", `Cell ${i + 1}`);
    cell.addEventListener("click", () => call("MakeMove", gameId, i).catch(() => {}));
    $("board").appendChild(cell);
    cells.push(cell);
}

function showError(message) {
    $("error").textContent = message;
}

// HubException messages arrive as "An unexpected error occurred invoking '...' on the server. HubException: <message>"
function errorText(err) {
    const match = /HubException: (.*)$/.exec(err?.message ?? "");
    return match ? match[1] : "Something went wrong.";
}

async function call(method, ...args) {
    await ready;
    showError("");
    try {
        return await connection.invoke(method, ...args);
    } catch (err) {
        showError(errorText(err));
        throw err;
    }
}

function enterGame(newRole, id) {
    role = newRole;
    gameId = id;
    $("home").hidden = true;
    $("game").hidden = false;
    $("game-code").textContent = id;

    if (role === "spectator") {
        history.replaceState(null, "", `?game=${id}&role=spectator`);
        $("join-link").textContent = `Players join at ${location.origin}/?game=${id}`;
        $("join-link").hidden = false;
    }
}

async function watch(code) {
    try {
        const state = await call("JoinAsSpectator", code);
        enterGame("spectator", state.gameId);
        render(state);
    } catch {
        // Error already shown.
    }
}

async function joinAsPlayer(code, name) {
    try {
        const info = await call("JoinAsPlayer", code, name);
        mySeat = info.seat;
        enterGame("player", code.toUpperCase());
        if (lastState) {
            render(lastState);
        }
    } catch {
        // Error already shown.
    }
}

function isMe(seat) {
    return role === "player" && seat === mySeat;
}

function listItem(text) {
    const item = document.createElement("li");
    item.textContent = text;
    return item;
}

function waitingText(state) {
    return `Waiting for players (${state.players.length}/2)…`;
}

function render(state) {
    lastState = state;
    $("game-title").textContent = TITLES[state.gameType];
    $("board").hidden = state.gameType !== "TicTacToe";
    $("rps").hidden = state.gameType !== "RockPaperScissors";

    if (state.gameType === "TicTacToe") {
        renderTicTacToe(state);
    } else {
        renderRockPaperScissors(state);
    }
}

// Tic-tac-toe

function ticTacToeStatus(state, myMark) {
    const nameOf = (mark) => state.players.find((p) => p.mark === mark)?.name ?? mark;
    switch (state.status) {
        case "WaitingForPlayers":
            return waitingText(state);
        case "InProgress":
            if (role === "player") {
                return state.currentTurn === myMark ? "Your turn" : `Waiting for ${nameOf(state.currentTurn)}…`;
            }
            return `${nameOf(state.currentTurn)} (${state.currentTurn}) to move`;
        default:
            if (state.isDraw) {
                return "It's a draw!";
            }
            if (role === "player") {
                return state.winner === myMark ? "You win!" : `${nameOf(state.winner)} wins.`;
            }
            return `${nameOf(state.winner)} (${state.winner}) wins!`;
    }
}

function renderTicTacToe(state) {
    const myMark = role === "player" ? state.players[mySeat]?.mark : null;
    const myTurn = myMark && state.status === "InProgress" && state.currentTurn === myMark;

    state.board.forEach((mark, i) => {
        cells[i].textContent = mark === "None" ? "" : mark;
        cells[i].dataset.mark = mark;
        cells[i].disabled = !(myTurn && mark === "None");
    });

    $("status").textContent = ticTacToeStatus(state, myMark);
    $("players").replaceChildren(...state.players.map((p, seat) =>
        listItem(`${p.mark}: ${p.name}${isMe(seat) ? " (you)" : ""}`)));
}

// Rock-paper-scissors

function rockPaperScissorsStatus(state) {
    if (state.status === "WaitingForPlayers") {
        return waitingText(state);
    }

    const round = `Round ${state.round}`;
    if (!state.isRoundComplete) {
        if (role === "player") {
            const other = state.players[1 - mySeat];
            return state.players[mySeat].hasPicked ? `Waiting for ${other.name}…` : `${round}: make your pick`;
        }
        return `${round}: waiting for picks…`;
    }

    if (state.roundWinner === null) {
        return `${round}: it's a draw!`;
    }
    if (role === "player") {
        return isMe(state.roundWinner) ? "You win this round!" : `${state.players[state.roundWinner].name} wins this round.`;
    }
    return `${state.players[state.roundWinner].name} wins ${round.toLowerCase()}!`;
}

function handCard(state, player, seat) {
    let symbol;
    let label;
    if (state.isRoundComplete) {
        symbol = SYMBOLS[player.pick];
        label = player.pick;
    } else if (isMe(seat) && myPick) {
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
    name.textContent = player.name + (isMe(seat) ? " (you)" : "");
    const symbolEl = document.createElement("div");
    symbolEl.className = "hand-symbol";
    symbolEl.textContent = symbol;
    const labelEl = document.createElement("div");
    labelEl.className = "hand-label";
    labelEl.textContent = label;
    card.append(name, symbolEl, labelEl);
    return card;
}

function renderRockPaperScissors(state) {
    if (myPick && myPick.round !== state.round) {
        myPick = null;
    }

    const me = role === "player" ? state.players[mySeat] : null;
    const canPick = Boolean(me) && state.status === "InProgress" && !me.hasPicked && !myPick;
    $("rps-picks").hidden = !canPick;
    $("next-round").hidden = !(me && state.isRoundComplete);

    $("status").textContent = rockPaperScissorsStatus(state);
    $("rps-hands").replaceChildren(...state.players.map((p, seat) => handCard(state, p, seat)));
    $("players").replaceChildren(
        ...state.players.map((p, seat) => listItem(`${p.name}${isMe(seat) ? " (you)" : ""}: ${p.score}`)),
        listItem(`Draws: ${state.draws}`));
}

document.querySelectorAll(".pick").forEach((button) => {
    button.addEventListener("click", () => {
        myPick = { round: lastState.round, choice: button.dataset.choice };
        render(lastState);
        call("Pick", gameId, button.dataset.choice).catch(() => {
            myPick = null;
            render(lastState);
        });
    });
});

$("next-round").addEventListener("click", () => {
    call("NextRound", gameId, lastState.round).catch(() => {});
});

document.querySelectorAll("[data-create]").forEach((button) => {
    button.addEventListener("click", async () => {
        try {
            await watch(await call("CreateGame", button.dataset.create));
        } catch {
            // Error already shown.
        }
    });
});

$("join-form").addEventListener("submit", (event) => {
    event.preventDefault();
    const code = $("code").value.trim();
    const name = $("name").value.trim();
    if (!code || !name) {
        showError("Enter a game code and your name.");
        return;
    }
    joinAsPlayer(code, name);
});

$("watch").addEventListener("click", () => {
    const code = $("code").value.trim();
    if (!code) {
        showError("Enter a game code to watch.");
        return;
    }
    watch(code);
});

const initialCode = params.get("game");
if (initialCode) {
    $("code").value = initialCode;
    if (params.get("role") === "spectator") {
        watch(initialCode);
    }
}
