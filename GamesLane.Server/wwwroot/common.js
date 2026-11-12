"use strict";

// Shared by all pages: login, the hub connection, error display, and the game page framework. Texts go through t()
// (see i18n.js), so pages with translations show them in the chosen language.

const $ = (id) => document.getElementById(id);
const params = new URLSearchParams(location.search);

translatePage();
$("account").before(languagePicker() ?? "");

const GAME_PAGES = {
    TicTacToe: "/tictactoe.html", TheGang: "/the-gang.html", Backgammon: "/backgammon.html",
    RichManPoorMan: "/rich-man-poor-man.html", Wazoo: "/wazoo.html", FinalCard: "/final-card.html", Yaniv: "/yaniv.html",
};

const help = startHelp();

// The login token is kept per browser tab, so each tab can have its own user.
// A new tab starts with the most recently logged-in user. A guest exists only in their token, which does not expire.
const TOKEN_KEY = "loginToken";
// The login token this tab had before logging in or out. The next page load gives that user's seats to the new user.
const PREVIOUS_TOKEN_KEY = "previousToken";

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
// { signedIn, kind, name, icon, uploadedIcon, gameOrder, settings, providers, icons, feedback, isAdmin }. kind is "Guest", "Registered" or a
// provider such as "Google"; gameOrder is a saved user's order of the games on the main screen, or null; settings is a
// saved user's { chat, sound, cards } (see userSettings), or null. Everyone is logged in, as a guest at least; signedIn is
// false only if the server could not be reached.
let account = null;
let isNewExternalUser = false;

// The user's settings from their profile: chat (the chat in game sessions), sound (every sound on the site: the
// chat's chime, game sounds and reading aloud) and cards (how playing cards are drawn: "New" or "Classic", see
// cardElement). A saved user's are saved with their account; a guest's in this browser. Chat and sound are on, and the
// cards new, until the user changes them. Pages listen for the SETTINGS_EVENT event, sent when they are known (once
// the user is) and whenever they change.
const SETTINGS_KEY = "settings";
const SETTINGS_EVENT = "settingschange";

function userSettings() {
    if (account?.settings) {
        return account.settings;
    }
    let saved = null;
    try {
        saved = JSON.parse(localStorage.getItem(SETTINGS_KEY) ?? "null");
    } catch {
        // Unreadable settings count as the defaults.
    }
    return { chat: saved?.chat !== false, sound: saved?.sound !== false, cards: saved?.cards === "Classic" ? "Classic" : "New" };
}

// Saves the settings (for a saved user, with their account). Returns false if they could not be saved.
async function saveSettings(settings) {
    if (account?.settings) {
        const response = await authFetch("/api/settings", {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(settings),
        }).catch(() => null);
        if (!response?.ok) {
            return false;
        }
        account.settings = settings;
    } else {
        localStorage.setItem(SETTINGS_KEY, JSON.stringify(settings));
    }
    dispatchEvent(new Event(SETTINGS_EVENT));
    return true;
}

// The current user from the server, or null if it could not be reached.
async function fetchAccount() {
    try {
        return await (await authFetch("/auth/me")).json();
    } catch {
        return null;
    }
}

// Someone without a valid login token becomes a new guest, with a random name and icon. Returns false if the server
// could not be reached.
async function becomeGuest() {
    const response = await fetch("/auth/guest", { method: "POST" }).catch(() => null);
    if (!response?.ok) {
        return false;
    }
    saveToken((await response.json()).accessToken);
    return true;
}

// After logging in or out, gives the previous user's seats to the new user.
async function transferSeats() {
    const previous = sessionStorage.getItem(PREVIOUS_TOKEN_KEY);
    sessionStorage.removeItem(PREVIOUS_TOKEN_KEY);
    if (previous && previous !== token) {
        await postJson("/auth/transfer", { previousToken: previous }).catch(() => null);
    }
}

const accountReady = (async () => {
    await completeExternalSignIn();
    account = await fetchAccount();
    if (account && !account.signedIn) {
        saveToken("");
        if (await becomeGuest()) {
            account = await fetchAccount();
        }
    }
    account ??= { signedIn: false, providers: [], icons: [] };
    // Every visit renews the login token, so a saved user's keeps working as long as they come back within 30 days.
    if (account.signedIn && account.accessToken) {
        saveToken(account.accessToken);
        await transferSeats();
    }
    renderAccount();
    dispatchEvent(new Event(SETTINGS_EVENT));
    setUpFeedbackLink();
    if (isNewExternalUser && account.signedIn) {
        openProfileDialog();
    }
    return account;
})();

// A keep-alive message every 5 seconds, and 10 seconds of silence from the server count as a lost connection (matching
// the server's settings in Program.cs).
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/game", { accessTokenFactory: () => token })
    .withAutomaticReconnect()
    .withKeepAliveInterval(5000)
    .withServerTimeout(10000)
    .build();

let pageClosing = false;
connection.onreconnecting(() => showError(t("Connection lost. Reconnecting…")));
connection.onclose(() => {
    if (!pageClosing) showError(t("Connection lost. Reload the page to reconnect."));
});

// When the page closes (or the browser leaves it), the connection is closed on purpose, so the server knows at once
// instead of waiting for the keep-alive messages to stop. A page the browser brings back from its cache reloads, to
// connect again.
window.addEventListener("pagehide", () => {
    pageClosing = true;
    connection.stop();
});
window.addEventListener("pageshow", (event) => {
    if (event.persisted) location.reload();
});

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
    showError(t("Could not connect to the server. Reload the page to try again."));
    return false;
}

const ready = accountReady.then(connect);

// Anonymous visit counts (see the privacy policy, and VisitReport on the server), from the main screen and the games'
// pages. Once a day (UTC) per browser: the visit, where it came from (a campaign's utm_source, another site, "invite"
// for a game's page opened from outside the site, or "direct"), the browser's language and the days since its previous
// visit. Once a day per game: opening its page. The browser keeps only the day and the games reported that day.
const VISITS_KEY = "visits";

function visitSource(gameType) {
    const campaign = params.get("utm_source");
    if (campaign) return campaign;
    let host = "";
    try {
        host = new URL(document.referrer).hostname;
    } catch {
        // No referrer.
    }
    if (host && host !== location.hostname) return host;
    return gameType && !host ? "invite" : "direct";
}

async function reportVisit() {
    const gameType = Object.keys(GAME_PAGES).find((type) => GAME_PAGES[type] === location.pathname) ?? null;
    if (!gameType && location.pathname !== "/" && location.pathname !== "/index.html") return;
    const today = new Date().toISOString().slice(0, 10);
    let seen = null;
    try {
        seen = JSON.parse(localStorage.getItem(VISITS_KEY) ?? "null");
    } catch {
        // Unreadable, as if never visited.
    }
    const firstToday = seen?.day !== today;
    const games = !firstToday && Array.isArray(seen.games) ? seen.games : [];
    const openGame = gameType && !games.includes(gameType) ? gameType : null;
    if (!firstToday && !openGame) return;

    const report = {
        game: openGame,
        firstToday,
        source: firstToday ? visitSource(gameType) : null,
        language: firstToday ? navigator.language ?? null : null,
        daysSinceLastVisit: firstToday && typeof seen?.day === "string" ? Math.round((Date.parse(today) - Date.parse(seen.day)) / 86400000) : null,
    };
    await accountReady;
    const response = await postJson("/api/visits", report).catch(() => null);
    if (response?.ok) {
        localStorage.setItem(VISITS_KEY, JSON.stringify({ day: today, games: openGame ? [...games, openGame] : games }));
    }
}

reportVisit();

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
    return match ? translateServerText(match[1]) : t("Something went wrong.");
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

    showBusy(t("Signing you in…"), 0);
    const response = await fetch("/auth/external", { method: "POST" }).catch(() => null);
    hideBusy();
    if (response?.ok) {
        const result = await response.json();
        saveToken(result.accessToken);
        isNewExternalUser = result.isNew;
    } else {
        showError(t("Sign-in did not complete. Please try again."));
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

// Shows the user's icon and name, which open their profile. Nothing is shown if the server could not be reached.
function renderAccount() {
    const bar = $("account");
    if (!account.signedIn) {
        bar.replaceChildren();
        return;
    }
    const name = document.createElement("span");
    name.textContent = account.name;
    const profile = smallButton("", openProfileDialog);
    profile.classList.add("account-name");
    profile.title = t("Your profile");
    profile.replaceChildren(iconElement(account.icon), name);
    bar.replaceChildren(profile);
}

// A row of icon buttons to choose from. Returns { get value() } with the chosen icon; `onChange` is called with it
// whenever another one is chosen.
function iconPicker(container, icons, selected, onChange = () => {}) {
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
            onChange(icon);
        });
        return button;
    });
    container.replaceChildren(...buttons);
    return { get value() { return value; } };
}

// Sends an account request; on success saves the new login token and reloads, otherwise shows the error in the dialog.
// If the request logs in another user (`changesUser`), the reloaded page gives them the previous user's seats.
// Returns true if the page is reloading.
async function submitAccount(url, body, errorElement, changesUser = false) {
    errorElement.textContent = "";
    const response = await postJson(url, body).catch(() => null);
    if (response?.ok) {
        if (changesUser) {
            sessionStorage.setItem(PREVIOUS_TOKEN_KEY, token);
        }
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
        ? t("Too many attempts. Wait a minute and try again.")
        : translateServerText(result?.error) ?? t(response ? "Something went wrong." : "Could not reach the server. Check your connection and try again.");
    return false;
}

// Submits a dialog form's account request, showing `busyText` on its submit button meanwhile.
function submitAccountForm(form, busyText, url, body, changesUser = false) {
    const button = form.querySelector('[type="submit"]');
    return withBusyButton(button, busyText, () => submitAccount(url, body, form.querySelector(".error"), changesUser));
}

function createDialog(html) {
    const dialog = document.createElement("dialog");
    dialog.className = "dialog";
    dialog.innerHTML = html;
    document.body.appendChild(dialog);
    dialog.querySelectorAll("[data-close]").forEach((b) => b.addEventListener("click", () => dialog.close()));
    return dialog;
}

// How the user logged in, shown under their name in the profile.
function accountKindText(kind) {
    if (kind === "Guest") return t("Guest");
    if (kind === "Registered") return t("GamesLane account");
    return t("{provider} account", { provider: kind });
}

// The profile: the user's icon and display name (each changed with its own button), the chat and sound switches, and
// the card style (see userSettings). Changes are kept only when saved. Saved users can also upload a picture as their
// icon, and log out (to become a new guest, with a random name and icon); admins also get links to the admin and cards
// pages. Guests see a button on top that opens logging in, creating an account (with the name and icon chosen below),
// and signing in with an external provider. The new user keeps the seats the previous one held.
function openProfileDialog() {
    const isGuest = account.kind === "Guest";
    const icons = account.uploadedIcon ? [account.uploadedIcon, ...account.icons] : account.icons;
    const dialog = createDialog(`
        <h2 class="profile-title">${t("Your profile")}</h2>
        ${isGuest ? `
        <section class="profile-guest">
            <p class="hint">${t("Playing as a guest. Log in or create an account to keep your profile, game order and game options on any device.")}</p>
            <button type="button" class="secondary" data-show-login aria-expanded="false">${t("Log in or create account")}</button>
            <div class="profile-account" hidden>
                <div class="tabs" role="tablist">
                    <button type="button" role="tab" data-tab="login">${t("Log in")}</button>
                    <button type="button" role="tab" data-tab="register">${t("Create account")}</button>
                </div>
                <form data-panel="login">
                    <label>${t("Username")} <input name="username" maxlength="20" autocomplete="username" required></label>
                    <label>${t("Password")} <input name="password" type="password" maxlength="100" autocomplete="current-password" required></label>
                    <p class="error" role="alert"></p>
                    <div class="actions"><button type="submit">${t("Log in")}</button></div>
                </form>
                <form data-panel="register">
                    <label>${t("Username")} <input name="username" maxlength="20" autocomplete="username" required pattern="[A-Za-z0-9_]{3,20}"></label>
                    <p class="hint">${t("3 to 20 letters, digits or underscores. You log in with it; others see your display name.")}</p>
                    <label>${t("Password")} <input name="password" type="password" minlength="8" maxlength="100" autocomplete="new-password" required></label>
                    <p class="hint">${t("At least 8 characters. It cannot be reset, so keep it safe.")}</p>
                    <p class="error" role="alert"></p>
                    <div class="actions"><button type="submit">${t("Create account")}</button></div>
                </form>
                <div class="login-providers"><p class="hint">${t("Or")}</p></div>
            </div>
        </section>` : ""}
        <form data-profile>
            <p class="hint" data-welcome hidden>${t("Welcome! Choose how other players see you.")}</p>
            <div class="profile-card">
                <button type="button" class="profile-avatar" data-edit-icon aria-expanded="false" title="${t("Change icon")}" aria-label="${t("Change icon")}">
                    <span class="profile-avatar-icon"></span><span class="profile-edit-badge" aria-hidden="true">✎</span>
                </button>
                <div class="profile-identity">
                    <div class="profile-name-row" data-name-view>
                        <strong class="profile-name" data-name-text></strong>
                        <button type="button" class="profile-edit" data-edit-name title="${t("Change name")}" aria-label="${t("Change name")}">✎</button>
                    </div>
                    <label class="profile-name-edit" data-name-edit hidden>${t("Display name")} <input name="name" maxlength="30" autocomplete="nickname" required></label>
                    <span class="profile-badge">${accountKindText(account.kind)}</span>
                </div>
            </div>
            <div class="profile-icons" data-icons hidden>
                <div class="icon-picker"></div>
                <label class="upload" ${isGuest ? "hidden" : ""}>${t("Upload a picture")} <input name="picture" type="file" accept="image/*"></label>
            </div>
            <section class="profile-section">
                <h3>${t("Preferences")}</h3>
                <label class="switch"><span>${t("Chat in games")}</span><input type="checkbox" role="switch" name="chat"></label>
                <label class="switch"><span>${t("Sounds (chat, games and reading aloud)")}</span><input type="checkbox" role="switch" name="sound"></label>
                <fieldset class="card-style">
                    <legend>${t("Card style")}</legend>
                    <label class="card-style-choice" data-style="New"><input type="radio" name="cards" value="New"><span class="card-style-preview"></span><span>GamesLane</span></label>
                    <label class="card-style-choice" data-style="Classic"><input type="radio" name="cards" value="Classic"><span class="card-style-preview"></span><span>${t("Classic cards")}</span></label>
                </fieldset>
            </section>
            <p class="error" role="alert"></p>
            <div class="actions profile-actions"><button type="submit" disabled>${t("Save")}</button><button type="button" class="secondary" data-close>${t("Cancel")}</button></div>
        </form>
        ${isGuest ? "" : `
        <div class="profile-footer">
            ${account.isAdmin ? '<span class="profile-admin"><a class="button secondary small" href="/admin.html">Admin page</a><a class="button secondary small" href="/cards.html">All cards</a></span>' : ""}
            <button type="button" class="secondary small" data-logout>${t("Log out")}</button>
        </div>`}`);
    dialog.classList.add("profile-dialog");
    const form = dialog.querySelector("[data-profile]");
    const error = form.querySelector(".error");
    const save = form.querySelector('[type="submit"]');
    const initial = { name: account.name, ...userSettings() };
    form.elements.name.value = account.name;
    form.elements.chat.checked = initial.chat;
    form.elements.sound.checked = initial.sound;
    form.elements.cards.value = initial.cards;
    form.querySelector("[data-welcome]").hidden = !isNewExternalUser;
    form.querySelector("[data-name-text]").textContent = account.name;
    form.querySelectorAll(".card-style-choice").forEach((choice) =>
        choice.querySelector(".card-style-preview").append(cardElement("K♥", choice.dataset.style), cardElement("A♠", choice.dataset.style)));

    const avatar = form.querySelector(".profile-avatar-icon");
    const showIcon = (icon) => avatar.replaceChildren(iconElement(icon));
    // Save is enabled once something differs from what was saved.
    const updateSave = () => {
        save.disabled = form.elements.name.value.trim() === initial.name && picker.value === account.icon
            && form.elements.chat.checked === initial.chat && form.elements.sound.checked === initial.sound
            && form.elements.cards.value === initial.cards;
    };
    const picker = iconPicker(form.querySelector(".icon-picker"), icons, account.icon, (icon) => {
        showIcon(icon);
        updateSave();
    });
    showIcon(picker.value);
    updateSave();
    form.addEventListener("input", updateSave);
    form.addEventListener("change", updateSave);

    const iconPanel = form.querySelector("[data-icons]");
    const editIcon = form.querySelector("[data-edit-icon]");
    editIcon.addEventListener("click", () => {
        iconPanel.hidden = !iconPanel.hidden;
        editIcon.setAttribute("aria-expanded", String(!iconPanel.hidden));
    });
    form.querySelector("[data-edit-name]").addEventListener("click", () => {
        form.querySelector("[data-name-view]").hidden = true;
        form.querySelector("[data-name-edit]").hidden = false;
        form.elements.name.focus();
        form.elements.name.select();
    });

    form.elements.picture.addEventListener("change", async () => {
        const file = form.elements.picture.files[0];
        if (file) {
            await withBusyButton(save, t("Uploading…"), () => uploadIcon(file, error));
            updateSave();
        }
    });
    form.addEventListener("submit", (event) => {
        event.preventDefault();
        const settings = { chat: form.elements.chat.checked, sound: form.elements.sound.checked, cards: form.elements.cards.value };
        withBusyButton(save, t("Saving…"), async () => {
            if (!(await saveSettings(settings))) {
                error.textContent = t("Something went wrong.");
                return false;
            }
            return submitAccount("/auth/profile", { name: form.elements.name.value, icon: picker.value }, error);
        });
    });
    if (isGuest) {
        const showLogin = dialog.querySelector("[data-show-login]");
        showLogin.addEventListener("click", () => {
            const panel = dialog.querySelector(".profile-account");
            panel.hidden = !panel.hidden;
            showLogin.setAttribute("aria-expanded", String(!panel.hidden));
        });
        setUpLogin(dialog, () => ({ name: form.elements.name.value, icon: picker.value }));
    } else {
        dialog.querySelector("[data-logout]").addEventListener("click", () => {
            sessionStorage.setItem(PREVIOUS_TOKEN_KEY, token);
            saveToken("");
            location.reload();
        });
    }
    dialog.addEventListener("close", () => dialog.remove());
    dialog.showModal();
}

// The feedback form: a problem, an idea or another comment, posted by the server as a public GitHub issue, with the
// page and language it was sent from. The sender's display name is included only if they tick the box. Once sent, it
// thanks them and links to the issue.
const FEEDBACK_MAX_LENGTH = 2000;

function openFeedbackDialog() {
    const types = ["Problem", "Idea", "Other"];
    const dialog = createDialog(`
        <form data-feedback-form>
            <h2>${t("Send feedback")}</h2>
            <p class="hint">${t("Tell us about a problem, or share an idea.")}</p>
            <fieldset class="option inline">
                <legend>${t("Kind of comment")}</legend>
                ${types.map((type, i) => `<label class="option-choice"><input type="radio" name="type" value="${type}"${i === 0 ? " checked" : ""}><span class="option-text"><strong>${t(type)}</strong></span></label>`).join("")}
            </fieldset>
            <label>${t("Your comment")} <textarea name="text" rows="5" maxlength="${FEEDBACK_MAX_LENGTH}" required></textarea></label>
            <label class="check"><input type="checkbox" name="includeName"> <span data-name></span></label>
            <p class="hint">${t("Comments are posted publicly on GitHub, so do not include personal details such as your email or phone number.")}</p>
            <p class="error" role="alert"></p>
            <div class="actions"><button type="submit">${t("Send comment")}</button><button type="button" class="secondary" data-close>${t("Cancel")}</button></div>
        </form>
        <div data-thanks hidden>
            <h2>${t("Thank you!")}</h2>
            <p>${t("Your comment was posted.")} <a data-issue target="_blank" rel="noopener">${t("See it on GitHub")}</a></p>
            <div class="actions"><button type="button" data-close>${t("Close")}</button></div>
        </div>`);
    const form = dialog.querySelector("[data-feedback-form]");
    const error = form.querySelector(".error");
    form.querySelector("[data-name]").textContent = t("Include my name ({name})", { name: account.name ?? "" });
    form.addEventListener("submit", (event) => {
        event.preventDefault();
        withBusyButton(form.querySelector('[type="submit"]'), t("Sending…"), async () => {
            error.textContent = "";
            const response = await postJson("/api/feedback", {
                type: form.elements.type.value,
                text: form.elements.text.value,
                includeName: form.elements.includeName.checked,
                page: location.pathname,
                language: language.code,
            }).catch(() => null);
            const result = await response?.json().catch(() => null);
            if (response?.ok && result?.url?.startsWith("https://github.com/")) {
                dialog.querySelector("[data-issue]").href = result.url;
                form.hidden = true;
                dialog.querySelector("[data-thanks]").hidden = false;
                return;
            }
            error.textContent = response?.status === 429
                ? t("You have sent several comments. Try again in an hour.")
                : translateServerText(result?.error) ?? t(response ? "Something went wrong." : "Could not reach the server. Check your connection and try again.");
        });
    });
    dialog.addEventListener("close", () => dialog.remove());
    dialog.showModal();
}

// The main screen's "Send feedback" link, shown when the server can post comments.
function setUpFeedbackLink() {
    const link = document.getElementById("feedback-link");
    if (!link) return;
    link.hidden = !account.feedback;
    link.addEventListener("click", (event) => {
        event.preventDefault();
        openFeedbackDialog();
    });
}

// Each external provider's logo, for its sign-in button.
const PROVIDER_LOGOS = {
    Google: `<svg viewBox="0 0 48 48" aria-hidden="true"><path fill="#EA4335" d="M24 9.5c3.54 0 6.71 1.22 9.21 3.6l6.85-6.85C35.9 2.38 30.47 0 24 0 14.62 0 6.51 5.38 2.56 13.22l7.98 6.19C12.43 13.72 17.74 9.5 24 9.5z"/><path fill="#4285F4" d="M46.98 24.55c0-1.57-.15-3.09-.38-4.55H24v9.02h12.94c-.58 2.96-2.26 5.48-4.78 7.18l7.73 6c4.51-4.18 7.09-10.36 7.09-17.65z"/><path fill="#FBBC05" d="M10.53 28.59c-.48-1.45-.76-2.99-.76-4.59s.27-3.14.76-4.59l-7.98-6.19C.92 16.46 0 20.12 0 24c0 3.88.92 7.54 2.56 10.78l7.97-6.19z"/><path fill="#34A853" d="M24 48c6.48 0 11.93-2.13 15.89-5.81l-7.73-6c-2.15 1.45-4.92 2.3-8.16 2.3-6.26 0-11.57-4.22-13.47-9.91l-7.98 6.19C6.51 42.62 14.62 48 24 48z"/></svg>`,
    Microsoft: `<svg viewBox="0 0 21 21" aria-hidden="true"><rect x="1" y="1" width="9" height="9" fill="#f25022"/><rect x="11" y="1" width="9" height="9" fill="#7fba00"/><rect x="1" y="11" width="9" height="9" fill="#00a4ef"/><rect x="11" y="11" width="9" height="9" fill="#ffb900"/></svg>`,
    Discord: `<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M20.317 4.37a19.79 19.79 0 0 0-4.885-1.515.074.074 0 0 0-.079.037c-.21.375-.444.865-.608 1.25a18.27 18.27 0 0 0-5.487 0 12.64 12.64 0 0 0-.617-1.25.077.077 0 0 0-.079-.037A19.74 19.74 0 0 0 3.677 4.37a.07.07 0 0 0-.032.027C.533 9.046-.32 13.58.099 18.057a.082.082 0 0 0 .031.057 19.9 19.9 0 0 0 5.993 3.03.078.078 0 0 0 .084-.028c.462-.63.874-1.295 1.226-1.994a.076.076 0 0 0-.041-.106 13.1 13.1 0 0 1-1.872-.892.077.077 0 0 1-.008-.128c.126-.094.252-.192.372-.291a.074.074 0 0 1 .078-.01c3.928 1.793 8.18 1.793 12.062 0a.074.074 0 0 1 .078.009c.12.099.246.198.373.292a.077.077 0 0 1-.006.127 12.3 12.3 0 0 1-1.873.892.077.077 0 0 0-.041.107c.36.698.772 1.362 1.225 1.993a.076.076 0 0 0 .084.028 19.84 19.84 0 0 0 6.002-3.03.077.077 0 0 0 .032-.054c.5-5.177-.838-9.674-3.549-13.66a.061.061 0 0 0-.031-.03zM8.02 15.33c-1.183 0-2.157-1.085-2.157-2.419 0-1.333.956-2.419 2.157-2.419 1.21 0 2.176 1.095 2.157 2.42 0 1.333-.956 2.418-2.157 2.418zm7.975 0c-1.183 0-2.157-1.085-2.157-2.419 0-1.333.955-2.419 2.157-2.419 1.21 0 2.176 1.095 2.157 2.42 0 1.333-.946 2.418-2.157 2.418z"/></svg>`,
};

// The profile dialog's login part, for guests: Log in and Create account tabs, and a button per external provider.
// `profile` gives the display name and icon for a new account.
function setUpLogin(dialog, profile) {
    const providers = dialog.querySelector(".login-providers");
    providers.hidden = account.providers.length === 0;
    for (const provider of account.providers) {
        const link = document.createElement("a");
        link.className = `button provider-button provider-${provider.toLowerCase()}`;
        link.href = externalSignInUrl(provider);
        const logo = el("span", "provider-logo");
        logo.innerHTML = PROVIDER_LOGOS[provider] ?? "";
        const label = el("span", "provider-label", t("Sign in with {provider}", { provider }));
        link.append(logo, label);
        // The dialog stays open while the browser goes to the provider; the link shows a spinner meanwhile.
        link.addEventListener("click", (event) => {
            if (link.classList.contains("is-busy")) {
                event.preventDefault();
                return;
            }
            sessionStorage.setItem(PREVIOUS_TOKEN_KEY, token);
            link.classList.add("is-busy");
            link.setAttribute("aria-disabled", "true");
            label.textContent = t("Connecting to {provider}…", { provider });
        });
        // Coming back with the browser's Back button can restore the page with the spinner still showing.
        window.addEventListener("pageshow", () => {
            link.classList.remove("is-busy");
            link.removeAttribute("aria-disabled");
            label.textContent = t("Sign in with {provider}", { provider });
        });
        providers.appendChild(link);
    }

    const show = (tab) => {
        dialog.querySelectorAll("[data-tab]").forEach((b) => b.setAttribute("aria-selected", String(b.dataset.tab === tab)));
        dialog.querySelectorAll("[data-panel]").forEach((p) => { p.hidden = p.dataset.panel !== tab; });
    };
    dialog.querySelectorAll("[data-tab]").forEach((b) => b.addEventListener("click", () => show(b.dataset.tab)));
    show("login");

    const panel = (name) => dialog.querySelector(`[data-panel="${name}"]`);
    const onSubmit = (name, busyText, url, body) => panel(name).addEventListener("submit", (event) => {
        event.preventDefault();
        submitAccountForm(event.target, busyText, url, body(new FormData(event.target)), true);
    });
    onSubmit("login", t("Logging in…"), "/auth/login", (f) => ({ username: f.get("username"), password: f.get("password") }));
    onSubmit("register", t("Creating account…"), "/auth/register", (f) => ({
        username: f.get("username"), password: f.get("password"), ...profile(),
    }));
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
        errorElement.textContent = t("That file could not be read as a picture.");
        return false;
    }

    const response = await authFetch("/auth/icon", { method: "POST", headers: { "Content-Type": blob.type }, body: blob }).catch(() => null);
    if (response?.ok) {
        saveToken((await response.json()).accessToken);
        location.reload();
        return true;
    }
    const result = await response?.json().catch(() => null);
    errorElement.textContent = translateServerText(result?.error) ?? t("The picture could not be uploaded.");
    return false;
}

// A button that copies `text`; it says "Copied!" for a moment, or shows `failure` if copying fails.
function copyButton(text, failure) {
    const button = el("button", "secondary small", t("Copy"));
    button.type = "button";
    button.hidden = !navigator.clipboard;
    let timer = 0;
    button.addEventListener("click", async () => {
        try {
            await navigator.clipboard.writeText(text);
            button.textContent = t("Copied!");
            clearTimeout(timer);
            timer = setTimeout(() => { button.textContent = t("Copy"); }, 2000);
        } catch {
            showError(failure);
        }
    });
    return button;
}

// Clicking the game code in the session bar copies it; its icon turns into a tick for a moment.
function setUpCodeCopy(sessionId) {
    const code = $("session-code").closest(".code");
    if (!navigator.clipboard || !code) {
        return;
    }
    const icon = code.querySelector("span");
    const iconText = icon.textContent;
    code.classList.add("copyable");
    code.setAttribute("role", "button");
    code.tabIndex = 0;
    code.title = t("Copy the game code");
    code.setAttribute("aria-label", `${t("Copy the game code")}: ${sessionId}`);
    let timer = 0;
    const copy = async () => {
        try {
            await navigator.clipboard.writeText(sessionId);
            icon.textContent = "✅";
            code.title = t("Copied!");
            clearTimeout(timer);
            timer = setTimeout(() => {
                icon.textContent = iconText;
                code.title = t("Copy the game code");
            }, 2000);
        } catch {
            showError(t("Could not copy the game code."));
        }
    };
    code.addEventListener("click", copy);
    code.addEventListener("keydown", (event) => {
        if (event.key === "Enter" || event.key === " ") {
            event.preventDefault();
            copy();
        }
    });
}

function setUpInviteButtons(joinUrl) {
    if (navigator.clipboard) {
        $("copy-link").hidden = false;
        $("copy-link").addEventListener("click", async () => {
            try {
                await navigator.clipboard.writeText(joinUrl);
                $("copy-link").textContent = t("Copied!");
                setTimeout(() => { $("copy-link").textContent = t("Copy link"); }, 2000);
            } catch {
                showError(t("Could not copy. Select the link and copy it manually."));
            }
        });
    }

    if (navigator.share) {
        $("share-link").hidden = false;
        $("share-link").addEventListener("click", () => {
            navigator.share({ title: t("Join my GamesLane game"), url: joinUrl }).catch(() => {});
        });
    }
}

function el(tag, className, ...content) {
    const element = document.createElement(tag);
    if (className) {
        element.className = className;
    }
    element.append(...content);
    return element;
}

// The help: ? in the top bar opens a side panel (the whole screen on phones) with the help in the chosen language,
// help.html in English and help.<code>.html in the others. Each section is a folded title with a short text and, folded
// inside it, More details. Opening the panel unfolds the sections about what the page shows: the main screen, or on a
// game page the game (and the session lobby while it is shown, see setLobby), and the other sections stay folded, so
// any game's rules can be read from anywhere. The first time a user opens the main screen or a game's page in this
// browser, a tip under ? points to it for 10 seconds (after the splash screen). Pages without help (e.g. the admin page)
// have no ?.
function startHelp() {
    const page = location.pathname === "/" ? "/index.html" : location.pathname;
    const section = page === "/index.html" ? "main-screen"
        : Object.values(GAME_PAGES).includes(page) ? page.slice(1, -".html".length) : null;
    if (!section) {
        return { setLobby() {} };
    }
    let topics = [section];

    const button = el("button", "help-button", "?");
    button.type = "button";
    button.title = t("Help");
    button.setAttribute("aria-label", t("Help"));
    button.setAttribute("aria-haspopup", "dialog");
    button.setAttribute("aria-expanded", "false");
    const tip = el("button", "help-tip", t(section === "main-screen" ? "New here? Tap ? for help." : "New to this game? Tap ? for how to play."));
    tip.type = "button";
    tip.hidden = true;
    (document.querySelector(".top .language") ?? $("account")).before(el("div", "help", button, tip));

    const close = el("button", "chat-close", "✕");
    close.type = "button";
    close.title = t("Close the help");
    close.setAttribute("aria-label", t("Close the help"));
    const body = el("div", "chat-body help-body", el("p", "hint", t("Loading…")));
    const panel = el("aside", "chat-panel help-panel", el("div", "chat-head", el("h2", null, t("Help")), close), body);
    panel.hidden = true;
    panel.setAttribute("role", "dialog");
    panel.setAttribute("aria-label", t("Help"));
    document.body.append(panel);

    const url = language === LANGUAGES[0] ? "/help.html" : `/help.${language.code}.html`;
    let loading = null;
    const load = () => loading ??= fetch(url)
        .then((response) => (response.ok ? response.text() : Promise.reject(new Error(response.statusText))))
        .then((html) => {
            const content = new DOMParser().parseFromString(html, "text/html").querySelector(".help-content");
            body.replaceChildren(...content.children);
            return true;
        })
        .catch(() => {
            loading = null;
            body.replaceChildren(el("p", "error", t("Could not load the help. Check your connection and try again.")));
            return false;
        });

    async function show(open) {
        panel.hidden = !open;
        button.setAttribute("aria-expanded", String(open));
        if (!open) return;
        tip.hidden = true;
        if (!(await load())) return;
        body.querySelectorAll(".help-section").forEach((s) => { s.open = topics.some((topic) => s.id === `help-${topic}`); });
        body.querySelectorAll(".help-more").forEach((more) => { more.open = false; });
        const first = body.querySelector(`#help-${topics[0]}`);
        body.scrollTop = first ? first.offsetTop - body.offsetTop : 0;
    }

    button.addEventListener("click", () => show(panel.hidden));
    close.addEventListener("click", () => show(false));
    tip.addEventListener("click", () => show(true));
    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape" && !panel.hidden) show(false);
    });

    const tipKey = `helpTipSeen:${section}`;
    if (!localStorage.getItem(tipKey)) {
        const splashing = document.documentElement.classList.contains("splashing");
        setTimeout(() => {
            if (!panel.hidden) return;
            localStorage.setItem(tipKey, "1");
            tip.hidden = false;
            setTimeout(() => { tip.hidden = true; }, 10000);
        }, splashing ? 5600 : 1000);
    }

    return {
        // While a game page shows the session lobby, the help also unfolds the lobby's section, first.
        setLobby(inLobby) {
            topics = inLobby ? ["lobby", section] : [section];
        },
    };
}

// Confetti falling over the page for a success of the user (e.g. winning a round, or the team reaching its goal in a
// cooperative game), with a short fanfare unless sound is off in the user's profile. Each success is celebrated once:
// `key` names it within the session's game (e.g. the round), and it is remembered for the browser tab, so reloading
// the page does not show it again. The confetti is not shown with reduced motion; the fanfare still plays.
const CONFETTI_COLORS = ["#e6a93c", "#c2561a", "#2f7d45", "#3f6fd8", "#c0281b", "#8a4fd8", "#f4efe6"];

// The fanfare is made by the browser, so no sound file is needed. Browsers only allow sound after the user has tapped
// the page, so it is set up on each tap and resumed when the page is shown again; on iPhones it is played as media, so
// the silent switch does not mute it.
let celebrationAudio = null;
addEventListener("pointerdown", () => {
    if (navigator.audioSession) {
        try {
            navigator.audioSession.type = "playback";
        } catch {
            // Not supported on this browser.
        }
    }
    try {
        celebrationAudio ??= new AudioContext();
        celebrationAudio.resume().catch(() => {});
    } catch {
        // No sound on this browser.
    }
}, { capture: true });
document.addEventListener("visibilitychange", () => {
    if (!document.hidden) celebrationAudio?.resume().catch(() => {});
});

function playFanfare() {
    const audio = celebrationAudio;
    if (!audio || !userSettings().sound) return;
    (audio.state === "running" ? Promise.resolve() : audio.resume()).then(() => {
        const start = audio.currentTime;
        // A rising C major arpeggio, its last note held.
        [[523, 0, 0.18], [659, 0.12, 0.18], [784, 0.24, 0.18], [1047, 0.36, 0.6]].forEach(([frequency, delay, length]) => {
            const tone = audio.createOscillator();
            const volume = audio.createGain();
            tone.type = "triangle";
            tone.frequency.value = frequency;
            volume.gain.setValueAtTime(0.0001, start + delay);
            volume.gain.exponentialRampToValueAtTime(0.3, start + delay + 0.02);
            volume.gain.exponentialRampToValueAtTime(0.0001, start + delay + length);
            tone.connect(volume).connect(audio.destination);
            tone.start(start + delay);
            tone.stop(start + delay + length + 0.02);
        });
    }).catch(() => {});
}

function celebrate(state, key) {
    const stored = `confetti:${params.get("session") ?? ""}:${state.gameNumber}:${key}`;
    if (sessionStorage.getItem(stored)) return;
    sessionStorage.setItem(stored, "1");
    playFanfare();
    if (matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    const layer = el("div", "confetti");
    layer.setAttribute("aria-hidden", "true");
    for (let i = 0; i < 140; i++) {
        const piece = el("span", "confetti-piece");
        piece.style.setProperty("--x", `${Math.random() * 100}vw`);
        piece.style.setProperty("--drift", `${(Math.random() - 0.5) * 40}vw`);
        piece.style.setProperty("--spin", `${(Math.random() - 0.5) * 1440}deg`);
        piece.style.animationDelay = `${Math.random() * 0.8}s`;
        piece.style.animationDuration = `${2.4 + Math.random() * 1.8}s`;
        piece.style.background = CONFETTI_COLORS[i % CONFETTI_COLORS.length];
        layer.append(piece);
    }
    document.body.append(layer);
    setTimeout(() => layer.remove(), 5200);
}

// Each suit has its own color (see .card.hearts … in styles.css).
const SUIT_CLASSES = { "♥": "hearts", "♣": "clubs", "♦": "diamonds", "♠": "spades" };

// The suits as shapes in a 100 × 100 box, so they look the same on every device (as text, some show them as emoji).
const SUIT_SHAPES = {
    "♥": [["path", { d: "M50 92C22 70 4 52 4 31C4 15 16 5 30 5C40 5 47 11 50 19C53 11 60 5 70 5C84 5 96 15 96 31C96 52 78 70 50 92Z" }]],
    "♦": [["path", { d: "M50 3L88 50L50 97L12 50Z" }]],
    "♣": [["circle", { cx: 50, cy: 29, r: 21 }], ["circle", { cx: 27, cy: 60, r: 21 }], ["circle", { cx: 73, cy: 60, r: 21 }],
        ["circle", { cx: 50, cy: 52, r: 14 }], ["path", { d: "M46 52H54L62 96H38Z" }]],
    "♠": [["path", { d: "M50 3C34 28 6 42 6 64C6 78 17 86 29 86C37 86 44 82 47 75L40 97H60L53 75C56 82 63 86 71 86C83 86 94 78 94 64C94 42 66 28 50 3Z" }]],
};

// The ranks in the middle of a card as script letters, like the L in the logo: the centre line of each letter in a
// 60 × 80 box, with swashes and curled ends, drawn as a road (a rim in the suit's color, black asphalt, a dashed
// yellow centre line).
const ROAD_RANKS = {
    A: "M3 64C-1 74 10 80 16 70C24 54 26 26 34 10C38 2 44 4 44 14C44 32 44 58 50 70C54 78 62 74 58 66M10 44C20 36 36 50 52 40C56 38 56 34 52 34",
    2: "M14 24C6 22 8 6 24 4C40 2 52 14 42 30C34 42 16 52 10 70C22 62 34 76 50 68C58 64 56 56 50 58",
    3: "M14 18C6 16 10 4 26 4C42 4 48 18 40 28C36 32 30 34 24 34C40 34 52 44 48 58C44 74 20 78 10 68C4 62 10 54 16 60",
    4: "M52 76C44 76 42 68 42 60C42 42 44 22 46 6C34 18 20 34 6 50C18 46 38 50 54 44C60 42 60 36 54 36",
    5: "M58 8C54 2 46 6 40 8C32 10 24 8 18 8C16 18 14 28 12 36C26 28 50 34 48 54C46 74 20 78 10 68C4 62 10 54 16 60",
    6: "M50 12C54 6 46 0 36 2C18 6 10 26 10 44C10 64 22 76 34 74C48 72 50 54 40 46C30 38 16 44 14 54",
    7: "M4 16C0 8 10 2 16 8C24 14 38 8 54 6C42 24 30 46 26 70C24 78 16 78 16 72C16 68 20 66 22 68",
    8: "M34 40C14 34 10 8 28 4C44 0 50 24 30 40C12 52 12 76 30 74C50 72 52 48 34 40C46 36 58 38 58 46",
    9: "M46 28C40 42 12 44 12 24C12 6 44 0 46 22C48 46 42 70 22 74C10 76 6 66 14 64",
    10: "M2 20C6 14 12 10 16 4C14 28 14 54 12 70C10 78 2 76 4 70M42 6C24 8 24 72 40 74C58 76 58 4 42 6C50 4 58 6 60 12",
    J: "M12 14C6 8 14 2 22 6C32 10 46 12 58 6M42 10C44 32 44 56 32 70C22 80 4 74 8 62C10 56 18 56 18 62",
    Q: "M30 6C10 8 6 44 14 60C24 78 50 72 52 44C54 18 42 4 30 6ZM24 56C36 58 40 72 54 76C58 77 60 72 56 70",
    K: "M18 6C16 26 16 52 14 72C12 80 4 76 6 70M54 6C58 2 52 0 48 6C38 18 28 34 18 44C30 46 34 60 42 72C46 78 56 78 58 70",
    // More ranks: 0 and 1, S (Skip, a circle crossed out) and R (Reverse, two curved arrows).
    0: "M30 6C10 6 8 40 10 56C12 72 22 76 30 76C42 76 50 66 51 48C52 26 46 6 30 6C38 4 46 8 48 14",
    1: "M12 22C22 18 30 12 38 4C36 28 36 52 34 72C32 80 22 78 24 72",
    S: "M30 12C14 12 6 26 6 40C6 56 18 68 30 68C46 68 54 54 54 40C54 24 44 12 30 12ZM13 58L47 22",
    R: "M10 36C10 18 24 10 42 12M34 3L43 12L34 21M50 44C50 62 36 70 18 68M26 59L17 68L26 77",
    // A big plus sign, in straight lines.
    "+": "M4 40H56M30 12V68",
};

// Ranks drawn as a plus sign and a digit side by side, in a box 100 wide: Draw Two, and the middle of Wild Draw Four
// and of the +3 cards.
const WIDE_ROAD_RANKS = { "+2": 2, "+3": 3, "+4": 4 };
const PLUS_ROAD = "M4 40H30M17 27V53";

// The slash across a crossed-out +3, from bottom left to top right of the +3's box.
const CROSS_OUT = "M4 78L96 2";

// Middles drawn as roads in their own box, one road per part (classes part-1, part-2 …, so each can have its own rim
// color): TAKI in block letters of straight lines, TA above KI, in a 100 × 120 box (one part per letter); and a crown
// with three points topped with balls and a jewel in the middle, in a 100 × 80 box.
const BOXED_ROADS = {
    TAKI: {
        className: "card-road taki",
        viewBox: "-8 -8 116 136",
        parts: ["M0 0H42M21 0V52", "M58 52L79 0L100 52M65 34H93", "M4 68V120M40 68L4 96M14 88L40 120", "M66 68H92M79 68V120M66 120H92"],
    },
    CROWN: {
        className: "card-road crown",
        viewBox: "-12 -14 124 100",
        parts: [
            "M14 66L6 24C16 34 24 40 30 40C36 32 44 20 50 8C56 20 64 32 70 40C76 40 84 34 94 24L86 66C66 71 34 71 14 66Z",
            "M0 14a4 4 0 1 0 8 0a4 4 0 1 0 -8 0M46 -2a4 4 0 1 0 8 0a4 4 0 1 0 -8 0M92 14a4 4 0 1 0 8 0a4 4 0 1 0 -8 0",
            "M47 50a3 3 0 1 0 6 0a3 3 0 1 0 -6 0",
        ],
    },
};

// How a rank shows in a card's corners: Skip and Reverse as symbols.
const RANK_LABELS = { S: "⊘", R: "⇄" };

// The rank drawn as a script road (TAKI and the crown as in BOXED_ROADS).
function roadRank(rank) {
    const boxed = BOXED_ROADS[rank];
    const digit = WIDE_ROAD_RANKS[rank];
    const parts = boxed ? boxed.parts.map((d) => [d, 0]) : digit ? [[PLUS_ROAD, 0], [ROAD_RANKS[digit], 40]] : [[ROAD_RANKS[rank], 0]];
    const className = boxed?.className ?? (digit ? "card-road wide" : "card-road");
    const viewBox = boxed?.viewBox ?? (digit ? "-6 -6 112 92" : "-6 -6 72 92");
    return svgElement("svg", { class: className, viewBox, "aria-hidden": "true" },
        ...["road-kerb", "road-rim", "road", "road-line"].flatMap((layer) =>
            parts.map(([d, dx], i) => svgElement("path", {
                class: boxed ? `${layer} part-${i + 1}` : layer, d, ...(dx ? { transform: `translate(${dx} 0)` } : {}),
            }))));
}

// An SVG element with its attributes and children.
function svgElement(tag, attributes, ...children) {
    const element = document.createElementNS("http://www.w3.org/2000/svg", tag);
    Object.entries(attributes).forEach(([name, value]) => element.setAttribute(name, value));
    element.append(...children);
    return element;
}

// A suit as a shape in the card's color (the suitless "★" of The Gang's Jack card as text).
function suitShape(suit, className) {
    const shapes = SUIT_SHAPES[suit];
    if (!shapes) {
        return el("span", className, suit);
    }
    return svgElement("svg", { class: className, viewBox: "0 0 100 100", "aria-hidden": "true" },
        ...shapes.map(([tag, attributes]) => svgElement(tag, attributes)));
}

// The middle of a card: a very large, faint silhouette of the suit, and the rank on it, drawn as a road.
function cardFace(rank, suit) {
    const face = el("span", "card-face");
    if (SUIT_SHAPES[suit]) {
        face.append(suitShape(suit, "card-silhouette"));
    }
    face.append(ROAD_RANKS[rank] || WIDE_ROAD_RANKS[rank] || BOXED_ROADS[rank] ? roadRank(rank) : el("span", "card-road-text", rank));
    return face;
}

// The classic cards (the profile's card style): where the suit symbols go on number cards, as [x%, y%] on the card
// face, as on classic playing cards. Symbols in the lower half are drawn upside down.
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

// The figures on classic jacks, queens and kings (\uFE0E asks for the plain symbol rather than an emoji).
const COURT_FIGURES = { J: "♞\uFE0E", Q: "♛\uFE0E", K: "♚\uFE0E" };

// The middle of a classic card: suit symbols for 2 to 10, one big symbol for an ace, a framed figure for J, Q and K.
function classicFace(rank, suit) {
    if (COURT_FIGURES[rank]) {
        return el("span", "classic-face court",
            el("span", "court-frame", el("span", "court-suit", suit), el("span", "court-figure", COURT_FIGURES[rank]),
                el("span", "court-suit bottom", suit)));
    }
    if (rank === "A") {
        return el("span", "classic-face ace", el("span", "ace-pip", suit));
    }
    return el("span", "classic-face", ...PIP_LAYOUTS[rank].map(([x, y]) => {
        const pip = el("span", y > 50 ? "pip flipped" : "pip", suit);
        pip.style.left = `${x}%`;
        pip.style.top = `${y}%`;
        return pip;
    }));
}

// A faint octagon in the four suit colors, eight slices from the middle, as the silhouette of a joker.
function octagonSilhouette() {
    const corner = (i) => {
        const angle = ((22.5 + 45 * i) * Math.PI) / 180;
        return `${(50 + 48 * Math.cos(angle)).toFixed(1)} ${(50 + 48 * Math.sin(angle)).toFixed(1)}`;
    };
    const colors = ["#d62828", "#23913a", "#e5a400", "#1d5bd6"];
    return svgElement("svg", { class: "card-silhouette octagon", viewBox: "0 0 100 100", "aria-hidden": "true" },
        ...Array.from({ length: 8 }, (_, i) => svgElement("path", { d: `M50 50L${corner(i)}L${corner(i + 1)}Z`, fill: colors[i % 4] })));
}

// A joker-like card: the colorful octagon with `centre` on it, both centred (e.g. the GamesLane logo), and `word`
// written downwards in the top left and (upside down) bottom right corners, each letter in a suit color in turn.
function jokerCard(card, word, centre) {
    card.classList.add("joker");
    const letters = (className) => el("span", className, ...[...word].map((letter) => el("span", null, letter)));
    card.append(el("span", "card-face", octagonSilhouette(), centre), letters("joker-word"), letters("joker-word bottom"));
    return card;
}

// A classic card: rank and suit in two corners and a classic face in the middle, hearts and diamonds in red; a joker is
// a big 🃏 with the word under it.
function classicCard(card, text) {
    card.classList.add("classic");
    if (text === "JOKER") {
        card.classList.add("joker");
        card.append(el("span", "classic-joker-face", "🃏"), el("span", "classic-joker-word", "JOKER"));
        return card;
    }
    const rank = text.slice(0, -1);
    const suit = text.slice(-1);
    card.classList.toggle("red", suit === "♥" || suit === "♦");
    const corner = (className) => el("span", className, el("span", "card-rank", rank), el("span", "classic-suit", suit));
    card.append(corner("classic-corner"), classicFace(rank, suit), corner("classic-corner bottom"));
    return card;
}

// A card such as "10♥", in `style` (by default the user's card style, see userSettings). GamesLane cards: rank and
// suit in block letters in the top left and bottom right corners, and the suit's silhouette with the rank in the middle,
// all in the suit's color. "JOKER" is a joker. In the GamesLane style only, also "0♥" to "9♥", "S♥" (Skip),
// "R♥" (Reverse), "+2♥", "+♥" (a big plus sign) and "TAKI♥" (TA above KI in the middle, TAKI written downwards in the
// corners); "COLOR" is drawn like the joker, and "+4" (Wild Draw Four) like it with a road "+4" in the middle and "+4"
// across the corners; "+3" like +4, and "+3X" like +3 crossed out (a red slash across the middle, a yellow and a green
// slash through the corners); "TAKI" (no suit) like the joker with TA above KI in the middle, each letter's rim in a suit color in turn;
// "CROWN" like the joker with a crown rimmed in gold in the middle and a gold ♛ in the corners. Without text, a
// face-down card.
function cardElement(text, style = userSettings().cards) {
    const card = el("span", "card");
    if (!text) {
        card.classList.add("empty");
        return card;
    }
    card.setAttribute("role", "img");
    card.setAttribute("aria-label", text === "JOKER" ? t("Joker") : text);
    if (style === "Classic") {
        return classicCard(card, text);
    }
    if (text === "JOKER" || text === "COLOR") {
        return jokerCard(card, text, el("span", "joker-logo"));
    }
    if (text === "+4" || text === "+3" || text === "+3X") {
        const rank = text.slice(0, 2);
        const road = roadRank(rank);
        card.classList.add("plus-draw");
        if (text === "+3X") {
            card.classList.add("crossed");
            road.append(svgElement("path", { class: "cross-out-edge", d: CROSS_OUT }), svgElement("path", { class: "cross-out", d: CROSS_OUT }));
        }
        return jokerCard(card, rank, road);
    }
    if (text === "TAKI") {
        card.classList.add("taki-joker");
        return jokerCard(card, text, roadRank(text));
    }
    if (text === "CROWN") {
        card.classList.add("crown");
        return jokerCard(card, "♛", roadRank(text));
    }
    const rank = text.slice(0, -1);
    const suit = text.slice(-1);
    if (SUIT_CLASSES[suit]) {
        card.classList.add(SUIT_CLASSES[suit]);
    }
    card.classList.toggle("taki", rank === "TAKI");
    const corner = (className) => el("span", className, el("span", "card-rank", RANK_LABELS[rank] ?? rank), suitShape(suit, "card-suit"));
    card.append(cardFace(rank, suit), corner("card-corner"), corner("card-corner bottom"));
    return card;
}

// A list item showing text, or other elements (e.g. from seatLabel).
function listItem(...content) {
    const item = document.createElement("li");
    item.append(...content);
    return item;
}

// A seat's name as shown on the game screen. An Away holder is marked "(away)"; any disconnected holder gets a spinner
// (see seatLabel).
function seatName(state, seat, view) {
    const info = state.seats[seat];
    if (!info) {
        return t("Empty seat");
    }
    return info.name + (view.isMe(seat) ? ` ${t("(you)")}` : "") + (info.status === "Away" ? ` ${t("(away)")}` : "");
}

// A small spinner for a seat whose holder is disconnected.
function reconnectingSpinner(label = t("Reconnecting…")) {
    const spinner = el("span", "seat-spinner");
    spinner.title = label;
    spinner.setAttribute("role", "img");
    spinner.setAttribute("aria-label", label);
    return spinner;
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
    if (info && info.status !== "Present") {
        label.append(reconnectingSpinner(t(info.status === "Away" ? "Away" : "Reconnecting…")));
    }
    return label;
}

// The score row in the status bar (#scores), for games that keep a score: the match facts (e.g. "Round 3"), then a chip
// per player with what comes before the name (e.g. a mark), the name, the score and any extra (e.g. a pip count), and
// the seat to act outlined. Players: [{ seat, before, score, extra, acting }].
function showScores(state, view, facts, players) {
    const chip = (player) => {
        const item = el("li", "score-chip", player.before ?? "", seatLabel(state, player.seat, view),
            el("strong", "score-value", String(player.score)));
        if (player.extra) {
            item.append(el("span", "score-extra", player.extra));
        }
        item.classList.toggle("acting", Boolean(player.acting));
        return item;
    };
    $("scores").replaceChildren(
        ...(facts.length ? [el("li", "score-facts", facts.filter(Boolean).join(" · "))] : []),
        ...players.map(chip));
}

// The session's chat, for everyone with the page open (players and watchers): 💬 in the session bar opens it, on the
// side of the page (the whole screen on phones). While it is closed, each message from someone else adds to the unread
// count on 💬, makes it flash, shows the message in a pop-up for a few seconds and plays a short sound. While it is
// open, new messages only appear in it (and the sound plays if the tab is hidden). The chat and the sound follow the
// user's settings (see userSettings), which the chat's Chat and Sound boxes change too: with the chat off, 💬 is hidden
// and nothing is shown or played. Browsers play sound only once the user has tapped or pressed a key on the page.
const CHAT_MAX_LENGTH = 300;

function startChat(sessionId, view) {
    const seenKey = `chatSeen:${sessionId}`;
    const messages = new Map();
    let seen = Number(sessionStorage.getItem(seenKey) ?? 0);
    let audio = null;
    let toastTimer = 0;

    const button = el("button", "session-menu-button chat-button", "💬");
    button.type = "button";
    button.hidden = true;
    button.setAttribute("aria-haspopup", "dialog");
    button.setAttribute("aria-expanded", "false");
    const badge = el("span", "chat-badge");
    badge.hidden = true;
    button.append(badge);

    const close = el("button", "chat-close", "✕");
    close.type = "button";
    close.title = t("Close the chat");
    close.setAttribute("aria-label", t("Close the chat"));
    const setting = (key, text) => {
        const box = el("input");
        box.type = "checkbox";
        box.name = key;
        box.addEventListener("change", async () => {
            box.disabled = true;
            if (!(await saveSettings({ ...userSettings(), [key]: box.checked }))) {
                box.checked = !box.checked;
                showError(t("Something went wrong."));
            }
            box.disabled = false;
        });
        return el("label", "chat-setting", box, text);
    };
    const empty = el("p", "hint chat-empty", t("No messages yet. Say hello!"));
    const list = el("ol", "chat-messages");
    list.setAttribute("aria-live", "polite");
    const body = el("div", "chat-body", empty, list);
    const input = el("input");
    input.type = "text";
    input.maxLength = CHAT_MAX_LENGTH;
    input.autocomplete = "off";
    input.placeholder = t("Write a message…");
    input.setAttribute("aria-label", t("Write a message…"));
    const send = el("button", "small", t("Send"));
    send.type = "submit";
    const form = el("form", "chat-form", input, send);
    const panel = el("aside", "chat-panel",
        el("div", "chat-head", el("h2", null, t("Chat")), close),
        el("div", "chat-settings", setting("chat", t("Chat")), setting("sound", t("Sound"))),
        body,
        form);
    panel.hidden = true;
    panel.setAttribute("role", "dialog");
    panel.setAttribute("aria-label", t("Chat"));
    const toast = el("button", "chat-toast");
    toast.type = "button";
    toast.hidden = true;
    document.body.append(panel, toast);

    const isOpen = () => !panel.hidden;
    const chatOn = () => userSettings().chat;
    const soundOn = () => chatOn() && userSettings().sound;
    const fromOthers = (message) => message.playerId !== view.playerId;

    function updateBadge() {
        const unread = [...messages.values()].filter((m) => m.number > seen && fromOthers(m)).length;
        badge.textContent = unread > 99 ? "99+" : String(unread);
        badge.hidden = unread === 0 || !chatOn();
        const label = badge.hidden ? t("Open the chat") : t("Chat: {n} new messages", { n: unread });
        button.title = label;
        button.setAttribute("aria-label", label);
    }

    function markSeen() {
        seen = Math.max(seen, 0, ...messages.keys());
        sessionStorage.setItem(seenKey, String(seen));
        updateBadge();
    }

    function messageItem(message) {
        const time = new Date(message.time).toLocaleTimeString(language.locale, { hour: "2-digit", minute: "2-digit" });
        return el("li", fromOthers(message) ? "chat-message" : "chat-message mine",
            iconElement(message.icon, "chat-icon"),
            el("div", "chat-text",
                el("span", "chat-meta", el("strong", null, message.name), " ", el("time", null, time)),
                el("p", null, message.text)));
    }

    function render() {
        const sorted = [...messages.values()].sort((a, b) => a.number - b.number);
        list.replaceChildren(...sorted.map(messageItem));
        empty.hidden = sorted.length > 0;
        body.scrollTop = body.scrollHeight;
    }

    // A short two-note chime, made in the browser so no sound file is needed.
    function chime() {
        if (!soundOn()) return;
        try {
            audio ??= new AudioContext();
            audio.resume().catch(() => {});
            const start = audio.currentTime;
            [880, 1320].forEach((frequency, i) => {
                const at = start + i * 0.12;
                const tone = audio.createOscillator();
                const volume = audio.createGain();
                tone.frequency.value = frequency;
                volume.gain.setValueAtTime(0.0001, at);
                volume.gain.exponentialRampToValueAtTime(0.2, at + 0.02);
                volume.gain.exponentialRampToValueAtTime(0.0001, at + 0.15);
                tone.connect(volume).connect(audio.destination);
                tone.start(at);
                tone.stop(at + 0.16);
            });
        } catch {
            // No sound on this browser.
        }
    }

    // Browsers allow sound only after the user has interacted with the page, so it is prepared on the first tap or key.
    const unlockSound = () => {
        try {
            audio ??= new AudioContext();
            audio.resume().catch(() => {});
        } catch {
            // No sound on this browser.
        }
    };
    document.addEventListener("pointerdown", unlockSound, { once: true });
    document.addEventListener("keydown", unlockSound, { once: true });

    function notify(message) {
        if (!chatOn()) return;
        button.classList.remove("flash");
        void button.offsetWidth;
        button.classList.add("flash");
        toast.replaceChildren(iconElement(message.icon, "chat-icon"),
            el("span", null, el("strong", null, message.name), `: ${message.text}`));
        toast.hidden = false;
        clearTimeout(toastTimer);
        toastTimer = setTimeout(() => { toast.hidden = true; }, 5000);
        chime();
    }

    function show(open) {
        panel.hidden = !open;
        button.setAttribute("aria-expanded", String(open));
        if (open) {
            toast.hidden = true;
            markSeen();
            body.scrollTop = body.scrollHeight;
            if (matchMedia("(hover: hover)").matches) input.focus();
        }
    }

    button.addEventListener("click", () => show(!isOpen()));
    close.addEventListener("click", () => show(false));
    toast.addEventListener("click", () => show(true));
    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape" && isOpen()) show(false);
    });
    form.addEventListener("submit", async (event) => {
        event.preventDefault();
        const text = input.value.trim();
        if (!text) return;
        send.disabled = true;
        try {
            await call("SendChatMessage", sessionId, text);
            input.value = "";
        } catch {
            // call() has shown the error.
        } finally {
            send.disabled = false;
            input.focus();
        }
    });

    // With the chat off, 💬 and the chat are hidden. The boxes in the chat show the current settings.
    function applySettings() {
        const { chat, sound } = userSettings();
        panel.querySelector('[name="chat"]').checked = chat;
        panel.querySelector('[name="sound"]').checked = sound;
        button.hidden = !chat;
        if (!chat) {
            show(false);
            toast.hidden = true;
        }
        updateBadge();
    }
    addEventListener(SETTINGS_EVENT, applySettings);
    accountReady.then(applySettings);

    return {
        button,
        // The messages from entering the session (again, after reconnecting): no notifications, only the unread count.
        load(history) {
            history.forEach((m) => messages.set(m.number, m));
            render();
            if (isOpen()) markSeen(); else updateBadge();
        },
        receive(message) {
            if (messages.has(message.number)) return;
            messages.set(message.number, message);
            render();
            if (isOpen()) {
                markSeen();
                if (document.hidden && fromOthers(message)) chime();
            } else {
                updateBadge();
                if (fromOthers(message)) notify(message);
            }
        },
    };
}

// A set option's value lists the choices turned off, separated by commas.
const turnedOff = (value) => (value ?? "").split(",").filter(Boolean);
// Whether an option applies, given the chosen values and all the options (e.g. card options only in Advanced mode): the
// option it depends on has one of its values and applies too.
const applies = (option, values, options) => !option.shownWhen
    || (option.shownWhen.values.includes(values[option.shownWhen.key])
        && applies(options.find((o) => o.key === option.shownWhen.key) ?? {}, values, options));

// A session's chosen options, one text per option that applies, e.g. ["Mode: Advanced", "Challenge cards: 5 of 6"].
function optionLines(state) {
    return state.optionList
        .filter((option) => applies(option, state.options, state.optionList))
        .map((option) => {
            const label = translateServerText(option.label);
            if (option.kind === "Set") {
                const on = option.choices.length - turnedOff(state.options[option.key]).length;
                return `${label}: ${on === option.choices.length ? t("all") : t("{on} of {total}", { on, total: option.choices.length })}`;
            }
            return `${label}: ${translateServerText(option.choices.find((c) => c.value === state.options[option.key])?.label) ?? ""}`;
        });
}

// The chosen options as "Game options:" and a bulleted list, one option per line, aligned to the start (shown on the
// session lobby's options button, and during a game in #game-options, see startSessionPage). Nothing for a game
// without options.
function optionsList(state) {
    if (state.optionList.length === 0) {
        return [];
    }
    return [el("strong", "options-title", t("Game options:")), el("ul", "options-lines", ...optionLines(state).map((line) => el("li", null, line)))];
}

/**
 * Runs a session page. The page URL is <page>?session=CODE. Anyone can watch; logged-in users can take seats.
 * While the session is in its lobby, the shared session lobby (#session-lobby) is shown; while a game is being
 * played, the game screen (#game-screen) is shown and render(game, state, view) is called on every update.
 * view.mySeats lists the seats this tab's user holds; view.isMe(seat) tells whether they hold a seat.
 * view.private is this user's private game information (e.g. their cards), for games that have it, or null.
 * The session bar shows the user's role ("You: …"): seatRole(seat, state) gives a seat's name (by default "Seat 2"), or
 * "Watching" for an observer. While a game is on, the status bar under it shows #status; while the game waits for
 * disconnected players, its players see a "Waiting for …" pop-up (see updateWaiting), and a user without a seat opening
 * or reconnecting to a game with disconnected players is offered their seats (see updateSeatOffer).
 */
function startSessionPage(gameType, render, seatRole = (seat) => t("Seat {n}", { n: seat + 1 })) {
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

    // Cards are drawn in the user's card style, known only once the user is.
    addEventListener(SETTINGS_EVENT, () => view.refresh());

    const joinUrl = `${location.origin}/join/${sessionId}`;
    $("session-code").textContent = sessionId;
    $("join-url").href = joinUrl;
    $("join-url").textContent = joinUrl;

    const act = (method, ...args) => call(method, sessionId, ...args).catch(() => {});

    // The one empty seat shown in the lobby: the next available one, or -1 if there is none (state.maxSeats is the number
    // of seats the chosen options allow).
    const nextSeat = (state) => state.seats.slice(0, state.maxSeats).findIndex((s) => !s);

    const holderName = (state, seat) => state.seats[seat]?.name ?? t("Empty seat");

    // While the game waits for disconnected players (state.waitingSeats), its players see a pop-up "Waiting for …" with
    // one button: Skip player where the game can skip them (state.canSkipWaiting), otherwise End game (after
    // confirming). It closes by itself once the game no longer waits, e.g. when the player comes back.
    const waiting = el("dialog", "dialog waiting-dialog");
    waiting.addEventListener("cancel", (event) => event.preventDefault());
    document.body.append(waiting);

    function updateWaiting(state) {
        const seats = state.phase === "Playing" && view.isPlayer ? state.waitingSeats : [];
        if (seats.length === 0) {
            if (waiting.open) waiting.close();
            return;
        }
        const button = el("button", state.canSkipWaiting ? "" : "danger", t(state.canSkipWaiting ? "Skip player" : "End game"));
        button.type = "button";
        button.addEventListener("click", () => {
            if (state.canSkipWaiting) {
                act("SkipPlayer", seats[0]);
            } else if (confirm(t("End the game? Its progress is lost, and everyone goes back to the lobby."))) {
                act("EndGame");
            }
        });
        waiting.replaceChildren(
            el("h2", null, t("Waiting for {name}", { name: seats.map((seat) => holderName(state, seat)).join(", ") }),
                reconnectingSpinner(t("Away"))),
            el("p", "hint", t(state.canSkipWaiting
                ? "This player is disconnected. Wait for them to come back, or skip their turn."
                : "This player is disconnected. Wait for them to come back, or end the game.")),
            el("div", "actions center", button));
        if (!waiting.open) waiting.showModal();
    }

    // A user without a seat who opens or reconnects to a game with disconnected players (entry.seatOffer) is offered
    // their seats: they take one, or choose Watch. A seat taken by someone else, or whose player comes back, leaves the
    // list; with none left, the user watches.
    let seatOffer = false;
    const offer = el("dialog", "dialog seat-offer");
    offer.addEventListener("cancel", (event) => {
        event.preventDefault();
        declineSeatOffer();
    });
    document.body.append(offer);

    function declineSeatOffer() {
        seatOffer = false;
        if (offer.open) offer.close();
        call("DeclineSeatOffer", sessionId).catch(() => {});
    }

    function updateSeatOffer(state) {
        if (!seatOffer) return;
        const seats = state.phase === "Playing" && !view.isPlayer ? state.absentSeats : [];
        if (seats.length === 0) {
            if (view.isPlayer) {
                seatOffer = false;
                if (offer.open) offer.close();
            } else {
                declineSeatOffer();
            }
            return;
        }
        const watch = el("button", "secondary", t("Watch"));
        watch.type = "button";
        watch.addEventListener("click", declineSeatOffer);
        offer.replaceChildren(
            el("h2", null, t("Join this game?")),
            el("p", "hint", t("These players are disconnected. Take one of their seats to play on in their place, or watch the game.")),
            el("ul", "seat-offer-list", ...seats.map((seat) => {
                const take = el("button", "small", t("Take this seat"));
                take.type = "button";
                take.addEventListener("click", () => act("TakeOfferedSeat", seat));
                const info = state.seats[seat];
                return el("li", null, iconElement(info?.icon ?? "+"),
                    el("span", "seat-offer-name", `${seatRole(seat, state)} · ${holderName(state, seat)}`), take);
            })),
            el("div", "actions center", watch));
        if (!offer.open) offer.showModal();
    }

    // The session bar's menu (☰): the invite link to copy or share, how many players are seated and how many watch, for
    // players, Leave my seat and (during a game) End game for everyone, and Send feedback when the server can post comments.
    const menuButton = el("button", "session-menu-button", "☰");
    menuButton.type = "button";
    menuButton.title = t("Game menu");
    menuButton.setAttribute("aria-label", t("Game menu"));
    menuButton.setAttribute("aria-haspopup", "true");
    menuButton.setAttribute("aria-expanded", "false");
    const menuAction = (text, className) => {
        const button = el("button", `session-menu-action ${className}`, text);
        button.type = "button";
        button.hidden = true;
        return button;
    };
    const share = el("button", "secondary small", t("Share"));
    share.type = "button";
    share.hidden = !navigator.share;
    share.addEventListener("click", () => navigator.share({ title: t("Join my GamesLane game"), url: joinUrl }).catch(() => {}));
    const seatedCount = el("span", "session-menu-value");
    const standUp = menuAction(t("Leave my seat (watch)"), "secondary");
    const endGame = menuAction(t("End game for everyone"), "danger");
    const feedback = menuAction(t("Send feedback"), "secondary");
    accountReady.then(() => { feedback.hidden = !account.feedback; });
    feedback.addEventListener("click", () => {
        showMenu(false);
        openFeedbackDialog();
    });
    const inviteRow = el("div", "session-menu-row", el("span", null, t("Invite link")),
        copyButton(joinUrl, t("Could not copy. Select the link and copy it manually.")), share);
    const menu = el("div", "session-menu",
        inviteRow,
        el("div", "session-menu-row", el("span", null, t("Players:")), seatedCount),
        el("div", "session-menu-actions", standUp, endGame, feedback));
    menu.hidden = true;
    menu.setAttribute("role", "menu");
    const showMenu = (open) => {
        menu.hidden = !open;
        menuButton.setAttribute("aria-expanded", String(open));
    };
    menuButton.addEventListener("click", () => showMenu(menu.hidden));
    document.addEventListener("click", (event) => {
        if (!menu.contains(event.target) && event.target !== menuButton) showMenu(false);
    });
    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape") showMenu(false);
    });
    $("session-code").closest(".code").after(menuButton);
    $("session-info").closest(".session-bar").append(menu);
    const chat = startChat(sessionId, view);
    menuButton.before(chat.button);

    // The session bar's role ("You: ⚪ White", or "👁 Watching") and, in its menu, how many players are seated and how
    // many connections watch.
    function updateSessionInfo(state) {
        const role = view.isPlayer
            ? t("You: {seats}", { seats: view.mySeats.map((seat) => seatRole(seat, state)).join(", ") })
            : `👁 ${t("Watching")}`;
        $("session-info").replaceChildren(el("span", "session-fact", role));
        seatedCount.textContent = t("{taken} players · {observers} watching",
            { taken: state.seats.filter(Boolean).length, observers: state.observerCount });
    }

    // A seat tile: a big icon, the name, and a note such as "Seat 2 · You". The user's own seat is a button to leave
    // it; the empty seat is a button to sit there (the server gives the next available seat, which is this one). In a
    // public game, leaving a seat leaves the game, and the empty seat waits for players joining from the main screen.
    function seatItem(state, seat) {
        const info = state.seats[seat];
        const action = view.isMe(seat) ? (state.isPublic ? { text: t("Leave game"), run: leaveGame } : { text: t("Leave seat"), run: () => act("LeaveSeat", seat) })
            : !info && !state.isPublic ? { text: t("Sit here"), run: () => act("TakeSeat") }
                : null;

        const tile = document.createElement(action ? "button" : "div");
        tile.className = "seat";
        tile.classList.toggle("empty", !info);
        tile.classList.toggle("mine", view.isMe(seat));
        tile.classList.toggle("away", Boolean(info && info.status !== "Present"));

        const avatar = iconElement(info ? info.icon : "+", "seat-avatar");
        const name = document.createElement("span");
        name.className = "seat-name";
        name.textContent = info ? info.name : t("Empty seat");
        const note = document.createElement("span");
        note.className = "seat-note";
        note.textContent = [t("Seat {n}", { n: seat + 1 }), view.isMe(seat) && t("You"),
            state.isPublic && info && info.playerId === state.hostPlayerId && t("Creator"), info?.status === "Away" && t("Away"),
            info?.status === "Reconnecting" && t("Reconnecting…")]
            .filter(Boolean)
            .join(" · ");
        if (info?.status === "Reconnecting") {
            note.append(reconnectingSpinner());
        }
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

    // Anyone in the session chooses the options for the next game; in a public game, only its creator (others only see
    // them). Saved users can also save them as their preferences (used automatically when they create a game) and load
    // them back.
    function openOptionsDialog() {
        const state = lastState;
        const canChange = !state.isPublic || isHost(state);
        const canSave = canChange && account?.signedIn && account.kind !== "Guest";
        const dialog = createDialog(`
            <form>
                <h2>${t("Game options")}</h2>
                <fieldset class="option-list"></fieldset>
                <div class="actions option-prefs" ${canSave ? "" : "hidden"}>
                    <button type="button" class="secondary small" data-save>${t("Save as my preferences")}</button>
                    <button type="button" class="secondary small" data-load>${t("Load my preferences")}</button>
                </div>
                <p class="hint" data-note role="status"></p>
                <p class="error" role="alert"></p>
                <div class="actions"><button type="submit">${t("Apply")}</button><button type="button" class="secondary" data-close>${t("Cancel")}</button></div>
            </form>`);
        const form = dialog.querySelector("form");
        const note = form.querySelector("[data-note]");
        const error = form.querySelector(".error");
        form.querySelector(".option-list").disabled = !canChange;
        form.querySelector('[type="submit"]').hidden = !canChange;
        if (!canChange) {
            note.textContent = t("The game's creator chooses the options.");
        }

        // One group per option: radio buttons for a choice, tick boxes for a set. Choices without hints sit in one row.
        // A choice with a picture (e.g. a card) shows it before the text; its box is hidden behind the picture.
        form.querySelector(".option-list").replaceChildren(...state.optionList.map((option) => {
            const fieldset = document.createElement("fieldset");
            fieldset.className = "option";
            fieldset.dataset.key = option.key;
            fieldset.classList.toggle("inline", option.choices.every((c) => !c.hint));
            const legend = document.createElement("legend");
            legend.textContent = translateServerText(option.label);
            fieldset.append(legend, ...option.choices.map((choice) => {
                const label = document.createElement("label");
                label.className = "option-choice";
                const input = document.createElement("input");
                input.type = option.kind === "Set" ? "checkbox" : "radio";
                input.name = option.key;
                input.value = choice.value;
                const text = document.createElement("span");
                text.className = "option-text";
                text.append(Object.assign(document.createElement("strong"), { textContent: translateServerText(choice.label) }));
                if (choice.hint) {
                    text.append(Object.assign(document.createElement("small"), { textContent: translateServerText(choice.hint) }));
                }
                if (choice.image) {
                    const picture = document.createElement("span");
                    picture.className = "option-picture";
                    picture.append(Object.assign(document.createElement("img"), { className: "option-image", src: choice.image, alt: "" }), input);
                    label.append(picture);
                } else {
                    label.append(input);
                }
                label.append(text);
                return label;
            }));
            return fieldset;
        }));

        const inputs = (option) => [...form.querySelectorAll(`input[name="${option.key}"]`)];
        const chosen = () => Object.fromEntries(state.optionList.map((option) => [option.key, option.kind === "Set"
            ? inputs(option).filter((i) => !i.checked).map((i) => i.value).join(",")
            : inputs(option).find((i) => i.checked)?.value ?? option.default]));
        // Shows only the options that apply to the current choices.
        const showApplicable = () => {
            const values = chosen();
            state.optionList.forEach((option) => {
                form.querySelector(`fieldset[data-key="${option.key}"]`).hidden = !applies(option, values, state.optionList);
            });
        };
        const fill = (values) => {
            state.optionList.forEach((option) => {
                const value = values[option.key] ?? option.default;
                inputs(option).forEach((input) => {
                    input.checked = option.kind === "Set" ? !turnedOff(value).includes(input.value) : input.value === value;
                });
            });
            showApplicable();
        };
        form.addEventListener("change", showApplicable);
        const url = `/api/options/${state.gameType}`;
        const unreachable = t("Could not reach the server. Check your connection and try again.");
        fill(state.options);

        form.querySelector("[data-save]").addEventListener("click", (event) => withBusyButton(event.currentTarget, t("Saving…"), async () => {
            note.textContent = error.textContent = "";
            const response = await authFetch(url, {
                method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify(chosen()),
            }).catch(() => null);
            if (response?.ok) {
                note.textContent = t("Saved. New games you start will use these options.");
            } else {
                error.textContent = translateServerText((await response?.json().catch(() => null))?.error)
                    ?? (response ? t("Something went wrong.") : unreachable);
            }
        }));
        form.querySelector("[data-load]").addEventListener("click", (event) => withBusyButton(event.currentTarget, t("Loading…"), async () => {
            note.textContent = error.textContent = "";
            const response = await authFetch(url).catch(() => null);
            if (!response?.ok) {
                error.textContent = response ? t("Something went wrong.") : unreachable;
                return;
            }
            const result = await response.json();
            fill(result.options);
            note.textContent = t(result.saved ? "Your preferences are loaded. Press Apply to use them." : "You have no saved preferences yet.");
        }));
        form.addEventListener("submit", (event) => {
            event.preventDefault();
            withBusyButton(form.querySelector('[type="submit"]'), t("Applying…"), async () => {
                error.textContent = "";
                try {
                    await connection.invoke("SetOptions", sessionId, chosen());
                    dialog.close();
                } catch (err) {
                    error.textContent = errorText(err);
                }
            });
        });
        dialog.addEventListener("close", () => dialog.remove());
        dialog.showModal();
    }

    function lobbyStatus(state, taken) {
        if (taken < state.minSeats) {
            return t("Waiting for players ({taken}/{needed} needed)…", { taken, needed: state.minSeats });
        }
        if (state.isPublic && !isHost(state)) {
            const host = state.seats.find((s) => s && s.playerId === state.hostPlayerId);
            return t("Waiting for {name} to start the game.", { name: host?.name ?? "" });
        }
        return t("Ready to start.");
    }

    // Whether this user creates the public game: they alone choose its options and start it.
    const isHost = (state) => Boolean(view.playerId) && state.hostPlayerId === view.playerId;

    // Leaving a public game gives up the user's seats and goes back to the main screen, since nobody watches it.
    let leaving = false;
    async function leaveGame() {
        leaving = true;
        await call("LeaveSession", sessionId).catch(() => {});
        location.href = "/";
    }

    function update(state) {
        lastState = state;
        view.mySeats = state.seats.flatMap((s, i) => (s && s.playerId === view.playerId ? [i] : []));
        // The server sends the state to everyone just before it answers EnterSession, so until then (no playerId yet)
        // the user's seats are not known.
        if (state.isPublic && !view.isPlayer) {
            if (view.playerId && !leaving) location.replace("/?left=1");
            return;
        }
        const inLobby = state.phase === "Lobby";
        const taken = state.seats.filter(Boolean).length;

        $("session-lobby").hidden = !inLobby;
        $("game-screen").hidden = inLobby;
        help.setLobby(inLobby);
        $("status-bar").hidden = inLobby;
        $("invite").hidden = !inLobby || state.isPublic;
        $("session-code").closest(".code").hidden = state.isPublic;
        inviteRow.hidden = state.isPublic;
        updateSessionInfo(state);
        standUp.textContent = state.isPublic ? t("Leave game") : t("Leave my seat (watch)");
        standUp.hidden = !view.isPlayer;
        endGame.hidden = inLobby || !view.isPlayer || state.phase === "Over";

        updateWaiting(state);
        updateSeatOffer(state);
        if (inLobby) {
            $("lobby-status").textContent = [translateServerText(state.notice), lobbyStatus(state, taken)].filter(Boolean).join(" ");
            $("seat-count").textContent = `${taken} / ${state.maxSeats}`;
            const next = nextSeat(state);
            $("seats").replaceChildren(...state.seats.flatMap((info, seat) => (info || seat === next ? [seatItem(state, seat)] : [])));
            $("lobby-options").hidden = state.optionList.length === 0;
            $("options-summary").replaceChildren(...optionsList(state));
            $("start-game").hidden = !(view.isPlayer && taken >= state.minSeats && (!state.isPublic || isHost(state)));
            $("start-game").textContent = t(state.gameNumber > 0 ? "New game" : "Start");
            return;
        }

        $("play-again").hidden = $("to-lobby").hidden = !(view.isPlayer && state.phase === "Over");
        const gameOptions = $("game-options");
        if (gameOptions) {
            gameOptions.replaceChildren(...optionsList(state));
            gameOptions.hidden = gameOptions.childElementCount === 0;
        }
        render(state.game, state, view);
        // Games mark their status with 👉 (the user has to act) or ⏳ (waiting for others). The marks only choose the
        // bar's color (gold or grey) and are not shown.
        const status = $("status");
        const mustAct = status.textContent.includes("👉")
            || [...$("status-bar").querySelectorAll(".status-actions button")].some((b) => !b.hidden && !b.classList.contains("secondary"));
        status.textContent = status.textContent.replace(/[👉⏳]\uFE0F?\s*/gu, "").trim();
        $("status-bar").classList.toggle("act", mustAct);
    }

    async function enter() {
        let entry;
        showBusy(t("Opening the game…"));
        try {
            entry = await call("EnterSession", sessionId);
        } catch (err) {
            if (/You are no longer in this game\./.test(err?.message ?? "")) {
                location.replace("/?left=1");
            }
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
        seatOffer = Boolean(entry.seatOffer);
        update(entry.state);
        chat.load(entry.chat ?? []);
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
    connection.on("ChatMessageReceived", (message) => chat.receive(message));
    connection.onreconnected(() => {
        showError("");
        enter();
    });

    endGame.addEventListener("click", () => {
        showMenu(false);
        if (confirm(t("End the game? Its progress is lost, and everyone goes back to the lobby."))) {
            act("EndGame");
        }
    });
    $("start-game").addEventListener("click", () => act("NewGame", lastState?.gameNumber ?? 0));
    $("lobby-options").addEventListener("click", openOptionsDialog);
    $("play-again").addEventListener("click", () => act("NewGame", lastState?.gameNumber ?? 0));
    $("to-lobby").addEventListener("click", () => act("ReturnToLobby"));
    standUp.addEventListener("click", () => {
        showMenu(false);
        if (lastState?.isPublic) {
            leaveGame();
            return;
        }
        view.mySeats.forEach((seat) => act("LeaveSeat", seat));
    });

    // Going back to the main screen gives up this user's seats.
    document.querySelectorAll("[data-leave]").forEach((link) => {
        link.addEventListener("click", async (event) => {
            event.preventDefault();
            leaving = true;
            await call("LeaveSession", sessionId).catch(() => {});
            location.href = link.href;
        });
    });

    setUpInviteButtons(joinUrl);
    setUpCodeCopy(sessionId);
    showBusy(t("Opening the game…"));
    ready.then((ok) => (ok ? enter() : hideBusy()));
    return view;
}
