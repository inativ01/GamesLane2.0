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

// The order of the games. Saved users can arrange it (Arrange: move games up and down); it is saved with their account
// and applied whenever they open the main screen. Games not in a saved order keep their usual place after the others.
const gameList = $("game-list");
const gameTiles = () => [...gameList.querySelectorAll("[data-create]")];
const usualOrder = gameTiles().map((tile) => tile.dataset.create);

function applyGameOrder(order) {
    const rank = (type) => (order.includes(type) ? order.indexOf(type) : order.length + usualOrder.indexOf(type));
    gameTiles()
        .sort((a, b) => rank(a.dataset.create) - rank(b.dataset.create))
        .forEach((tile) => gameList.appendChild(tile));
}

async function saveGameOrder() {
    const response = await authFetch("/api/game-order", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(gameTiles().map((tile) => tile.dataset.create)),
    }).catch(() => null);
    if (!response?.ok) {
        showError((await response?.json().catch(() => null))?.error ?? "The order of the games could not be saved.");
    }
}

// The list shown while arranging: each game's picture and name, with buttons to move it up or down.
function renderArrangeList() {
    const tiles = gameTiles();
    $("arrange-list").replaceChildren(...tiles.map((tile, i) => {
        const move = (delta, label, symbol) => {
            const button = smallButton(symbol, () => {
                const other = tiles[i + delta];
                gameList.insertBefore(delta < 0 ? tile : other, delta < 0 ? other : tile);
                renderArrangeList();
                saveGameOrder();
            });
            button.disabled = !tiles[i + delta];
            button.setAttribute("aria-label", `${label}: ${tile.querySelector(".game-title").textContent}`);
            return button;
        };
        const item = listItem(
            Object.assign(document.createElement("img"), { src: tile.querySelector("img").src, alt: "" }),
            Object.assign(document.createElement("span"), { className: "arrange-name", textContent: tile.querySelector(".game-title").textContent }),
            move(-1, "Move up", "▲"),
            move(1, "Move down", "▼"));
        return item;
    }));
}

$("arrange-games").addEventListener("click", () => {
    const arranging = $("arrange-list").hidden;
    $("arrange-list").hidden = !arranging;
    gameList.hidden = arranging;
    $("arrange-games").textContent = arranging ? "Done" : "Arrange";
    if (arranging) {
        renderArrangeList();
    }
});

accountReady.then(() => {
    if (account.gameOrder) {
        applyGameOrder(account.gameOrder);
    }
    $("arrange-games").hidden = !(account.signedIn && account.kind !== "Guest");
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
