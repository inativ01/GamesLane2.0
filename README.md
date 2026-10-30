# GamesLane 2.0

GamesLane 2.0 is an online platform for playing classic board and card games with friends and family, wherever they are.

It is a complete reimplementation of the original [GamesLane](https://github.com/inativ01/GamesLane), with a new backend built on .NET and written in C#.

## How It Works


### Users
- A **user** is a person who is logged in. At most one user is logged in per browser tab; there may be no user logged in. Different tabs may have different users.
- A user has a **user ID**, a **display name** and an **icon** (a built-in emoji, or an uploaded picture).
- There are three ways to log in:
  - **Guest:** just a name (and a built-in icon). Guests are not saved in the database.
  - **Registered user:** a unique username (3–20 letters, digits or `_`, any case) and a password (at least 8 characters), plus a display name and icon. Passwords cannot be reset, since no email is collected.
  - **Google** (or Microsoft): the account is saved by provider and account ID. The display name and icon default to the Google name and picture; changing them affects only GamesLane.
- Registered and Google users can upload a picture as their icon; the page shrinks it to 128×128 before uploading.
- A login lasts 30 days after the last visit. Saved users who are inactive for 30 days are deleted automatically.
- Changing the display name or icon also updates it on any seat the user holds.

### Lobby (main screen)
- A list of games. A new session can be started only when a user is logged in.
- A way to join a session by entering its code. This does not require a logged-in user.
- A **Login** button, shown only when no user is logged in, and a **Logout** button, shown only when a user is logged in.

### Sessions
- A **session** is one instance of a specific game, identified by a unique code.
- A session can be created from the lobby, joined by entering its code, or opened directly from its URL. Joining does not require a logged-in user.
- A session is either **inactive** (showing the session lobby) or **active** (showing the game screen).
- The game code is shown in the game's banner at the top of the session's page; clicking it copies it to the clipboard.
- Once nobody has the session open (players or spectators) and no seat is taken, the session is deleted after 30 seconds. Its code then no longer works.

### Session lobby (inactive session)
- A screen shared by all games, showing the game's **seats**.
- Only a logged-in user can take a seat. A user may take more than one seat (and then sees the private information of all of them).
- A user can leave a seat. A seat whose user has disconnected can be taken by another user.
- A user who loses their connection (closed tab, phone asleep, network lost) keeps their seats for 30 seconds and gets them back if they return in time. After that, the seats are freed.
- Creating a session does not seat the creator; they join the session and can then take a seat.
- Each game defines its maximum number of seats (up to 12) and the minimum needed to start (which may equal the maximum). Current games have 2 seats, both required.
- When the minimum number of seats is taken, a **Start** button appears. Any seated user can start the game.
- If an unfinished game was interrupted, the seated users can either **Resume** it or start a **New game** (once the minimum number of seats is taken).
- Anyone not seated (logged in or not) is a **spectator**.

### Game screen (active session)
- Once the game starts, the session lobby is replaced by a game-specific screen.
- Seated players may see a different version of the screen than spectators (for example, their own cards in hand). Spectators never see any player's private information.
- Anyone can watch an active game. Taking a seat in an active game is normally not allowed, but a game may allow it.
- A seated user can **stand up** (become a spectator) or **leave** (go back to the main lobby, giving up their seats).
- If players leave during a game and at least the minimum number remain, the game may continue (a per-game option). Otherwise the game pauses and the session goes back to the session lobby.
- When a game is resumed, anyone who took a seat in the meantime continues the game in place of the previous user.
- When a game ends, anyone seated can choose **Play again** while at least the minimum number of players are seated, or go **Back to session lobby** (for everyone). A new game is played by whoever is seated (any number between the minimum and the maximum). If fewer than the minimum remain, the session goes back to the session lobby automatically.
- Rock-paper-scissors never ends by itself; players stand up or leave when they are done.

A typical setup is a shared screen (such as a TV or laptop) connected as a spectator to show the board, while each player uses their own phone to view their hand and play.

## Technology

- **Backend:** .NET (C#), ASP.NET Core
- **Real-time updates:** SignalR
- **Frontend:** plain HTML, CSS and JavaScript, served by the backend
- **Tests:** xUnit

## Project Structure

```
GamesLane.slnx                 Solution file
GamesLane.Server/              ASP.NET Core backend
  Auth/AuthSetup.cs            Login tokens, rate limiting, Google/Microsoft sign-in
  Auth/AccountEndpoints.cs     Guest login, registration, login, profile, icon and saved game options endpoints
  Data/                        Database (EF Core + PostgreSQL): tables, migrations, connection setup
  Users/                       User rules, user service, saved game options, and deletion of inactive users
  Hubs/GameHub.cs              SignalR hub for creating, joining and playing sessions
  Hubs/DisconnectedSeatsMonitor.cs  Frees the seats of users disconnected for over 30 seconds; deletes abandoned sessions
  Sessions/GameSession.cs      Seats, session lobby and game flow shared by all games
  Sessions/                    Game-specific sessions and the in-memory session store
  Games/GameOptions.cs         Game options (rule variants): each game's options, and checking chosen values
  Games/TicTacToe/             Tic-tac-toe rules
  Games/RockPaperScissors/     Rock-paper-scissors rules
  Games/TheGang/               The Gang rules: cards, poker hand ranking, heists
  wwwroot/                     Web pages
    index.html, home.js        Main screen: start a session or join one by code
    common.js                  Shared: login dialog and account bar, hub connection, session lobby and page framework
    tictactoe.html/.js         Tic-tac-toe page
    rock-paper-scissors.html/.js  Rock-paper-scissors page
    the-gang.html/.js          The Gang page
    images/                    Logo and main-screen background (small copies of the originals), game pictures (SVG)
GamesLane.Server.Tests/        Unit tests
```

## Games

- **Tic-tac-toe** (2 players): after each board, the wins and draws are kept and players start the next round. The starting player alternates: X starts the odd rounds, O the even ones.
- **Rock-paper-scissors** (2 players): both players pick secretly. When both have picked, the picks are revealed and the winner scores a point. Play continues for as many rounds as you like.
- **The Gang** (3–6 players), the cooperative poker game by John Cooper and Kory Heath. Each heist:
  - Every player gets 2 private cards, shown only on their own screen. Five shared cards are dealt: none at first, then 3, 4 and 5 as the stages go on.
  - Four betting stages (white, yellow, orange, red). In each, every player takes a numbered chip, from 1 (weakest hand) up to the number of players (strongest), from the middle or from another player. Tapping your own chip returns it to the middle. Next to the player's cards are the chips they took in this heist's earlier stages. The stage ends when every player holds a chip and has pressed **Ready** (the 👍 button); any chip change clears everyone's Ready.
  - Players work out their hand's strength themselves; its name is only shown when the hands are revealed.
  - Hands are then revealed one at a time, from the lowest red chip to the highest, with each hand's name. If any hand is weaker than the one before it, the alarm goes off; otherwise the vault opens. Equal hands may be in either order.
  - 3 vaults win the game; 3 alarms lose it.
  - **Advanced mode** (option **Mode**; Basic is the default) adds challenge and specialist cards. The first heist gets 0–3 challenge cards (option, default 1); after a successful heist, one challenge card is added, and after a failed heist, one specialist card. Cards stay in play for the rest of the game. They are drawn at random from the enabled ones (all by default; players can turn cards off), each at most once per game. As in the original game, any combination can come up; with Quick Access (no white chips), Motion Detector and Laser Tripwires have no effect. The option **Cards that cancel each other** (Allow by default, or Avoid) keeps such pairs from being in play together. The cards in play lie on the right of the table (at the bottom on narrow screens), with the ones added for this heist marked **New**. With both Investor and Math Whiz used, both facts are shown. The full card texts are in `docs/the-gang/`.
  - All 10 challenge cards and all 10 specialist cards are included. Motion Detector and Laser Tripwires are checked when the first 3 shared cards appear; the player who gets new private cards is shown to everyone. Locked chips (Noise Sensors, Ventilation Shaft) stay with whoever takes them first: they cannot be taken, swapped or returned. With Blackout, earlier stages' chips are hidden. Investor and Math Whiz show every player's count or total to everyone. Coordinator and Con Artist pause the betting for a step: everyone picks a card to pass to the next player (it moves once all have picked), or confirms they have seen their cards (then all private cards are shuffled and dealt again).
  - **Specialist cards are optional.** The group may use any of them, in any order, at any time during the betting, each at most once per heist; when everyone is ready in the red stage, the hands are revealed however many are left unused. Next to a player's hand, each unused specialist card is a folded panel. Cards without a choice (Investor, Math Whiz, Coordinator, Con Artist) take effect once everyone presses the 👍.
  - **Group decisions** (Informant, Getaway Driver, Mastermind, Hacker, Jack, Muscle, Retina Scan, Fingerprint Scan): any player proposes a player and/or a value; everyone sees it, and it takes effect once every voter has pressed the 👍 (any change clears the agreement). Informant's player then secretly shows one card to one other player; Hacker's player draws a card and Jack's player gets the suitless Jack card (never part of a flush), and each then discards one; the stage cannot end until they have. Getaway Driver and Mastermind show the answer to everyone. Muscle's hand beats every hand of the same ranking at the reveal. The scans are decided just before the strongest hand is revealed, by everyone except the user who holds that hand's seat, for all of their seats, since that hand is on their screen (if one user plays every seat, only the strongest hand's seat sits out); a wrong guess fails the heist.
  - **Spectator screen** (e.g. a TV): the whole table fits on the screen without scrolling. The table is sized to the largest that fits, so it grows on large screens and shrinks on small ones; on landscape screens the heist's cards are in a column on the right, and on very wide screens (or without heist cards) the chips and the revealed hands sit side by side.
  - Not yet included: in-game chat.

### Game options

A game can offer **options**, i.e. rule variants (so far only The Gang's). Each option has a key, a label and a list of choices with a default; `Games/GameOptions.cs` lists them per game. A **choice** option is one of its choices. A **set** option (e.g. The Gang's challenge cards) is a list of tick boxes, all on by default; its value lists the choices turned off, separated by commas, so cards added later are on by default. An option can apply only when another option has a certain value (`shownWhen`, e.g. the card options only in Advanced mode).

- In the session lobby, everyone sees the chosen options; seated players can change them with **Game options**. They apply to the next new game; a paused game keeps the options it started with.
- Saved users (registered or Google, not guests) can **save** the options as their preferences for that game, and **load** them back in the dialog. A session starts with its creator's saved preferences.
- Chosen values are checked against the game's options: unknown options or values are refused, and options left out get their default. Saved preferences that no longer match (e.g. after an option was removed) fall back to the defaults.

## Game Hub API

Clients connect to the SignalR hub at `/hubs/game` and call these methods:

| Method | Returns | Description |
| --- | --- | --- |
| `CreateSession(gameType)` | session code | Logged-in users only. Creates a session (`"TicTacToe"`, `"RockPaperScissors"` or `"TheGang"`) and returns its 6-character code. The creator is not seated. The session starts with the creator's saved options for the game, if any. |
| `EnterSession(sessionId)` | `{ state, playerId }` | Subscribes to the session's updates. For a logged-in user, reconnects them to their seats, updates their name on them, and returns their `playerId`; otherwise `playerId` is `null`. Pages call this when they open and after reconnecting. |
| `TakeSeat(sessionId, seat)` | — | Logged-in users only. Takes an empty seat, or the seat of a disconnected user, under the user's display name. Only in the session lobby. |
| `LeaveSeat(sessionId, seat)` | — | Gives up one of the caller's seats (used for **Leave seat** and **Stand up**). |
| `LeaveSession(sessionId)` | — | Stops receiving updates and gives up all the caller's seats (used when going back to the main screen). |
| `NewGame(sessionId, gameNumber)` | — | Seated users only. Starts a new game for everyone seated, replacing game `gameNumber` (0 before the first game). Refused while a game is in progress. If that game was already replaced, nothing changes. |
| `SetOptions(sessionId, options)` | — | Seated users only, in the session lobby. Sets the options for the next new game, e.g. `{ "mode": "Advanced" }`. Options left out get their default. |
| `ResumeGame(sessionId)` | — | Seated users only. Continues the paused game; whoever now holds each of its seats plays that seat. |
| `ReturnToLobby(sessionId)` | — | Seated users only. After a game has ended, takes everyone back to the session lobby. |
| `MakeMove(sessionId, seat, cell)` | — | Tic-tac-toe: places the mark of `seat` (0 is X, 1 is O), which the caller must hold. Cells are numbered 0–8, left to right, top to bottom. |
| `Pick(sessionId, seat, choice)` | — | Rock-paper-scissors: picks `"Rock"`, `"Paper"` or `"Scissors"` for `seat`, which the caller must hold. |
| `NextRound(sessionId, round)` | — | Rock-paper-scissors and tic-tac-toe: starts the round after `round`. If it has already started, nothing changes. |
| `TakeChip(sessionId, seat, chip)` | — | The Gang: `seat`, which the caller must hold, takes a chip of the current stage, from the middle or from another player. |
| `ReturnChip(sessionId, seat)` | — | The Gang: puts `seat`'s chip back in the middle. |
| `SetReady(sessionId, seat, ready)` | — | The Gang: marks `seat` ready (or not) to end the stage. |
| `PassCard(sessionId, seat, cardIndex)` | — | The Gang, Coordinator: `seat` picks its card number `cardIndex` (from 0) to pass to the next player. |
| `ConfirmSeen(sessionId, seat)` | — | The Gang, Con Artist: `seat` has seen its cards. |
| `Propose(sessionId, seat, card, targetSeat, value)` | — | The Gang: `seat` proposes a choice for the group decision of `card` (e.g. `"mastermind"`): a seat and/or a value (a card value such as `"9"`, or a hand ranking such as `"Pair"`). Clears everyone's agreement. |
| `Agree(sessionId, seat, card, agree)` | — | The Gang: `seat` agrees (or no longer agrees) to the proposal; once every voter agrees, the card takes effect. |
| `ShowCard(sessionId, seat, cardIndex, toSeat)` | — | The Gang, Informant: the chosen `seat` secretly shows its card number `cardIndex` to `toSeat`. |
| `DiscardCard(sessionId, seat, cardIndex)` | — | The Gang, Hacker and Jack: the chosen `seat` discards its card number `cardIndex`. |
| `RevealNext(sessionId, revealed)` | — | The Gang: any player reveals the next hand, if `revealed` hands are revealed so far. |
| `NextHeist(sessionId, heist)` | — | The Gang: any player deals the heist after `heist`, once its hands are revealed. |

After every change, the server sends a `StateUpdated` message with the public session state to everyone in the session. In games with private information (The Gang), each player's connections then also receive a `PrivateStateUpdated` message with that user's own cards; nobody else receives it. `EnterSession` returns the same private information as `private`. The public session state has these fields:

| Field | Description |
| --- | --- |
| `sessionId`, `gameType` | The session code and which game it is |
| `phase` | `"Lobby"` (session lobby) or `"Playing"` (game screen) |
| `minSeats`, `maxSeats` | Seats needed to start, and the number of seats |
| `seats` | One entry per seat: `{ playerId, name, icon, isConnected }`, or `null` when empty. `icon` is an emoji or a picture address starting with `/`. A page finds its user's seats by comparing `playerId` |
| `gameNumber` | 0 until the first game starts, then counts games |
| `isGameOver` | The current game has ended |
| `canResume` | In the lobby with an unfinished, paused game |
| `game` | Game-specific state, or `null` before the first game |
| `optionList` | The game's options: `{ key, label, choices: [{ value, label, hint }], default, kind, shownWhen }`, where `kind` is `"Choice"` or `"Set"` and `shownWhen` is `{ key, value }` or `null` (empty for games without options) |
| `options` | The chosen value of each option, e.g. `{ "mode": "Basic" }`, used by the next new game |

In rock-paper-scissors, the game state only shows whether each seat has picked; the picks themselves are revealed once both have picked. Errors (e.g. "It is not your turn.") are returned as hub errors to the caller.

### Rejoining

Players are identified by their user ID. After a refresh or dropped connection, the page reconnects and calls `EnterSession`, and the player keeps their seats, as long as they return within 30 seconds.

### Login

A logged-in user is identified by a signed **login token** holding their user ID, display name, icon and kind (`Guest`, `Registered`, or the provider, e.g. `Google`). The page keeps it in the browser tab's `sessionStorage`, so each tab can have its own user; a newly opened tab starts with the most recently logged-in user. The page sends the token as an `Authorization: Bearer` header, and as the `access_token` query parameter when connecting to the hub. Logging out simply forgets the token in that tab.

Tokens last 30 days. Every page load calls `GET /auth/me`, which returns a renewed token with the saved user's current name and icon (so changes made in another tab or device show up), and records the user as active at most once a day. If the saved user was deleted, `/auth/me` reports them as logged out.

Tokens are encrypted and signed with keys stored in the `DataProtectionKeys` table, so they stay valid across restarts and deploys.

User IDs are a GUID for saved users and `guest:<random>` for guests.

External sign-in: the page sends the browser to `/auth/signin/{provider}` with a `returnUrl` containing `signin=external`. After sign-in, the provider's result is kept in a short-lived cookie; the returning page calls `POST /auth/external`, which finds or creates the user, exchanges the cookie for a login token and clears it. On the first sign-in, the profile dialog opens so the user can confirm their name and icon.

### Database

PostgreSQL on Neon, accessed with EF Core. Tables (created and updated automatically when the server starts):

| Table | Contents |
| --- | --- |
| `Users` | `Id`, `Username` / `NormalizedUsername` (registered users; unique regardless of case), `PasswordHash`, `DisplayName`, `Icon`, `CreatedAt`, `LastActiveAt` |
| `ExternalLogins` | `Provider` + `ProviderKey` (unique), `UserId` |
| `UserIcons` | Uploaded pictures: `Id`, `UserId`, `ContentType`, `Data`, `CreatedAt`. Served at `/api/icons/{id}`; a new upload gets a new ID |
| `UserGameOptions` | Saved game options: `UserId` + `GameType` (key), `Options` (JSON, e.g. `{"mode":"Advanced"}`), `UpdatedAt` |
| `DataProtectionKeys` | The keys that encrypt and sign login tokens |

Deleting a user also deletes their external logins, icons and saved game options. To change the tables, edit the classes in `Data/` and run `dotnet ef migrations add <Name> --project GamesLane.Server --output-dir Data/Migrations` (first `dotnet tool restore`, and set `$env:ASPNETCORE_ENVIRONMENT = "Development"` so the connection string is read from user secrets).

To browse the database, use the Neon console's SQL Editor, pgAdmin, DBeaver, or the PostgreSQL extension for VS Code (not SSMS, which is for SQL Server only). Table and column names are case-sensitive, e.g. `select * from "Users"`.

### HTTP endpoints

| Endpoint | Description |
| --- | --- |
| `GET /api/sessions/{code}` | `{ sessionId, gameType, page }` for an existing session, or 404 |
| `GET /join/{code}` | Shareable join link; redirects to the session's page (`<page>?session=CODE`) |
| `GET /auth/me` | `{ signedIn, kind, name, icon, uploadedIcon, accessToken, providers, icons }`. `accessToken` is a renewed login token; `providers` lists the external sign-in options that are configured; `icons` lists the built-in icons |
| `POST /auth/guest` | Body `{ name, icon }`. Logs in as a guest |
| `POST /auth/register` | Body `{ username, password, name, icon }`. Creates a registered user and logs in. Rate-limited |
| `POST /auth/login` | Body `{ username, password }`. Logs in a registered user. Rate-limited (10 attempts per minute per address) |
| `POST /auth/profile` | Body `{ name, icon }`, logged-in users only. Changes the display name and icon (a built-in icon, or the user's uploaded picture) |
| `POST /auth/icon` | Body: a PNG, JPEG or WebP picture (up to 200 KB). Saved users only. Uploads it and makes it the icon |
| `GET /api/icons/{id}` | An uploaded picture |
| `GET /auth/signin/{provider}?returnUrl=/...` | Starts sign-in with `google` or `microsoft` |
| `POST /auth/external` | Exchanges the result of an external sign-in for `{ accessToken, isNew }` |
| `GET /api/options/{gameType}` | Saved users only. `{ saved, options }`: the user's saved options for the game, or its defaults (`saved: false`) |
| `PUT /api/options/{gameType}` | Saved users only. Body: the options, e.g. `{ "mode": "Advanced" }`. Saves them as the user's preferences (204), or `{ error }` (400) |

All the `POST /auth/...` endpoints return `{ accessToken }` on success, or `{ error }` (status 400) with a message to show.
| `GET /health` | Health check |

## Sign-in Setup

Guest and username logins always work. Each external provider appears on the site only when its client ID and secret are configured. Set them as environment variables (on Render: **Environment** tab) — never commit them to the repo:

| Provider | Variables | Where to get them | Redirect URI to register |
| --- | --- | --- | --- |
| Google | `Authentication__Google__ClientId`, `Authentication__Google__ClientSecret` | [Google Cloud Console](https://console.cloud.google.com/apis/credentials) → Create credentials → OAuth client ID → Web application | `https://<your-site>/signin-google` |
| Microsoft | `Authentication__Microsoft__ClientId`, `Authentication__Microsoft__ClientSecret` | [Microsoft Entra admin center](https://entra.microsoft.com/) → App registrations → New registration, choosing "personal Microsoft accounts" plus work/school accounts; then Certificates & secrets → New client secret | `https://<your-site>/signin-microsoft` |

For local testing, use `dotnet user-secrets` in `GamesLane.Server` and register `https://localhost:<port>/signin-google` (or `-microsoft`) as an extra redirect URI.

## Project Status and External Services

_Last updated: 2026-10-29. Keep this section current; it is the hand-off point between work sessions._

### Local machine
- Repo folder: `C:\Users\inati\iCloudDrive\Programming\GamesLane2.0` (inside iCloud Drive; sync can lock files during moves).
- Installed: Git (user Isaac Nativ), GitHub CLI (`gh`, signed in), .NET 10 SDK, Node.js (useful for `node --check` on the scripts in `wwwroot`). Not installed: Docker.
- The original JavaScript/Firebase app is in the sibling folder `GamesLane` (github.com/inativ01/GamesLane). It is separate; do not modify it.

### GitHub
- Repo: https://github.com/inativ01/GamesLane2.0 (public), branch `main`.
- Every push to `main` triggers a Render deploy.

### Render (hosting)
- Live site: https://gameslane.onrender.com
- Free web service, built from the `Dockerfile` at the repo root; auto-deploys from `main`.
- Server reads the `PORT` setting; health check at `/health`.
- Free tier: sleeps after 15 min idle (~1 min to wake). Every restart/deploy ends live games; logins survive.
- Environment variables (values only in Render, never in the repo):
  - `ConnectionStrings__GamesLane`: Neon `production` branch (set; required — the server does not start without it). Either the .NET (`Host=...;`) or the URL (`postgresql://...`) format works.
  - `Authentication__Google__ClientId` / `__ClientSecret`: set, working
  - `Authentication__Microsoft__ClientId` / `__ClientSecret`: not set yet

### Neon (PostgreSQL database)
- Project `GamesLane` (ID `gentle-cherry-51286211`), region AWS US East 2 (Ohio).
- Branches: `production` (the live site) and `dev` (local development; its connection string is in `dotnet user-secrets` as `ConnectionStrings:GamesLane`).
- Free plan: 1 GB, sleeps after 5 min idle and wakes automatically, 6-hour restore window.
- Tables are created by EF Core migrations when the server starts (see Database above).

### Sign-in providers
- **Google:** working on the live site. OAuth client in Google Cloud Console (project "GamesLane"); redirect URI `https://gameslane.onrender.com/signin-google`; privacy policy at `/privacy.html`.
- **Microsoft:** code ready; app registration in the Entra admin center not done yet (redirect URI `https://gameslane.onrender.com/signin-microsoft`).
- **Apple:** deferred (needs a paid Apple Developer membership).

### Next steps
1. Linking a Google account to a registered user (deferred).
2. Microsoft sign-in: app registration in the Entra admin center and the two environment variables on Render.
3. Game history and stats (deferred).

### Working conventions
- Ask before adding or upgrading packages.
- Interfaces go in an `Interfaces/` subfolder of each folder, in the parent folder's namespace.
- When asked to "not commit", leave changes uncommitted for review.
- Never put secrets in the repo, chat or commands; they live only in Render's Environment tab (or `dotnet user-secrets` locally).

### Known limitations
- Games are in memory only and are lost on restart.
- A guest exists only in the browser where they logged in; logging in again as a guest creates a new user.
- Forgotten passwords cannot be reset (no email is collected).
- The login-token keys are stored unencrypted in the database (the server logs a warning about this at startup), so the connection string must be kept secret.
- Logging in or out mid-game loses your seat (it is freed 30 seconds later, and the game pauses).
- One server instance only (no scale-out).

## Running Locally

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
- SignalR hub: `/hubs/game`

To play: on the main screen, log in and choose **New session** for a game. The session lobby shows the code, a join link (`/join/{code}`) to send to the others, and the seats. Anyone who opens the link can watch; logged-in users choose **Sit here**, and once enough seats are taken, any seated user presses **Start**. To use a shared screen (e.g. a TV), open the session there without taking a seat. To try several players on one computer, use a separate tab for each user. To play from phones on the same Wi-Fi, run the server with `--urls http://0.0.0.0:5000` and open `http://<computer's IP address>:5000`.

## Running Tests

```
dotnet test
```
