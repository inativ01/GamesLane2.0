# GamesLane 2.0

GamesLane 2.0 is an online platform for playing classic board and card games with friends and family, wherever they are.

It is a complete reimplementation of the original [GamesLane](https://github.com/inativ01/GamesLane), with a new backend built on .NET and written in C#.

**Play now:** https://gameslane.onrender.com

## Features

- **Games:** Tic-tac-toe, Rock-paper-scissors, Backgammon (matches with a doubling cube), The Gang (the cooperative poker game, 3–8 players, with its challenge and specialist cards) Rich man, poor man (a shedding card game, 6–12 players), Yatzee (the classic dice game, 1–8 players) and Uno (2–10 players, to a score target or as a pyramid).
- **Play together in real time:** a private game with family and friends has a code and a join link; a public game matches you with other people on GamesLane. A typical setup is a shared screen (a TV or laptop) showing the table, while each player plays on their own phone.
- **Accounts:** everyone starts as a guest (a random name and icon) and can log in with a username and password, or with Google.
- **Never lose a game:** players who drop out get their seat back when they return, others can take over an absent seat, and sessions survive a server restart (they are kept for 6 hours after everyone has left).
- **Languages:** English and Hebrew (right to left).

## Documentation

- [User manual](docs/user-manual.md): how to play, sessions and seats, and the rules of every game.
- [Developer guide](docs/developer-guide.md): architecture, project structure, the hub API, database, setup, deployment and project status.

## Tech stack

- .NET 10 (C#), ASP.NET Core and SignalR
- PostgreSQL (Neon) with EF Core
- Plain HTML, CSS and JavaScript (no build step)
- xUnit tests
- Hosted on Render (Docker)

## Getting started

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) and a PostgreSQL connection string:

```
cd GamesLane.Server
dotnet user-secrets set 'ConnectionStrings:GamesLane' '<connection string>'
cd ..
dotnet run --project GamesLane.Server
```

Open the address shown in the console (e.g. `http://localhost:5xxx`). Sign-in providers, the database and other settings are described in the [developer guide](docs/developer-guide.md#running-locally).

## Running tests

```
dotnet test
```

## Repository layout

```
GamesLane.Server/        ASP.NET Core backend: game rules, sessions, SignalR hub, auth, database, and the web pages (wwwroot/)
GamesLane.Server.Tests/  Unit tests
docs/                    User manual, developer guide, and The Gang's card texts
Dockerfile               Build for Render
```
