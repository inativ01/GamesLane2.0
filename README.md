# GamesLane 2.0

GamesLane 2.0 is an online platform for playing classic board and card games with friends and family, wherever they are.

It is a complete reimplementation of the original [GamesLane](https://github.com/inativ01/GamesLane), with a new backend built on .NET and written in C#.

## How It Works

Each game session can be joined in one of two roles:

### Player
- Takes part in the game.
- Usually joins from a phone, which acts as a private, personal device.
- Sees their own private information (for example, the cards in their hand) and makes moves from it.

### Spectator
- Watches the game without taking part.
- Sees the shared game board and all public game state.
- Never sees any player's private information, such as cards in hand.

A typical setup is a shared screen (such as a TV or laptop) connected as a spectator to show the board, while each player uses their own phone to view their hand and play.

## Technology

- **Backend:** .NET (C#), ASP.NET Core
- **Real-time updates:** SignalR
- **Frontend:** plain HTML, CSS and JavaScript, served by the backend
- **Tests:** xUnit

## Project Structure

```
GamesLane.slnx                 Solution file
src/GamesLane.Server/          ASP.NET Core backend
  Auth/AuthSetup.cs            Sign-in with Google and Microsoft accounts
  Hubs/GameHub.cs              SignalR hub for creating, joining and playing games
  Sessions/                    Game sessions (players, status) and the in-memory session store
  Games/TicTacToe/             Tic-tac-toe rules
  Games/RockPaperScissors/     Rock-paper-scissors rules
  wwwroot/                     Web pages
    index.html, home.js        Main screen: start a game or join one by code
    common.js                  Shared: hub connection, sign-in bar, game page framework
    tictactoe.html/.js         Tic-tac-toe page
    rock-paper-scissors.html/.js  Rock-paper-scissors page
tests/GamesLane.Server.Tests/  Unit tests
```

## Games

- **Tic-tac-toe** (2 players)
- **Rock-paper-scissors** (2 players): both players pick secretly. When both have picked, the picks are revealed and the winner scores a point. Play continues for as many rounds as you like.

## Game Hub API

Clients connect to the SignalR hub at `/hubs/game` and call these methods:

| Method | Returns | Description |
| --- | --- | --- |
| `CreateGame(gameType)` | game code | Creates a game (`"TicTacToe"` or `"RockPaperScissors"`) and returns its 6-character code. |
| `EnterGame(gameId)` | `{ state, player }` | Subscribes to the game's updates. If the caller already holds a seat, reconnects them to it and returns `player` (`{ playerId, seat, gameType }`); otherwise `player` is `null`. Game pages call this when they open and after reconnecting. |
| `JoinAsPlayer(gameId, playerName)` | `{ playerId, seat, gameType }` | Joins as a player. Seats are 0 and 1 in join order (in tic-tac-toe, seat 0 is X). The game starts when both have joined. |
| `JoinAsSpectator(gameId)` | game state | Joins as a spectator and returns the current state. |
| `MakeMove(gameId, cell)` | — | Tic-tac-toe: places the caller's mark. Cells are numbered 0–8, left to right, top to bottom. |
| `PlayAgain(gameId, gameNumber)` | — | Tic-tac-toe: after game `gameNumber` has ended, starts a new game with the same players. Refused while the game is in progress. If the new game has already started, nothing changes. |
| `Pick(gameId, choice)` | — | Rock-paper-scissors: picks `"Rock"`, `"Paper"` or `"Scissors"` for the current round. |
| `NextRound(gameId, round)` | — | Rock-paper-scissors: starts the round after `round`. If it has already started, nothing changes. |
| `Leave(gameId)` | — | Stops receiving updates for the game. |

After every change, the server sends a `StateUpdated` message with the public game state to everyone in the game. The state's `gameType` field tells clients which game it is, and each player's `isConnected` shows whether they are currently online. In rock-paper-scissors, the public state only shows whether each player has picked; the picks themselves are revealed once both players have picked. Errors (e.g. "It is not your turn.") are returned as hub errors to the caller.

### Rejoining

Players are identified by their signed-in account or, for guests, by a random guest ID kept in the browser tab (passed as `?guestId=` when connecting to the hub). After a refresh or dropped connection, the game page reconnects and calls `EnterGame`, and the player gets their seat back.

### HTTP endpoints

| Endpoint | Description |
| --- | --- |
| `GET /api/games/{code}` | `{ gameId, gameType, page }` for an existing game, or 404 |
| `GET /join/{code}` | Shareable join link; redirects to the game's page |
| `GET /auth/me` | `{ signedIn, name, provider, providers }`; `providers` lists the sign-in options that are configured |
| `GET /auth/signin/{provider}?returnUrl=/...` | Starts sign-in with `google` or `microsoft` |
| `POST /auth/signout` | Signs out |
| `GET /health` | Health check |

## Sign-in Setup

Sign-in is optional; guests can always play. Each provider appears on the site only when its client ID and secret are configured. Set them as environment variables (on Render: **Environment** tab) — never commit them to the repo:

| Provider | Variables | Where to get them | Redirect URI to register |
| --- | --- | --- | --- |
| Google | `Authentication__Google__ClientId`, `Authentication__Google__ClientSecret` | [Google Cloud Console](https://console.cloud.google.com/apis/credentials) → Create credentials → OAuth client ID → Web application | `https://<your-site>/signin-google` |
| Microsoft | `Authentication__Microsoft__ClientId`, `Authentication__Microsoft__ClientSecret` | [Microsoft Entra admin center](https://entra.microsoft.com/) → App registrations → New registration, choosing "personal Microsoft accounts" plus work/school accounts; then Certificates & secrets → New client secret | `https://<your-site>/signin-microsoft` |

For local testing, use `dotnet user-secrets` in `src/GamesLane.Server` and register `https://localhost:<port>/signin-google` (or `-microsoft`) as an extra redirect URI.

## Running Locally

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet run --project src/GamesLane.Server
```

- Web page: `/` (the address is shown in the console, e.g. `http://localhost:5xxx`)
- Join link: `/join/{code}` opens the game's page
- Health check: `GET /health`
- SignalR hub: `/hubs/game`

To play: on the main screen, enter your name and pick a game. Choose **Play** to join as the first player, or **Show on this screen** to use the screen as a shared board (e.g. a TV). Each game has its own page, which shows the code and a join link (`/join/{code}`) to send to the other players. They open the link, enter their name and choose **Join as player**. To play from phones on the same Wi-Fi, run the server with `--urls http://0.0.0.0:5000` and open `http://<computer's IP address>:5000`.

## Running Tests

```
dotnet test
```
