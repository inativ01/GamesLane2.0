"use strict";

// Main screen: create a game or join one by code, then go to that game's page.

function goToGame(gameType, gameId, mode) {
    const extra = mode === "player" ? "&autojoin=1" : mode === "spectator" ? "&role=spectator" : "";
    location.href = `${GAME_PAGES[gameType]}?game=${gameId}${extra}`;
}

async function findGame(code) {
    const response = await fetch(`/api/games/${encodeURIComponent(code)}`);
    if (response.status === 404) {
        showError(`Game ${code.toUpperCase()} was not found.`);
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
        const asPlayer = button.dataset.role === "player";
        if (asPlayer && !requireName($("name"))) {
            return;
        }

        try {
            const gameId = await call("CreateGame", button.dataset.create);
            goToGame(button.dataset.create, gameId, asPlayer ? "player" : "spectator");
        } catch {
            // Error already shown.
        }
    });
});

$("join-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const code = $("code").value.trim();
    if (!code) {
        showError("Enter a game code.");
        return;
    }
    if (!requireName($("name"))) {
        return;
    }
    const game = await findGame(code);
    if (game) {
        goToGame(game.gameType, game.gameId, "player");
    }
});

$("watch").addEventListener("click", async () => {
    const code = $("code").value.trim();
    if (!code) {
        showError("Enter a game code to watch.");
        return;
    }
    const game = await findGame(code);
    if (game) {
        goToGame(game.gameType, game.gameId, null);
    }
});

$("name").value = savedName();
setUpAccount().then((account) => {
    if (account?.signedIn && !$("name").value) {
        $("name").value = account.name ?? "";
    }
});

if (params.get("game")) {
    $("code").value = params.get("game");
}
if (params.get("notfound")) {
    showError(`Game ${params.get("game") ?? ""} was not found. It may have ended when the server restarted.`);
}
if (params.get("signin") === "failed") {
    showError("Sign-in did not complete. Please try again.");
}
