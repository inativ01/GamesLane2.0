"use strict";

// Main screen: start a new session (logged-in users only) or join one by code, then go to that game's page.

function goToSession(gameType, sessionId) {
    location.href = `${GAME_PAGES[gameType]}?session=${sessionId}`;
}

async function findSession(code) {
    const response = await fetch(`/api/sessions/${encodeURIComponent(code)}`);
    if (response.status === 404) {
        showError(`Session ${code.toUpperCase()} was not found.`);
        return null;
    }
    if (!response.ok) {
        showError("Something went wrong.");
        return null;
    }
    return response.json();
}

document.querySelectorAll("[data-create]").forEach((button) => {
    button.addEventListener("click", async () => {
        try {
            const sessionId = await call("CreateSession", button.dataset.create);
            goToSession(button.dataset.create, sessionId);
        } catch {
            // Error already shown.
        }
    });
});

$("join-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const code = $("code").value.trim();
    if (!code) {
        showError("Enter a session code.");
        return;
    }
    const session = await findSession(code);
    if (session) {
        goToSession(session.gameType, session.sessionId);
    }
});

accountReady.then(() => {
    document.querySelectorAll("[data-create]").forEach((button) => {
        button.disabled = !account.signedIn;
    });
    $("login-to-start").hidden = account.signedIn;
});

if (params.get("session")) {
    $("code").value = params.get("session");
}
if (params.get("notfound")) {
    showError(`Session ${params.get("session") ?? ""} was not found. It may have ended when the server restarted.`);
}
if (params.get("signin") === "failed") {
    showError("Sign-in did not complete. Please try again.");
}
