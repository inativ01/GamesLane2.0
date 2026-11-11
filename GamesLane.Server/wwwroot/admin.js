"use strict";

// The admin page (English only): the games started per day over the last N days, as a line per game (each game can be
// shown or hidden), and each game's total. Only admins get the report; anyone else is told the page is not available.

const GAME_LABELS = {
    TheGang: "The Gang", Backgammon: "Backgammon", RichManPoorMan: "Rich man, poor man", FinalCard: "Final Card",
    Yaniv: "Yaniv", Hearts: "Hearts", Wazoo: "Wazoo", TicTacToe: "Tic-tac-toe",
};
const GAME_COLORS = {
    TheGang: "#d62828", Backgammon: "#8a5a2b", RichManPoorMan: "#e5a400", FinalCard: "#1d5bd6",
    Yaniv: "#23913a", Hearts: "#e0567a", Wazoo: "#8e44ad", TicTacToe: "#17a2b8",
};
const HIDDEN_KEY = "admin.hiddenGames";
const CHART = { width: 800, height: 320, left: 44, right: 12, top: 12, bottom: 34 };

let report = null;
const hidden = new Set(JSON.parse(localStorage.getItem(HIDDEN_KEY) ?? "[]"));

// The highest value of the y axis and its step: about 5 round steps above the highest count shown.
function yScale(max) {
    const rough = Math.max(1, max) / 5;
    const power = 10 ** Math.floor(Math.log10(rough));
    const step = [1, 2, 5, 10].map((m) => m * power).find((s) => s >= rough);
    return { step: Math.max(1, step), top: Math.max(1, Math.ceil(Math.max(1, max) / Math.max(1, step)) * Math.max(1, step)) };
}

// The graph: a line per game shown, the days along the bottom (about 10 labels), the counts up the side, and a column
// per day that shows that day's counts when hovered or tapped.
function renderChart() {
    const { width, height, left, right, top, bottom } = CHART;
    const shown = report.games.filter((g) => !hidden.has(g.gameType));
    const days = report.days;
    const { step, top: yTop } = yScale(Math.max(0, ...shown.flatMap((g) => g.counts)));
    const plotWidth = width - left - right;
    const plotHeight = height - top - bottom;
    const x = (i) => left + (days.length === 1 ? plotWidth / 2 : (i * plotWidth) / (days.length - 1));
    const y = (n) => top + plotHeight - (n * plotHeight) / yTop;
    const svg = svgElement("svg", { viewBox: `0 0 ${width} ${height}`, class: "admin-svg", role: "img", "aria-label": "Games played per day" });

    for (let n = 0; n <= yTop; n += step) {
        svg.append(svgElement("line", { x1: left, x2: width - right, y1: y(n), y2: y(n), class: "admin-grid" }));
        const label = svgElement("text", { x: left - 6, y: y(n) + 4, "text-anchor": "end", class: "admin-axis" });
        label.textContent = String(n);
        svg.append(label);
    }
    const every = Math.max(1, Math.ceil(days.length / 10));
    days.forEach((day, i) => {
        if (i % every === 0 || i === days.length - 1) {
            const label = svgElement("text", { x: x(i), y: height - bottom + 18, "text-anchor": "middle", class: "admin-axis" });
            label.textContent = day.slice(5);
            svg.append(label);
        }
    });
    shown.forEach((game) => {
        svg.append(svgElement("polyline", {
            points: game.counts.map((n, i) => `${x(i)},${y(n)}`).join(" "),
            class: "admin-line", stroke: GAME_COLORS[game.gameType] ?? "#555",
        }));
        if (days.length <= 60) {
            game.counts.forEach((n, i) => svg.append(svgElement("circle", { cx: x(i), cy: y(n), r: 3, fill: GAME_COLORS[game.gameType] ?? "#555" })));
        }
    });

    const guide = svgElement("line", { y1: top, y2: top + plotHeight, class: "admin-guide", visibility: "hidden" });
    svg.append(guide);
    const columnWidth = days.length === 1 ? plotWidth : plotWidth / (days.length - 1);
    days.forEach((day, i) => {
        const column = svgElement("rect", { x: x(i) - columnWidth / 2, y: top, width: columnWidth, height: plotHeight, class: "admin-hover" });
        const show = () => {
            guide.setAttribute("x1", x(i));
            guide.setAttribute("x2", x(i));
            guide.setAttribute("visibility", "visible");
            const tip = $("admin-tooltip");
            tip.textContent = `${day}: ${shown.map((g) => `${GAME_LABELS[g.gameType] ?? g.gameType} ${g.counts[i]}`).join(", ") || "no game shown"}`;
            tip.hidden = false;
        };
        column.addEventListener("mouseenter", show);
        column.addEventListener("click", show);
        svg.append(column);
    });
    svg.addEventListener("mouseleave", () => {
        guide.setAttribute("visibility", "hidden");
        $("admin-tooltip").hidden = true;
    });
    $("admin-chart").replaceChildren(svg);
}

// A checkbox per game, in its line's color, to show or hide its line (remembered on this device).
function renderGames() {
    $("admin-games").replaceChildren(...report.games.map((game) => {
        const box = el("input");
        box.type = "checkbox";
        box.checked = !hidden.has(game.gameType);
        box.addEventListener("change", () => {
            if (box.checked) hidden.delete(game.gameType); else hidden.add(game.gameType);
            localStorage.setItem(HIDDEN_KEY, JSON.stringify([...hidden]));
            renderChart();
        });
        const swatch = el("span", "admin-swatch");
        swatch.style.background = GAME_COLORS[game.gameType] ?? "#555";
        return el("label", "admin-game", box, swatch, GAME_LABELS[game.gameType] ?? game.gameType);
    }));
}

// Each game's total over the days shown, most played first, and the total of all games.
function renderTotals() {
    $("admin-total-head").textContent = `Last ${report.days.length} days`;
    const rows = [...report.games].sort((a, b) => b.total - a.total)
        .map((g) => el("tr", null, el("td", null, GAME_LABELS[g.gameType] ?? g.gameType), el("td", null, String(g.total))));
    const all = report.games.reduce((sum, g) => sum + g.total, 0);
    rows.push(el("tr", "admin-all", el("td", null, "All games"), el("td", null, String(all))));
    $("admin-totals").replaceChildren(...rows);
}

async function load(days) {
    $("admin-status").textContent = "Loading…";
    $("admin-status").hidden = false;
    const response = await authFetch(`/api/admin/stats?days=${days}`).catch(() => null);
    if (!response?.ok) {
        $("admin-report").hidden = true;
        $("admin-status").textContent = response?.status === 404 ? "This page is only for admins."
            : response?.status === 400 ? (await response.json().catch(() => null))?.error ?? "Choose 1 to 365 days."
                : "Could not reach the server. Try again.";
        return;
    }
    report = await response.json();
    $("admin-status").hidden = true;
    $("admin-report").hidden = false;
    renderGames();
    renderChart();
    renderTotals();
}

$("admin-days").addEventListener("submit", (event) => {
    event.preventDefault();
    load(Number($("admin-days").elements.days.value));
});

accountReady.then(() => load(Number($("admin-days").elements.days.value)));
