"use strict";

// The admin page (English only): the games started per day over the last N days, as a line per game (each game can be
// shown or hidden), and each game's total; then the activity over the same days: the visits per day, each game from
// opening its page to finishing a game, and the visits by return, source and language. Only admins get the reports;
// anyone else is told the page is not available.

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

const RETURN_LABELS = { New: "New", Within7Days: "Back within 7 days", After7Days: "Back after 7 days or more" };
const VISITS_COLOR = "#19285d";

let report = null;
let activity = null;
const hidden = new Set(JSON.parse(localStorage.getItem(HIDDEN_KEY) ?? "[]"));

// The highest value of the y axis and its step: about 5 round steps above the highest count shown.
function yScale(max) {
    const rough = Math.max(1, max) / 5;
    const power = 10 ** Math.floor(Math.log10(rough));
    const step = [1, 2, 5, 10].map((m) => m * power).find((s) => s >= rough);
    return { step: Math.max(1, step), top: Math.max(1, Math.ceil(Math.max(1, max) / Math.max(1, step)) * Math.max(1, step)) };
}

// A graph: a line per series ({ name, color, counts }), the days along the bottom (about 10 labels), the counts up the
// side, and a column per day that shows that day's counts in `tooltip` when hovered or tapped.
function lineChart(days, shown, label, tooltip) {
    const { width, height, left, right, top, bottom } = CHART;
    const { step, top: yTop } = yScale(Math.max(0, ...shown.flatMap((s) => s.counts)));
    const plotWidth = width - left - right;
    const plotHeight = height - top - bottom;
    const x = (i) => left + (days.length === 1 ? plotWidth / 2 : (i * plotWidth) / (days.length - 1));
    const y = (n) => top + plotHeight - (n * plotHeight) / yTop;
    const svg = svgElement("svg", { viewBox: `0 0 ${width} ${height}`, class: "admin-svg", role: "img", "aria-label": label });

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
    shown.forEach((series) => {
        svg.append(svgElement("polyline", {
            points: series.counts.map((n, i) => `${x(i)},${y(n)}`).join(" "),
            class: "admin-line", stroke: series.color,
        }));
        if (days.length <= 60) {
            series.counts.forEach((n, i) => svg.append(svgElement("circle", { cx: x(i), cy: y(n), r: 3, fill: series.color })));
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
            tooltip.textContent = `${day}: ${shown.map((s) => `${s.name} ${s.counts[i]}`).join(", ") || "no game shown"}`;
            tooltip.hidden = false;
        };
        column.addEventListener("mouseenter", show);
        column.addEventListener("click", show);
        svg.append(column);
    });
    svg.addEventListener("mouseleave", () => {
        guide.setAttribute("visibility", "hidden");
        tooltip.hidden = true;
    });
    return svg;
}

// The games played graph: a line per game shown.
function renderChart() {
    const shown = report.games.filter((g) => !hidden.has(g.gameType))
        .map((g) => ({ name: GAME_LABELS[g.gameType] ?? g.gameType, color: GAME_COLORS[g.gameType] ?? "#555", counts: g.counts }));
    $("admin-chart").replaceChildren(lineChart(report.days, shown, "Games played per day", $("admin-tooltip")));
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

// A table body's rows, from an array of cells per row; rows with a class are given as { className, cells }.
function tableRows(rows) {
    return rows.map((row) => el("tr", row.className ?? null, ...(row.cells ?? row).map((cell) => el("td", null, String(cell)))));
}

// The share of `part` in `whole`, as a whole percentage, or "–" without a whole.
const percent = (part, whole) => (whole > 0 ? `${Math.round((100 * part) / whole)}%` : "–");

// Each game from opening its page to finishing a game, most started first, and all games together.
function renderFunnel() {
    const columns = ["opened", "created", "joined", "started", "finished", "invitesOpened", "invitedJoins"];
    const cells = (name, g) => [name, g.opened, g.created, g.joined, g.started, g.finished, percent(g.finished, g.started), g.invitesOpened, g.invitedJoins];
    const all = Object.fromEntries(columns.map((c) => [c, activity.games.reduce((sum, g) => sum + g[c], 0)]));
    $("activity-games").replaceChildren(...tableRows([
        ...[...activity.games].sort((a, b) => b.started - a.started).map((g) => cells(GAME_LABELS[g.gameType] ?? g.gameType, g)),
        { className: "admin-all", cells: cells("All games", all) },
    ]));
}

// The visits per day, and by return, source and language.
function renderActivity() {
    const total = activity.visits.reduce((sum, n) => sum + n, 0);
    $("activity-total").textContent = `${total} visits in the last ${activity.days.length} days.`;
    $("activity-chart").replaceChildren(lineChart(activity.days, [{ name: "Visits", color: VISITS_COLOR, counts: activity.visits }],
        "Visits per day", $("activity-tooltip")));
    renderFunnel();
    const named = (list, label = (name) => name) => tableRows(list.map((c) => [label(c.name), c.count, percent(c.count, total)]));
    $("activity-returning").replaceChildren(...named(activity.returning, (name) => RETURN_LABELS[name] ?? name));
    $("activity-sources").replaceChildren(...named(activity.sources));
    $("activity-languages").replaceChildren(...named(activity.languages));
}

async function loadActivity(days) {
    $("activity-status").textContent = "Loading…";
    $("activity-status").hidden = false;
    const response = await authFetch(`/api/admin/activity?days=${days}`).catch(() => null);
    if (!response?.ok) {
        $("activity-report").hidden = true;
        $("activity-status").textContent = response?.status === 404 ? "This page is only for admins." : "Could not load the activity. Try again.";
        return;
    }
    activity = await response.json();
    $("activity-status").hidden = true;
    $("activity-report").hidden = false;
    renderActivity();
}

async function load(days) {
    $("admin-status").textContent = "Loading…";
    $("admin-status").hidden = false;
    const response = await authFetch(`/api/admin/stats?days=${days}`).catch(() => null);
    if (!response?.ok) {
        $("admin-report").hidden = true;
        $("activity-panel").hidden = true;
        $("admin-status").textContent = response?.status === 404 ? "This page is only for admins."
            : response?.status === 400 ? (await response.json().catch(() => null))?.error ?? "Choose 1 to 365 days."
                : "Could not reach the server. Try again.";
        return;
    }
    report = await response.json();
    $("admin-status").hidden = true;
    $("admin-report").hidden = false;
    $("activity-panel").hidden = false;
    renderGames();
    renderChart();
    renderTotals();
    await loadActivity(days);
}

$("admin-days").addEventListener("submit", (event) => {
    event.preventDefault();
    load(Number($("admin-days").elements.days.value));
});

accountReady.then(() => load(Number($("admin-days").elements.days.value)));
