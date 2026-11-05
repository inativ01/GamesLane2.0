"use strict";

// Shared by all pages: login, the hub connection, error display, and the game page framework. Texts go through t()
// (see i18n.js), so pages with translations show them in the chosen language.

const $ = (id) => document.getElementById(id);
const params = new URLSearchParams(location.search);

translatePage();
$("account").before(languagePicker() ?? "");

const GAME_PAGES = {
    TicTacToe: "/tictactoe.html", RockPaperScissors: "/rock-paper-scissors.html", TheGang: "/the-gang.html", Backgammon: "/backgammon.html",
    RichManPoorMan: "/rich-man-poor-man.html", Yatzee: "/yatzee.html", Uno: "/uno.html",
};

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
// { signedIn, kind, name, icon, uploadedIcon, gameOrder, providers, icons }. kind is "Guest", "Registered" or a provider
// such as "Google"; gameOrder is a saved user's order of the games on the main screen, or null. Everyone is logged in,
// as a guest at least; signedIn is false only if the server could not be reached.
let account = null;
let isNewExternalUser = false;

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

// The profile: change the display name and icon. Saved users can also upload a picture as their icon, and log out (to
// become a new guest, with a random name and icon). Guests can log in, create an account (with the name and icon chosen
// above), or sign in with an external provider. The new user keeps the seats the previous one held.
function openProfileDialog() {
    const isGuest = account.kind === "Guest";
    const icons = account.uploadedIcon ? [account.uploadedIcon, ...account.icons] : account.icons;
    const dialog = createDialog(`
        <form data-profile>
            <h2>${t("Your profile")}</h2>
            <p class="hint" data-welcome hidden>${t("Welcome! Choose how other players see you.")}</p>
            <label>${t("Display name")} <input name="name" maxlength="30" autocomplete="nickname" required></label>
            <div class="icon-picker"></div>
            <label class="upload" ${isGuest ? "hidden" : ""}>${t("Upload a picture")} <input name="picture" type="file" accept="image/*"></label>
            <p class="error" role="alert"></p>
            <div class="actions"><button type="submit">${t("Save")}</button><button type="button" class="secondary" data-close>${t("Cancel")}</button></div>
        </form>
        ${isGuest ? `
        <div class="profile-account">
            <p class="hint">${t("You are playing as a guest. Log in, or create an account to keep your name and icon on any device.")}</p>
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
                <p class="hint">${t("Your account gets the display name and icon chosen above.")}</p>
                <p class="error" role="alert"></p>
                <div class="actions"><button type="submit">${t("Create account")}</button></div>
            </form>
            <div class="login-providers"><p class="hint">${t("Or")}</p></div>
        </div>` : `
        <div class="actions profile-logout"><button type="button" class="secondary small" data-logout>${t("Log out")}</button></div>`}`);
    const form = dialog.querySelector("[data-profile]");
    const error = form.querySelector(".error");
    form.elements.name.value = account.name;
    form.querySelector("[data-welcome]").hidden = !isNewExternalUser;
    const picker = iconPicker(form.querySelector(".icon-picker"), icons, account.icon);

    form.elements.picture.addEventListener("change", async () => {
        const file = form.elements.picture.files[0];
        if (file) {
            await withBusyButton(form.querySelector('[type="submit"]'), t("Uploading…"), () => uploadIcon(file, error));
        }
    });
    form.addEventListener("submit", (event) => {
        event.preventDefault();
        submitAccountForm(form, t("Saving…"), "/auth/profile", { name: form.elements.name.value, icon: picker.value });
    });
    if (isGuest) {
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

// The profile dialog's login part, for guests: Log in and Create account tabs, and a button per external provider.
// `profile` gives the display name and icon for a new account.
function setUpLogin(dialog, profile) {
    const providers = dialog.querySelector(".login-providers");
    providers.hidden = account.providers.length === 0;
    for (const provider of account.providers) {
        const link = document.createElement("a");
        link.className = "button secondary";
        link.href = externalSignInUrl(provider);
        link.textContent = t("Sign in with {provider}", { provider });
        // The dialog stays open while the browser goes to the provider; the link shows a spinner meanwhile.
        link.addEventListener("click", (event) => {
            if (link.classList.contains("is-busy")) {
                event.preventDefault();
                return;
            }
            sessionStorage.setItem(PREVIOUS_TOKEN_KEY, token);
            link.classList.add("is-busy");
            link.setAttribute("aria-disabled", "true");
            link.textContent = t("Connecting to {provider}…", { provider });
        });
        // Coming back with the browser's Back button can restore the page with the spinner still showing.
        window.addEventListener("pageshow", () => {
            link.classList.remove("is-busy");
            link.removeAttribute("aria-disabled");
            link.textContent = t("Sign in with {provider}", { provider });
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
// Hearts and diamonds are red. "JOKER" is a joker. Without text, a face-down card.
function cardElement(text) {
    const card = el("span", "card");
    if (!text) {
        card.classList.add("empty");
        return card;
    }
    if (text === "JOKER") {
        card.classList.add("joker");
        card.setAttribute("role", "img");
        card.setAttribute("aria-label", t("Joker"));
        card.append(el("span", "joker-face", "🃏"), el("span", "joker-word", "JOKER"));
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

// A list item showing text, or other elements (e.g. from seatLabel).
function listItem(...content) {
    const item = document.createElement("li");
    item.append(...content);
    return item;
}

// A seat's name as shown on the game screen. An Away holder is marked "(away)"; a reconnecting one gets a spinner
// (see seatLabel).
function seatName(state, seat, view) {
    const info = state.seats[seat];
    if (!info) {
        return t("Empty seat");
    }
    return info.name + (view.isMe(seat) ? ` ${t("(you)")}` : "") + (info.status === "Away" ? ` ${t("(away)")}` : "");
}

// A small spinner for a seat whose holder is reconnecting.
function reconnectingSpinner() {
    const spinner = el("span", "seat-spinner");
    spinner.title = t("Reconnecting…");
    spinner.setAttribute("role", "img");
    spinner.setAttribute("aria-label", t("Reconnecting…"));
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
    if (info?.status === "Reconnecting") {
        label.append(reconnectingSpinner());
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

/**
 * Runs a session page. The page URL is <page>?session=CODE. Anyone can watch; logged-in users can take seats.
 * While the session is in its lobby, the shared session lobby (#session-lobby) is shown; while a game is being
 * played, the game screen (#game-screen) is shown and render(game, state, view) is called on every update.
 * view.mySeats lists the seats this tab's user holds; view.isMe(seat) tells whether they hold a seat.
 * view.private is this user's private game information (e.g. their cards), for games that have it, or null.
 * The session bar shows the user's role ("You: …"): seatRole(seat, state) gives a seat's name (by default "Seat 2"), or
 * "Watching" for an observer. While a game is on, the status bar under it shows #status and the absent players' banner.
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

    const joinUrl = `${location.origin}/join/${sessionId}`;
    $("session-code").textContent = sessionId;
    $("join-url").href = joinUrl;
    $("join-url").textContent = joinUrl;

    const act = (method, ...args) => call(method, sessionId, ...args).catch(() => {});

    // The one empty seat shown in the lobby: the next available one, or -1 if there is none.
    const nextSeat = (state) => state.seats.findIndex((s) => !s);

    // In the status bar: a banner while the game waits for absent players, or has absent seats that can be taken, with
    // Take this seat for logged-in users.
    const banner = el("div", "absent-banner");
    banner.hidden = true;
    banner.setAttribute("role", "status");
    $("status-bar").append(banner);

    // The session bar's menu (☰), during a game: the invite link to copy or share, how many players are seated, for
    // players, Leave my seat and End game for everyone, and Send feedback when the server can post comments.
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

    function updateBanner(state) {
        const absent = state.phase === "Paused" || state.phase === "Playing" ? state.absentSeats : [];
        banner.hidden = absent.length === 0;
        if (banner.hidden) {
            return;
        }
        // Away players by name, then the empty seats as a count.
        const away = absent.filter((seat) => state.seats[seat]).map((seat) => state.seats[seat].name);
        const empty = absent.length - away.length;
        const names = [...away, ...(empty === 1 ? [t("1 empty seat")] : empty > 1 ? [t("{n} empty seats", { n: empty })] : [])].join(", ");
        const text = state.phase === "Paused" ? t("Game paused: {names}", { names }) : t("Free seats: {names}", { names });
        // Anyone can take a seat; a player takes it as a second seat. Nobody can in a public game.
        const take = el("button", "small", view.isPlayer ? t("Take additional seat") : t("Take this seat"));
        take.type = "button";
        take.hidden = state.isPublic;
        take.addEventListener("click", () => act("TakeSeat"));
        banner.replaceChildren(el("span", null, text), take);
    }

    // The session bar's role ("You: ⚪ White", or "👁 Watching") and, in its menu, how many players are seated.
    function updateSessionInfo(state) {
        const role = view.isPlayer
            ? t("You: {seats}", { seats: view.mySeats.map((seat) => seatRole(seat, state)).join(", ") })
            : `👁 ${t("Watching")}`;
        $("session-info").replaceChildren(el("span", "session-fact", role));
        seatedCount.textContent = t("{taken} of {total} seated",
            { taken: state.seats.filter(Boolean).length, total: state.seats.length });
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

    // A set option's value lists the choices turned off, separated by commas.
    const turnedOff = (value) => (value ?? "").split(",").filter(Boolean);
    // Whether an option applies, given the chosen values (e.g. card options only in Advanced mode).
    const applies = (option, values) => !option.shownWhen || values[option.shownWhen.key] === option.shownWhen.value;

    // The chosen options in a few words, e.g. "Mode: Advanced · Challenge cards: 5 of 6".
    function optionsSummary(state) {
        return state.optionList
            .filter((option) => applies(option, state.options))
            .map((option) => {
                const label = translateServerText(option.label);
                if (option.kind === "Set") {
                    const on = option.choices.length - turnedOff(state.options[option.key]).length;
                    return `${label}: ${on === option.choices.length ? t("all") : t("{on} of {total}", { on, total: option.choices.length })}`;
                }
                return `${label}: ${translateServerText(option.choices.find((c) => c.value === state.options[option.key])?.label) ?? ""}`;
            })
            .join(" · ");
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
                form.querySelector(`fieldset[data-key="${option.key}"]`).hidden = !applies(option, values);
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
        $("status-bar").hidden = inLobby;
        $("invite").hidden = !inLobby || state.isPublic;
        $("session-code").closest(".code").hidden = state.isPublic;
        inviteRow.hidden = state.isPublic;
        updateSessionInfo(state);
        menuButton.hidden = inLobby;
        if (inLobby) showMenu(false);
        standUp.textContent = state.isPublic ? t("Leave game") : t("Leave my seat (watch)");
        standUp.hidden = inLobby || !view.isPlayer;
        endGame.hidden = inLobby || !view.isPlayer || state.phase === "Over";

        if (inLobby) {
            $("lobby-status").textContent = [translateServerText(state.notice), lobbyStatus(state, taken)].filter(Boolean).join(" ");
            $("seat-count").textContent = `${taken} / ${state.seats.length}`;
            const next = nextSeat(state);
            $("seats").replaceChildren(...state.seats.flatMap((info, seat) => (info || seat === next ? [seatItem(state, seat)] : [])));
            $("lobby-options").hidden = state.optionList.length === 0;
            $("options-summary").textContent = `${t("Game options:")} ${optionsSummary(state)}`;
            $("start-game").hidden = !(view.isPlayer && taken >= state.minSeats && (!state.isPublic || isHost(state)));
            $("start-game").textContent = t(state.gameNumber > 0 ? "New game" : "Start");
            return;
        }

        $("play-again").hidden = $("to-lobby").hidden = !(view.isPlayer && state.phase === "Over");
        updateBanner(state);
        render(state.game, state, view);
        // Games mark their status with 👉 (the user has to act) or ⏳ (waiting for others). The marks only choose the
        // bar's colour (gold or grey) and are not shown.
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
