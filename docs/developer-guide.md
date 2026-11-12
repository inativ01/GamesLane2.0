# GamesLane developer guide

How GamesLane is built, and how to work on it. For what users see and the game rules, see the [user manual](user-manual.md).

- [Technology](#technology)
- [Architecture](#architecture)
- [Project structure](#project-structure)
- [Sessions and seats](#sessions-and-seats)
- [Adding a game](#adding-a-game)
- [Translations](#translations)
- [Game Hub API](#game-hub-api)
- [Login](#login)
- [Database](#database)
- [Game options](#game-options)
- [HTTP endpoints](#http-endpoints)
- [Sign-in setup](#sign-in-setup)
- [Running locally](#running-locally)
- [Running tests](#running-tests)
- [Project status and external services](#project-status-and-external-services)
- [Working conventions](#working-conventions)
- [Known limitations](#known-limitations)

## Technology

- **Backend:** .NET 10 (C#), ASP.NET Core
- **Real-time updates:** SignalR
- **Database:** PostgreSQL (Neon), with EF Core
- **Frontend:** plain HTML, CSS and JavaScript, served by the backend (no build step)
- **Tests:** xUnit
- **Hosting:** Render, from the `Dockerfile` at the repo root

## Architecture

```
Browser page (wwwroot/*.html + *.js)
   │  SignalR /hubs/game          HTTP /auth/*, /api/*
   ▼                              ▼
GameHub ──► GameSessionStore ──► SessionRepository ──► Sessions table (JSON snapshot)
   │              │
   │              └─ game-specific session (e.g. RichManPoorManSession : GameSession)
   │                     └─ game rules (e.g. RichManPoorManGame)
   └─ StateUpdated / PrivateStateUpdated / ChatMessageReceived to the pages
```

- **Game rules** (`Games/<Game>/`) are plain classes with no knowledge of seats, users or connections: they take players as numbers and enforce the rules (e.g. `RichManPoorManGame.TryThrow`). They are unit-tested directly.
- **Sessions** (`Sessions/`) wrap one game each. `GameSession` is the shared base class: seats, the session lobby, seat states, options, starting and ending games, and saving. Each game's session (e.g. `BackgammonSession`) maps seats to the game's players and builds the public and private state sent to pages. All public members are thread-safe: they lock the session's `Sync` object.
- **The hub** (`Hubs/GameHub.cs`) is the only entry point for game actions. Each hub method finds the session, calls one `Try…` method on it (which returns an error message on failure), and on success calls `BroadcastState` (except `SendChatMessage`, which sends only the new message and saves the session): the public state to everyone in the session's SignalR group, each user's private state to their own connections, then saves the session. Failures are returned to the caller as a `HubException` with the error message.
- **The session store** (`Sessions/GameSessionStore.cs`) keeps the sessions being played in memory, creates sessions with new codes (`Create`, by `GameType`), loads saved sessions on demand (`GetAsync`), and gives each game its page (`PageFor`). `SessionRepository` reads and writes the `Sessions` table.
- **Background services:**
  - `DisconnectedSeatsMonitor` (every 5 seconds): marks users disconnected for 60 seconds (`GameSession.ReconnectWindow`) as Away, broadcasts the change, and removes sessions nobody has had open for their `IdleLimit`: 5 minutes once a game has started (they stay saved); before that (they are deleted), 10 minutes for a private session with players seated, otherwise 30 seconds. A public session with nobody seated is deleted at once.
  - `ExpiredSessionCleanup` (every hour, first 2 minutes after the server starts): saves the sessions in memory, then deletes saved sessions 6 hours after their last activity.
  - `InactiveUserCleanup`: deletes saved users inactive for 30 days, except the admins (`AdminUsers`).
- **Games played:** `GameSessionStore` counts every game started (`IGameSession.GamesStarted`: Start, each Play again, and starting when the last seat fills) when the session is next saved, adding it to that day's (UTC) row in `GamePlays` (`Stats/GamePlayRepository`, one upsert per count). A session loaded again is not counted again. `Stats/GamePlayStats` builds the admin page's report (every day and game, with zeros) and the order of the games by popularity (last 30 days; ties in `UsualOrder`, the main screen's usual order).
- **Activity:** anonymous daily counts in `ActivityCounts` (`Stats/ActivityRepository`, one upsert per count), added through `ActivityCounter.CountAsync` (today in UTC; errors are logged, not thrown). Each row is a day, an `ActivityEvent`, a game (or `""`) and a detail (or `""`); nothing identifies a visitor, player or session. Counted on the server: `CreateGame` (`GameHub.CreateSession`; detail `Public` or `Private`), `JoinGame` (`JoinPublicGame`, detail `Public`; `TakeSeat` by a user without a seat beside others, detail `Private`, i.e. joining a game one was invited to; the creator's first seat is not counted), `FinishGame` (`GameSessionStore`, once per game when a save finds `IGameSession.HasFinishedGame`, i.e. the Over phase; a game ended early is not counted, a session loaded again is not counted again, and Tic-tac-toe, which never ends by itself, is never counted) and `OpenInvite` (`/join/{code}` for a private session). Counted from the pages: `common.js` (`reportVisit`, on the main screen and the game pages) posts a `VisitReport` to `/api/visits` once a day (UTC) per browser (`Visit` with the game page it arrived on, `VisitSource`, `VisitLanguage`, `VisitReturn`) and once a day per game page opened (`OpenGame`); `localStorage` `visits` keeps only `{ day, games }`. The source is the page's `utm_source`, else the referring site's host, else `invite` for a game page opened from outside the site (an invite link), else `direct`; the language is `navigator.language`. `Stats/VisitRules` keeps only plain values (sources: up to 50 lower-case letters, digits, `.`, `-`, `_`, without `www.`, otherwise `other`; languages: `en-US`, `he`, `zh-TW` from `zh-Hant-TW`, otherwise `other`; returns: `New`, `Within7Days`, `After7Days`). `Stats/ActivityStats` builds the admin page's activity report.
- **Admins:** the saved users listed by ID in `Admin:UserIds` (`appsettings.json`; e.g. on Render, `Admin__UserIds__0` overrides it). `AdminUsers.IsAdmin` checks the login token's user ID; `/auth/me` returns `isAdmin`, and the profile dialog then links to `/cards.html` (every GamesLane card; the page has no private data, so it is not checked) and `/admin.html` (English only; the page itself can be opened by anyone, but `/api/admin/stats` and `/api/admin/activity` answer only admins, so others see "This page is only for admins."): an SVG graph of the games started per day over the last N days (1–365, 30 by default; a checkbox per game shows or hides its line, remembered in `localStorage`), and each game's total; then, over the same days, the **Activity** panel: a graph of the visits per day, a table per game (opened, created, joined, started, finished, finished of started, invites opened, invited joins; `lineChart` draws both graphs), and the visits by return, source and language.
- **Pages:** `common.js` holds the shared page framework. `startSessionPage(gameType, render)` connects to the hub, calls `EnterSession`, shows the shared session lobby while the session is in its lobby, and otherwise calls the game's `render(game, state, view)` on every update; `view` holds the user's seats (`mySeats`, `isMe(seat)`) and private state (`view.private`). `startSessionPage` also fills the fixed session bar (the user's role in words, the game code that copies on click, the 💬 chat (`startChat`: its panel, unread count, pop-up and chime), and the ☰ menu with the invite link, players seated and observers, Leave my seat and, during a game, End game for everyone) and shows the status bar (`#status` and any turn buttons the page puts there) during a game, with the "Waiting for …" pop-up and the newcomer's seat offer (`updateWaiting`, `updateSeatOffer`), highlighted (class `act`) while `#status` contains 👉 or a primary button in its `.status-actions` is shown, so each game marks the texts that ask the user to act with 👉 and puts its turn and between-rounds buttons (and the shared `#play-again` and `#to-lobby`) there; games that keep a score and have no players' table (Tic-tac-toe, Backgammon, Wazoo) fill the status bar's score row (`#scores`) with `showScores(state, view, facts, players)`; the optional `seatRole(seat, state)` gives a seat's name for "You: …" in the session bar (by default "Seat 2"). Each game page (e.g. `rich-man-poor-man.js`) only draws its game screen and calls its hub methods. The user's profile settings come from `userSettings()` (`{ chat, sound, cards }`; a saved user's from `/auth/me`, a guest's from `localStorage`) and are changed with `saveSettings(settings)`; pages listen for the `settingschange` event (`SETTINGS_EVENT`), sent once the user is known and after every change (`startSessionPage` redraws the game screen then). Playing cards are drawn by `cardElement(text, style)` in the user's card style unless `style` is given (`cards`: `"New"`, the default, or `"Classic"`, drawn by `classicCard`); the new cards draw the suits as SVG shapes (`SUIT_SHAPES`) and each rank as a script road (`ROAD_RANKS`, the centre line of each letter; `WIDE_ROAD_RANKS` for +2, +3 and +4; `BOXED_ROADS` for TAKI, TA above KI in block letters, and the crown, one road per part with classes `part-1`, `part-2` …), and also draw cards the classic style does not have: 0 and 1, Skip (`"S♥"`), Reverse (`"R♥"`), `"+2♥"`, a big plus sign (`"+♥"`), TAKI (`"TAKI♥"`, written downwards in the corners), `"TAKI"` (no suit: the joker's octagon, each letter's rim in a suit color in turn), `"CROWN"` (the joker's octagon with a crown rimmed in gold and a gold ♛ in the corners), `"COLOR"` (like the joker), `"+4"` (the joker's octagon with a road +4), `"+3"` (like +4) and `"+3X"` (+3 crossed out: a red slash, `CROSS_OUT`, across the middle, and a slash through each corner's +3, yellow in the top left and green in the bottom right); the cards page `/cards.html` shows them all; the classic ones keep their own class names (`classic-…`, `pip`, `court-…`) so the two styles' CSS does not clash. Every sound must check `userSettings().sound` (the chat's chime, The Gang's chip alert, Rich man, poor man's turn chime and reading aloud, Hearts' breaking glass and regal doom, the confetti fanfare); `playSound(sound)` plays a sound built by the page on the fanfare's audio, and checks it. `celebrate(state, key)` shows confetti and plays a fanfare (`playFanfare`) for a success of the user; each game page calls it when a seat the user holds wins (or, in The Gang, for everyone, observers too, each time the gang opens a vault), with a `key` naming the success within the session's game (e.g. `round-3`), which is remembered in `sessionStorage` so it shows once. **Help:** `startHelp()` adds **?** to the top bar of the main screen and the game pages (not the admin or cards page). It opens a side panel (built like the chat's) that loads the `.help-content` of `help.html` (English) or `help.<code>.html` once, and unfolds the sections about the page: `help-main-screen`, or `help-<page>` on a game page (the page's name in `GAME_PAGES`, e.g. `help-yaniv`), with `help-lobby` first while `startSessionPage` shows the lobby (`help.setLobby`). Each section is a `<details class="help-section">` with a short text (on game pages, "How to play" and "On this screen") and a folded `<details class="help-more">`. The first visit to each of those pages in a browser shows a tip under **?** for 10 seconds (`helpTipSeen:<section>` in `localStorage`; on the main screen, after the splash).

## Project structure

```
GamesLane.slnx                 Solution file
Dockerfile                     Build for Render
GamesLane.Server/              ASP.NET Core backend
  Program.cs                   Services, endpoints and the hub
  Auth/AuthSetup.cs            Login tokens, rate limiting, Google/Microsoft/Discord sign-in
  Auth/AccountEndpoints.cs     Guests, registration, login, seat transfer, profile, icon and saved game options endpoints
  Data/                        Database (EF Core + PostgreSQL): tables, migrations, connection setup
  Feedback/                    The feedback form's endpoint: FeedbackRules (a comment's checks and its GitHub issue), GitHubIssueTracker (creates the issue), FeedbackEndpoints (POST /api/feedback, rate limit)
  Users/                       User rules, user service, saved game options, and deletion of inactive users
  Stats/                       Games played per day: GamePlayRepository (the GamePlays table), GamePlayStats (the admin report, popularity); activity: ActivityCounter, ActivityRepository (the ActivityCounts table), VisitRules, ActivityStats (the admin activity report); StatsEndpoints
  Hubs/GameHub.cs              SignalR hub for creating, joining and playing sessions
  Hubs/DisconnectedSeatsMonitor.cs  Marks users disconnected for 60 seconds as Away; removes sessions nobody has open (deletes those never started)
  Sessions/GameSession.cs      Seats, session lobby and game flow shared by all games
  Sessions/TrickTakingSession.cs  Shared by the trick-taking games' sessions: 4 seats, playing a card, the next hand, the trick as everyone sees it
  Sessions/                    Game-specific sessions; the session store (memory, and the database through SessionRepository); SessionSnapshot, which saves a session as JSON; ExpiredSessionCleanup (hourly, deletes sessions 6 hours after their last activity)
  Games/GameOptions.cs         Game options (rule variants): each game's options, and checking chosen values
  Games/Tricks/                Shared by the trick-taking games: the 52-card deck, hand order and trick winner (TrickCards), and the play of the tricks (TrickTakingGame: 4 players, 13 cards each, following suit, trumps)
  Games/TicTacToe/             Tic-tac-toe rules
  Games/TheGang/               The Gang rules: cards, poker hand ranking, heists
  Games/Backgammon/            Backgammon rules: position, legal moves, turns, doubling cube, matches, options
  Games/RichManPoorMan/        Rich man, poor man rules: the deck (one set of 2 to A per player), rounds, players who left, the exchange, options
  Games/Wazoo/                Wazoo rules: scoring each box (with the joker rule), scorecards and bonuses, turns
  Games/FinalCard/             Final Card rules: cards and decks (US and Israeli Style), turns and action cards (TAKI runs, Crown, +3 and +3 Breaker in Israeli Style), drawing, Final Card calls, hands and scores, the pyramid, options
  Games/Yaniv/                 Yaniv rules: the deck with 2 jokers, card values, series and straights (YanivCards), turns, Yaniv and Assaf, scores, the bonus, players out, options
  Games/Hearts/                Hearts rules: passing (left, right, across, hold), the 2♣ lead, no points on the first trick, breaking hearts, points, shooting the moon, the game to 100
  Games/Spades/                Spades rules: teams of partners across, the deal moving left, bidding (Nil and Blind Nil options, looking at the cards), spades trumps and breaking spades, team scores, bags, the game to 500 (or −200)
  wwwroot/                     Web pages
    index.html, home.js        Main screen: public games (create, or join a random one) and private games (create, or join by code); the list of games to choose from; going back to games waiting for the user
    common.js                  Shared: automatic guest, account bar and profile dialog (with login), help button and panel, hub connection, session lobby and page framework, playing cards (new and classic styles)
    tricks.js                  Shared by the trick-taking games' pages: the table with the four players around the trick (the user at the bottom), the hand, the players' table under it (renderTrickTable, wireTrickTable), texts about seats
    i18n.js                    Shared: languages. The English texts in the code are the keys: t("Log in") gives the chosen language's text. Also matches the server's English messages to their translations, and the language picker
    lang/he.js, es.js, fr.js, pt.js, zh-CN.js, zh-TW.js  Hebrew, Spanish (Mexico), French (France), Portuguese (Brazil), Simplified Chinese (mainland China) and Traditional Chinese (Taiwan) translations (a page offers a language by loading its file before i18n.js)
    styles.css                 Styles for all pages (on phones, double-tapping does not zoom in; pinching still zooms)
    admin.html/.js             Admin page: games played per day (graph) and per game (totals), and the activity (visits, each game's activity, returns, sources, languages), for admins only
    cards.html                 Cards page: the card back and every GamesLane playing card at several sizes (linked from the profile for admins)
    privacy.html               Privacy policy in English (the address registered with Google); privacy.he.html, .es.html, .fr.html, .pt.html, .zh-CN.html, .zh-TW.html: the policy in each language, linked from the main screen in the chosen language
    help.html                  Help in English: one section for the main screen, the lobby and each game; help.he.html, .es.html, .fr.html, .pt.html, .zh-CN.html, .zh-TW.html: the help in each language, shown in the help panel (common.js)
    tictactoe.html/.js         Tic-tac-toe page
    the-gang.html/.js          The Gang page
    backgammon.html/.js        Backgammon page (the board is drawn as SVG)
    rich-man-poor-man.html/.js Rich man, poor man page
    wazoo.html/.js            Wazoo page
    final-card.html/.js        Final Card page (GamesLane cards, each color drawn as the suit of that color)
    yaniv.html/.js             Yaniv page (the piles on top, the players' table shared with Rich man, poor man's styles)
    hearts.html/.js            Hearts page (the table and hands of tricks.js; choosing the cards to pass)
    spades.html/.js            Spades page (the table and hands of tricks.js; the bid under the hand)
    images/                    Logo, the GamesLane name for the header, the splash screens and the card back (web-sized copies of the originals in docs/), game pictures (SVG)
    images/the-gang/           A picture (SVG) for each challenge and specialist card, named by the card's key, e.g. quick-access.svg
    images/flags/              Flags for the language picker
GamesLane.Server.Tests/        Unit tests
docs/                          This guide and the user manual; original logo, name and splash pictures (kept out of git); docs/the-gang/: The Gang's card texts
```

## Sessions and seats

The seat states (Present, Reconnecting, Away, Vacant) and what each game does with absent seats are described in the [user manual](user-manual.md#disconnections-and-seats). In the code:

- `GameSession` tracks each user's connections and when they disconnected. `UpdateAwaySeats` (called by `DisconnectedSeatsMonitor`) turns users disconnected for `ReconnectWindow` into Away; outside a game, their seats are freed.
- **Noticing a lost connection:** the page closes its hub connection on `pagehide`, so closing a tab or leaving the page disconnects at once (a page the browser restores from its cache reloads). When a browser cannot say goodbye (a phone app closed or asleep, a lost network), the keep-alive messages tell: each side sends one every 5 seconds and treats 10 seconds of silence as a lost connection (`KeepAliveInterval` and `ClientTimeoutInterval` in `Program.cs`, `withKeepAliveInterval` and `withServerTimeout` in `common.js`; keep them matching, with the timeout at least twice the interval).
- **Absent seats during a game:** a seat is absent (`IsAbsent`) as soon as its holder has no connection, or it is empty; `Disconnect` and reconnecting call `OnSeatsChanged` (Rich man, poor man marks its players inactive there, `RichManPoorManGame.SetActive`). The game is never paused: each session says, in `AwaitedSeats`, the seats whose action it needs now (the seat to play, or the seats that have not yet done what all must do). Once all of them are absent, they are the public state's `waitingSeats`, and the page shows its players the "Waiting for …" pop-up. A session that can skip them overrides `CanSkipAwaited` and `TrySkipAwaited(seat)` (Rich man, poor man while playing: `RichManPoorManGame.TrySkip`; Yaniv while playing: `YanivGame.TrySkip`), used by `TrySkipPlayer`; otherwise the players end the game (`TryEndGame`). In a public Yaniv game, `YanivSession` also skips by itself the turns of seats that are Away or Vacant (`IsAwayOrVacant`), after every action and change of seats, as long as a player in the game is present.
- **Seat offers:** players cannot take a seat during a game. Each `Enter` (opening the page, or reconnecting) of a logged-in user without a seat in a private game in progress offers them the absent seats (`OfferedSeats`: those `SeatingDuringGameError` allows) if there are any (`_seatOffers`), and otherwise withdraws their offer. `TryTakeSeat` during a game requires an offer; `DeclineSeatOffer` withdraws it until the next `Enter`. The page keeps the dialog in step with `absentSeats`. Offers are cleared when a game starts or ends.
- Each game defines its maximum number of seats (up to 12) and the minimum needed to start (which may equal the maximum). A game whose number of players depends on its options overrides `SeatLimitFor(options)` (all seats by default): in the lobby, only that many seats can be taken (`TryTakeSeat`, `TryTakeNextSeat`), a public game starts by itself once they are all taken and is open to join until then, the public state's `maxSeats` is that number, and `TrySetOptions` refuses options that allow fewer players than are seated ("With these options, at most 4 players can play.").
- Private game information is not sent while the session is in the lobby.
- **Public games** (`MakePublic`, called by the store's `CreateAsync` before the session can be found; the rules are in the [user manual](user-manual.md#public-games)): `GameSession` keeps the host (`_host`, the creator; `UpdateHost` passes it to the first seated player when the host's seat is freed), which alone may `TrySetOptions` and start from the lobby (`TryNewGame`). `TryTakeSeat` refuses seats once a game is on screen and starts the game (`TryStartGame`) when a seat taken in the lobby fills the last one. Giving up a game seat during a game (`TryLeaveSeat`, `LeaveAllSeats`) ends the game into the lobby with a notice ("Ada left the game.") unless `ContinuesWithAbsentPlayers`. `IsOpenToJoin` (a public lobby with a player and a free seat) is what `JoinPublicGame` looks for. The hub refuses `EnterSession` to users without a seat, and `TakeSeat`; `/api/sessions/{code}` and `/join/{code}` treat public sessions as not found.
- **Waiting games:** `IsWaitingFor(userKey)` is true for a game in progress where the user holds a game seat but has no connection. `WaitingGames` lists them (most recent activity first, from `IGameSessionStore.LastActivity`, the time of the last save), and the main screen goes back to a public one or offers the private ones.
- Main overridable members of `GameSession`: `Type`, `MinSeats`, `MaxSeats`, `IsGameOver`, `StartNewGame(seats)`, `GetGameState()` (public state), `GetPrivateGameState(seats)` (a user's private state, or null), `AwaitedSeats`, `CanSkipAwaited`, `TrySkipAwaited(seat)`, `OnSeatsChanged()`, `OnRestored()`, `SeatingDuringGameError(seat)`, `ContinuesWithAbsentPlayers` (a public game goes on when a player gives up their seat).
- **Saving and loading:** a session is saved in the database after every change (by `BroadcastState`). Once nobody has had it open for its idle limit (see `DisconnectedSeatsMonitor` above), it is removed from memory; it is loaded again when someone opens it (by its link or code, or when a player reconnects). The server loads nothing when it starts. A session loaded again starts with every holder disconnected since it was saved (`Restore`), so they become Away after the reconnect window; in the lobby, their seats are freed. A session whose game never started (`HasStarted`), or a public session with nobody seated, is deleted instead of saved when it is removed from memory. A session removed unchanged (compared with what was last saved or loaded) is not saved again, so only real changes count as activity.
- **Expiry:** sessions are saved so a game survives a server restart (a deploy or a crash) or a break of a few hours, not to be resumed days later. `ExpiredSessionCleanup` deletes them 6 hours after their last activity. It runs every hour while the server runs; a server that is asleep does not wake for it, and cleans up after it wakes. It saves the sessions in memory first, so a session someone has open is never deleted.
- **Saved sessions** (`Sessions/SessionSnapshot.cs`): a session is saved as JSON holding every field of the session and of what it holds (seats, options, the game with its cards, checkers or chips), public or private, except open connections (marked `[NotSaved]`) and the games' dice, shuffles and random draws, which a session gives back to its game after loading (`OnRestored`). Cards are saved as text such as `"10♥"`. `Version` is a fingerprint of all those fields' names and types: after a change to the fields of a game or session, saves made by the old code are no longer loaded (that session's code then says "not found"). A new field therefore needs no other work, but changing fields ends the saved games of that type.

## Adding a game

1. **Rules:** `Games/<Game>/` with the game class and its interface in `Games/<Game>/Interfaces/`. Add unit tests in `GamesLane.Server.Tests/`. A trick-taking game for 4 players derives from `TrickTakingGame` (`Games/Tricks/`), which plays the tricks; the game adds what comes before them (e.g. passing or bidding, then `StartPlay(leader)`), its trump suit, its own limits on the cards that can be played (`RuleError`), and the scoring once the 13 tricks are played (`OnHandPlayed`). `TrickTakingGame` also lets the leader claim the remaining tricks (`CanClaim`, `TryClaim`) when, judged only from their own cards and those already played, each of their cards is higher than every card of its suit still out and, with a trump suit, no trump is out unless they hold only trumps; they take every card still in a hand (`Claimer`, and each player's cards then, `ClaimedHand(player)`) and `OnHandPlayed` scores the hand.
2. **Session:** a value in the `GameType` enum (`Sessions/GameSession.cs`); a session class deriving from `GameSession` in `Sessions/`, with its interface (the game's `Try…` actions) in `Sessions/Interfaces/`. A trick-taking game's session derives from `TrickTakingSession<TGame>`, which plays cards (`ITrickTakingSession.TryPlayCard`, the hub's `PlayTrickCard`), claims the remaining tricks (`TryClaim`, the hub's `ClaimTricks`) and deals the next hand (`NextRound`); its page loads `tricks.js` after `common.js`.
3. **Store:** add the session to `GameSessionStore.Create` and its page to `GameSessionStore.PageFor`.
4. **Hub:** add the game's methods to `IGameHub` and `GameHub` (each one calls `Act` with the session's `Try…` method), and list them in the [Game Hub API](#game-hub-api).
5. **Options** (if any): add them to `GameOptions.For`.
6. **Page:** `wwwroot/<game>.html` and `.js` using `startSessionPage`, its page in `GAME_PAGES` (`common.js`), a tile on the main screen (`index.html`, `data-create="<GameType>"`) with a picture in `images/games/`, and its styles in `styles.css`.
7. **Texts:** the translations in `lang/he.js`, `lang/es.js`, `lang/fr.js`, `lang/pt.js`, `lang/zh-CN.js` and `lang/zh-TW.js` (see [Translations](#translations)), and a `help-<game>` section in `help.html` and every `help.<code>.html`.
8. **Docs:** the game's rules and screens in the [user manual](user-manual.md), and its folders here.

## Translations

The texts in the pages and scripts are in English, and they are also the keys of the translations (see `i18n.js`).

- The languages are listed in `LANGUAGES` in `i18n.js`: the code (also the file name in `lang/`), the locale (the language's variant, used to choose a voice for reading aloud), the name in the language itself and the flag in `images/flags/`. Until the user chooses, the page opens in the first of the browser's preferred languages (`navigator.languages`) that it offers. A code is the language (e.g. `fr`), or for Chinese the language and region (`zh-CN`, `zh-TW`): `languageCode()` turns a browser's tag into a code, giving `zh-TW` for the Traditional script (`zh-Hant`) or Taiwan, Hong Kong or Macau, and `zh-CN` for any other Chinese. Chinese pages use Chinese fonts (`:lang(zh-CN)` and `:lang(zh-TW)` in `styles.css`). Reading aloud picks a voice by the locale's language, preferring the exact locale, and for Chinese a Mandarin voice over a Hong Kong (Cantonese) one. The feedback form sends the code with the comment.
- Load the files in `lang/` and then `i18n.js` in the page's `<head>` (untranslated pages load only `i18n.js`). SignalR's script sets a global `t` while it loads, so it must come before `i18n.js`, which defines `t()`.
- The privacy policy is a separate page per language, `privacy.<code>.html` (`privacy.html` is the English one), with the same sections, bold parts and links. **Changing the policy means changing every page and its "Last updated" date.** The main screen's footer links to the chosen language's page (`home.js`).
- The help is also a page per language, `help.<code>.html` (`help.html` is the English one), with the same sections (`id="help-…"`) and the same number of folded parts, headings and list items. **Changing the help means changing every page.** Hebrew uses the plural, Chinese the polite 您 and 请 / 請, and the other languages the informal "you", as in `lang/`.
- Every language translates the same texts as `lang/he.js`; `TranslationsTests` checks this, that translations use only the varying parts of their text, that each language has its flag, that every translated page loads every language, that every language has a privacy policy with the same sections and links as the English one, and a help page with the same sections as the English one, which has a section for the main screen, the lobby and every game.
- Mark fixed texts in the HTML with `data-i18n` (the element's whole text is translated) or `data-i18n-attr="title,aria-label"`; in scripts, wrap texts in `t("...")`, with varying parts as `{name}`: `t("Seat {n}", { n })`. Write whole sentences as keys, not pieces glued together, since word order differs in Hebrew.
- Texts from the server (error messages, option names) go through `translateServerText()`, which finds their translation by their English text. **Changing an English message on the server means changing its key in every file in `lang/` too.**
- Hebrew style: instructions in the plural (e.g. הקישו על הלוח), so they suit everyone. A text about the viewer has its own "you" form (e.g. "You rolled {n}." / "{name} rolled {n}."; see `about()` in `backgammon.js`). Texts about other players avoid male and female verb forms (e.g. "ל־{name} יצא {n}").
- Spoken texts (Rich man, poor man's **Read throws aloud**): a single card is read as a whole text per value (`SPOKEN_SINGLES` in `rich-man-poor-man.js`: `"A single nine"`, `"A single queen"`, …), so translations can match the value's gender (`"חמש יחיד"`, `"מלכה יחידה"`). 2 to 12 cards are read as their group (`SPOKEN_GROUPS`: `"Pair of {card}"`, `"Triplet of {card}"`, …), with `{card}` the card's value in the singular (`SPOKEN_VALUES`: `"nine"`, `"jack"`, `"ace"`, …), e.g. `"Triplet of {card}": "שלישיית {card}"` and `"jack": "נסיך"`. All are translated in every language. Without a voice for the language on the device, the English text is spoken.
- Spanish, French and Portuguese style: the informal "you" (tú, tu, você). Colors stand alone or in brackets (e.g. "Ficha 3 (Rojo)"), since one color text serves Backgammon, The Gang and Final Card, and the nouns they go with differ in gender.
- Chinese style: the polite 您, and instructions start with 请 (Simplified) or 請 (Traditional). Traditional Chinese uses Taiwan's words, not a character conversion of the Simplified text (e.g. 登入 / 登录 for log in, 帳號 / 账户 for account, 設定 / 设置 for settings, 抽牌 / 摸牌 for draw). Quotation marks are “…” in Simplified and 「…」 in Traditional. Spoken throws use Chinese numbers and the letters J, Q, K and A (e.g. "一对K", "三张九").
- Terms still to be confirmed by the user: צעדים (pips), משחק קרופורד (Crawford game), גמון / בקגמון (gammon / backgammon), "(אני)" for the viewer's own seat; game names שש־בש, הכנופיה, איקס־עיגול; in Rich man, poor man: בחר / פס (Select / Pass), מסירה (Give), עשיר / עני (rich man / poor man), נסיך (jack) and the spoken numbers (e.g. "שלוש תשיעיות"); in Wazoo: the name וואזו, the boxes (שלשות … שישיות, שלישייה, רביעייה, פול האוס, רצף קטן / גדול, צ'אנס), משבצת (box), כרטיס הניקוד (scorecard) and נשמרה (a kept die); in The Gang: שוד (heist), צ'יפ (chip), קלפי כיס (pocket cards), כספת / אזעקה (vault / alarm), אתגר / מומחה (challenge / specialist), the card names (e.g. גישה מהירה, גלאי תנועה, האפלה, מודיע, נהג מילוט, המוח, נסיך, שרירן) and the hand rankings (קלף גבוה, זוג, שני זוגות, סטרייט, פלאש, סטרייט פלאש, רויאל פלאש); in Final Card: the name קלף אחרון, יד (hand), ג'וקר / ג'וקר +4 (Wild / Wild Draw Four), קח 2, דילוג, היפוך כיוון, שליפה (drawing), שרשור קלפי שליפה (stacking), and פירמידה; in Yaniv: הערימה הסגורה / הפתוחה (closed / open pile), זריקה (discarding), ניקוד היד (hand score), בחוץ (out); לקיחה (trick); in Hearts: the name לבבות, מלכת העלה (queen of spades), העברת קלפים (passing), "לקח את כל הנקודות" (shooting the moon). Confirmed: the names יניב and אסף (Assaf), סדרת משחקים (match), עשיר ועני (the game's name), אחדים / שתיימים (ones / twos, in Wazoo and in Rich man, poor man's spoken throws).
- Spanish, French and Portuguese terms still to be confirmed by native speakers (Spanish / French / Portuguese):
  - Game names: Gato / Morpion / Jogo da Velha (Tic-tac-toe), Backgammon / Backgammon / Gamão, Rico o pobretón / Riche ou fauché / Do luxo ao lixo (Rich man, poor man); The Gang, Wazoo and Yaniv keep their names; Final Card is Última carta / Dernière carte / Última carta.
  - Shared: partida / partie / partida (a game), lugar / place / lugar (seat), sala / salon / sala (lobby), nombre visible / pseudo / nome de exibição (display name).
  - Backgammon: casillas / pips / casas (pips), serie / match / partida (match), cubo doblador / videau / dado de dobra (doubling cube), Blanco / Blanc / Branco and Negro / Noir / Preto.
  - Rich man, poor man: Rico / Riche / Rico and Pobretón / Fauché / Pobre, Paso / Passer / Passo (Pass), puesto / place / posição (place), tirar / poser / jogar (throw), and the spoken throws (e.g. "3 doses", "3 deux", "três dois").
  - Wazoo: the boxes (Unos … Seises, Tercia, Póker, Full, Escalera corta / larga, Libre; As … Six, Brelan, Carré, Full, Petite / Grande suite, Chance; Uns … Seis, Trinca, Quadra, Full house, Sequência baixa / alta, Chance), casilla / case / categoria (box), hoja de puntos / feuille de score / cartela (scorecard).
  - The Gang: golpe / casse / assalto (heist), ficha / jeton / ficha (chip), cartas de mano / cartes en main / cartas na mão (pocket cards), cartas comunitarias / cartes communes / cartas comunitárias (shared cards), jugada / combinaison / combinação (hand ranking), reto / défi / desafio (challenge), the card names (e.g. Apagón / Coupure de courant / Apagão for Blackout, Matón / Gros bras / Brutamontes for Muscle) and the hand rankings (e.g. Escalera / Quinte / Sequência for Straight, Color / Couleur / Flush for Flush).
  - The privacy policy (`privacy.he.html`, `privacy.es.html`, `privacy.fr.html`, `privacy.pt.html`) and the help (`help.he.html`, `help.es.html`, `help.fr.html`, `help.pt.html`), including the Hebrew ones.
  - Final Card: Alto / Stop / Pare (Stop), Cambio de sentido / Changement de sens / Mudar sentido (Change Direction), Cambio de color / Changement de couleur / Mudar cor (Change Color), Más / Plus / Mais (Plus), Corona / Couronne / Coroa (Crown), Rompe +3 / Casse +3 / Quebra +3 (+3 Breaker), Estilo estadounidense / israelí, Style américain / israélien, Estilo americano / israelense (US / Israeli Style), mano / donne / mão (hand), tomar / piocher / comprar (draw). TAKI and SuperTAKI keep their names.
  - Yaniv: montón cerrado / abierto, tas fermé / ouvert, monte fechado / aberto (closed / open pile), cantar / annoncer / chamar Yaniv (call Yaniv), ronda / manche / rodada (round), Fuera / Éliminé / Fora (out), bono / bonus / bônus.
  - Trick-taking games: baza / pli / vaza (trick), seguir el palo / fournir / seguir o naipe (follow suit).
  - Hearts: the names Corazones / Dame de pique / Copas, pasar / passer / passar (pass cards), Se llevó todo / Grand chelem / Levou tudo (shooting the moon).
  - Spades: the names Picas / Pique / Espadas, apostar / annoncer / apostar (bid), Nil a ciegas / Nil aveugle / Nil às cegas (Blind Nil), bazas de más / plis en trop / vazas a mais (bags).
- Spades terms still to be confirmed: in Hebrew the name ספיידס, הכרזה (bid), אפס / אפס עיוור (Nil / Blind Nil), עודפים (bags); in Chinese the name 黑桃王, 叫墩 (bid), 零墩 / 盲零墩 (Nil / Blind Nil), 超墩 (bags).
- Chinese terms still to be confirmed by native speakers (Simplified / Traditional): the game names 双陆棋 / 雙陸棋, 井字棋 / 井字遊戲, 富翁与穷人 / 富翁與窮人 and 最后一张 / 最後一張; 王牌 / 鬼牌 (joker); in Backgammon: 克劳福德局 / 克勞福德局 (Crawford game), 全胜 / 全勝 and 完胜 / 完勝 (gammon / backgammon), 加倍骰 (doubling cube); in Wazoo: the boxes (一点 … 六点 / 一點 … 六點, 三条 / 三條, 四条 / 四條, 葫芦 / 葫蘆, 小顺 / 小順, 大顺 / 大順, 机会 / 機會); in The Gang: 劫案 / 搶案 (heist), 底牌 (pocket cards), 人头牌 / 人頭牌 (face cards) and the card names (e.g. 停电 / 停電, 线人 / 線人, 打手); in Final Card: 停止, 转向 / 迴轉, 变色 / 變色, 加号 / 加號, 王冠, +3 破解; in Yaniv: 暗牌堆 / 明牌堆 (closed / open pile); 墩 (trick); in Hearts: the names 红心大战 / 傷心小棧, 传牌 / 傳牌 (pass cards), 全收 (shooting the moon); and the privacy policy and help pages (`privacy.zh-CN.html`, `privacy.zh-TW.html`, `help.zh-CN.html`, `help.zh-TW.html`).

## Game Hub API

Clients connect to the SignalR hub at `/hubs/game` and call these methods:

| Method | Returns | Description |
| --- | --- | --- |
| `CreateSession(gameType, isPublic)` | session code | Logged-in users only. Creates a private or public session (`"TicTacToe"`, `"TheGang"`, `"Backgammon"`, `"RichManPoorMan"`, `"Wazoo"`, `"FinalCard"`, `"Yaniv"`, `"Hearts"` or `"Spades"`) and returns its 6-character code. The creator of a private session is not seated; the creator of a public one is, and is its host. The session starts with the creator's saved options for the game, if any. |
| `JoinPublicGame(gameType)` | session code, or `null` | Seats the caller in a random public session of `gameType` that is in its lobby with a player and a free seat, and returns its code; `null` if there is none. The game starts if this fills the last seat. |
| `WaitingGames()` | `[{ sessionId, gameType, isPublic }]` | The games in progress where the caller holds a seat but has no connection (Reconnecting or Away), most recently active first. |
| `EnterSession(sessionId)` | `{ state, playerId, private, chat, seatOffer }` | Subscribes to the session's updates. For a logged-in user, reconnects them to their seats, updates their name and icon on them, and returns their `playerId`; otherwise `playerId` is `null`. `private` is the user's private game information, or `null`. `chat` is the session's latest chat messages, oldest first (see `SendChatMessage`). `seatOffer` tells whether the user is offered the absent seats of the game in progress (a logged-in user without a seat opening or reconnecting to a private game with absent seats; see `TakeOfferedSeat`). Pages call this when they open and after reconnecting. A public session refuses users who hold no seat in it ("You are no longer in this game."). |
| `TakeSeat(sessionId)` | — | Logged-in users only. Takes the next available seat under the user's display name: in the session lobby, the first empty one; during a game, for a user offered a seat only, the first absent seat of the game. Not while a game is Over, nor during Rich man, poor man's exchange except for an absent player's seat (otherwise use `RequestSeat`), nor in public sessions. |
| `TakeOfferedSeat(sessionId, seat)` | — | A user offered a seat (`seatOffer`): takes the absent game seat `seat` and continues that player's game; the previous holder becomes a spectator. |
| `DeclineSeatOffer(sessionId)` | — | A user offered a seat watches instead, until they open the game or reconnect again. |
| `SkipPlayer(sessionId, seat)` | — | Players in the game only. Skips the turn of `seat`, an absent seat in `waitingSeats`, when `canSkipWaiting` (Rich man, poor man while playing: the seat passes, or a round's starter hands the start to the next player; Yaniv while playing: the seat's turn passes with its hand unchanged). If the game no longer waits for it, nothing changes. |
| `LeaveSeat(sessionId, seat)` | — | Gives up one of the caller's seats (used for **Leave seat** and **Leave my seat (watch)**). In a public session, giving up a game seat during a game ends the game (except Rich man, poor man and Yaniv). |
| `LeaveSession(sessionId)` | — | Stops receiving updates and gives up all the caller's seats (used when going back to the main screen, and for **Leave game** in public sessions). |
| `NewGame(sessionId, gameNumber)` | — | Seated users only (in a public session's lobby, its host only). Starts a new game for everyone seated, replacing game `gameNumber` (0 before the first game). Refused while a game is in progress. If that game was already replaced, nothing changes. |
| `SetOptions(sessionId, options)` | — | Anyone in the session (in a public session, its host), in the session lobby. Sets the options for the next new game, e.g. `{ "mode": "Advanced" }`. Options left out get their default. |
| `EndGame(sessionId)` | — | Seated users only. During a game, ends it and takes everyone back to the session lobby; the seats move up, others can join, and a new game can be started. |
| `ReturnToLobby(sessionId)` | — | Seated users only. After a game has ended, takes everyone back to the session lobby. |
| `SendChatMessage(sessionId, text)` | — | Logged-in users (guests too) who have the session open, seated or watching. Runs of spaces and line breaks become one space; the message must then have 1 to 300 characters (`GameSession.MaxChatLength`). Everyone in the session receives a `ChatMessageReceived` message with `{ number, playerId, name, icon, text, time }`: `number` counts the session's messages from 1, and `playerId` is the writer's, so a page recognises its user's own messages. The session keeps its latest 100 messages (`GameSession.ChatHistoryLength`) and saves them with the session. |
| `MakeMove(sessionId, seat, cell)` | — | Tic-tac-toe: places the mark of `seat` (0 is X, 1 is O), which the caller must hold. Cells are numbered 0–8, left to right, top to bottom. |
| `NextRound(sessionId, round)` | — | Tic-tac-toe: starts the round after `round`; backgammon: starts the game of the match after game `round`; Rich man, poor man: starts the game after game `round` (or with fewer than 4 players left, goes back to the session lobby); Final Card (to a score target): deals the hand after hand `round`; Yaniv (to a score target): deals the round after round `round`; trick-taking games (Hearts, Spades): deals the hand after hand `round`. If it has already started, nothing changes. |
| `Roll(sessionId, seat)` | — | Backgammon: `seat`, which the caller must hold, rolls the dice; during the opening roll, its one die. |
| `MoveChecker(sessionId, seat, from, to)` | — | Backgammon: `seat` moves a checker, by one die or, for a combined move in `legalMoves`, by several. Points are numbered from that seat's side: 1 to 24 in the direction it moves, 25 for the bar, 0 for off the board. |
| `UndoMove(sessionId, seat)` | — | Backgammon: takes back the last checker `seat` moved in this turn. |
| `EndTurn(sessionId, seat)` | — | Backgammon: ends `seat`'s turn, once it has used as many dice as the rules require. |
| `OfferDouble(sessionId, seat)` | — | Backgammon: `seat` offers to double before rolling. |
| `TakeDouble(sessionId, seat)` / `DropDouble(sessionId, seat)` | — | Backgammon: `seat` takes the double (the cube doubles and it owns it) or drops it (losing the game at the cube's value). |
| `TakeChip(sessionId, seat, chip)` | — | The Gang: `seat`, which the caller must hold, takes a chip of the current stage, from the middle or from another player. |
| `ReturnChip(sessionId, seat)` | — | The Gang: puts `seat`'s chip back in the middle. |
| `SetReady(sessionId, seat, ready)` | — | The Gang: marks `seat` ready (or not) to end the stage, together with the caller's other seats. |
| `PassCard(sessionId, seat, cardIndex)` | — | The Gang, Coordinator: `seat` picks its card number `cardIndex` (from 0) to pass to the next player. |
| `ConfirmSeen(sessionId, seat)` | — | The Gang, Con Artist: `seat` (and the caller's other seats) has seen its cards. |
| `Propose(sessionId, seat, card, targetSeat, value)` | — | The Gang: `seat` proposes a choice for the group decision of `card` (e.g. `"mastermind"`): a seat and/or a value (a card value such as `"9"`, or a hand ranking such as `"Pair"`). Clears everyone's agreement. |
| `Agree(sessionId, seat, card, agree)` | — | The Gang: `seat` agrees (or no longer agrees) to the proposal, for all the caller's voting seats; once every voter agrees, the card takes effect. |
| `ShowCard(sessionId, seat, cardIndex, toSeat)` | — | The Gang, Informant: the chosen `seat` secretly shows its card number `cardIndex` to `toSeat`. |
| `DiscardCard(sessionId, seat, cardIndex)` | — | The Gang, Hacker and Jack: the chosen `seat` discards its card number `cardIndex`. |
| `ChooseJokerCard(sessionId, seat, index)` | — | The Gang, with jokers: `seat` keeps card `index` (0 or 1) of the two offered for its joker; picking an offered joker swaps it for two more cards. |
| `ConfirmRedraw(sessionId, seat)` | — | The Gang, Motion Detector and Laser Tripwires: `seat`, which got new pocket cards, confirms it. |
| `RevealNext(sessionId, revealed)` | — | The Gang: any player reveals the next hand, if `revealed` hands are revealed so far. |
| `NextHeist(sessionId, heist)` | — | The Gang: any player deals the heist after `heist`, once its hands are revealed. |
| `ThrowCards(sessionId, seat, cards)` | — | Rich man, poor man: `seat`, which the caller must hold, throws `cards` (short text such as `["9♥", "9♠"]`), all of one number. |
| `Pass(sessionId, seat)` | — | Rich man, poor man: `seat` passes its turn. The round's starter cannot pass. |
| `GiveCards(sessionId, seat, cards)` | — | Rich man, poor man, in the exchange: `seat` gives `cards` to its partner; the poorer player of a pair must give its highest cards. |
| `RequestSeat(sessionId)` / `CancelSeatRequest(sessionId)` | — | Rich man, poor man, in the exchange: the logged-in caller asks to join the next game, or withdraws the request. |
| `AcceptSeat(sessionId, playerId, place)` / `DeclineSeat(sessionId, playerId)` | — | Rich man, poor man, in the exchange: a player accepts the request of `playerId` at `place` (1 for #1), which deals the cards again for the new number of players, or declines it. |
| `RollDice(sessionId, seat)` | — | Wazoo: `seat`, which the caller must hold, rolls the dice that are not kept (all five on the turn's first roll), up to three times per turn. |
| `HoldDie(sessionId, seat, die, held)` | — | Wazoo: between rolls, `seat` keeps (`held` true) or lets go of die `die` (0 to 4). |
| `Score(sessionId, seat, category)` | — | Wazoo: `seat` scores the dice in box `category` (`"Ones"` … `"Sixes"`, `"ThreeOfAKind"`, `"FourOfAKind"`, `"FullHouse"`, `"SmallStraight"`, `"LargeStraight"`, `"Wazoo"`, `"Chance"`), which ends its turn. |
| `PlayCard(sessionId, seat, card, color)` | — | Final Card: `seat`, which the caller must hold, plays `card` (short text: the color's letter `R`, `Y`, `G` or `B` and `0`–`9`, `S` for Stop, `R` for Change Direction, `+2`, `+` for Plus or `T` for TAKI, e.g. `"R7"`, `"GS"`, `"B+2"`, `"Y+"`, `"RT"`; or without a color `"W"` (Change Color), `"W+4"` (Change Color +4), `"WT"` (SuperTAKI), `"WC"` (Crown), `"W+3"` (+3) and `"W+3X"` (+3 Breaker)), with `color` (`"Red"`, `"Yellow"`, `"Green"` or `"Blue"`) for a Change Color, a Change Color +4, or a SuperTAKI after a Crown (`anyCard`), otherwise `null`. After drawing, only the card drawn. In Israeli Style, any seat but the one of the +3 (`threeBy`) can play a +3 Breaker on it, in or out of turn. |
| `DrawCard(sessionId, seat)` | — | Final Card: with penalty cards to draw (`penaltyCards`), `seat` draws them all, at any time during a hand, and the turn does not change. Otherwise, on its turn: `seat` draws the cards stacked for it, or else one card (with the drawing option, cards until one can be played). If the card drawn can be played, `seat` plays it or keeps it; otherwise its turn ends. A seat with penalty cards can do nothing else until it draws them; the seat to play waits for them when the turn passed while they were owed (`waitingForPenalty`). In Israeli Style, the card drawn is never played on that turn, drawing is refused during a TAKI ("Close the TAKI first."), and on a +3 (`threeBy`) `seat` draws 3 cards, every other seat but the one of the +3 has 3 penalty cards to draw, and `seat` plays on. |
| `CloseTaki(sessionId, seat)` | — | Final Card, Israeli Style: `seat`, whose turn it is, closes its TAKI (`takiColor`; a `"ClosedTaki"` event): the action of the TAKI's last card is carried out, and the turn passes unless that card gives `seat` another turn (a Plus, or a Stop with two players). A TAKI also closes by itself when `seat` has no more card to play in it, or plays its last card. |
| `PassAfterDraw(sessionId, seat)` | — | Final Card: `seat` keeps the card it drew, which ends its turn. |
| `Challenge(sessionId, seat)` | — | Final Card, with the Change Color +4 option `"Challenge"`: `seat`, whose turn it is (`canChallenge`), challenges the Change Color +4 played on it, instead of drawing. If its player held a card of the color that was to play, they have the cards as penalty cards (`"ChallengeWon"`) and `seat` plays on; otherwise `seat` has them and 2 more as penalty cards and misses its turn (`"ChallengeLost"`). |
| `CallFinalCard(sessionId, seat)` | — | Final Card: `seat` calls Final Card, at any time during a hand. It protects `seat` when it holds one card, or two on its turn with one playable, and is not protected yet; and every other seat that holds one card unprotected (but the seat to play during its TAKI) has 2 penalty cards to draw (4 in Israeli Style; a `"Caught"` event each). Refused with "Nobody to catch." when it does neither. |
| `Discard(sessionId, seat, cards, takeOpen)` | — | Yaniv: `seat`, which the caller must hold, discards `cards` (short text such as `["5♣", "6♣", "JOKER"]`: one card, 2 to 4 of the same value, or 3 or more in a row of one suit, jokers standing for any card) and takes the open pile's top card (`takeOpen` true) or the closed pile's top card. The cards discarded then go on the open pile (`YanivCards.TryArrange`, mirrored by `arrange` in `yaniv.js`): a series with its jokers first, a straight from lowest to highest with spare jokers below it, or above it once it starts at the ace (the only way a joker ends up on top). |
| `CallYaniv(sessionId, seat)` | — | Yaniv: `seat` calls Yaniv on its turn, with a hand score of 7 or less; every other player then plays one last turn, and the round ends. |
| `PlayTrickCard(sessionId, seat, card)` | — | Trick-taking games: `seat`, which the caller must hold, plays `card` (short text such as `"10♥"`) to the trick on its turn. It must follow the suit led if it can ("You must follow suit."), and keep to the game's own rules. Once 4 cards are played, the highest trump (or else the highest card of the suit led) wins the trick, and its winner leads the next one. |
| `ClaimTricks(sessionId, seat)` | — | Trick-taking games, at the start of a trick: `seat`, which the caller must hold and which leads, claims the remaining tricks when its cards win every one of them whatever anyone plays, judged from its own cards and those already played ("You can claim only at the start of a trick.", "Your cards do not win every remaining trick."). It takes every card still in a hand, and the hand is scored. |
| `PassCards(sessionId, seat, cards)` | — | Hearts, while passing: `seat`, which the caller must hold, passes `cards` (3 of its cards, e.g. `["Q♠", "A♥", "K♥"]`). Once every seat has, each gets the cards passed to it, and the holder of the 2♣ leads. |
| `LookAtCards(sessionId, seat)` | — | Spades, while bidding with the Blind Nil option: `seat`, which the caller must hold, looks at its cards (its private `cards` are empty until then), so it can no longer bid Blind Nil. |
| `Bid(sessionId, seat, tricks, blind)` | — | Spades, while bidding: `seat`, whose turn it is to bid, bids `tricks` (1 to 13, or 0 for Nil with the option); `blind` for Blind Nil, before looking at its cards. After the fourth bid, the seat on the dealer's left leads. |

Seats are numbered from 0 in the API. Errors (e.g. "It is not your turn.") are returned as hub errors to the caller.

### Session state

After every change, the server sends a `StateUpdated` message with the public session state to everyone in the session. In games with private information (The Gang, Rich man, poor man, Final Card, Yaniv, Hearts, Spades), each player's connections then also receive a `PrivateStateUpdated` message with that user's own cards; nobody else receives it. `EnterSession` returns the same private information as `private`. Chat messages are not part of the session state: each one is sent once, as `ChatMessageReceived`. The public session state has these fields:

| Field | Description |
| --- | --- |
| `sessionId`, `gameType` | The session code and which game it is |
| `phase` | `"Lobby"` (session lobby), `"Playing"` (game screen) or `"Over"` (the game has ended by itself) |
| `minSeats`, `maxSeats` | Seats needed to start, and the number of seats a game can use with the chosen options (`seats` may have more) |
| `seats` | One entry per seat: `{ playerId, name, icon, status }`, or `null` when empty (Vacant). `status` is `"Present"`, `"Reconnecting"` or `"Away"`. `icon` is an emoji or a picture address starting with `/`. A page finds its user's seats by comparing `playerId` |
| `gameNumber` | 0 until the first game starts, then counts games |
| `absentSeats` | During a game, its seats that are absent (Reconnecting, Away or Vacant), in order: a user offered a seat can take one; otherwise empty |
| `waitingSeats` | During a game, the absent seats it waits for (its awaited seats, once all of them are absent), in order; otherwise empty. Its players then see the "Waiting for …" pop-up |
| `canSkipWaiting` | Whether `waitingSeats` can be skipped (`SkipPlayer`); otherwise the players can only end the game |
| `game` | Game-specific state, or `null` before the first game |
| `optionList` | The game's options: `{ key, label, choices: [{ value, label, hint, image }], default, kind, shownWhen }`, where `image` is a picture's address or `null`, `kind` is `"Choice"` or `"Set"` and `shownWhen` is `{ key, values }` (the option applies when option `key` has one of `values`) or `null` (empty for games without options) |
| `options` | The chosen value of each option, e.g. `{ "mode": "Basic" }`, used by the next new game |
| `isPublic` | Whether this is a public session |
| `hostPlayerId` | In a public session, the `playerId` of its host (the creator), who alone changes the options and starts the game from the lobby; otherwise `null` |
| `notice` | A message for everyone, e.g. "Ada left the game." after a player left a public game; cleared when the next game starts. Otherwise `null` |
| `observerCount` | The number of open connections to the session of users who hold no seat. Updated when one opens or closes the session, without saving it |

Game-specific state:

- **Backgammon:** the board from white's side (`points`: entry `i` is white's point `i + 1`, positive for white checkers and negative for black), `bar`, `off`, `pips` and `scores` by seat, the `phase` (`"Opening"`, `"Rolling"`, `"Doubled"`, `"Moving"`, `"GameOver"`, `"MatchOver"`), the seat to move (`turn`), each seat's opening die (`openingDice`, `null` until rolled) and the decided `openingRoll`, the `dice` and `diceLeft`, the number of rolls so far (`rolls`, so a page can tell a new roll), the `legalMoves` (moves by one die, then the combined moves of one checker by several dice; see `IBackgammonGame.CombinedMoves`) and the moves `played` so far (one per die, in the mover's numbering), the `lastTurn`, the cube (`cubeValue`, `cubeOwner`, `canDouble`, `isCrawford`) and the last game's `result`.
- **Rich man, poor man** (`RichManPoorManState` in `Sessions/RichManPoorManSession.cs`): the `players` in place order, each with `seat`, `place`, `cardCount`, `passed`, `roundThrow` (the cards they last threw in this round, or `null`; after a round ends, its `passed` and `roundThrow` stay until the next round's first throw), `finished`, `isActive`, `exchangeCount` and `hasGiven`; the `turn`, `currentThrow` and `lastThrow` (`{ seat, cards }`), the number of `throws` so far (so a page can tell a new throw), `lastRoundWinner`, `ranking` and the `joinRequests`. Each hand's private state (`RichManPoorManHand`) has its `cards`, and for the exchange its `partnerSeat`, the `requiredGift` (the poorer player's highest cards, only while exchanging), the `gift` it has given (until all have given) and the cards it `received`.
- **Wazoo** (`WazooState` in `Sessions/WazooSession.cs`): the `round`, the seat to play (`turn`), the `dice` (all 0 before the turn's first roll), which are `held`, the `rollsLeft` in the turn, the number of rolls so far (`rolls`, so a page can tell a new roll), the `possibleScores` of the seat to play (one per box in the order of `Score` above, `null` where it cannot score), the `players` in seat order (`seat`, `scores` per box, `upperTotal`, `upperBonus`, `lowerTotal`, `wazooBonus`, `total`), the `lastTurn` (`seat`, `category`, `points`, `bonus`, `dice`), `isOver` and the `winners` (seats).
- **Final Card** (`FinalCardState` in `Sessions/FinalCardSession.cs`): the rules (`style`: `"US"` or `"Israeli"`, `end`: `"OneGame"`, `"Score"` or `"Pyramid"`, `target`, `stacking`: `"None"`, `"TwoOnTwo"`, `"SameCard"` or `"FourOnTwo"`, `drawUntilPlayable`, `wildDrawFour` (Change Color +4): `"Always"`, `"NoColor"` or `"Challenge"`; the defaults are the official UNO rules: `"US"`, `"Score"`, 500, `"None"`, `false`, `"Challenge"`; in Israeli Style, `end` comes from the `israeliEnd` option, `"OneGame"` or `"Pyramid"`, with `"TwoOnTwo"`, `false` and `"Always"`), the `handNumber`, the `phase` (`"Playing"`, `"HandOver"`, `"GameOver"`), the seat to play (`turn`), the `direction` (1 in seat order, -1 after a Change Direction), the `topCard`, the `color` to play, the cards in the `drawPile`, the `pendingDraw` (cards stacked for the seat to play), `hasDrawn` (the seat to play has drawn a card it may play or keep), `canChallenge` (the seat to play can challenge the Change Color +4 played on it), `waitingForPenalty` (the seat to play waits for other seats to draw their penalty cards), in Israeli Style `takiColor` (the color of the TAKI the seat to play is running, or `null`), `threeBy` (the seat of the +3 nobody has answered yet, or `null`) and `anyCard` (a Crown leads, so any card can be played), the `players` in seat order (`seat`, `cardCount`, `score`, `handSize` (pyramid: the cards last dealt), `finished` (pyramid place, or `null`), `protected` (called Final Card and holds one card), `penaltyCards` (the penalty cards it has to draw)), the last action's `events` (`seat`, `kind`: `"TurnedUp"`, `"Played"`, `"Drew"`, `"Penalty"` (penalty cards to draw from a draw card, a +3 or a +3 Breaker), `"Skipped"`, `"Passed"`, `"CalledFinalCard"`, `"ClosedTaki"`, `"Caught"`, `"ChallengeWon"`, `"ChallengeLost"`, `"NewCards"` or `"Finished"`, and its `card`, `count`, chosen `color` and `bySeat`, for a challenge the player of the Change Color +4), the number of `actions` so far, the `lastResult` (`seat`, `points`), the pyramid's `ranking` and the `winner`. Each hand's private state (`FinalCardHand`) has its `cards` (sorted by color and value) and those that are `playable` now.
- **Yaniv** (`YanivState` in `Sessions/YanivSession.cs`): the rules (`end`: `"OneRound"` or `"Score"`, `target`, `bonus`), the `roundNumber`, the `phase` (`"Playing"`, `"RoundOver"`, `"GameOver"`), the seat to play (`turn`; after a round, its winner), the seat that called Yaniv (`caller`, or `null`) and the seats still to play their last turn (`lastTurns`), the open pile's top card (`openTop`, or `null` while it is empty), the cards in the `openPile` and the `closedPile`, the `players` in seat order (`seat`, `cardCount`, `score`, `isOut`), the `lastTurn` (`seat`, the `cards` discarded, `tookOpen`, the open card `taken` (`null` for a closed card, which is not shown), `skipped`), the number of `actions` so far, the `lastResult` (`callerSeat`, `winnerSeat`, `assaf`, and per player `seat`, `cards`, `handScore`, `points` gained, `bonus`, `score`, `isOut`) and the `winner`. Each hand's private state (`YanivHand`) has its `cards` (jokers first, then from ace to king), its `handScore` and `canCallYaniv`.
- **Hearts** (`HeartsState` in `Sessions/HeartsSession.cs`): the `target` (100), the `handNumber`, the `phase` (`"Passing"`, `"Playing"`, `"HandOver"`, `"GameOver"`), the `pass` (`"Left"`, `"Right"`, `"Across"` or `"Hold"`), the seat to play (`turn`), `heartsBroken`, the `trick` in progress and the `lastTrick` of the hand (`cards`: `[{ seat, card }]` in the order played, and `winnerSeat` once complete; `TrickState`), the `claim` of the hand's remaining tricks (the claimer's `seat`, and the `hands` every seat held then, `[{ seat, cards }]`, the claimer's first, then in turn order; `TrickClaimState`) or `null`, the `players` in seat order (`seat`, `cardCount`, `score`, `handPoints` taken in this hand, `hasPassed`, and the `heartsTaken` and whether they `tookQueen` (Q♠) in this hand), the number of `actions` so far, the `lastResult` (per player `seat`, `points` gained and `score`, and the `moonSeat` that shot the moon, or `null`) and the `winners` (the lowest scores, once the game is over). Each hand's private state (`HeartsHand`) has its `cards` (by suit: clubs, diamonds, spades, hearts; each from 2 to A), those that are `playable` now, the seat it passes to (`passTo`), the cards it `passed` (or `null`), those it `received`, and whether it `canClaim` the remaining tricks now. The page plays a breaking glass when `heartsBroken` turns true and a regal doom once the Q♠ is in the `trick` or `lastTrick` (or a player's `tookQueen` turns true, after a claim), for everyone with the page open (`playSound` in `common.js`).
- **Spades** (`SpadesState` in `Sessions/SpadesSession.cs`): the rules (`nil`, `blindNil`, the `target` 500), the `handNumber`, the `phase` (`"Bidding"`, `"Playing"`, `"HandOver"`, `"GameOver"`), the `dealer`, the seat to bid (`bidder`), the seat to play (`turn`), `spadesBroken`, the `trick` in progress and the `lastTrick` of the hand (`cards`: `[{ seat, card }]` in the order played, and `winnerSeat` once complete; `TrickState`), the `players` in seat order (`seat`, `team` 0 or 1, `cardCount`, `bid` (`null` before bidding, 0 for Nil), `blind`, the `tricks` taken), the `teams` (`team`, `seats`, `score`, `bags`), the number of `actions` so far, the `lastResult` (per team `team`, `bid` (without Nil bids), `tricks` (without its Nil bidders'), `made`, `points` gained, `newBags`, `bagPenalty`, `score`, `bags`; and the `nils`: `seat`, `blind`, `made`) and the `winner` (a team). Each hand's private state (`SpadesHand`) has its `cards` (by suit: clubs, diamonds, spades, hearts; each from 2 to A; empty while it has not `looked`), those that are `playable` now, and `looked`.

### Rejoining

Players are identified by their user ID. After a refresh or dropped connection, the page reconnects and calls `EnterSession`, and the player keeps their seats: in the session lobby, as long as they return within 60 seconds; during a game, at any time, unless someone has taken their seat.

## Login

Every user is logged in, at least as a guest. A user is identified by a signed **login token** holding their user ID, display name, icon and kind (`Guest`, `Registered`, or the provider, e.g. `Google`). The page keeps it in the browser tab's `sessionStorage`, so each tab can have its own user; a newly opened tab starts with the most recently logged-in user. The page sends the token as an `Authorization: Bearer` header, and as the `access_token` query parameter when connecting to the hub.

Saved users' tokens last 30 days. Every page load calls `GET /auth/me`, which returns a renewed token with the saved user's current name and icon (so changes made in another tab or device show up), and records the user as active at most once a day. If the saved user was deleted, `/auth/me` reports them as logged out.

**Guests** are stored only in their login token, in the browser's `localStorage` (and the tab's `sessionStorage`); nothing about them is on the server. Their tokens do not expire (`ExpiresUtc` is `DateTimeOffset.MaxValue`), and the 30-day deletion of inactive users applies only to saved users. When `/auth/me` reports no user (a first visit, cleared site data, a deleted saved user, or after logging out), the page calls `POST /auth/guest`, which creates a guest named `Guest-` and a random 3-digit number with a random built-in icon, and reloads the account.

**Seat transfer:** before logging in, creating an account, signing in with a provider or logging out, the page keeps the current token in `sessionStorage` (`previousToken`). After the reload, once it has the new token, it calls `POST /auth/transfer` with the previous token, before connecting to the hub. The server checks that the previous token is valid and has not expired (which proves it is the same person), then in every session in memory gives the previous user's seats, player ID, connection status (Reconnecting or Away) and any Rich man, poor man request to join to the new user, with the new name and icon (`GameSession.TransferSeats`), and broadcasts the new state. Sessions not in memory are not changed.

Tokens are encrypted and signed with keys stored in the `DataProtectionKeys` table, so they stay valid across restarts and deploys.

User IDs are a GUID for saved users and `guest:<random>` for guests.

External sign-in: the page sends the browser to `/auth/signin/{provider}` with a `returnUrl` containing `signin=external`. After sign-in, the provider's result is kept in a short-lived cookie; the returning page calls `POST /auth/external`, which finds or creates the user, exchanges the cookie for a login token and clears it. On the first sign-in, the profile dialog opens so the user can confirm their name and icon.

## Database

PostgreSQL on Neon, accessed with EF Core. Tables (created and updated automatically when the server starts):

| Table | Contents |
| --- | --- |
| `Users` | `Id`, `Username` / `NormalizedUsername` (registered users; unique regardless of case), `PasswordHash`, `DisplayName`, `Icon`, `CreatedAt`, `LastActiveAt`, `GameOrder` (the order of the games on the main screen, e.g. `Backgammon,TheGang`, or null), `ChatOn`, `SoundOn`, `CardStyle` (`New` or `Classic`; `New` for existing and new users) |
| `ExternalLogins` | `Provider` + `ProviderKey` (unique), `UserId` |
| `UserIcons` | Uploaded pictures: `Id`, `UserId`, `ContentType`, `Data`, `CreatedAt`. Served at `/api/icons/{id}`; a new upload gets a new ID |
| `UserGameOptions` | Saved game options: `UserId` + `GameType` (key), `Options` (JSON, e.g. `{"mode":"Advanced"}`), `UpdatedAt` |
| `Sessions` | Saved game sessions: `Id` (the code), `GameType`, `Version`, `Data` (JSON), `UpdatedAt` (the last activity). See [Sessions and seats](#sessions-and-seats) |
| `GamePlays` | Games started: `Day` (UTC) + `GameType` (key), `Count`. Nothing about the players or sessions |
| `ActivityCounts` | Anonymous activity: `Day` (UTC) + `Event` + `GameType` (or `""`) + `Detail` (or `""`) (key), `Count`. See **Activity** above |
| `DataProtectionKeys` | The keys that encrypt and sign login tokens |

Deleting a user also deletes their external logins, icons and saved game options. To change the tables, edit the classes in `Data/` and run `dotnet ef migrations add <Name> --project GamesLane.Server --output-dir Data/Migrations` (first `dotnet tool restore`, and set `$env:ASPNETCORE_ENVIRONMENT = "Development"` so the connection string is read from user secrets). The earlier migrations were merged into one, `InitialCreate`, which creates every table; it keeps the first migration's ID, so databases created before the merge treat it as already applied. New migrations go on top of it.

To browse the database, use the Neon console's SQL Editor, pgAdmin, DBeaver, or the PostgreSQL extension for VS Code (not SSMS, which is for SQL Server only). Table and column names are case-sensitive, e.g. `select * from "Users"`.

## Game options

Each option has a key, a label and a list of choices with a default; `Games/GameOptions.cs` lists them per game. A **choice** option's value is one of its choices. A **set** option's value lists the choices turned off, separated by commas, so choices added later (e.g. new cards) are on by default. An option can apply only when another option, listed before it, has one of some values and applies itself (`shownWhen`, a `GameOptionCondition` with the key and the values; e.g. Final Card's `target` needs `end` `"Score"`, which needs `style` `"US"`). Chosen values are checked against the game's options: unknown options or values are refused, and options left out get their default. Saved preferences that no longer match (e.g. after an option was removed) fall back to the defaults. On the pages, `optionsList(state)` in `common.js` shows the chosen options as "Game options:" and a bulleted list (`optionLines(state)`, one text per option that applies), aligned to the start: in the session lobby's options button, and during a game in `#game-options`, which `startSessionPage` fills on every game page (each page has the element at the bottom of the part of the table players can hide, so it hides with it: The Gang's `#table-body`, and `#rp-table-body`, `#final-card-table-body` and `#yaniv-table-body`; in games without such a table, at the bottom of `#game-screen`; it stays hidden for a game without options).

## HTTP endpoints

| Endpoint | Description |
| --- | --- |
| `GET /api/sessions/{code}` | `{ sessionId, gameType, page }` for an existing private session, or 404 (also for public sessions) |
| `GET /join/{code}` | Shareable join link; redirects to the private session's page (`<page>?session=CODE`), counting `OpenInvite`, or if there is no such private session, to the main screen with the code filled in and a message |
| `GET /auth/me` | `{ signedIn, kind, name, icon, uploadedIcon, gameOrder, settings, accessToken, providers, icons, feedback, isAdmin }`. `gameOrder` is a saved user's order of the games (game names), or `null`; `settings` is a saved user's `{ chat, sound, cards }`, or `null` for guests (whose settings stay in their browser); `accessToken` is a renewed login token; `providers` lists the external sign-in options that are configured; `icons` lists the built-in icons; `feedback` tells whether comments can be sent (see [Feedback setup](#feedback-setup)); `isAdmin` tells an admin (see **Admins** above) |
| `POST /auth/guest` | Logs in as a new guest named `Guest-` and a random 3-digit number (e.g. `Guest-042`), with a random built-in icon. The token does not expire |
| `POST /auth/transfer` | Body `{ previousToken }`, logged-in users only. Gives the seats of the user in `previousToken` to the current user in every session in memory (204), or `{ error }` (400) if the token is not valid or has expired |
| `POST /auth/register` | Body `{ username, password, name, icon }`. Creates a registered user and logs in. Rate-limited |
| `POST /auth/login` | Body `{ username, password }`. Logs in a registered user. Rate-limited (10 attempts per minute per address) |
| `POST /auth/profile` | Body `{ name, icon }`, logged-in users only. Changes the display name and icon (a built-in icon, or the user's uploaded picture) |
| `POST /auth/icon` | Body: a PNG, JPEG or WebP picture (up to 200 KB). Saved users only. Uploads it and makes it the icon |
| `GET /api/icons/{id}` | An uploaded picture |
| `GET /auth/signin/{provider}?returnUrl=/...` | Starts sign-in with `google`, `microsoft` or `discord` |
| `POST /auth/external` | Exchanges the result of an external sign-in for `{ accessToken, isNew }` |
| `GET /api/options/{gameType}` | Saved users only. `{ saved, options }`: the user's saved options for the game, or its defaults (`saved: false`) |
| `PUT /api/options/{gameType}` | Saved users only. Body: the options, e.g. `{ "mode": "Advanced" }`. Saves them as the user's preferences (204), or `{ error }` (400) |
| `GET /api/games/popular` | Everyone. The game names, most played in the last 30 days first (ties in the usual order); no counts |
| `GET /api/admin/stats?days=N` | Admins only (others: 404). N from 1 to 365 (30 by default; otherwise `{ error }`, 400). `{ days, games: [{ gameType, counts, total }] }`: the last N days (UTC, today last) and, per game, its count each day and its total |
| `GET /api/admin/activity?days=N` | Admins only (others: 404). N as for `/api/admin/stats`. `{ days, visits, games: [{ gameType, opened, created, joined, started, finished, invitesOpened, invitedJoins }], returning, sources, languages }`: the visits each day, each game's totals (in the usual order), and `[{ name, count }]` visits by return bucket (in order), source and language (most first) |
| `POST /api/visits` | Logged-in users (guests too; others: 401). Body `{ game, firstToday, source, language, daysSinceLastVisit }` (see **Activity** above). Adds the counts (204). Rate-limited (30 per hour per user) |
| `PUT /api/game-order` | Saved users only. Body: game names in the order to show them, e.g. `["Backgammon", "TheGang"]`, each existing and listed once. Saves the order (204), or `{ error }` (400) |
| `PUT /api/settings` | Saved users only. Body `{ chat, sound, cards }`: the profile's **Chat in games** and **Sounds** settings (both `true` for new users) and **Card style** (`"New"`, the default when left out, or `"Classic"`). Saves them (204), or `{ error }` (400), e.g. for an unknown card style |
| `POST /api/feedback` | Body `{ type, text, includeName, page, language }`, logged-in users only (guests too). `type` is `"Problem"`, `"Idea"` or `"Other"`; `text` has 1 to 2000 characters. Posts the comment as a GitHub issue and returns `{ url }`, its address. `{ error }` (400) for an invalid comment, 404 when feedback is not configured, 429 after 5 comments in an hour from the same user, `{ error }` (502) if GitHub refused or could not be reached |
| `GET /health` | Health check |

All the `POST /auth/...` endpoints except `/auth/external` and `/auth/transfer` return `{ accessToken }` on success, or `{ error }` (status 400) with a message to show.

Static files are served with `Cache-Control: no-cache`, so browsers check for a newer version and a deploy takes effect immediately.

## Sign-in setup

Guests and username logins always work. Each external provider appears on the site only when its client ID and secret are configured. Set them as environment variables (on Render: **Environment** tab) — never commit them to the repo:

| Provider | Variables | Where to get them | Redirect URI to register |
| --- | --- | --- | --- |
| Google | `Authentication__Google__ClientId`, `Authentication__Google__ClientSecret` | [Google Cloud Console](https://console.cloud.google.com/apis/credentials) → Create credentials → OAuth client ID → Web application | `https://<your-site>/signin-google` |
| Microsoft | `Authentication__Microsoft__ClientId`, `Authentication__Microsoft__ClientSecret` | [Microsoft Entra admin center](https://entra.microsoft.com/) → App registrations → New registration, choosing "personal Microsoft accounts" plus work/school accounts; then Certificates & secrets → New client secret | `https://<your-site>/signin-microsoft` |
| Discord | `Authentication__Discord__ClientId`, `Authentication__Discord__ClientSecret` | [Discord Developer Portal](https://discord.com/developers/applications) → New Application → OAuth2 (Client ID, Reset Secret) | `https://<your-site>/signin-discord` |

For local testing, use `dotnet user-secrets` in `GamesLane.Server` and register `https://localhost:<port>/signin-google` (or `-microsoft`, `-discord`) as an extra redirect URI.

Discord sign-in (the `AspNet.Security.OAuth.Discord` package) asks only for the `identify` scope. The display name defaults to the user's Discord name (`global_name`, or the username if not set), and the icon to their avatar (`AuthSetup.DiscordAvatarUrl`, from Discord's CDN).

## Feedback setup

The feedback form (**Send feedback** on the main screen and in the game menu) posts each comment as an issue in the GitHub repository. It appears only when the server has a GitHub token; the token stays on the server and is never sent to pages.

1. On GitHub: **Settings → Developer settings → Personal access tokens → Fine-grained tokens → Generate new token**. Give it a name (e.g. "GamesLane feedback") and an expiry date, choose **Only select repositories** and pick this repository, and under **Repository permissions** set **Issues** to **Read and write** (nothing else). Copy the token.
2. On Render: **Environment** tab of the service → add `Feedback__GitHubToken` with the token, and save (the service restarts). Never commit the token to the repo.
3. Optional: `Feedback__GitHubRepository` (`owner/name`) sends the issues to another repository; by default they go to `inativ01/GamesLane2.0`.
4. When the token expires, create a new one and replace the variable; until then, sending fails with "Your comment could not be sent. Try again later."

Each issue's title is the kind and the start of the comment (e.g. "[Idea] Add chess"); its text is the comment, then the kind, page, language and the sender's display name (or "name not shared"). Issues get the label `feedback`, plus `bug` for a problem or `enhancement` for an idea. Mentions (@name) in comments are broken up so they notify nobody. The repository is public, so the issues are public; the form says so. For local testing, use `dotnet user-secrets set 'Feedback:GitHubToken' '<token>'` (better with a test repository in `Feedback:GitHubRepository`).

## Running locally

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) and a PostgreSQL connection string (the Neon `dev` branch), set once with:

```
cd GamesLane.Server
dotnet user-secrets set 'ConnectionStrings:GamesLane' '<connection string>'
cd ..
dotnet run --project GamesLane.Server
```

- Web page: `/` (the address is shown in the console, e.g. `http://localhost:5xxx`)
- Join link: `/join/{code}` opens the session's page
- Health check: `GET /health`
- Cards page: `/cards.html`
- SignalR hub: `/hubs/game`

To try several players on one computer, use a separate tab for each user. To play from phones on the same Wi-Fi, run the server with `--urls http://0.0.0.0:5000` and open `http://<computer's IP address>:5000`.

## Running tests

```
dotnet test
```

The scripts in `wwwroot` have no build step; `node --check <file>.js` checks a script for syntax errors.

## Project status and external services

_Last updated: 2026-11-05. Keep this section current; it is the hand-off point between work sessions._

### Local machine
- Repo folder: being moved out of iCloud Drive (it was `C:\Users\inati\iCloudDrive\Programming\GamesLane2.0`). In iCloud, sync locked files during moves, and once Git's index lost most of the tracked files, so a commit deleted them (fixed in `f82caee`). Before every push, check the full file list of the commit (`git show --stat HEAD`).
- Installed: Git (user Isaac Nativ), GitHub CLI (`gh`, signed in), .NET 10 SDK, Node.js (useful for `node --check` on the scripts in `wwwroot`). Not installed: Docker.
- The original JavaScript/Firebase app is in the sibling folder `GamesLane` (github.com/inativ01/GamesLane). It is separate; do not modify it.

### GitHub
- Repo: https://github.com/inativ01/GamesLane2.0 (public), branch `main`.
- Every push to `main` triggers a Render deploy.

### Render (hosting)
- Live site: https://gameslane.onrender.com
- Free web service, built from the `Dockerfile` at the repo root; auto-deploys from `main`.
- Server reads the `PORT` setting; health check at `/health`.
- Free tier: sleeps after 15 min idle (~1 min to wake). Sessions are saved in the database, so games survive restarts and deploys (unless a deploy changes a game's saved fields, see [Sessions and seats](#sessions-and-seats)); logins survive too.
- Environment variables (values only in Render, never in the repo):
  - `ConnectionStrings__GamesLane`: Neon `production` branch (set; required — the server does not start without it). Either the .NET (`Host=...;`) or the URL (`postgresql://...`) format works.
  - `Authentication__Google__ClientId` / `__ClientSecret`: set, working
  - `Authentication__Microsoft__ClientId` / `__ClientSecret`: not set yet
  - `Authentication__Discord__ClientId` / `__ClientSecret`: not set yet

### Neon (PostgreSQL database)
- Project `GamesLane` (ID `gentle-cherry-51286211`), region AWS US East 2 (Ohio).
- Branches: `production` (the live site) and `dev` (local development; its connection string is in `dotnet user-secrets` as `ConnectionStrings:GamesLane`).
- Free plan: 1 GB, sleeps after 5 min idle and wakes automatically, 6-hour restore window.
- Tables are created by EF Core migrations when the server starts (see [Database](#database)).

### Sign-in providers
- **Google:** working on the live site. OAuth client in Google Cloud Console (project "GamesLane"); redirect URI `https://gameslane.onrender.com/signin-google`; privacy policy at `/privacy.html`.
- **Microsoft:** code ready; app registration in the Entra admin center not done yet (redirect URI `https://gameslane.onrender.com/signin-microsoft`).
- **Discord:** code ready; application in the Discord Developer Portal not created yet (redirect URI `https://gameslane.onrender.com/signin-discord`).
- **Apple:** deferred (needs a paid Apple Developer membership).

### Next steps
1. Linking a Google account to a registered user (deferred).
2. Microsoft and Discord sign-in: app registration (Entra admin center, Discord Developer Portal) and the environment variables on Render.
3. Game history (deferred). Visitor countries need a location lookup (e.g. MaxMind GeoLite2) or Cloudflare in front of Render; until then the browser language stands in.
4. Backgammon: first play-test on phones and a TV; possible extras later (a move list, variants such as Nackgammon or Acey-deucey).
5. Rich man, poor man: first play-test with real players on phones and a TV.
6. Hebrew: every page is translated; first check on Render, and the user to review the terms listed under [Translations](#translations).
7. Wazoo: first play-test.
8. Final Card: first play-test.
9. Spanish, French and Portuguese: every page is translated; first check on Render (including long texts on phones), and native speakers to review the terms listed under [Translations](#translations).
10. Feedback: create the GitHub token and set `Feedback__GitHubToken` on Render (see [Feedback setup](#feedback-setup)), then send a first comment.
11. Yaniv: first play-test.
12. Simplified and Traditional Chinese: every page is translated; first check on Render (including the fonts on phones and reading aloud), and native speakers to review the terms listed under [Translations](#translations).
13. Hearts: first play-test.
13. Spades: first play-test.

## Working conventions
- Ask before adding or upgrading packages.
- Interfaces go in an `Interfaces/` subfolder of each folder, in the parent folder's namespace.
- When asked to "not commit", leave changes uncommitted for review. For larger features done unattended, commit and push each completed step.
- Never put secrets in the repo, chat or commands; they live only in Render's Environment tab (or `dotnet user-secrets` locally).
- Keep the documentation current: user-visible behaviour and game rules in the [user manual](user-manual.md), design, APIs and setup in this guide, and only the overview in the [README](../README.md).

## Known limitations
- Sessions are kept in memory by one server, and saved in the database; running more than one server instance would need a shared store (no scale-out).
- A guest exists only in the browser where they were created; clearing the browser's site data (or losing the server's token keys) ends them, and the next visit creates a new guest.
- Forgotten passwords cannot be reset (no email is collected).
- The login-token keys are stored unencrypted in the database (the server logs a warning about this at startup), so the connection string must be kept secret.
- Seats move to the new user after logging in or out only in sessions that are in memory at the time; a seat in a session nobody has open stays with the previous user.
