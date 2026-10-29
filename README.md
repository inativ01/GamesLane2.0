# GamesLane 2.0

GamesLane 2.0 is an online platform for playing classic board and card games with friends and family, wherever they are.

It is a complete reimplementation of the original [GamesLane](https://github.com/inativ01/GamesLane), with a new backend built on .NET and written in C#.

## How It Works


### Users
- A **user** is a person who is logged in. At most one user is logged in per browser tab; there may be no user logged in. Different tabs may have different users.
- A user has a **user ID** and a **display name**. More properties (e.g. an icon) will be added later.
- To log in, the user either enters a name, or signs in with Google (the display name defaults to the Google name). The user can change the display name; this changes only the GamesLane name, not the Google account.
- A login lasts 30 days. Until there is a database, a name-only user exists only in the browser where they logged in.
- Changing the display name also updates it on any seat the user holds.

### Lobby (main screen)
- A list of games. A new session can be started only when a user is logged in.
- A way to join a session by entering its code. This does not require a logged-in user.
- A **Login** button, shown only when no user is logged in, and a **Logout** button, shown only when a user is logged in.

### Sessions
- A **session** is one instance of a specific game, identified by a unique code.
- A session can be created from the lobby, joined by entering its code, or opened directly from its URL. Joining does not require a logged-in user.
- A session is either **inactive** (showing the session lobby) or **active** (showing the game screen).

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
  Auth/AuthSetup.cs            Login by name or with Google/Microsoft accounts; per-tab login tokens
  Hubs/GameHub.cs              SignalR hub for creating, joining and playing sessions
  Hubs/DisconnectedSeatsMonitor.cs  Frees the seats of users disconnected for over 30 seconds
  Sessions/GameSession.cs      Seats, session lobby and game flow shared by all games
  Sessions/                    Game-specific sessions and the in-memory session store
  Games/TicTacToe/             Tic-tac-toe rules
  Games/RockPaperScissors/     Rock-paper-scissors rules
  wwwroot/                     Web pages
    index.html, home.js        Main screen: start a session or join one by code
    common.js                  Shared: login dialog and account bar, hub connection, session lobby and page framework
    tictactoe.html/.js         Tic-tac-toe page
    rock-paper-scissors.html/.js  Rock-paper-scissors page
GamesLane.Server.Tests/        Unit tests
```

## Games

- **Tic-tac-toe** (2 players)
- **Rock-paper-scissors** (2 players): both players pick secretly. When both have picked, the picks are revealed and the winner scores a point. Play continues for as many rounds as you like.

## Game Hub API

Clients connect to the SignalR hub at `/hubs/game` and call these methods:

| Method | Returns | Description |
| --- | --- | --- |
| `CreateSession(gameType)` | session code | Logged-in users only. Creates a session (`"TicTacToe"` or `"RockPaperScissors"`) and returns its 6-character code. The creator is not seated. |
| `EnterSession(sessionId)` | `{ state, playerId }` | Subscribes to the session's updates. For a logged-in user, reconnects them to their seats, updates their name on them, and returns their `playerId`; otherwise `playerId` is `null`. Pages call this when they open and after reconnecting. |
| `TakeSeat(sessionId, seat)` | — | Logged-in users only. Takes an empty seat, or the seat of a disconnected user, under the user's display name. Only in the session lobby. |
| `LeaveSeat(sessionId, seat)` | — | Gives up one of the caller's seats (used for **Leave seat** and **Stand up**). |
| `LeaveSession(sessionId)` | — | Stops receiving updates and gives up all the caller's seats (used when going back to the main screen). |
| `NewGame(sessionId, gameNumber)` | — | Seated users only. Starts a new game for everyone seated, replacing game `gameNumber` (0 before the first game). Refused while a game is in progress. If that game was already replaced, nothing changes. |
| `ResumeGame(sessionId)` | — | Seated users only. Continues the paused game; whoever now holds each of its seats plays that seat. |
| `ReturnToLobby(sessionId)` | — | Seated users only. After a game has ended, takes everyone back to the session lobby. |
| `MakeMove(sessionId, seat, cell)` | — | Tic-tac-toe: places the mark of `seat` (0 is X, 1 is O), which the caller must hold. Cells are numbered 0–8, left to right, top to bottom. |
| `Pick(sessionId, seat, choice)` | — | Rock-paper-scissors: picks `"Rock"`, `"Paper"` or `"Scissors"` for `seat`, which the caller must hold. |
| `NextRound(sessionId, round)` | — | Rock-paper-scissors: starts the round after `round`. If it has already started, nothing changes. |

After every change, the server sends a `StateUpdated` message with the public session state to everyone in the session:

| Field | Description |
| --- | --- |
| `sessionId`, `gameType` | The session code and which game it is |
| `phase` | `"Lobby"` (session lobby) or `"Playing"` (game screen) |
| `minSeats`, `maxSeats` | Seats needed to start, and the number of seats |
| `seats` | One entry per seat: `{ playerId, name, isConnected }`, or `null` when empty. A page finds its user's seats by comparing `playerId` |
| `gameNumber` | 0 until the first game starts, then counts games |
| `isGameOver` | The current game has ended |
| `canResume` | In the lobby with an unfinished, paused game |
| `game` | Game-specific state, or `null` before the first game |

In rock-paper-scissors, the game state only shows whether each seat has picked; the picks themselves are revealed once both have picked. Errors (e.g. "It is not your turn.") are returned as hub errors to the caller.

### Rejoining

Players are identified by their user ID. After a refresh or dropped connection, the page reconnects and calls `EnterSession`, and the player keeps their seats, as long as they return within 30 seconds.

### Login

A logged-in user is identified by a signed **login token**. The page keeps it in the browser tab's `sessionStorage`, so each tab can have its own user; a newly opened tab starts with the most recently logged-in user. The page sends the token as an `Authorization: Bearer` header, and as the `access_token` query parameter when connecting to the hub. Logging out simply forgets the token in that tab. Tokens last 30 days.

User IDs are `name:<random>` for users who logged in by name, and `google:<account>` (or `microsoft:<account>`) for external accounts. For external accounts, the display name defaults to the account's name; changing it affects only GamesLane.

External sign-in: the page sends the browser to `/auth/signin/{provider}` with a `returnUrl` containing `signin=external`. After sign-in, the provider's result is kept in a short-lived cookie; the returning page calls `POST /auth/external`, which exchanges it for a login token and clears the cookie.

### HTTP endpoints

| Endpoint | Description |
| --- | --- |
| `GET /api/sessions/{code}` | `{ sessionId, gameType, page }` for an existing session, or 404 |
| `GET /join/{code}` | Shareable join link; redirects to the session's page (`<page>?session=CODE`) |
| `GET /auth/me` | `{ signedIn, userId, name, provider, providers }`; `providers` lists the external sign-in options that are configured |
| `POST /auth/login` | Body `{ name }`. Logs in by name (1–30 characters) and returns a login token (`{ accessToken, ... }`) |
| `POST /auth/name` | Body `{ name }`, logged-in users only. Changes the display name and returns a new login token |
| `GET /auth/signin/{provider}?returnUrl=/...` | Starts sign-in with `google` or `microsoft` |
| `POST /auth/external` | Exchanges the result of an external sign-in for a login token |
| `GET /health` | Health check |

## Sign-in Setup

Logging in by name always works. Each external provider appears on the site only when its client ID and secret are configured. Set them as environment variables (on Render: **Environment** tab) — never commit them to the repo:

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
- Free tier: sleeps after 15 min idle (~1 min to wake). Every restart/deploy ends live games and logs users out (login tokens become invalid).
- Environment variables (values only in Render, never in the repo):
  - `ConnectionStrings__GamesLane`: Neon database (set, **not yet used by the code**)
  - `Authentication__Google__ClientId` / `__ClientSecret`: set, working
  - `Authentication__Microsoft__ClientId` / `__ClientSecret`: not set yet

### Neon (PostgreSQL database)
- Project `GamesLane` (ID `gentle-cherry-51286211`), region AWS US East 2 (Ohio), branch `production`.
- Free plan: 1 GB, sleeps after 5 min idle and wakes automatically, 6-hour restore window.
- No database code exists yet (see Next steps).

### Sign-in providers
- **Google:** working on the live site. OAuth client in Google Cloud Console (project "GamesLane"); redirect URI `https://gameslane.onrender.com/signin-google`; privacy policy at `/privacy.html`.
- **Microsoft:** code ready; app registration in the Entra admin center not done yet (redirect URI `https://gameslane.onrender.com/signin-microsoft`).
- **Apple:** deferred (needs a paid Apple Developer membership).

### Next steps
1. Database: add EF Core + Npgsql (packages awaiting approval: `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design`, `dotnet-ef` tool, optionally `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`).
2. `Data/` folder with a `User` table; save users on sign-in; apply migrations on startup.
3. Game history and stats; then update the privacy policy (it currently says there is no database).

### Working conventions
- Ask before adding or upgrading packages.
- Interfaces go in an `Interfaces/` subfolder of each folder, in the parent folder's namespace.
- When asked to "not commit", leave changes uncommitted for review.
- Never put secrets in the repo, chat or commands; they live only in Render's Environment tab (or `dotnet user-secrets` locally).

### Known limitations
- Games are in memory only and are lost on restart.
- Login tokens are signed with keys held in memory, so a server restart logs everyone out.
- Until there is a database, a user who logged in by name exists only in that browser; logging in again creates a new user.
- Logging in or out mid-game loses your seat (it is freed 30 seconds later, and the game pauses).
- One server instance only (no scale-out).

## Running Locally

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
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
