"use strict";

// Languages. The texts in the pages and scripts are in English, and they are also the keys of the translations:
// t("Log in") gives the text in the chosen language, or the English text when it has no translation. Parts that vary
// are written {name}, e.g. t("Seat {n}", { n: 2 }). Each language's translations are in lang/<code>.js, which fills
// TRANSLATIONS and is loaded before this script. A page offers the languages whose translations it loads; pages
// without translations stay in English. The choice is remembered in this browser; until the user chooses, the first of
// the browser's preferred languages that the page offers, otherwise English. The locale is the variant of the language
// (e.g. Mexican Spanish), used to choose a voice for reading aloud. Chinese has two languages: Simplified (zh-CN) and
// Traditional (zh-TW), which browsers set to the Traditional script or to Taiwan, Hong Kong or Macau get.

var TRANSLATIONS = window.TRANSLATIONS ?? {};

const LANGUAGES = [
    { code: "en", locale: "en-US", name: "English", flag: "images/flags/us.svg" },
    { code: "he", locale: "he-IL", name: "עברית", flag: "images/flags/il.svg", rtl: true },
    { code: "es", locale: "es-MX", name: "Español", flag: "images/flags/mx.svg" },
    { code: "fr", locale: "fr-FR", name: "Français", flag: "images/flags/fr.svg" },
    { code: "pt", locale: "pt-BR", name: "Português", flag: "images/flags/br.svg" },
    { code: "zh-CN", locale: "zh-CN", name: "简体中文", flag: "images/flags/cn.svg" },
    { code: "zh-TW", locale: "zh-TW", name: "繁體中文", flag: "images/flags/tw.svg" },
];
const LANGUAGE_KEY = "language";
const offeredLanguages = LANGUAGES.filter((lang) => lang.code === "en" || TRANSLATIONS[lang.code]);

// The code in LANGUAGES for a browser's language tag, e.g. "fr" for "fr-CA" and "zh-TW" for "zh-Hant" or "zh-HK".
function languageCode(tag) {
    const parts = tag.toLowerCase().split("-");
    if (parts[0] !== "zh") return parts[0];
    const traditional = !parts.includes("hans") && parts.some((part) => ["hant", "tw", "hk", "mo"].includes(part));
    return traditional ? "zh-TW" : "zh-CN";
}

const language = (() => {
    const browserLanguage = (navigator.languages?.length ? navigator.languages : [navigator.language ?? ""])
        .map(languageCode)
        .find((code) => offeredLanguages.some((lang) => lang.code === code));
    const preferred = localStorage.getItem(LANGUAGE_KEY) ?? browserLanguage;
    return offeredLanguages.find((lang) => lang.code === preferred) ?? LANGUAGES[0];
})();
document.documentElement.lang = language.code;
document.documentElement.dir = language.rtl ? "rtl" : "ltr";

function t(text, values = {}) {
    const translated = TRANSLATIONS[language.code]?.[text] ?? text;
    return translated.replace(/\{(\w+)\}/g, (match, name) => (name in values ? String(values[name]) : match));
}

// A text from the server (an error message, or a game option's name) in the chosen language. A translation with
// varying parts, e.g. "At least {n} players must be seated.", also matches the server's text with any value in them.
// Keys that are mostly varying parts (e.g. "{color}: {dice}") are left out, so they do not match unrelated texts.
let serverPatterns = null;

function translateServerText(text) {
    const table = TRANSLATIONS[language.code];
    if (!text || !table) return text;
    if (table[text]) return table[text];
    serverPatterns ??= Object.keys(table)
        .filter((key) => key.includes("{") && key.replace(/\{\w+\}/g, "").replace(/[^A-Za-z]/g, "").length >= 4)
        .map((key) => {
            const names = [];
            const source = key.replace(/[.*+?^${}()|[\]\\]/g, "\\$&").replace(/\\\{(\w+)\\\}/g, (match, name) => {
                names.push(name);
                return "(.+?)";
            });
            return { key, names, regex: new RegExp(`^${source}$`) };
        });
    for (const { key, names, regex } of serverPatterns) {
        const match = regex.exec(text);
        if (match) return t(key, Object.fromEntries(names.map((name, i) => [name, match[i + 1]])));
    }
    return text;
}

// Translates the page's own texts: the title, the text of elements marked data-i18n, and the attributes listed in
// data-i18n-attr (e.g. "title,aria-label").
function translatePage() {
    document.title = t(document.title);
    document.querySelectorAll("[data-i18n]").forEach((element) => {
        element.textContent = t(element.textContent.trim());
    });
    document.querySelectorAll("[data-i18n-attr]").forEach((element) => {
        element.dataset.i18nAttr.split(",").forEach((name) => element.setAttribute(name, t(element.getAttribute(name))));
    });
}

// The language picker for the header: the current language's flag, which opens a menu of the languages. Choosing one
// reloads the page in it. Null when the page offers only English.
function languagePicker() {
    if (offeredLanguages.length < 2) return null;
    const flag = (lang) => Object.assign(document.createElement("img"), { className: "flag", src: lang.flag, alt: "" });
    const picker = document.createElement("div");
    picker.className = "language";
    const button = document.createElement("button");
    button.type = "button";
    button.className = "language-button";
    button.title = t("Language");
    button.setAttribute("aria-label", t("Language"));
    button.setAttribute("aria-haspopup", "true");
    button.setAttribute("aria-expanded", "false");
    button.append(flag(language));
    const menu = document.createElement("div");
    menu.className = "language-menu";
    menu.hidden = true;
    const show = (open) => {
        menu.hidden = !open;
        button.setAttribute("aria-expanded", String(open));
    };
    menu.append(...offeredLanguages.map((lang) => {
        const choice = document.createElement("button");
        choice.type = "button";
        choice.lang = lang.code;
        choice.setAttribute("aria-pressed", String(lang === language));
        choice.append(flag(lang), lang.name);
        choice.addEventListener("click", () => {
            if (lang === language) {
                show(false);
                return;
            }
            localStorage.setItem(LANGUAGE_KEY, lang.code);
            location.reload();
        });
        return choice;
    }));
    button.addEventListener("click", () => show(menu.hidden));
    document.addEventListener("click", (event) => {
        if (!picker.contains(event.target)) show(false);
    });
    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape") show(false);
    });
    picker.append(button, menu);
    return picker;
}
