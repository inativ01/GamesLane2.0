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

## Project Structure

```
GamesLane.slnx                 Solution file
src/GamesLane.Server/          ASP.NET Core backend
  Hubs/GameHub.cs              SignalR hub for creating, joining and playing games
  Sessions/                    Game sessions (players, status) and the in-memory session store
  Games/TicTacToe/             Tic-tac-toe rules
```

## Games

- **Tic-tac-toe** (2 players)

## Game Hub API

Clients connect to the SignalR hub at `/hubs/game` and call these methods:

| Method | Returns | Description |
| --- | --- | --- |
| `CreateGame()` | game code | Creates a new game and returns its 6-character code. |
| `JoinAsPlayer(gameId, playerName)` | `{ playerId, mark }` | Joins as a player. The first player is X, the second is O. The game starts when both have joined. |
| `JoinAsSpectator(gameId)` | game state | Joins as a spectator and returns the current state. |
| `MakeMove(gameId, cell)` | — | Places the caller's mark. Cells are numbered 0–8, left to right, top to bottom. |
| `Leave(gameId)` | — | Stops receiving updates for the game. |

After every change, the server sends a `StateUpdated` message with the public game state to everyone in the game. Errors (e.g. "It is not your turn.") are returned as hub errors to the caller.

## Running Locally

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet run --project src/GamesLane.Server
```

- Health check: `GET /health`
- SignalR hub: `/hubs/game`
