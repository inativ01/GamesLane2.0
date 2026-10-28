"use strict";

// Shared by all pages: login, the hub connection, error display, and the game page framework.

const $ = (id) => document.getElementById(id);
const params = new URLSearchParams(location.search);

const GAME_PAGES = { TicTacToe: "/tictactoe.html", RockPaperScissors: "/rock-paper-scissors.html" };

// The login token is kept per browser tab, so each tab can have its own user.
// A new tab starts with the most recently logged-in user.
const TOKEN_KEY = "loginToken";

function loadToken() {
    let value = sessionStorage.getItem(TOKEN_KEY);
    if (value === null) {
        value = localStorage.getItem(TOKEN_KEY) ?? "";
        sessionStorage.setItem(TOKEN_KEY, value);
    }
    return value;
}

let token = loadToken();

function saveToken(value) {
    if (!value && localStorage.getItem(TOKEN_KEY) === token) {
        localStorage.removeItem(TOKEN_KEY);
    } else if (value) {
        localStorage.setItem(TOKEN_KEY, value);
    }
    token = value;
    sessionStorage.setItem(TOKEN_KEY, value);
}

function authFetch(url, options = {}) {
    const headers = { ...options.headers };
    if (token) {
        headers.Authorization = `Bearer ${token}`;
    }
    return fetch(url, { ...options, headers });
}

function postJson(url, body) {
    return authFetch(url, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
}

// The current user: { signedIn, userId, name, provider, providers }, set once accountReady resolves.
let account = null;
let justSignedInExternally = false;

const accountReady = (async () => {
    await completeExternalSignIn();
    try {
        account = await (await authFetch("/auth/me")).json();
    } catch {
        account = { signedIn: false, providers: [] };
    }
    if (token && !account.signedIn) {
        saveToken("");
    }
    renderAccount();
    if (justSignedInExternally && account.signedIn) {
        openLoginDialog(true);
    }
    return account;
})();

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/game", { accessTokenFactory: () => token })
    .withAutomaticReconnect()
    .build();

connection.onreconnecting(() => showError("Connection lost. Reconnecting…"));
connection.onclose(() => showError("Connection lost. Reload the page to reconnect."));

const ready = accountReady.then(() => connection.start()).then(
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

// After signing in with an external provider (e.g. Google), the page returns with ?signin=external
// and exchanges the short-lived sign-in cookie for this tab's login token.
async function completeExternalSignIn() {
    if (params.get("signin") !== "external") {
        return;
    }
    const url = new URL(location.href);
    url.searchParams.delete("signin");
    history.replaceState(null, "", url.pathname + url.search);

    const response = await fetch("/auth/external", { method: "POST" }).catch(() => null);
    if (response?.ok) {
        saveToken((await response.json()).accessToken);
        justSignedInExternally = true;
    } else {
        showError("Sign-in did not complete. Please try again.");
    }
}

function externalSignInUrl(provider) {
    const url = new URL(location.href);
    url.searchParams.set("signin", "external");
    return `/auth/signin/${provider.toLowerCase()}?returnUrl=${encodeURIComponent(url.pathname + url.search)}`;
}

function smallButton(text, onClick) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "secondary small";
    button.textContent = text;
    button.addEventListener("click", onClick);
    return button;
}

// Shows the user's name with Change name and Log out, or a Log in button.
function renderAccount() {
    const bar = $("account");
    if (account.signedIn) {
        const name = document.createElement("span");
        name.textContent = account.name;
        bar.replaceChildren(
            name,
            smallButton("Change name", () => openLoginDialog(true)),
            smallButton("Log out", () => {
                saveToken("");
                location.reload();
            }));
    } else {
        bar.replaceChildren(smallButton("Log in", () => openLoginDialog(false)));
    }
}

document.querySelectorAll("[data-login]").forEach((button) => {
    button.addEventListener("click", () => openLoginDialog(false));
});

let loginDialog = null;

// Log in by name or with an external provider, or (rename = true) change the logged-in user's display name.
function openLoginDialog(rename) {
    if (!loginDialog) {
        loginDialog = createLoginDialog();
    }
    loginDialog.rename = rename;
    $("login-title").textContent = rename ? "Your display name" : "Log in";
    $("login-submit").textContent = rename ? "Save" : "Log in";
    $("login-name").value = rename ? account.name : "";
    $("login-error").textContent = "";
    $("login-providers").hidden = rename || account.providers.length === 0;
    loginDialog.showModal();
    $("login-name").focus();
}

function createLoginDialog() {
    const dialog = document.createElement("dialog");
    dialog.className = "dialog";
    dialog.innerHTML = `
        <form id="login-form">
            <h2 id="login-title"></h2>
            <label>Your name <input id="login-name" maxlength="30" autocomplete="nickname"></label>
            <p id="login-error" class="error" role="alert"></p>
            <div class="actions">
                <button id="login-submit" type="submit"></button>
                <button id="login-cancel" type="button" class="secondary">Cancel</button>
            </div>
            <div id="login-providers" class="login-providers">
                <p class="hint">Or</p>
            </div>
        </form>`;
    document.body.appendChild(dialog);

    for (const provider of account.providers) {
        const link = document.createElement("a");
        link.className = "button secondary";
        link.href = externalSignInUrl(provider);
        link.textContent = `Sign in with ${provider}`;
        $("login-providers").appendChild(link);
    }

    $("login-cancel").addEventListener("click", () => dialog.close());
    $("login-form").addEventListener("submit", async (event) => {
        event.preventDefault();
        const name = $("login-name").value.trim();
        if (!name) {
            $("login-error").textContent = "Enter your name.";
            return;
        }

        const response = await postJson(dialog.rename ? "/auth/name" : "/auth/login", { name }).catch(() => null);
        if (response?.ok) {
            saveToken((await response.json()).accessToken);
            location.reload();
        } else if (response?.status === 401) {
            saveToken("");
            location.reload();
        } else {
            const body = await response?.json().catch(() => null);
            $("login-error").textContent = body?.error ?? "Something went wrong.";
        }
    });
    return dialog;
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
 * Runs a game page. The page URL is <page>?game=CODE. Anyone can watch; logged-in users can take a seat.
 * Calls render(state, view) on every update. view.player is { seat, ... } when this tab's user holds a seat.
 */
function startGamePage(gameType, render) {
    const gameId = (params.get("game") ?? "").toUpperCase();
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

    const joinUrl = `${location.origin}/join/${gameId}`;
    $("game-code").textContent = gameId;
    $("join-url").href = joinUrl;
    $("join-url").textContent = joinUrl;

    function update(state) {
        lastState = state;
        const waiting = state.status === "WaitingForPlayers";
        $("join-panel").hidden = view.isPlayer || !waiting;
        $("take-seat").hidden = !account?.signedIn;
        $("login-to-play").hidden = Boolean(account?.signedIn);
        $("invite").hidden = !waiting;
        render(state, view);
    }

    async function join() {
        try {
            view.player = await call("JoinAsPlayer", gameId);
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
    }

    connection.on("StateUpdated", update);
    connection.onreconnected(() => {
        showError("");
        enter();
    });

    $("take-seat").addEventListener("click", join);

    setUpInviteButtons(joinUrl);
    ready.then((ok) => ok && enter());
    return view;
}
