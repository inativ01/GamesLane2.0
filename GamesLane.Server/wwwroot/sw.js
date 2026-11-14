"use strict";

// The service worker that makes GamesLane installable as an app. Games are played live, so every page and file comes
// from the server as usual; nothing is cached. Only when a page cannot be reached (no connection) does it show a short
// page saying so, in the device's language, with a button to try again.

const OFFLINE_TEXTS = {
    en: { title: "You are offline", text: "GamesLane needs an internet connection.", retry: "Try again" },
    he: { title: "אין חיבור לאינטרנט", text: "GamesLane צריך חיבור לאינטרנט.", retry: "לנסות שוב", rtl: true },
    es: { title: "Sin conexión", text: "GamesLane necesita conexión a internet.", retry: "Intentar de nuevo" },
    fr: { title: "Pas de connexion", text: "GamesLane a besoin d'une connexion internet.", retry: "Réessayer" },
    pt: { title: "Sem conexão", text: "O GamesLane precisa de conexão com a internet.", retry: "Tentar de novo" },
    "zh-CN": { title: "您已离线", text: "GamesLane 需要连接互联网。", retry: "重试" },
    "zh-TW": { title: "您已離線", text: "GamesLane 需要連上網際網路。", retry: "重試" },
};

// The texts for the device's first language that has them, chosen as languageCode in i18n.js does (the user's choice
// of language on the site is not known here).
function offlineTexts() {
    for (const tag of self.navigator.languages ?? [self.navigator.language ?? "en"]) {
        const parts = tag.toLowerCase().split("-");
        const traditional = !parts.includes("hans") && parts.some((part) => ["hant", "tw", "hk", "mo"].includes(part));
        const texts = OFFLINE_TEXTS[parts[0] !== "zh" ? parts[0] : traditional ? "zh-TW" : "zh-CN"];
        if (texts) return texts;
    }
    return OFFLINE_TEXTS.en;
}

function offlinePage() {
    const { title, text, retry, rtl } = offlineTexts();
    const html = `<!DOCTYPE html>
<html dir="${rtl ? "rtl" : "ltr"}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="theme-color" content="#19285d"><title>GamesLane</title>
<style>body{margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;background:#f5f1ea;
color:#222;font-family:system-ui,sans-serif;text-align:center}main{padding:1.5rem}h1{font-size:1.4rem;color:#9f4413}
button{margin-top:1rem;padding:0.7rem 1.4rem;border:0;border-radius:8px;background:#c2561a;color:#fff;font-size:1rem}</style>
</head><body><main><h1>${title}</h1><p>${text}</p><button type="button" onclick="location.reload()">${retry}</button></main></body></html>`;
    return new Response(html, { status: 503, headers: { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store" } });
}

self.addEventListener("install", () => self.skipWaiting());
self.addEventListener("activate", (event) => event.waitUntil(self.clients.claim()));

self.addEventListener("fetch", (event) => {
    if (event.request.mode !== "navigate") return;
    event.respondWith(fetch(event.request).catch(offlinePage));
});
