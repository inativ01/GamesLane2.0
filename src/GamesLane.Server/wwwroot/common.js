"use strict";

// Shared by all pages: the hub connection, error display, saved name, and the game page framework.

const $ = (id) => document.getElementById(id);
const params = new URLSearchParams(location.search);

const GAME_PAGES = { TicTacToe: "/tictactoe.html", RockPaperScissors: "/rock-paper-scissors.html" };

// A random ID kept by this browser tab, so a guest gets their seat back after a refresh or reconnect.
// It is per tab, so several tabs in one browser can play as different guests.
function guestId() {
    let id = sessionStorage.getItem("guestId");
    if (!id) {
        id = Array.from(crypto.getRandomValues(new Uint8Array(16)), (b) => b.toString(16).padStart(2, "0")).join("");
        sessionStorage.setItem("guestId", id);
    }
    return id;
}

const connection = new signalR.HubConnectionBuilder()
    .withUrl(`/hubs/game?guestId=${guestId()}`)
    .withAutomaticReconnect()
    .build();

connection.onreconnecting(() => showError("Connection lost. Reconnecting…"));
connection.onclose(() => showError("Connection lost. Reload the page to reconnect."));

const ready = connection.start().then(
    () => true,
    () => {
        showError("Could not connect to the server. Reload the page to try again.");
        return false;
    });

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

function savedName() {
    return localStorage.getItem("playerName") ?? "";
}

// Returns the trimmed name from the input and remembers it, or shows an error if it is empty.
function requireName(input) {
    const name = input.value.trim();
    if (!name) {
        showError("Enter your name first.");
        input.focus();
        return null;
    }
    localStorage.setItem("playerName", name);
    return name;
}

// Shows "Signed in as ..." with Sign out, or a Sign in button for each configured provider.
async function setUpAccount() {
    const bar = $("account");
    let account;
    try {
        account = await (await fetch("/auth/me")).json();
    } catch {
        return null;
    }

    if (account.signedIn) {
        const text = document.createElement("span");
        text.textContent = `Signed in as ${account.name}`;
        const signOut = document.createElement("button");
        signOut.type = "button";
        signOut.className = "secondary small";
        signOut.textContent = "Sign out";
        signOut.addEventListener("click", async () => {
            await fetch("/auth/signout", { method: "POST" });
            location.reload();
        });
        bar.replaceChildren(text, signOut);
    } else {
        const returnUrl = encodeURIComponent(location.pathname + location.search);
        bar.replaceChildren(...account.providers.map((provider) => {
            const link = document.createElement("a");
            link.className = "button secondary small";
            link.href = `/auth/signin/${provider.toLowerCase()}?returnUrl=${returnUrl}`;
            link.textContent = `Sign in with ${provider}`;
            return link;
        }));
    }
    return account;
}

function setUpInviteButtons(joinUrl) {
    if (navigator.clipboard) {
        $("copy-link").hidden = false;
        $("copy-link").addEventListener("click", async () => {
            try {
                await navigator.clipboard.writeText(joinUrl);
                $("copy-link").textContent = "Copied!";
                setTimeout(() => { $("copy-link").textContent = "Copy link"; }, 2000);
            } catch {
                showError("Could not copy. Select the link and copy it manually.");
            }
        });
    }

    if (navigator.share) {
        $("share-link").hidden = false;
        $("share-link").addEventListener("click", () => {
            navigator.share({ title: "Join my GamesLane game", url: joinUrl }).catch(() => {});
        });
    }
}

function listItem(text) {
    const item = document.createElement("li");
    item.textContent = text;
    return item;
}

function playerLabel(player, seat, view) {
    return player.name + (view.isMe(seat) ? " (you)" : "") + (player.isConnected ? "" : " (disconnected)");
}

/**
 * Runs a game page. The page URL is <page>?game=CODE, plus &role=spectator for a shared screen
 * and &autojoin=1 to join as a player straight away using the saved name.
 * Calls render(state, view) on every update. view.player is { seat, ... } when this browser holds a seat.
 */
function startGamePage(gameType, render) {
    const gameId = (params.get("game") ?? "").toUpperCase();
    const sharedScreen = params.get("role") === "spectator";
    let autoJoin = params.get("autojoin") === "1";
    let lastState = null;

    const view = {
        gameId,
        player: null,
        get isPlayer() { return this.player !== null; },
        isMe(seat) { return this.player?.seat === seat; },
        refresh() { if (lastState) update(lastState); },
    };

    if (!gameId) {
        location.replace("/");
        return view;
    }

    if (autoJoin) {
        history.replaceState(null, "", `${location.pathname}?game=${gameId}`);
    }

    const joinUrl = `${location.origin}/join/${gameId}`;
    $("game-code").textContent = gameId;
    $("join-url").href = joinUrl;
    $("join-url").textContent = joinUrl;
    $("join-name").value = savedName();

    function update(state) {
        lastState = state;
        const waiting = state.status === "WaitingForPlayers";
        $("join-panel").hidden = view.isPlayer || sharedScreen || !waiting;
        $("invite").hidden = !(sharedScreen || (view.isPlayer && waiting));
        render(state, view);
    }

    async function join(name) {
        try {
            view.player = await call("JoinAsPlayer", gameId, name);
            view.refresh();
        } catch {
            // Error already shown.
        }
    }

    async function enter() {
        let entry;
        try {
            entry = await call("EnterGame", gameId);
        } catch {
            $("game").hidden = true;
            return;
        }

        if (entry.state.gameType !== gameType) {
            location.replace(`${GAME_PAGES[entry.state.gameType]}${location.search}`);
            return;
        }

        view.player = entry.player ?? null;
        update(entry.state);

        if (autoJoin && !view.isPlayer && !$("join-panel").hidden && savedName()) {
            autoJoin = false;
            await join(savedName());
        }
    }

    connection.on("StateUpdated", update);
    connection.onreconnected(() => {
        showError("");
        enter();
    });

    $("join-panel").addEventListener("submit", (event) => {
        event.preventDefault();
        const name = requireName($("join-name"));
        if (name) {
            join(name);
        }
    });

    setUpInviteButtons(joinUrl);
    setUpAccount().then((account) => {
        if (account?.signedIn && !$("join-name").value) {
            $("join-name").value = account.name ?? "";
        }
    });
    ready.then((ok) => ok && enter());
    return view;
}
