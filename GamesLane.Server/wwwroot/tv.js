"use strict";

// The web TV app (tv.html), for TVs with a browser or a web view (Google TV / Android TV, LG, Samsung): the same steps as
// the Apple TV app (TvModel.swift). The TV registers once (POST /api/tv/register, the token kept in this browser),
// connects to the hub as the TV (TvConnect, TvUpdated), shows its pairing code and QR code, then the user it is linked
// to, and the game they open: the game's own page in a frame, as an observer (?display=tv, see tvDisplay in common.js),
// in the user's language. ?name= gives the TV's name (e.g. from the Android TV app); without it the server names it.

const TV_TOKEN_KEY = "tvToken";
// GameSessionStore.PageFor on the server (checked by a test).
const TV_GAME_PAGES = {
    TicTacToe: "/tictactoe.html", PokerHeist: "/poker-heist.html", Backgammon: "/backgammon.html",
    RichManPoorMan: "/rich-man-poor-man.html", Wazoo: "/wazoo.html", FinalCard: "/final-card.html", Yaniv: "/yaniv.html",
    Hearts: "/hearts.html", Spades: "/spades.html", Dominoes: "/dominoes.html",
};
const RETRY_DELAYS = [0, 2000, 5000];
const panel = document.getElementById("tv-panel");
const frame = document.getElementById("tv-game");
const reconnecting = document.getElementById("tv-reconnecting");
let token = localStorage.getItem(TV_TOKEN_KEY);
let status = null;
let connected = false;
let codeTimer = 0;
let gameUrl = "about:blank";
let missingSession = null;

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const unauthorized = (err) => /\b401\b/.test(String(err ?? ""));

// The TV's texts in the language of the user it shows, otherwise in the TV's own (as t() in i18n.js).
function tvLanguage() {
    const code = status?.viewer?.language;
    return offeredLanguages.find((lang) => lang.code === code) ?? language;
}

function say(text, values = {}) {
    const translated = TRANSLATIONS[tvLanguage().code]?.[text] ?? text;
    return translated.replace(/\{(\w+)\}/g, (match, name) => (name in values ? String(values[name]) : match));
}

function node(tag, className, ...children) {
    const element = document.createElement(tag);
    if (className) element.className = className;
    element.append(...children);
    return element;
}

function icon(value) {
    if (!value?.startsWith("/")) return node("span", "tv-icon", value ?? "");
    return Object.assign(node("img", "tv-icon"), { src: value, alt: "" });
}

function render() {
    const lang = tvLanguage();
    document.documentElement.lang = lang.code;
    document.documentElement.dir = lang.rtl ? "rtl" : "ltr";
    reconnecting.textContent = say("Reconnecting…");
    reconnecting.hidden = !status || connected;

    const viewer = status?.viewer;
    const session = viewer?.sessionId && TV_GAME_PAGES[viewer.gameType] && missingSession !== viewer.sessionId ? viewer.sessionId : null;
    const url = session
        ? `${TV_GAME_PAGES[viewer.gameType]}?session=${encodeURIComponent(session)}&display=tv&lang=${encodeURIComponent(lang.code)}`
        : "about:blank";
    if (url !== gameUrl) {
        gameUrl = url;
        // Replaced, not added to the history, so the remote's Back button leaves the app.
        frame.contentWindow.location.replace(url);
    }
    frame.hidden = !session;
    panel.hidden = Boolean(session);

    if (!status) {
        panel.replaceChildren(node("p", "tv-message", say("Connecting…")));
    } else if (!viewer) {
        const pairUrl = `${location.origin}/?tv=${status.code}`;
        const qr = Object.assign(node("img", "tv-qr"), { src: `/api/tv/qr?text=${encodeURIComponent(pairUrl)}`, alt: "" });
        const lines = [
            node("p", "tv-message", say("Scan the QR code with your phone, or enter this code in your GamesLane profile.")),
            node("p", "tv-code", status.code),
            node("p", "tv-muted", location.host),
        ];
        if (status.pairedWith.length > 0) {
            lines.push(node("p", "tv-muted", say("Paired with {names}", { names: status.pairedWith.join(", ") })));
        }
        panel.replaceChildren(qr, node("div", "tv-pairing", ...lines));
    } else if (!session) {
        panel.replaceChildren(icon(viewer.icon), node("p", "tv-message", say("Waiting for {name} to open a game…", { name: viewer.name })));
    }
}

function apply(next) {
    status = next;
    if (missingSession && missingSession !== status.viewer?.sessionId) missingSession = null;
    clearTimeout(codeTimer);
    codeTimer = setTimeout(tvConnect, Math.max(Date.parse(status.codeExpiresAt) - Date.now(), 1000));
    render();
}

async function tvConnect() {
    try {
        apply(await connection.invoke("TvConnect"));
    } catch {
        // The connection was lost: the TV asks again once it is back.
    }
}

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/game", { accessTokenFactory: () => token })
    .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (context) => (unauthorized(context.retryReason) ? null : RETRY_DELAYS[context.previousRetryCount] ?? 10000),
    })
    .withKeepAliveInterval(5000)
    .withServerTimeout(10000)
    .build();

connection.on("TvUpdated", apply);
connection.onreconnecting(() => { connected = false; render(); });
connection.onreconnected(() => { connected = true; tvConnect(); });
let closing = false;
connection.onclose((err) => {
    connected = false;
    render();
    if (!closing) start(unauthorized(err));
});

// Registers the TV if it has no token yet (trying again every 5 seconds), then connects. A token the server refuses is
// forgotten: the TV registers again, and must be paired again.
async function start(refused = false) {
    if (refused) {
        localStorage.removeItem(TV_TOKEN_KEY);
        token = null;
        status = null;
        render();
    }
    while (!token) {
        const response = await fetch("/api/tv/register", {
            method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ name: new URLSearchParams(location.search).get("name") }),
        }).catch(() => null);
        if (response?.ok) {
            token = (await response.json()).accessToken;
            localStorage.setItem(TV_TOKEN_KEY, token);
        } else {
            await sleep(5000);
        }
    }
    try {
        await connection.start();
    } catch (err) {
        if (!unauthorized(err)) await sleep(5000);
        return start(unauthorized(err));
    }
    connected = true;
    await tvConnect();
}

// The game page tells when its game cannot be found: the waiting screen shows until the user opens another.
addEventListener("message", (event) => {
    if (event.origin === location.origin && event.data?.tvGameNotFound && event.data.tvGameNotFound === status?.viewer?.sessionId) {
        missingSession = event.data.tvGameNotFound;
        render();
    }
});

// The screen stays on: in the Android TV app (its plugin, see common.js), and in browsers that can keep it awake.
let wakeLock = null;
async function keepAwake() {
    window.Capacitor?.Plugins?.GamesLane?.keepAwake?.({ on: true }).catch(() => {});
    if (!document.hidden && !wakeLock && navigator.wakeLock) {
        wakeLock = await navigator.wakeLock.request("screen").catch(() => null);
        wakeLock?.addEventListener("release", () => { wakeLock = null; });
    }
}
document.addEventListener("visibilitychange", keepAwake);
window.addEventListener("pagehide", () => {
    closing = true;
    connection.stop();
});

keepAwake();
render();
start();
