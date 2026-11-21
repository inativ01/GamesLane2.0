"use strict";

// Main screen: a saved user's Saved games (when they have any), public games (create one, or join a random one waiting
// for players) and private games (create one, or join one by its code), then go to that game's page. Choosing to create
// or join a game shows the list of games.

function goToSession(gameType, sessionId) {
    location.href = `${GAME_PAGES[gameType]}?session=${sessionId}`;
}

async function findSession(code) {
    const response = await fetch(`/api/sessions/${encodeURIComponent(code)}`).catch(() => null);
    if (response?.status === 404) {
        showError(t("No game with code {code} was found.", { code: code.toUpperCase() }));
        return null;
    }
    if (!response?.ok) {
        showError(t(response ? "Something went wrong." : "Could not reach the server. Check your connection and try again."));
        return null;
    }
    return response.json();
}

// Creates a game and opens its page. The overlay stays up until the game's page has loaded.
async function startGame(gameType, isPublic) {
    showBusy(t("Starting your game…"), 0);
    try {
        const sessionId = await call("CreateSession", gameType, isPublic);
        goToSession(gameType, sessionId);
    } catch {
        hideBusy();
    }
}

// The name of a game, as shown in the list.
const gameTitle = (gameType) => document.querySelector(`[data-create="${gameType}"] .game-title`)?.textContent ?? gameType;

// Joins a random public game of this type that is waiting for players. If there is none, offers to create one.
async function joinPublicGame(gameType) {
    showBusy(t("Finding a game…"), 0);
    let sessionId;
    try {
        sessionId = await call("JoinPublicGame", gameType);
    } catch {
        hideBusy();
        return;
    }
    if (sessionId) {
        goToSession(gameType, sessionId);
        return;
    }
    hideBusy();

    const dialog = createDialog(`
        <h2>${t("No game to join")}</h2>
        <p></p>
        <div class="actions">
            <button type="button" data-create-new>${t("Create a new game")}</button>
            <button type="button" class="secondary" data-close>${t("Back to the main menu")}</button>
        </div>`);
    dialog.querySelector("p").textContent = t("No public {game} game is waiting for players right now.", { game: gameTitle(gameType) });
    let creating = false;
    dialog.querySelector("[data-create-new]").addEventListener("click", () => {
        creating = true;
        dialog.close();
        startGame(gameType, true);
    });
    dialog.addEventListener("close", () => {
        dialog.remove();
        if (!creating) backToMenu();
    });
    dialog.showModal();
}

// What choosing a game in the list does, for each button that opens it.
const PICKS = {
    "public-create": { title: "Create a public game", run: (gameType) => startGame(gameType, true) },
    "public-join": { title: "Join a public game", run: joinPublicGame },
    "private-create": { title: "Create a private game", run: (gameType) => startGame(gameType, false) },
};
let pick = null;
let hasSavedGames = false;

// Shows the main menu, or the list of games for one of PICKS. The list is a step in the browser's history, so the
// Back button returns to the menu.
function showView(name) {
    pick = PICKS[name] ?? null;
    $("menu").hidden = Boolean(pick);
    $("saved-games").hidden = Boolean(pick) || !hasSavedGames;
    $("picker").hidden = !pick;
    if (pick) {
        $("picker-title").textContent = t(pick.title);
    }
}

function openPicker(name) {
    history.pushState({ pick: name }, "");
    showView(name);
}

function backToMenu() {
    if (history.state?.pick) {
        history.back();
    } else {
        showView(null);
    }
}

addEventListener("popstate", () => showView(history.state?.pick));
showView(history.state?.pick);
document.querySelectorAll("[data-pick]").forEach((button) => {
    button.addEventListener("click", () => openPicker(button.dataset.pick));
});
$("picker-back").addEventListener("click", backToMenu);

// The order of the games, the same whichever button opened the list. Users drag a game by its handle to move it. A
// saved user's order is kept with their account; a guest's in this browser. It is applied whenever the main screen opens.
// Games not in the order (all of them for users who never arranged them, and new ones) follow, most played in the last
// 30 days first (from /api/games/popular; ties, or without it, in their usual order). Alt+Up and Alt+Down move the
// focused game with the keyboard.
const GAME_ORDER_KEY = "gameOrder";
const gameList = $("game-list");
const gameTiles = () => [...gameList.querySelectorAll("[data-create]")];
const currentOrder = () => gameTiles().map((tile) => tile.dataset.create);
const usualOrder = currentOrder();
const isSavedUser = () => account.signedIn && account.kind !== "Guest";

async function popularOrder() {
    const response = await fetch("/api/games/popular").catch(() => null);
    const order = response?.ok ? await response.json().catch(() => null) : null;
    return Array.isArray(order) ? order : usualOrder;
}

function applyGameOrder(order, rest) {
    const after = (type) => (rest.includes(type) ? rest.indexOf(type) : rest.length + usualOrder.indexOf(type));
    const rank = (type) => (order.includes(type) ? order.indexOf(type) : order.length + after(type));
    gameTiles()
        .sort((a, b) => rank(a.dataset.create) - rank(b.dataset.create))
        .forEach((tile) => gameList.appendChild(tile));
}

async function saveGameOrder() {
    if (!isSavedUser()) {
        localStorage.setItem(GAME_ORDER_KEY, JSON.stringify(currentOrder()));
        return;
    }
    const response = await authFetch("/api/game-order", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(currentOrder()),
    }).catch(() => null);
    if (!response?.ok) {
        showError(translateServerText((await response?.json().catch(() => null))?.error) ?? t("The order of the games could not be saved."));
    }
}

// Dragging a game by its handle: the game follows the pointer up and down the list, and the new order is saved when it
// is dropped. Works with a mouse and with touch. The other games move around the dragged one, which itself stays in
// place in the page, so the browser keeps sending it the pointer's movements.
function makeDraggable(tile) {
    const handle = Object.assign(document.createElement("span"), { className: "game-handle", textContent: "⋮⋮", title: t("Drag to move") });
    handle.setAttribute("aria-hidden", "true");
    tile.prepend(handle);
    handle.addEventListener("click", (event) => {
        event.preventDefault();
        event.stopPropagation();
    });
    handle.addEventListener("pointerdown", (event) => {
        event.preventDefault();
        handle.setPointerCapture(event.pointerId);
        const before = currentOrder().join();
        tile.classList.add("dragging");
        const middle = (t) => {
            const box = t.getBoundingClientRect();
            return box.top + box.height / 2;
        };
        const move = (e) => {
            if (e.pointerId !== event.pointerId) return;
            let other;
            while ((other = tile.previousElementSibling) && e.clientY < middle(other)) {
                gameList.insertBefore(other, tile.nextElementSibling);
            }
            while ((other = tile.nextElementSibling) && e.clientY > middle(other)) {
                gameList.insertBefore(other, tile);
            }
        };
        const drop = (e) => {
            if (e.pointerId !== event.pointerId) return;
            removeEventListener("pointermove", move);
            removeEventListener("pointerup", drop);
            removeEventListener("pointercancel", drop);
            tile.classList.remove("dragging");
            if (currentOrder().join() !== before) {
                saveGameOrder();
            }
        };
        addEventListener("pointermove", move);
        addEventListener("pointerup", drop);
        addEventListener("pointercancel", drop);
    });
    tile.addEventListener("keydown", (event) => {
        if (!event.altKey || (event.key !== "ArrowUp" && event.key !== "ArrowDown")) return;
        event.preventDefault();
        const other = event.key === "ArrowUp" ? tile.previousElementSibling : tile.nextElementSibling;
        if (!other) return;
        gameList.insertBefore(event.key === "ArrowUp" ? tile : other, event.key === "ArrowUp" ? other : tile);
        tile.focus();
        saveGameOrder();
    });
}

accountReady.then(async () => {
    const saved = isSavedUser() ? account.gameOrder : JSON.parse(localStorage.getItem(GAME_ORDER_KEY) ?? "null");
    const order = Array.isArray(saved) ? saved : [];
    if (order.length < usualOrder.length) {
        applyGameOrder(order, await popularOrder());
    } else {
        applyGameOrder(order, usualOrder);
    }
    if (account.signedIn) {
        gameTiles().forEach(makeDraggable);
        gameList.classList.add("arrangeable");
        $("arrange-hint").hidden = false;
    }
});

document.querySelectorAll("[data-create]").forEach((button) => {
    button.addEventListener("click", () => pick?.run(button.dataset.create));
});

const SAVED_PHASES = { Lobby: "In the lobby", Playing: "Game in progress", Over: "Game over" };

// The Saved games panel, first on the main screen when the user has saved games: how many of their limit (from the
// server, the user's own) they use, then each game with its code, state ("Waiting for you" when it waits for them, see
// WaitingGames), players and, in its last 30 days, when it will be deleted; with Open, and Remove. A game that can no
// longer be opened has only Remove.
function showSavedGames(games, limit, waitingIds) {
    hasSavedGames = games.length > 0;
    $("saved-games").hidden = Boolean(pick) || !hasSavedGames;
    $("saved-count").textContent = t("{count} of {limit}", { count: games.filter((game) => game.canOpen).length, limit });
    $("saved-list").replaceChildren(...games.map((game) => {
        const details = game.canOpen
            ? [t(waitingIds.has(game.sessionId) ? "Waiting for you" : SAVED_PHASES[game.phase]), game.players.join(", ")]
            : [t("This game can no longer be opened (GamesLane was updated).")];
        const info = el("span", "saved-info",
            el("strong", null, `${game.gameType ? gameTitle(game.gameType) : ""} · ${game.sessionId}`),
            el("span", "saved-details", details.filter(Boolean).join(" · ")));
        if (game.daysLeft) {
            info.append(el("span", "saved-expiry", game.daysLeft === 1
                ? t("This game will be deleted in 1 day.")
                : t("This game will be deleted in {days} days.", { days: game.daysLeft })));
        }
        const row = el("li", "saved-game", info);
        if (game.canOpen) {
            const open = el("button", "small", t("Open game"));
            open.type = "button";
            open.addEventListener("click", () => goToSession(game.gameType, game.sessionId));
            row.append(open);
        }
        const remove = el("button", "secondary small", t("Remove"));
        remove.type = "button";
        remove.addEventListener("click", async () => {
            try {
                await call("RemoveSavedGame", game.sessionId);
            } catch {
                return;
            }
            showSavedGames(games.filter((other) => other !== game), limit, waitingIds);
        });
        row.append(remove);
        return row;
    }));
}

// A user coming back to the main screen returns at once to a public game in progress that waits for them. Their saved
// games are shown first (see showSavedGames); other games with family and friends that wait for them are offered, with
// Return and Leave.
ready.then(async (ok) => {
    if (!ok) return;
    const [games, saved] = await Promise.all([
        connection.invoke("WaitingGames").catch(() => []),
        accountReady.then(() => (isSavedUser() ? connection.invoke("SavedGames").catch(() => null) : null)),
    ]);
    const publicGame = games.find((game) => game.isPublic);
    if (publicGame) {
        showBusy(t("Returning to your game…"), 0);
        goToSession(publicGame.gameType, publicGame.sessionId);
        return;
    }
    const savedGames = saved?.games ?? [];
    const savedIds = new Set(savedGames.map((game) => game.sessionId));
    showSavedGames(savedGames, saved?.limit ?? 0, new Set(games.map((game) => game.sessionId)));
    const others = games.filter((game) => !savedIds.has(game.sessionId));
    const box = $("waiting-games");
    box.replaceChildren(...others.map((game) => {
        const back = el("button", "small", t("Return"));
        back.type = "button";
        back.addEventListener("click", () => goToSession(game.gameType, game.sessionId));
        const leave = el("button", "secondary small", t("Leave"));
        leave.type = "button";
        const row = el("div", "waiting-game", el("span", null, t("You have a game in progress: {game}", { game: gameTitle(game.gameType) })), back, leave);
        leave.addEventListener("click", async () => {
            await call("LeaveSession", game.sessionId).catch(() => {});
            row.remove();
            box.hidden = box.children.length === 0;
        });
        return row;
    }));
    box.hidden = others.length === 0;
});

$("join-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const code = $("code").value.trim();
    if (!code) {
        showError(t("Enter a game code."));
        return;
    }
    showBusy(t("Finding the game…"));
    const session = await findSession(code);
    if (session) {
        goToSession(session.gameType, session.sessionId);
    } else {
        hideBusy();
    }
});

// The offer to install GamesLane as an app (see manifest.webmanifest), under the main menu. Browsers that can install
// it themselves (Chrome and Edge, on Android and on computers) tell the page so, and Install app opens their install
// dialog. On iPhone and iPad, only Safari's Share menu can install it, so Install app shows how. Not offered in the
// installed app (or the iPhone and Android apps, GamesLane.Mobile), once it is installed, or after Not now (remembered in this browser).
const INSTALL_DISMISSED_KEY = "installDismissed";
const isInstalledApp = matchMedia("(display-mode: standalone)").matches || navigator.standalone === true
    || window.Capacitor?.isNativePlatform?.() === true;
const isAppleMobile = /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
let installPrompt = null;

function offerInstall(offered) {
    $("install-app").hidden = !offered || isInstalledApp || localStorage.getItem(INSTALL_DISMISSED_KEY) !== null;
}

function showAppleInstallSteps() {
    const dialog = createDialog(`
        <h2>${t("Install GamesLane")}</h2>
        <ol class="install-steps">
            <li>${t("Tap the Share button (the square with an arrow).")}</li>
            <li>${t("Scroll down and tap Add to Home Screen.")}</li>
            <li>${t("Tap Add.")}</li>
        </ol>
        <div class="actions"><button type="button" data-close>${t("Got it")}</button></div>`);
    dialog.addEventListener("close", () => dialog.remove());
    dialog.showModal();
}

addEventListener("beforeinstallprompt", (event) => {
    event.preventDefault();
    installPrompt = event;
    offerInstall(true);
});
addEventListener("appinstalled", () => {
    installPrompt = null;
    offerInstall(false);
});
$("install-app-button").addEventListener("click", async () => {
    if (!installPrompt) {
        showAppleInstallSteps();
        return;
    }
    // The browser's dialog can be opened only once per offer; the browser offers again later if it was declined.
    const prompt = installPrompt;
    installPrompt = null;
    offerInstall(false);
    await prompt.prompt();
});
$("install-app-later").addEventListener("click", () => {
    localStorage.setItem(INSTALL_DISMISSED_KEY, "1");
    offerInstall(false);
});
offerInstall(isAppleMobile);

// The privacy policy in the chosen language: privacy.html in English, privacy.<code>.html in the others.
$("privacy-link").href = language === LANGUAGES[0] ? "/privacy.html" : `/privacy.${language.code}.html`;

if (params.get("session")) {
    $("code").value = params.get("session");
}
if (params.get("notfound")) {
    showError(t("No game with code {code} was found. It may have ended.", { code: params.get("session") ?? "" }));
}
if (params.get("signin") === "failed") {
    showError(t("Sign-in did not complete. Please try again."));
}
if (params.get("left")) {
    showError(t("You are no longer in this game."));
}
