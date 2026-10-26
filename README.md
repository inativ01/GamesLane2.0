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
  Hubs/GameHub.cs              SignalR hub for creating, joining and playing games
  Sessions/                    Game sessions (players, status) and the in-memory session store
  Games/TicTacToe/             Tic-tac-toe rules
  Games/RockPaperScissors/     Rock-paper-scissors rules
  wwwroot/                     Web page for players and spectators
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
| `JoinAsPlayer(gameId, playerName)` | `{ playerId, seat, gameType }` | Joins as a player. Seats are 0 and 1 in join order (in tic-tac-toe, seat 0 is X). The game starts when both have joined. |
| `JoinAsSpectator(gameId)` | game state | Joins as a spectator and returns the current state. |
| `MakeMove(gameId, cell)` | — | Tic-tac-toe: places the caller's mark. Cells are numbered 0–8, left to right, top to bottom. |
| `Pick(gameId, choice)` | — | Rock-paper-scissors: picks `"Rock"`, `"Paper"` or `"Scissors"` for the current round. |
| `NextRound(gameId, round)` | — | Rock-paper-scissors: starts the round after `round`. If it has already started, nothing changes. |
| `Leave(gameId)` | — | Stops receiving updates for the game. |

After every change, the server sends a `StateUpdated` message with the public game state to everyone in the game. The state's `gameType` field tells clients which game it is. In rock-paper-scissors, the public state only shows whether each player has picked; the picks themselves are revealed once both players have picked. Errors (e.g. "It is not your turn.") are returned as hub errors to the caller.

## Running Locally

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet run --project src/GamesLane.Server
```

- Web page: `/` (the address is shown in the console, e.g. `http://localhost:5xxx`)
- Health check: `GET /health`
- SignalR hub: `/hubs/game`

To play: open the web page on a shared screen and choose a game to create. The screen shows the game and its code. Each player opens the page on their phone, enters the code and their name, and chooses **Join as player**. To play from phones on the same Wi-Fi, run the server with `--urls http://0.0.0.0:5000` and open `http://<computer's IP address>:5000`.

## Running Tests

```
dotnet test
```
