"use strict";

// Main screen: start a new game (logged-in users only; others are asked to log in) or join one by code,
// then go to that game's page. A game clicked while logged out is remembered in this tab and started once the
// user has logged in (the page reloads after logging in, or returns here after Google sign-in).

const PENDING_GAME_KEY = "pendingGame";

function goToSession(gameType, sessionId) {
    location.href = `${GAME_PAGES[gameType]}?session=${sessionId}`;
}

async function findSession(code) {
    const response = await fetch(`/api/sessions/${encodeURIComponent(code)}`).catch(() => null);
    if (response?.status === 404) {
        showError(`No game with code ${code.toUpperCase()} was found.`);
        return null;
    }
    if (!response?.ok) {
        showError(response ? "Something went wrong." : "Could not reach the server. Check your connection and try again.");
        return null;
    }
    return response.json();
}

// Creates a game and opens its page. The overlay stays up until the game's page has loaded.
async function startGame(gameType) {
    showBusy("Starting your game…", 0);
    try {
        const sessionId = await call("CreateSession", gameType);
        goToSession(gameType, sessionId);
    } catch {
        hideBusy();
    }
}

// The order of the games. Logged-in users drag a game by its handle to move it. A saved user's order is kept with
// their account; a guest's in this browser. It is applied whenever the main screen opens. Games not in the order (e.g.
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
        showError((await response?.json().catch(() => null))?.error ?? "The order of the games could not be saved.");
    }
}

// Dragging a game by its handle: the game follows the pointer up and down the list, and the new order is saved when it
// is dropped. Works with a mouse and with touch. The other games move around the dragged one, which itself stays in
// place in the page, so the browser keeps sending it the pointer's movements.
function makeDraggable(tile) {
    const handle = Object.assign(document.createElement("span"), { className: "game-handle", textContent: "⋮⋮", title: "Drag to move" });
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
    button.addEventListener("click", async () => {
        await accountReady;
        if (account.signedIn) {
            startGame(button.dataset.create);
            return;
        }

        // Remember the game while the login dialog is open; closing it without logging in forgets it.
        // Logging in reloads the page (or leaves it for Google sign-in), so the dialog's close event does not fire then.
        sessionStorage.setItem(PENDING_GAME_KEY, button.dataset.create);
        openLoginDialog();
        loginDialog.addEventListener("close", () => sessionStorage.removeItem(PENDING_GAME_KEY), { once: true });
    });
});

// After logging in, start the game the user clicked. A new Google user first sees the profile dialog.
// Until we know whether the user is logged in (the server may be waking up), show that the game is on its way.
if (sessionStorage.getItem(PENDING_GAME_KEY)) {
    showBusy("Starting your game…", 0);
}

accountReady.then(() => {
    const pending = sessionStorage.getItem(PENDING_GAME_KEY);
    sessionStorage.removeItem(PENDING_GAME_KEY);
    hideBusy();
    if (!pending || !account.signedIn) {
        return;
    }

    const openDialog = document.querySelector("dialog[open]");
    if (openDialog) {
        // Saving the profile reloads the page, so keep the game for that; closing the dialog starts it now.
        sessionStorage.setItem(PENDING_GAME_KEY, pending);
        openDialog.addEventListener("close", () => {
            sessionStorage.removeItem(PENDING_GAME_KEY);
            startGame(pending);
        }, { once: true });
    } else {
        startGame(pending);
    }
});

$("join-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const code = $("code").value.trim();
    if (!code) {
        showError("Enter a game code.");
        return;
    }
    showBusy("Finding the game…");
    const session = await findSession(code);
    if (session) {
        goToSession(session.gameType, session.sessionId);
    } else {
        hideBusy();
    }
});

if (params.get("session")) {
    $("code").value = params.get("session");
}
if (params.get("notfound")) {
    showError(`No game with code ${params.get("session") ?? ""} was found. It may have ended.`);
}
if (params.get("signin") === "failed") {
    showError("Sign-in did not complete. Please try again.");
}
