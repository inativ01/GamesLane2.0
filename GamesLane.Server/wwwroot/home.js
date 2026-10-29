"use strict";

// Main screen: start a new game (logged-in users only; others are asked to log in) or join one by code,
// then go to that game's page. A game clicked while logged out is remembered in this tab and started once the
// user has logged in (the page reloads after logging in, or returns here after Google sign-in).

const PENDING_GAME_KEY = "pendingGame";

function goToSession(gameType, sessionId) {
    location.href = `${GAME_PAGES[gameType]}?session=${sessionId}`;
}

async function findSession(code) {
    const response = await fetch(`/api/sessions/${encodeURIComponent(code)}`);
    if (response.status === 404) {
        showError(`No game with code ${code.toUpperCase()} was found.`);
        return null;
    }
    if (!response.ok) {
        showError("Something went wrong.");
        return null;
    }
    return response.json();
}

async function startGame(gameType) {
    try {
        const sessionId = await call("CreateSession", gameType);
        goToSession(gameType, sessionId);
    } catch {
        // Error already shown.
    }
}

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
accountReady.then(() => {
    const pending = sessionStorage.getItem(PENDING_GAME_KEY);
    sessionStorage.removeItem(PENDING_GAME_KEY);
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
    const session = await findSession(code);
    if (session) {
        goToSession(session.gameType, session.sessionId);
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
