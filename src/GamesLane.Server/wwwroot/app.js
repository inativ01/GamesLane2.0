"use strict";

const $ = (id) => document.getElementById(id);
const params = new URLSearchParams(location.search);

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/game")
    .build();

let role = null; // "player" or "spectator"
let gameId = null;
let myMark = null;
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
        myMark = info.mark;
        enterGame("player", code.toUpperCase());
        if (lastState) {
            render(lastState);
        }
    } catch {
        // Error already shown.
    }
}

function nameOf(state, mark) {
    return state.players.find((p) => p.mark === mark)?.name ?? mark;
}

function statusText(state) {
    switch (state.status) {
        case "WaitingForPlayers":
            return `Waiting for players (${state.players.length}/2)…`;
        case "InProgress":
            if (role === "player") {
                return state.currentTurn === myMark ? "Your turn" : `Waiting for ${nameOf(state, state.currentTurn)}…`;
            }
            return `${nameOf(state, state.currentTurn)} (${state.currentTurn}) to move`;
        default:
            if (state.isDraw) {
                return "It's a draw!";
            }
            if (role === "player") {
                return state.winner === myMark ? "You win!" : `${nameOf(state, state.winner)} wins.`;
            }
            return `${nameOf(state, state.winner)} (${state.winner}) wins!`;
    }
}

function render(state) {
    lastState = state;
    const myTurn = role === "player" && state.status === "InProgress" && state.currentTurn === myMark;

    state.board.forEach((mark, i) => {
        cells[i].textContent = mark === "None" ? "" : mark;
        cells[i].dataset.mark = mark;
        cells[i].disabled = !(myTurn && mark === "None");
    });

    $("status").textContent = statusText(state);
    $("players").replaceChildren(...state.players.map((p) => {
        const item = document.createElement("li");
        item.textContent = `${p.mark}: ${p.name}${role === "player" && p.mark === myMark ? " (you)" : ""}`;
        return item;
    }));
}

$("create").addEventListener("click", async () => {
    try {
        await watch(await call("CreateGame"));
    } catch {
        // Error already shown.
    }
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
