"use strict";

// Shared by all pages: login, the hub connection, error display, and the game page framework.

const $ = (id) => document.getElementById(id);
const params = new URLSearchParams(location.search);

const GAME_PAGES = { TicTacToe: "/tictactoe.html", RockPaperScissors: "/rock-paper-scissors.html", TheGang: "/the-gang.html" };

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

// The "please wait" overlay (see showBusy). Declared before accountReady, which may show it as soon as the page loads.
let busyElement = null;
let busyTimer = null;

// The current user, set once accountReady resolves:
// { signedIn, kind, name, icon, uploadedIcon, providers, icons }. kind is "Guest", "Registered" or a provider such as "Google".
let account = null;
let isNewExternalUser = false;

const accountReady = (async () => {
    await completeExternalSignIn();
    try {
        account = await (await authFetch("/auth/me")).json();
    } catch {
        account = { signedIn: false, providers: [], icons: [] };
    }
    // Every visit renews the login token, so it keeps working as long as the user comes back within 30 days.
    if (account.signedIn && account.accessToken) {
        saveToken(account.accessToken);
    } else if (token && !account.signedIn) {
        saveToken("");
    }
    renderAccount();
    if (isNewExternalUser && account.signedIn) {
        openProfileDialog();
    }
    return account;
})();

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/game", { accessTokenFactory: () => token })
    .withAutomaticReconnect()
    .build();

connection.onreconnecting(() => showError("Connection lost. Reconnecting…"));
connection.onclose(() => showError("Connection lost. Reload the page to reconnect."));

// Connects to the server. A failed attempt is retried a few times over about 10 seconds, since the server may be
// waking up or a new version going live; only then is the error shown.
async function connect() {
    for (const delay of [0, 1000, 2000, 3000, 4000]) {
        await new Promise((resolve) => setTimeout(resolve, delay));
        try {
            await connection.start();
            return true;
        } catch (err) {
            console.warn("Could not connect to the server; retrying.", err);
        }
    }
    showError("Could not connect to the server. Reload the page to try again.");
    return false;
}

const ready = accountReady.then(connect);

function showError(message) {
    $("error").textContent = message;
}

// A full-screen "please wait" overlay with a spinner, for waits on the server (which may need a few seconds to wake up)
// or on Google. It appears only if the wait lasts longer than `delay` milliseconds, so quick actions do not flash.
function showBusy(message, delay = 300) {
    clearTimeout(busyTimer);
    busyTimer = setTimeout(() => {
        if (!busyElement) {
            busyElement = document.createElement("div");
            busyElement.className = "busy";
            busyElement.setAttribute("role", "status");
            busyElement.setAttribute("aria-live", "polite");
            busyElement.innerHTML = `<div class="busy-box"><span class="spinner" aria-hidden="true"></span><p></p></div>`;
            document.body.appendChild(busyElement);
        }
        busyElement.querySelector("p").textContent = message;
        busyElement.hidden = false;
    }, delay);
}

function hideBusy() {
    clearTimeout(busyTimer);
    if (busyElement) {
        busyElement.hidden = true;
    }
}

// Coming back with the browser's Back button can restore a page that was still showing the overlay.
window.addEventListener("pageshow", (event) => {
    if (event.persisted) {
        hideBusy();
    }
});

// Shows a spinner and `text` on a button while `action` runs (for buttons inside dialogs, which sit above the overlay).
// Returns the action's result. The button is restored unless the action returns true (meaning the page is reloading).
async function withBusyButton(button, text, action) {
    const label = button.textContent;
    button.disabled = true;
    button.classList.add("is-busy");
    button.textContent = text;
    let reloading = false;
    try {
        reloading = await action();
        return reloading;
    } finally {
        if (!reloading) {
            button.disabled = false;
            button.classList.remove("is-busy");
            button.textContent = label;
        }
    }
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

    showBusy("Signing you in…", 0);
    const response = await fetch("/auth/external", { method: "POST" }).catch(() => null);
    hideBusy();
    if (response?.ok) {
        const result = await response.json();
        saveToken(result.accessToken);
        isNewExternalUser = result.isNew;
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

// An icon: a built-in emoji, or an uploaded picture (its address starts with "/").
function iconElement(icon, className = "icon") {
    if (icon?.startsWith("/")) {
        const img = document.createElement("img");
        img.className = className;
        img.src = icon;
        img.alt = "";
        return img;
    }
    const span = document.createElement("span");
    span.className = className;
    span.textContent = icon ?? "";
    return span;
}

// Shows the user's icon and name (which open their profile) and Log out, or a Log in button.
function renderAccount() {
    const bar = $("account");
    if (account.signedIn) {
        const name = document.createElement("span");
        name.textContent = account.name;
        const profile = smallButton("", openProfileDialog);
        profile.classList.add("account-name");
        profile.title = "Your profile";
        profile.replaceChildren(iconElement(account.icon), name);
        bar.replaceChildren(
            profile,
            smallButton("Log out", () => {
                saveToken("");
                location.reload();
            }));
    } else {
        const login = smallButton("Log in", openLoginDialog);
        login.classList.remove("secondary");
        bar.replaceChildren(login);
    }
}

document.querySelectorAll("[data-login]").forEach((button) => {
    button.addEventListener("click", openLoginDialog);
});

// A row of icon buttons to choose from. Returns { get value() } with the chosen icon.
function iconPicker(container, icons, selected) {
    let value = icons.includes(selected) ? selected : icons[0];
    const buttons = icons.map((icon) => {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "icon-choice";
        button.setAttribute("aria-pressed", String(icon === value));
        button.appendChild(iconElement(icon));
        button.addEventListener("click", () => {
            value = icon;
            buttons.forEach((b) => b.setAttribute("aria-pressed", String(b === button)));
        });
        return button;
    });
    container.replaceChildren(...buttons);
    return { get value() { return value; } };
}

// Sends an account request; on success saves the new login token and reloads, otherwise shows the error in the dialog.
// Returns true if the page is reloading.
async function submitAccount(url, body, errorElement) {
    errorElement.textContent = "";
    const response = await postJson(url, body).catch(() => null);
    if (response?.ok) {
        saveToken((await response.json()).accessToken);
        location.reload();
        return true;
    }
    if (response?.status === 401) {
        saveToken("");
        location.reload();
        return true;
    }
    const result = await response?.json().catch(() => null);
    errorElement.textContent = response?.status === 429
        ? "Too many attempts. Wait a minute and try again."
        : result?.error ?? (response ? "Something went wrong." : "Could not reach the server. Check your connection and try again.");
    return false;
}

// Submits a dialog form's account request, showing `busyText` on its submit button meanwhile.
function submitAccountForm(form, busyText, url, body) {
    const button = form.querySelector('[type="submit"]');
    return withBusyButton(button, busyText, () => submitAccount(url, body, form.querySelector(".error")));
}

function createDialog(html) {
    const dialog = document.createElement("dialog");
    dialog.className = "dialog";
    dialog.innerHTML = html;
    document.body.appendChild(dialog);
    dialog.querySelectorAll("[data-close]").forEach((b) => b.addEventListener("click", () => dialog.close()));
    return dialog;
}

let loginDialog = null;

// Log in as a guest (name only), as a registered user, by creating an account, or with an external provider.
function openLoginDialog() {
    loginDialog ??= createLoginDialog();
    loginDialog.querySelectorAll(".error").forEach((e) => { e.textContent = ""; });
    loginDialog.showModal();
}

function createLoginDialog() {
    const dialog = createDialog(`
        <h2>Log in</h2>
        <div class="tabs" role="tablist">
            <button type="button" role="tab" data-tab="guest">Guest</button>
            <button type="button" role="tab" data-tab="login">Log in</button>
            <button type="button" role="tab" data-tab="register">Create account</button>
        </div>
        <form data-panel="guest">
            <p class="hint">Play with just a name. Nothing is saved.</p>
            <label>Your name <input name="name" maxlength="30" autocomplete="nickname" required></label>
            <div class="icon-picker"></div>
            <p class="error" role="alert"></p>
            <div class="actions"><button type="submit">Play as guest</button><button type="button" class="secondary" data-close>Cancel</button></div>
        </form>
        <form data-panel="login">
            <label>Username <input name="username" maxlength="20" autocomplete="username" required></label>
            <label>Password <input name="password" type="password" maxlength="100" autocomplete="current-password" required></label>
            <p class="error" role="alert"></p>
            <div class="actions"><button type="submit">Log in</button><button type="button" class="secondary" data-close>Cancel</button></div>
        </form>
        <form data-panel="register">
            <label>Username <input name="username" maxlength="20" autocomplete="username" required pattern="[A-Za-z0-9_]{3,20}"></label>
            <p class="hint">3 to 20 letters, digits or underscores. You log in with it; others see your display name.</p>
            <label>Password <input name="password" type="password" minlength="8" maxlength="100" autocomplete="new-password" required></label>
            <p class="hint">At least 8 characters. It cannot be reset, so keep it safe.</p>
            <label>Display name <input name="name" maxlength="30" autocomplete="nickname" required></label>
            <div class="icon-picker"></div>
            <p class="error" role="alert"></p>
            <div class="actions"><button type="submit">Create account</button><button type="button" class="secondary" data-close>Cancel</button></div>
        </form>
        <div class="login-providers"><p class="hint">Or</p></div>`);

    const providers = dialog.querySelector(".login-providers");
    providers.hidden = account.providers.length === 0;
    for (const provider of account.providers) {
        const link = document.createElement("a");
        link.className = "button secondary";
        link.href = externalSignInUrl(provider);
        link.textContent = `Sign in with ${provider}`;
        // The dialog stays open while the browser goes to the provider; the link shows a spinner meanwhile.
        link.addEventListener("click", (event) => {
            if (link.classList.contains("is-busy")) {
                event.preventDefault();
                return;
            }
            link.classList.add("is-busy");
            link.setAttribute("aria-disabled", "true");
            link.textContent = `Connecting to ${provider}…`;
        });
        // Coming back with the browser's Back button can restore the page with the spinner still showing.
        window.addEventListener("pageshow", () => {
            link.classList.remove("is-busy");
            link.removeAttribute("aria-disabled");
            link.textContent = `Sign in with ${provider}`;
        });
        providers.appendChild(link);
    }

    const show = (tab) => {
        dialog.querySelectorAll("[data-tab]").forEach((b) => b.setAttribute("aria-selected", String(b.dataset.tab === tab)));
        dialog.querySelectorAll("[data-panel]").forEach((p) => { p.hidden = p.dataset.panel !== tab; });
    };
    dialog.querySelectorAll("[data-tab]").forEach((b) => b.addEventListener("click", () => show(b.dataset.tab)));
    show("guest");

    const panel = (name) => dialog.querySelector(`[data-panel="${name}"]`);
    const guestIcon = iconPicker(panel("guest").querySelector(".icon-picker"), account.icons, null);
    const registerIcon = iconPicker(panel("register").querySelector(".icon-picker"), account.icons, null);
    const onSubmit = (name, busyText, url, body) => panel(name).addEventListener("submit", (event) => {
        event.preventDefault();
        submitAccountForm(event.target, busyText, url, body(new FormData(event.target)));
    });
    onSubmit("guest", "Logging in…", "/auth/guest", (f) => ({ name: f.get("name"), icon: guestIcon.value }));
    onSubmit("login", "Logging in…", "/auth/login", (f) => ({ username: f.get("username"), password: f.get("password") }));
    onSubmit("register", "Creating account…", "/auth/register", (f) => ({
        username: f.get("username"), password: f.get("password"), name: f.get("name"), icon: registerIcon.value,
    }));
    return dialog;
}

// Change the display name and icon. Saved users can also upload a picture as their icon.
function openProfileDialog() {
    const canUpload = account.kind !== "Guest";
    const icons = account.uploadedIcon ? [account.uploadedIcon, ...account.icons] : account.icons;
    const dialog = createDialog(`
        <form>
            <h2>Your profile</h2>
            <p class="hint" data-welcome hidden>Welcome! Choose how other players see you.</p>
            <label>Display name <input name="name" maxlength="30" autocomplete="nickname" required></label>
            <div class="icon-picker"></div>
            <label class="upload" ${canUpload ? "" : "hidden"}>Upload a picture <input name="picture" type="file" accept="image/*"></label>
            <p class="error" role="alert"></p>
            <div class="actions"><button type="submit">Save</button><button type="button" class="secondary" data-close>Cancel</button></div>
        </form>`);
    const form = dialog.querySelector("form");
    const error = form.querySelector(".error");
    form.elements.name.value = account.name;
    form.querySelector("[data-welcome]").hidden = !isNewExternalUser;
    const picker = iconPicker(form.querySelector(".icon-picker"), icons, account.icon);

    form.elements.picture.addEventListener("change", async () => {
        const file = form.elements.picture.files[0];
        if (file) {
            await withBusyButton(form.querySelector('[type="submit"]'), "Uploading…", () => uploadIcon(file, error));
        }
    });
    form.addEventListener("submit", (event) => {
        event.preventDefault();
        submitAccountForm(form, "Saving…", "/auth/profile", { name: form.elements.name.value, icon: picker.value });
    });
    dialog.addEventListener("close", () => dialog.remove());
    dialog.showModal();
}

// Shrinks a picture to a 128×128 square in the browser and uploads it as the user's icon.
async function uploadIcon(file, errorElement) {
    let blob;
    try {
        const image = await createImageBitmap(file);
        const size = Math.min(image.width, image.height);
        const canvas = document.createElement("canvas");
        canvas.width = canvas.height = 128;
        canvas.getContext("2d").drawImage(image, (image.width - size) / 2, (image.height - size) / 2, size, size, 0, 0, 128, 128);
        blob = await new Promise((resolve) => canvas.toBlob(resolve, "image/webp", 0.85));
    } catch {
        errorElement.textContent = "That file could not be read as a picture.";
        return false;
    }

    const response = await authFetch("/auth/icon", { method: "POST", headers: { "Content-Type": blob.type }, body: blob }).catch(() => null);
    if (response?.ok) {
        saveToken((await response.json()).accessToken);
        location.reload();
        return true;
    }
    const result = await response?.json().catch(() => null);
    errorElement.textContent = result?.error ?? "The picture could not be uploaded.";
    return false;
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

// A list item showing text, or other elements (e.g. from seatLabel).
function listItem(...content) {
    const item = document.createElement("li");
    item.append(...content);
    return item;
}

// A seat's name as shown on the game screen.
function seatName(state, seat, view) {
    const info = state.seats[seat];
    if (!info) {
        return "Empty seat";
    }
    return info.name + (view.isMe(seat) ? " (you)" : "") + (info.isConnected ? "" : " (disconnected)");
}

// A seat's icon and name, optionally after a prefix such as "X: ".
function seatLabel(state, seat, view, prefix = "") {
    const label = document.createElement("span");
    label.className = "seat-label";
    const info = state.seats[seat];
    label.append(prefix);
    if (info) {
        label.append(iconElement(info.icon));
    }
    label.append(seatName(state, seat, view));
    return label;
}

/**
 * Runs a session page. The page URL is <page>?session=CODE. Anyone can watch; logged-in users can take seats.
 * While the session is in its lobby, the shared session lobby (#session-lobby) is shown; while a game is being
 * played, the game screen (#game-screen) is shown and render(game, state, view) is called on every update.
 * view.mySeats lists the seats this tab's user holds; view.isMe(seat) tells whether they hold a seat.
 * view.private is this user's private game information (e.g. their cards), for games that have it, or null.
 */
function startSessionPage(gameType, render) {
    const sessionId = (params.get("session") ?? "").toUpperCase();
    let lastState = null;

    const view = {
        sessionId,
        playerId: null,
        private: null,
        mySeats: [],
        get isPlayer() { return this.mySeats.length > 0; },
        isMe(seat) { return this.mySeats.includes(seat); },
        refresh() { if (lastState) update(lastState); },
    };

    if (!sessionId) {
        location.replace("/");
        return view;
    }

    const joinUrl = `${location.origin}/join/${sessionId}`;
    $("session-code").textContent = sessionId;
    $("join-url").href = joinUrl;
    $("join-url").textContent = joinUrl;

    const act = (method, ...args) => call(method, sessionId, ...args).catch(() => {});

    // A seat tile: a big icon, the name, and a note such as "Seat 2 · You". If the user can sit down or leave,
    // the whole tile is a button, labelled with what tapping it does.
    function seatItem(state, seat) {
        const info = state.seats[seat];
        let action = null;
        if (view.isMe(seat)) {
            action = { text: "Leave seat", run: () => act("LeaveSeat", seat) };
        } else if (account?.signedIn && (!info || !info.isConnected)) {
            action = { text: info ? "Take this seat" : "Sit here", run: () => act("TakeSeat", seat) };
        }

        const tile = document.createElement(action ? "button" : "div");
        tile.className = "seat";
        tile.classList.toggle("empty", !info);
        tile.classList.toggle("mine", view.isMe(seat));
        tile.classList.toggle("away", Boolean(info && !info.isConnected));

        const avatar = iconElement(info ? info.icon : "+", "seat-avatar");
        const name = document.createElement("span");
        name.className = "seat-name";
        name.textContent = info ? info.name : "Empty seat";
        const note = document.createElement("span");
        note.className = "seat-note";
        note.textContent = [`Seat ${seat + 1}`, view.isMe(seat) && "You", info && !info.isConnected && "Disconnected"]
            .filter(Boolean)
            .join(" · ");
        tile.append(avatar, name, note);

        if (action) {
            tile.type = "button";
            const label = document.createElement("span");
            label.className = "seat-action";
            label.textContent = action.text;
            tile.append(label);
            tile.addEventListener("click", action.run);
        }

        const item = document.createElement("li");
        item.append(tile);
        return item;
    }

    function lobbyStatus(state, taken) {
        if (state.canResume) {
            return "The game is paused. Resume it, or start a new game.";
        }
        if (taken < state.minSeats) {
            return `Waiting for players (${taken}/${state.minSeats} needed)…`;
        }
        return "Ready to start.";
    }

    function update(state) {
        lastState = state;
        view.mySeats = state.seats.flatMap((s, i) => (s && s.playerId === view.playerId ? [i] : []));
        const inLobby = state.phase === "Lobby";
        const taken = state.seats.filter(Boolean).length;

        $("session-lobby").hidden = !inLobby;
        $("game-screen").hidden = inLobby;
        $("invite").hidden = !inLobby;

        if (inLobby) {
            $("lobby-status").textContent = lobbyStatus(state, taken);
            $("seat-count").textContent = `${taken} / ${state.seats.length}`;
            $("seats").replaceChildren(...state.seats.map((_, seat) => seatItem(state, seat)));
            $("login-to-play").hidden = Boolean(account?.signedIn);
            $("resume-game").hidden = !(view.isPlayer && state.canResume);
            $("start-game").hidden = !(view.isPlayer && taken >= state.minSeats);
            $("start-game").textContent = state.gameNumber > 0 ? "New game" : "Start";
            return;
        }

        $("game-over-actions").hidden = !(view.isPlayer && state.isGameOver);
        $("stand-up").hidden = !view.isPlayer;
        render(state.game, state, view);
    }

    async function enter() {
        let entry;
        showBusy("Opening the game…");
        try {
            entry = await call("EnterSession", sessionId);
        } catch {
            $("session").hidden = true;
            return;
        } finally {
            hideBusy();
        }

        if (entry.state.gameType !== gameType) {
            location.replace(`${GAME_PAGES[entry.state.gameType]}${location.search}`);
            return;
        }

        view.playerId = entry.playerId ?? null;
        view.private = entry.private ?? null;
        update(entry.state);
    }

    // The server sends the public state to everyone, then each player's private state to them only.
    // Private state that no longer matches the user's seats is dropped.
    connection.on("StateUpdated", (state) => {
        const seats = state.seats.flatMap((s, i) => (s && s.playerId === view.playerId ? [i] : []));
        if (seats.length === 0) {
            view.private = null;
        }
        update(state);
    });
    connection.on("PrivateStateUpdated", (privateState) => {
        view.private = privateState;
        view.refresh();
    });
    connection.onreconnected(() => {
        showError("");
        enter();
    });

    $("start-game").addEventListener("click", () => act("NewGame", lastState?.gameNumber ?? 0));
    $("resume-game").addEventListener("click", () => act("ResumeGame"));
    $("play-again").addEventListener("click", () => act("NewGame", lastState?.gameNumber ?? 0));
    $("to-lobby").addEventListener("click", () => act("ReturnToLobby"));
    $("stand-up").addEventListener("click", () => {
        view.mySeats.forEach((seat) => act("LeaveSeat", seat));
    });

    // Going back to the main screen gives up this user's seats.
    document.querySelectorAll("[data-leave]").forEach((link) => {
        link.addEventListener("click", async (event) => {
            event.preventDefault();
            await call("LeaveSession", sessionId).catch(() => {});
            location.href = link.href;
        });
    });

    setUpInviteButtons(joinUrl);
    showBusy("Opening the game…");
    ready.then((ok) => (ok ? enter() : hideBusy()));
    return view;
}
