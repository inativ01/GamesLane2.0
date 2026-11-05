"use strict";

// Main screen: public games (create one, or join a random one waiting for players) and private games (create one, or
// join one by its code), then go to that game's page. Choosing to create or join a game shows the list of games.

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

// Shows the main menu, or the list of games for one of PICKS. The list is a step in the browser's history, so the
// Back button returns to the menu.
function showView(name) {
    pick = PICKS[name] ?? null;
    $("menu").hidden = Boolean(pick);
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
// saved user's order is kept with their account; a guest's in this browser. It is applied whenever the main screen opens. Games not in the order (e.g.
// new ones) keep their usual place after the others. Alt+Up and Alt+Down move the focused game with the keyboard.
const GAME_ORDER_KEY = "gameOrder";
const gameList = $("game-list");
const gameTiles = () => [...gameList.querySelectorAll("[data-create]")];
const currentOrder = () => gameTiles().map((tile) => tile.dataset.create);
const usualOrder = currentOrder();
const isSavedUser = () => account.signedIn && account.kind !== "Guest";

function applyGameOrder(order) {
    const rank = (type) => (order.includes(type) ? order.indexOf(type) : order.length + usualOrder.indexOf(type));
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

accountReady.then(() => {
    const order = isSavedUser() ? account.gameOrder : JSON.parse(localStorage.getItem(GAME_ORDER_KEY) ?? "null");
    if (Array.isArray(order)) {
        applyGameOrder(order);
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

// A user coming back to the main screen returns at once to a public game in progress that waits for them. Games with
// family and friends that wait for them are offered, with Return and Leave.
ready.then(async (ok) => {
    if (!ok) return;
    const games = await connection.invoke("WaitingGames").catch(() => []);
    const publicGame = games.find((game) => game.isPublic);
    if (publicGame) {
        showBusy(t("Returning to your game…"), 0);
        goToSession(publicGame.gameType, publicGame.sessionId);
        return;
    }
    const box = $("waiting-games");
    box.replaceChildren(...games.map((game) => {
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
    box.hidden = games.length === 0;
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
