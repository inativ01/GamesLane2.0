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
- A splash screen (the portrait or landscape picture, to suit the screen) is shown for 5 seconds when the app is first opened in a browser tab and whenever the home icon is used; then the main screen appears. It is not shown again when the page reloads (e.g. after logging in).
- The top bar of every page shows the GamesLane name picture and, on the right, a **Log in** button when no user is logged in, or the user's icon and name (which open their profile) and a **Log out** button.
- **Languages:** English and Hebrew. On pages that have a Hebrew translation (so far the main screen, Backgammon and Rich man, poor man, including the login and profile dialogs, the session lobby and the server's messages), the top bar shows the current language's flag; tapping it opens a menu of the languages, and choosing one reloads the page in it. Hebrew pages read from right to left (the backgammon board stays as it is). The choice is remembered in the browser; until the user chooses, Hebrew if the browser prefers it, otherwise English. Pages without a translation (The Gang, Rock-paper-scissors, Tic-tac-toe) stay in English and show no flag.
- **Translating a page** (see `i18n.js`):
  - Load `lang/he.js` and then `i18n.js` in the page's `<head>` (untranslated pages load only `i18n.js`).
  - Mark fixed texts in the HTML with `data-i18n` (the element's whole text is translated) or `data-i18n-attr="title,aria-label"`; in scripts, wrap texts in `t("...")`, with varying parts as `{name}`: `t("Seat {n}", { n })`. Write whole sentences as keys, not pieces glued together, since word order differs in Hebrew.
  - Texts from the server (error messages, option names) go through `translateServerText()`, which finds their translation by their English text. **Changing an English message on the server means changing its key in `lang/he.js` too.**
  - Hebrew style: instructions in the plural (e.g. הקישו על הלוח), so they suit everyone. A text about the viewer has its own "you" form (e.g. "You rolled {n}." / "{name} rolled {n}."; see `about()` in `backgammon.js`). Texts about other players avoid male and female verb forms (e.g. "ל־{name} יצא {n}").
  - Terms still to be confirmed by the user: צעדים (pips), משחק קרופורד (Crawford game), גמון / בקגמון (gammon / backgammon), "(אני)" for the viewer's own seat; game names שש־בש, הכנופיה, איקס־עיגול, אבן, נייר ומספריים; in Rich man, poor man: זריקה / פס (Throw / Pass), מסירה (Give), עשיר / עני (rich man / poor man), נסיך (jack) and the spoken numbers (e.g. "3 תשיעיות"). Confirmed: משחקון (match), עשיר ועני (the game's name).
- A list of games. A new session can be started only when a user is logged in; choosing a game while logged out opens the login dialog, and the session is started once the user has logged in.
- Logged-in users can reorder the games by dragging a game by its ⋮⋮ handle (mouse or touch; Alt+↑/↓ with the keyboard). A saved user's order (registered or Google) is saved with their account; a guest's is remembered in the browser. The order is applied whenever the main screen opens. Games not in a saved order (e.g. new ones) keep their usual place after the others.
- A way to join a session by entering its code. This does not require a logged-in user.
- A link to the privacy policy (`/privacy.html`).

### Sessions
- A **session** is one instance of a specific game, identified by a unique code.
- A session can be created from the lobby, joined by entering its code, or opened directly from its URL. Joining does not require a logged-in user.
- A session's status is **Lobby** (the session lobby), **Playing** (the game screen), **Paused** (the game screen, waiting for an absent player) or **Over** (the game has ended by itself and shows its result). Each game also has its own phases inside Playing, e.g. the exchange in Rich man, poor man or the reveal in The Gang. See **Disconnections and seats** below.
- The game code is shown in the game's banner at the top of the session's page; clicking it copies it to the clipboard.
- Sessions are saved in the database and kept for 30 days after their last activity, so players can come back to a game later, even after the server restarts. Its code then no longer works.

### Session lobby (inactive session)
- A screen shared by all games, showing the game's **seats**.
- Only a logged-in user can take a seat. Users do not choose a seat: the taken seats are always the first ones, followed by a single empty seat (**Sit here**), until all seats are taken. A user may take more than one seat (and then sees the private information of all of them).
- A user can leave a seat by tapping it. The seats after it then move up one, so there are no gaps.
- On every page except the main screen, a home icon in the top bar goes back to the main screen (for players, this gives up their seats).
- A user who loses their connection (closed tab, phone asleep, network lost) keeps their seats for 60 seconds, and gets them back if they return in time; their seat shows a spinner meanwhile. In the lobby, the seats are then freed, as if they had left. During a game, see **Disconnections and seats** below.
- Creating a session does not seat the creator; they join the session and can then take a seat.
- Each game defines its maximum number of seats (up to 12) and the minimum needed to start (which may equal the maximum). Tic-tac-toe, rock-paper-scissors and backgammon have 2 seats, both required; The Gang has 8 seats, at least 3 required; Rich man, poor man has 12 seats, at least 6 required.
- When the minimum number of seats is taken, a **Start** button appears (**New game** after the first game). Any seated user can start the game.
- While the session is in the lobby, an **Invite friends** panel shows the join link (`/join/{code}`), with **Copy link** and **Share…** buttons where the browser supports them.
- Anyone not seated (logged in or not) is a **spectator**.

### Game screen (active session)
- Once the game starts, the session lobby is replaced by a game-specific screen.
- Seated players may see a different version of the screen than spectators (for example, their own cards in hand). Spectators never see any player's private information.
- Anyone can watch an active game. During a game, the only seats that can be taken are those of absent players (see below).
- A seated user can **stand up** (become a spectator) or **leave** (go back to the main lobby, giving up their seats).
- A player can **End game** at any time during a game (after confirming): everyone goes back to the session lobby, the seats move up, others can join, and **Start** begins a new game. Private game information is not sent while the session is in the lobby.
- When a game ends (The Gang, and a backgammon match), anyone seated can choose **Play again** while at least the minimum number of players are seated, or go **Back to the lobby** (for everyone). A new game is played by whoever is seated (any number between the minimum and the maximum). If fewer than the minimum remain, the session goes back to the session lobby automatically.
- Tic-tac-toe and rock-paper-scissors never end by themselves; players stand up or leave when they are done.

### Disconnections and seats
- **Seat states:**

  | State | Meaning | Starts | Ends |
  | --- | --- | --- | --- |
  | **Present** | The holder is connected | Taking the seat, or reconnecting | The holder disconnects or leaves |
  | **Reconnecting** | The holder lost their connection less than 60 seconds ago; the seat shows a spinner. For the game, the seat is still present: nothing else changes | Disconnecting | Coming back (Present), or 60 seconds (Away) |
  | **Away** | The holder has been disconnected for 60 seconds or more; the seat is still theirs | 60 seconds after disconnecting | The holder comes back (at any time), someone takes the seat, or the game ends |
  | **Vacant** | Nobody holds the seat | **Stand up**, the home icon, or an Away seat freed (below) | Someone takes it |

  An **absent** seat is one that is Away or Vacant.
- **Only while a game is in progress** (Playing or Paused) are Away seats kept. In the session lobby, and once a game is Over, an Away seat is freed.
- **Taking an absent seat:** during a game, anyone logged in can take an absent seat of the game with **Take this seat**, and continues that player's game (their cards, checkers or chips). If the seat was Away, its holder loses it: when they come back, they are a spectator.
- **What a game does with absent seats** depends on the game:

  | Game | With an absent seat | Effect |
  | --- | --- | --- |
  | Tic-tac-toe, Rock-paper-scissors, Backgammon, The Gang | **Wait:** the session is Paused | The game screen shows "Waiting for …" with **Take this seat** and **End game**. Present players can still do what does not need the absent seat (e.g. finish their own turn, pick, move their chip); the game stops where it needs the absent player |
  | Rich man, poor man | **Act automatically:** the seat passes | The game goes on and never pauses; the seat keeps its cards. At the card exchange, absent seats are removed (see the game's rules) |

- **Ending:** any one present player can **End game** during a game (after confirming), which takes everyone back to the session lobby. The session also goes back to the lobby with **Back to the lobby** after a game is Over, and when there are too few players for the next game (**Play again** with fewer than the minimum seated, or Rich man, poor man's exchange with fewer than 6).
- **Saving and loading:** a session is saved in the database after every change. Once nobody has had it open for 5 minutes, it is removed from memory; it is loaded again when someone opens it (by its link or code, or when a player reconnects). The server loads nothing when it starts. A session loaded again starts with every holder Away (nobody is connected yet), counted from when they disconnected; in the lobby, their seats are freed. A session that never had a seat taken is deleted instead of saved when it is removed from memory.
- **Expiry:** sessions are deleted 30 days after their last activity, by a job that runs once a day.

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
  Hubs/DisconnectedSeatsMonitor.cs  Marks users disconnected for 60 seconds as Away; removes sessions nobody has open from memory
  Sessions/GameSession.cs      Seats, session lobby and game flow shared by all games
  Sessions/                    Game-specific sessions; the session store (memory, and the database through SessionRepository); SessionSnapshot, which saves a session as JSON
  Games/GameOptions.cs         Game options (rule variants): each game's options, and checking chosen values
  Games/TicTacToe/             Tic-tac-toe rules
  Games/RockPaperScissors/     Rock-paper-scissors rules
  Games/TheGang/               The Gang rules: cards, poker hand ranking, heists
  Games/Backgammon/            Backgammon rules: position, legal moves, turns, doubling cube, matches, options
  Games/RichManPoorMan/        Rich man, poor man rules: the deck (one set of 2 to A per player), rounds, players who left, the exchange
  wwwroot/                     Web pages
    index.html, home.js        Main screen: start a session or join one by code
    common.js                  Shared: login dialog and account bar, hub connection, session lobby and page framework, playing cards
    i18n.js                    Shared: languages. The English texts in the code are the keys: t("Log in") gives the chosen language's text. Also matches the server's English messages to their translations, and the language picker
    lang/he.js                 Hebrew translations (a page offers Hebrew by loading this file before i18n.js)
    styles.css                 Styles for all pages
    privacy.html               Privacy policy
    tictactoe.html/.js         Tic-tac-toe page
    rock-paper-scissors.html/.js  Rock-paper-scissors page
    the-gang.html/.js          The Gang page
    backgammon.html/.js        Backgammon page (the board is drawn as SVG)
    rich-man-poor-man.html/.js Rich man, poor man page
    images/                    Logo, the GamesLane name for the header and the splash screens (web-sized copies of the originals in docs/), game pictures (SVG)
    images/the-gang/           A picture (SVG) for each challenge and specialist card, named by the card's key, e.g. quick-access.svg
    images/flags/              Flags for the language picker
GamesLane.Server.Tests/        Unit tests
docs/                          Original logo, name and splash pictures (kept out of git); docs/the-gang/: The Gang's card texts
```

## Games

- **Tic-tac-toe** (2 players): after each board, the wins and draws are kept and players start the next round. The starting player alternates: X starts the odd rounds, O the even ones.
- **Rock-paper-scissors** (2 players): both players pick secretly. When both have picked, the picks are revealed and the winner scores a point. Play continues for as many rounds as you like.
- **Backgammon** (2 players): standard rules. Seat 0 plays white, seat 1 black.
  - **A match** to 1, 3, 5, 7 or 9 points (option **Match length**, default 5). A single game scores 1 point, a gammon (the loser bore off no checker) 2, a backgammon (and still has a checker on the bar or in the winner's home board) 3, times the cube. When the match ends, **Play again** starts a new one; between games, **Next game**.
  - **Opening roll:** at the start of each game, each player rolls their own die (tap the board, or **Roll**); each die stays on its player's side. On a tie, both dice stay on view until a player rolls again, and both roll again. The higher die moves first, with both dice.
  - **A turn:** tap the board (or press **Roll**) to roll, move the checkers, then press **Done**. The dice tumble for one second when rolled; between turns, the last roll stays on the board, faded. **Undo** takes back the last checker moved in the turn. Tap a checker and then one of its highlighted destinations; a checker that can only go to one place moves at once. The rules are enforced: a checker on the bar must enter first, a point with two or more opposing checkers is blocked, a single opposing checker is hit, bearing off only once all checkers are home (with a higher number only when no checker is on a higher point), as many dice as possible must be used (all four with doubles), and if only one die can be used, it must be the larger one when possible.
  - **Automatic turns:** if no checker can move, the turn passes; if there is only one way to play the dice, the game plays it and the turn ends. A message says so.
  - **Doubling cube** (option, on by default; not in 1-point matches): before rolling, the player may **Double** (or tap the cube, which is outlined when they may); the other player **Take**s (the cube doubles and they own it) or **Drop**s (losing the game at the cube's value). Crawford rule: no doubling in the game after a player first comes within one point of winning the match.
  - **The board** is seen from each player's own side of the table: the board is turned around (rotated, not mirrored) between the players. Black's home board is at the bottom right, bearing off into the tray on the right, with its two back checkers at the top right; white's home board is at the bottom left, bearing off into the tray on the left, with its two back checkers at the top left. Spectators see white's side, and a user holding both seats sees the side of the seat that must act. It shows the dice on the mover's side (used dice faded), the cube, both pip counts and the match score. The previous turn's moves are highlighted, and so are the current turn's for everyone watching; the checker just moved drops into place. The board scales to fit the screen, in portrait or landscape, and on a TV: it shrinks so that the whole page (scores, board and buttons) is visible without scrolling.
- **The Gang** (3–8 players), the cooperative poker game by John Cooper and Kory Heath. Each heist:
  - Every player gets 2 pocket cards, shown only on their own screen. Five shared cards are dealt: none at first, then 3, 4 and 5 as the stages go on.
  - Four betting stages (white, yellow, orange, red). In each, every player takes a numbered chip, from 1 (weakest hand) up to the number of players (strongest), from the middle or from another player. Tapping your own chip returns it to the middle. Players are numbered 1 to the number of players in seat order: each hand is titled "Player N", and the table's chip list shows the numbers. Next to the player's cards are the chips they took in this heist's earlier stages. Once every player holds a chip, the **Ready** button (👍) appears; the stage ends when everyone has pressed it. Any chip change clears everyone's Ready (and hides the button again while a chip is in the middle). A user with several seats presses Ready once, for all their hands.
  - **Chip taken:** when another player takes one of your chips, your device plays a short alert sound (after you have tapped the page once, as browsers require). Nothing plays when you take a chip between your own seats.
  - Players work out their hand's strength themselves; its value is only shown when the hands are revealed. With the option **My hand's ranking** (Hidden by default, or Shown), each player sees next to their own cards the ranking their pocket cards make with the shared cards so far (e.g. "Pair", then "Two pair" once more shared cards appear); nobody else sees it.
  - Hands are then revealed one at a time, from the lowest red chip to the highest, each with its ranking and its five cards, most important first, in bold (e.g. "Two pair: 9, 9, 5, 5, K"). A hand at least as strong as the one before it has a thick green frame; a weaker one has a thick red frame, with the first reason in red: the ranking if it is lower, otherwise the first lower card values (e.g. "Two pair: 9, 9, 5, 5, **7**" after "Two pair: 9, 9, 5, 5, K"). If any hand is weaker than the one before it, the alarm goes off; otherwise the vault opens. Equal hands may be in either order.
  - 3 vaults win the game; 3 alarms lose it.
  - **Jokers** (option, off by default, in both modes): the deck has two jokers.
    - **In a hand:** before the betting, a player dealt a joker gets two cards and taps the one to keep; the other is discarded. If one of the two is a joker, they either keep the other card, or tap the joker to swap it for two more cards (each choice is between two cards). With two jokers in hand, they are swapped one after the other. The betting starts once every joker is swapped. Everyone sees that a player swapped a joker (🃏 next to their name in the chip table), but not which cards.
    - **Cards drawn later** (Motion Detector, Laser Tripwires, Hacker): a joker is swapped the same way, and the stage cannot end until it is.
    - **On the table:** a joker is replaced by two cards, shown side by side in a frame; if one of them is the other joker, it is replaced too, and all three cards show. Each hand uses at most one card of the frame; the game always counts the best hand it allows.
  - **Advanced mode** (option **Mode**; Basic is the default) adds challenge and specialist cards. The first heist gets 0–3 challenge cards (option, default 1); after a successful heist, one challenge card is added, and after a failed heist, one specialist card. Cards stay in play for the rest of the game. They are drawn at random from the enabled ones (all by default; players can turn cards off), each at most once per game. As in the original game, any combination can come up; with Quick Access (no white chips), Motion Detector and Laser Tripwires have no effect. The option **Cards that cancel each other** (Allow by default, or Avoid) keeps such pairs from being in play together. The cards in play lie on the right of the table (at the bottom on narrow screens), each showing a picture (drawn for this site from the card's name, not taken from the original game), its kind and its name, with the ones added for this heist marked **New**; tapping a card shows its picture and what it does in a popup. With the option **Card descriptions** (Tap to read by default, or On the cards), each card on the table also shows what it does. With both Investor and Math Whiz in play, both facts are shown. The full card texts are in `docs/the-gang/`.
  - All 10 challenge cards and all 10 specialist cards are included. Motion Detector and Laser Tripwires are checked when the first 3 shared cards appear; the player who gets new pocket cards is shown to everyone. That player gets a panel saying their cards were replaced, with a **Got it** button; the stage cannot end until they press it. Their white chip no longer counts: it is crossed out in red wherever it is shown (the chip table, next to their hand). Locked chips (Noise Sensors, Ventilation Shaft) stay with whoever takes them first: they cannot be taken, swapped or returned. With Blackout, earlier stages' chips are hidden: the chip table shows each as a chip of its stage's colour with "?" instead of the number. The specialist cards that start "Once the pocket cards have been dealt" (Coordinator, Con Artist, Investor, Math Whiz) are not optional and happen by themselves in every heist, before the betting: first Coordinator (everyone picks a card to pass to the next player; it moves once all have picked), then Con Artist (everyone confirms they have seen their cards, then all pocket cards are shuffled and dealt again), then Investor and Math Whiz show every player's count or total to everyone, in the chip table.
  - **The "Decide which of you…" specialist cards are optional.** The group may use any of them, in any order, at any time during the betting, each at most once per heist; when everyone is ready in the red stage, the hands are revealed however many are left unused. Next to a player's hand, each unused one is a folded panel.
  - **Group decisions** (Informant, Getaway Driver, Mastermind, Hacker, Jack, Muscle, Retina Scan, Fingerprint Scan): any player proposes a player and/or a value; everyone sees it, and it takes effect once every voter has pressed the 👍 (any change clears the agreement). Each user votes once, for all their seats, and the count is per user (e.g. "👍 1 of 3" with 3 users); the same goes for Con Artist's "I've seen my cards". Informant's player then secretly shows one card to one other player; Hacker's player draws a card and Jack's player gets the suitless Jack card (never part of a flush), and each then discards one; the stage cannot end until they have. Getaway Driver and Mastermind show the answer to everyone. Muscle's hand beats every hand of the same ranking at the reveal; a green **💪 Muscle** badge marks that player's own hand (next to their pocket cards) and, at the reveal, their revealed hand. The scans are decided just before the strongest hand is revealed, by everyone except the user who holds that hand's seat, for all of their seats, since that hand is on their screen (if one user plays every seat, only the strongest hand's seat sits out); a wrong guess fails the heist. If the hands are already out of order (including the strongest hand, not yet revealed), the heist fails anyway, so the scans are skipped.
  - **What the cards did is shown in the chip table, not on the cards.** Under each player's name, one line per card, starting with the card's name: e.g. "Motion Detector: new pocket cards were dealt", "Investor: 2 face cards", "Math Whiz: 18 total", "Informant: showed a card to …", "Hacker: drew a card and discarded one", "Getaway Driver: High card", "Mastermind: one 9", and the scans' guesses about the strongest hand with a green **PASS** or red **FAIL** badge. A note under the chip table shows group decisions under way (e.g. "Mastermind: deciding, 1 of 3 agreed").
  - **Unused cards are faded** (grey and see-through): Motion Detector or Laser Tripwires once it is known that it did not go off (when the first 3 shared cards appear, or at once with Quick Access), optional specialist cards the group has not used yet in this heist, and scans that are not needed.
  - **Spectator screen** (e.g. a TV): the whole table fits on the screen without scrolling. The table is sized to the largest that fits, so it grows on large screens and shrinks on small ones; on landscape screens the heist's cards are in a column on the right, and on very wide screens (or without heist cards) the chips and the revealed hands sit side by side.
  - Players can **Hide table** (and **Show table** again), e.g. when a shared screen shows it; the browser remembers the choice. Until they choose, the table is hidden on phones and tablets and shown on computers.
  - Not yet included: in-game chat.

- **Rich man, poor man** (עשיר ועני, 6–12 players): a shedding card game, in English and Hebrew. The rules:

- **Goal:** get rid of all your cards. A game does not have a single winner: it ends with a full ranking of the players, in the order they emptied their hands. A player may empty their hand in the middle of a round; their place in the ranking is kept and they take no further part in that game. The game ends when every active player has emptied their hand (the last player keeps throwing alone until their hand is empty).
- **The deck:** one set of 2 to A per player, so every player gets 13 cards and each number appears once per player: 6 players use 6 sets, 9 players use 9 sets, 12 players use 12 sets. The sets are drawn as ordinary playing cards, taking the suits ♠, ♥, ♦, ♣ in turn (the fifth set is ♠ again), with ♥ and ♦ in red. **Suits do not matter** at all; only the numbers count. With more than 4 players, the same number appears more than 4 times (with 9 players, there are nine 7s).
- **Order of strength:** 2 is the lowest, then 3, 4, … 10, J, Q, K, and A is the highest.
- **Dealing:** the whole deck is shuffled and dealt; every player gets 13 cards.
- **Rounds:** a game is played in rounds.
  - The player who starts the round throws any number **N** of cards of one number (e.g. one 5, or three 9s). N stays the same until the round ends.
  - In turn, each other player either throws N cards of a **strictly higher** number (not equal), or passes. A player may pass even if they could play. Passing is only for that turn: a player who passed may throw again when their turn comes round, if the round is still going.
  - The round ends when the play comes back to the last player who threw cards, everyone else having passed since that throw. That player wins the round and starts the next one, choosing a new N.
  - **Aces end the round at once:** nothing beats an A, so the player who throws Aces wins the round immediately.
  - If the round winner has no cards left, the next player in turn who still has cards starts the next round.
  - Players who have emptied their hands are skipped.
- **Who starts:**
  - **The very first round of the first game:** the player with the most 2s. On a tie, the one of them with the most 3s; then the most 4s; and so on (if two players have exactly the same cards, one of them at random).
  - **The first round of every later game:** the poorest player (the last in the ranking).
- **Direction of play, in every game (the first one too):** in descending player numbers, from the poor towards the rich, then from the richest player (#1) straight back to the poorest (the last).
- **Between games:**
  - A seated player presses **Next game** to start it; there is no long-term score: each game only decides the seats and the exchange of the next one.
  1. **New seats:** the players are re-seated by the last game's ranking: the winner becomes player #1 (the rich man), the second becomes #2, and so on; the last is the poor man.
  2. **Card exchange:** the cards are dealt again, then player #1 exchanges cards with the last player, #2 with the second-to-last, and so on:

     | Players | Cards exchanged |
     | --- | --- |
     | 6 | #1↔#6: 3, #2↔#5: 2, #3↔#4: 1 |
     | 7 | #1↔#7: 3, #2↔#6: 2, #3↔#5: 1; #4 does not exchange |
     | 8 | #1↔#8: 4, #2↔#7: 3, #3↔#6: 2, #4↔#5: 1 |
     | 9 | #1↔#9: 4, #2↔#8: 3, #3↔#7: 2, #4↔#6: 1; #5 does not exchange |
     | 10 | #1↔#10: 4, #2↔#9: 3, #3↔#8: 2, #4↔#7: 1; #5 and #6 do not exchange |
     | 11 | #1↔#11: 4, #2↔#10: 3, #3↔#9: 2, #4↔#8: 1, #5↔#7: 1; #6 does not exchange |
     | 12 | #1↔#12: 4, #2↔#11: 3, #3↔#10: 2, #4↔#9: 1, #5↔#8: 1; #6 and #7 do not exchange |

  3. **How each pair exchanges:** the poorer player must give their N highest cards: the game picks them, and the player confirms with **Give these cards**. The richer player taps any N cards and presses **Give**. Nobody gets any cards until every pair has given: then all the cards are exchanged at once, everyone gets their new cards together, and the poorest player starts. Nobody sees the cards they will receive before choosing the cards they give.
- **An absent player during a game** (Away or Vacant, see **Disconnections and seats**): their cards are held until the end of the game. They become inactive and pass automatically; if they are to start a round, the next active player in turn starts it. An Away player who comes back plays on. Anyone can take an absent seat (**Take this seat** in the player list, no approval needed) and continue the game in their place, with their cards and their place. An inactive player gets no finishing place. When the game ends and **Next game** is pressed, absent seats are removed, and the exchange is for the remaining number of players (the others keep their order, by the ranking); a player who becomes absent during the exchange is removed too.
- **Fewer than 6 players:** a game in progress continues with however many active players are left, but there can be no exchange with fewer than 6 players: **Next game** then takes everyone back to the session lobby (a new game can start there once 6 are seated). Likewise, if players leave during the exchange and fewer than 6 remain, everyone goes back to the lobby.
- **Joining:** during a game, nobody can take a seat other than a seat whose player left. During the exchange, an observer who is logged in can **Ask to join**; any player sees the request and can **Accept** it, choosing the newcomer's place (#1 to the last; the players from that place down move down one), or **Decline** it. A player who leaves during the exchange is dropped at once. Each time a player is added or dropped, one set of 2 to A is added to or removed from the deck (so each number still appears once per player), all the cards are shuffled and dealt again, any cards already given are cancelled, and the exchange starts again for the new number of players.
- **Screens:** each player's phone shows their hand sorted from low to high, with equal numbers together; they tap cards of one number, then **Throw**, or **Pass**. Tapping a card when there is a throw to beat picks as many cards of that number as are needed. A user holding several seats sees one hand per seat. The table (shown to everyone, and to observers such as a TV) shows the cards to beat and who threw them, then the round's winner; and the players in place order (#1, the rich man, to the last, the poor man), each with their number of cards, whose turn it is, who has passed in this round, who left, the finishing places so far, and during the exchange, who has given their cards. After a game, it shows the next game's places. On a screen without a seat, **Read throws aloud** says each throw out loud (e.g. "3 nines"), in the page's language, using the browser's speech; the choice is remembered in the browser.

### Game options

A game can offer **options**, i.e. rule variants (so far The Gang's and backgammon's). Each option has a key, a label and a list of choices with a default; `Games/GameOptions.cs` lists them per game. A **choice** option is one of its choices. A **set** option (e.g. The Gang's challenge cards) is a list of choices, all on by default (for cards, each with its picture: tap a card to turn it on or off; a card that is off is greyed out); its value lists the choices turned off, separated by commas, so cards added later are on by default. An option can apply only when another option has a certain value (`shownWhen`, e.g. the card options only in Advanced mode).

- In the session lobby, everyone sees the chosen options; seated players can change them with **Game options**. They apply to the next new game; a paused game keeps the options it started with.
- Saved users (registered or Google, not guests) can **save** the options as their preferences for that game, and **load** them back in the dialog. A session starts with its creator's saved preferences.
- Chosen values are checked against the game's options: unknown options or values are refused, and options left out get their default. Saved preferences that no longer match (e.g. after an option was removed) fall back to the defaults.

## Game Hub API

Clients connect to the SignalR hub at `/hubs/game` and call these methods:

| Method | Returns | Description |
| --- | --- | --- |
| `CreateSession(gameType)` | session code | Logged-in users only. Creates a session (`"TicTacToe"`, `"RockPaperScissors"`, `"TheGang"`, `"Backgammon"` or `"RichManPoorMan"`) and returns its 6-character code. The creator is not seated. The session starts with the creator's saved options for the game, if any. |
| `EnterSession(sessionId)` | `{ state, playerId, private }` | Subscribes to the session's updates. For a logged-in user, reconnects them to their seats, updates their name and icon on them, and returns their `playerId`; otherwise `playerId` is `null`. `private` is the user's private game information, or `null`. Pages call this when they open and after reconnecting. |
| `TakeSeat(sessionId)` | — | Logged-in users only. Takes the next available seat under the user's display name: in the session lobby, the first empty one; during a game, the first absent seat of the game (Away or Vacant), whose game the user continues. Not while a game is Over, nor during Rich man, poor man's exchange (use `RequestSeat`). |
| `LeaveSeat(sessionId, seat)` | — | Gives up one of the caller's seats (used for **Leave seat** and **Stand up**). |
| `LeaveSession(sessionId)` | — | Stops receiving updates and gives up all the caller's seats (used when going back to the main screen). |
| `NewGame(sessionId, gameNumber)` | — | Seated users only. Starts a new game for everyone seated, replacing game `gameNumber` (0 before the first game). Refused while a game is in progress. If that game was already replaced, nothing changes. |
| `SetOptions(sessionId, options)` | — | Seated users only, in the session lobby. Sets the options for the next new game, e.g. `{ "mode": "Advanced" }`. Options left out get their default. |
| `EndGame(sessionId)` | — | Seated users only. During a game (Playing or Paused), ends it and takes everyone back to the session lobby; the seats move up, others can join, and a new game can be started. |
| `ReturnToLobby(sessionId)` | — | Seated users only. After a game has ended, takes everyone back to the session lobby. |
| `MakeMove(sessionId, seat, cell)` | — | Tic-tac-toe: places the mark of `seat` (0 is X, 1 is O), which the caller must hold. Cells are numbered 0–8, left to right, top to bottom. |
| `Pick(sessionId, seat, choice)` | — | Rock-paper-scissors: picks `"Rock"`, `"Paper"` or `"Scissors"` for `seat`, which the caller must hold. |
| `NextRound(sessionId, round)` | — | Rock-paper-scissors and tic-tac-toe: starts the round after `round`; backgammon: starts the game of the match after game `round`; Rich man, poor man: starts the game after game `round` (or with fewer than 6 players left, goes back to the session lobby). If it has already started, nothing changes. |
| `Roll(sessionId, seat)` | — | Backgammon: `seat`, which the caller must hold, rolls the dice; during the opening roll, its one die. |
| `MoveChecker(sessionId, seat, from, to)` | — | Backgammon: `seat` moves a checker. Points are numbered from that seat's side: 1 to 24 in the direction it moves, 25 for the bar, 0 for off the board. |
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

After every change, the server sends a `StateUpdated` message with the public session state to everyone in the session. In games with private information (The Gang), each player's connections then also receive a `PrivateStateUpdated` message with that user's own cards; nobody else receives it. `EnterSession` returns the same private information as `private`. The public session state has these fields:

| Field | Description |
| --- | --- |
| `sessionId`, `gameType` | The session code and which game it is |
| `phase` | `"Lobby"` (session lobby), `"Playing"` (game screen), `"Paused"` (game screen, waiting for an absent player) or `"Over"` (the game has ended by itself) |
| `minSeats`, `maxSeats` | Seats needed to start, and the number of seats |
| `seats` | One entry per seat: `{ playerId, name, icon, status }`, or `null` when empty (Vacant). `status` is `"Present"`, `"Reconnecting"` or `"Away"`. `icon` is an emoji or a picture address starting with `/`. A page finds its user's seats by comparing `playerId` |
| `gameNumber` | 0 until the first game starts, then counts games |
| `absentSeats` | During a game, its seats that are absent (Away or Vacant), in order: they can be taken with **Take this seat**; otherwise empty |
| `game` | Game-specific state, or `null` before the first game |
| `optionList` | The game's options: `{ key, label, choices: [{ value, label, hint, image }], default, kind, shownWhen }`, where `image` is a picture's address or `null`, `kind` is `"Choice"` or `"Set"` and `shownWhen` is `{ key, value }` or `null` (empty for games without options) |
| `options` | The chosen value of each option, e.g. `{ "mode": "Basic" }`, used by the next new game |

In rock-paper-scissors, the game state only shows whether each seat has picked; the picks themselves are revealed once both have picked. In backgammon, the game state has the board from white's side (`points`: entry `i` is white's point `i + 1`, positive for white checkers and negative for black), `bar`, `off`, `pips` and `scores` by seat, the `phase` (`"Opening"`, `"Rolling"`, `"Doubled"`, `"Moving"`, `"GameOver"`, `"MatchOver"`), the seat to move (`turn`), each seat's opening die (`openingDice`, `null` until rolled) and the decided `openingRoll`, the `dice` and `diceLeft`, the number of rolls so far (`rolls`, so a page can tell a new roll), the `legalMoves` and the moves `played` so far (in the mover's numbering), the `lastTurn`, the cube (`cubeValue`, `cubeOwner`, `canDouble`, `isCrawford`) and the last game's `result`. Errors (e.g. "It is not your turn.") are returned as hub errors to the caller.

### Rejoining

Players are identified by their user ID. After a refresh or dropped connection, the page reconnects and calls `EnterSession`, and the player keeps their seats: in the session lobby, as long as they return within 60 seconds; during a game, at any time, unless someone has taken their seat.

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
| `Users` | `Id`, `Username` / `NormalizedUsername` (registered users; unique regardless of case), `PasswordHash`, `DisplayName`, `Icon`, `CreatedAt`, `LastActiveAt`, `GameOrder` (the order of the games on the main screen, e.g. `Backgammon,TheGang`, or null) |
| `ExternalLogins` | `Provider` + `ProviderKey` (unique), `UserId` |
| `UserIcons` | Uploaded pictures: `Id`, `UserId`, `ContentType`, `Data`, `CreatedAt`. Served at `/api/icons/{id}`; a new upload gets a new ID |
| `UserGameOptions` | Saved game options: `UserId` + `GameType` (key), `Options` (JSON, e.g. `{"mode":"Advanced"}`), `UpdatedAt` |
| `Sessions` | Saved game sessions: `Id` (the code), `GameType`, `Version`, `Data` (JSON), `UpdatedAt` (the last activity) |
| `DataProtectionKeys` | The keys that encrypt and sign login tokens |

**Saved sessions** (`Sessions/SessionSnapshot.cs`): a session is saved as JSON holding every field of the session and of what it holds (seats, options, the game with its cards, checkers or chips), public or private, except open connections (marked `[NotSaved]`) and the games' dice, shuffles and random draws, which a session gives back to its game after loading (`OnRestored`). Cards are saved as text such as `"10♥"`. `Version` is a fingerprint of all those fields' names and types: after a change to the fields of a game or session, saves made by the old code are no longer loaded (that session's code then says "not found"). A new field therefore needs no other work, but changing fields ends the saved games of that type.

Deleting a user also deletes their external logins, icons and saved game options. To change the tables, edit the classes in `Data/` and run `dotnet ef migrations add <Name> --project GamesLane.Server --output-dir Data/Migrations` (first `dotnet tool restore`, and set `$env:ASPNETCORE_ENVIRONMENT = "Development"` so the connection string is read from user secrets).

To browse the database, use the Neon console's SQL Editor, pgAdmin, DBeaver, or the PostgreSQL extension for VS Code (not SSMS, which is for SQL Server only). Table and column names are case-sensitive, e.g. `select * from "Users"`.

### HTTP endpoints

| Endpoint | Description |
| --- | --- |
| `GET /api/sessions/{code}` | `{ sessionId, gameType, page }` for an existing session, or 404 |
| `GET /join/{code}` | Shareable join link; redirects to the session's page (`<page>?session=CODE`), or if there is no such session, to the main screen with the code filled in and a message |
| `GET /auth/me` | `{ signedIn, kind, name, icon, uploadedIcon, gameOrder, accessToken, providers, icons }`. `gameOrder` is a saved user's order of the games (game names), or `null`; `accessToken` is a renewed login token; `providers` lists the external sign-in options that are configured; `icons` lists the built-in icons |
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
| `PUT /api/game-order` | Saved users only. Body: game names in the order to show them, e.g. `["Backgammon", "TheGang"]`, each existing and listed once. Saves the order (204), or `{ error }` (400) |
| `GET /health` | Health check |

All the `POST /auth/...` endpoints except `/auth/external` return `{ accessToken }` on success, or `{ error }` (status 400) with a message to show.

## Sign-in Setup

Guest and username logins always work. Each external provider appears on the site only when its client ID and secret are configured. Set them as environment variables (on Render: **Environment** tab) — never commit them to the repo:

| Provider | Variables | Where to get them | Redirect URI to register |
| --- | --- | --- | --- |
| Google | `Authentication__Google__ClientId`, `Authentication__Google__ClientSecret` | [Google Cloud Console](https://console.cloud.google.com/apis/credentials) → Create credentials → OAuth client ID → Web application | `https://<your-site>/signin-google` |
| Microsoft | `Authentication__Microsoft__ClientId`, `Authentication__Microsoft__ClientSecret` | [Microsoft Entra admin center](https://entra.microsoft.com/) → App registrations → New registration, choosing "personal Microsoft accounts" plus work/school accounts; then Certificates & secrets → New client secret | `https://<your-site>/signin-microsoft` |

For local testing, use `dotnet user-secrets` in `GamesLane.Server` and register `https://localhost:<port>/signin-google` (or `-microsoft`) as an extra redirect URI.

## Project Status and External Services

_Last updated: 2026-11-02. Keep this section current; it is the hand-off point between work sessions._

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
4. Backgammon: first play-test on phones and a TV; possible extras later (a move list, variants such as Nackgammon or Acey-deucey).
5. Rich man, poor man: first play-test with real players on phones and a TV.
6. Hebrew: translate The Gang, Rock-paper-scissors and Tic-tac-toe (main screen and Backgammon are done; first check on Render, and the user to review the terms listed under Languages).

### Working conventions
- Ask before adding or upgrading packages.
- Interfaces go in an `Interfaces/` subfolder of each folder, in the parent folder's namespace.
- When asked to "not commit", leave changes uncommitted for review. For larger features done unattended, commit and push each completed step.
- Never put secrets in the repo, chat or commands; they live only in Render's Environment tab (or `dotnet user-secrets` locally).

### Known limitations
- Sessions are kept in memory by one server, and saved in the database; running more than one server instance would need a shared store.
- A guest exists only in the browser where they logged in; logging in again as a guest creates a new user.
- Forgotten passwords cannot be reset (no email is collected).
- The login-token keys are stored unencrypted in the database (the server logs a warning about this at startup), so the connection string must be kept secret.
- Logging in or out mid-game leaves your seat Away (the game pauses, or in Rich man, poor man passes for you) until you log in again as the same user.
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

To play: on the main screen, log in and choose **New session** for a game. The session lobby shows the code, a join link (`/join/{code}`) to send to the others, and the seats. Anyone who opens the link can watch; logged-in users tap **Sit here**, and once enough seats are taken, any seated user presses **Start**. To use a shared screen (e.g. a TV), open the session there without taking a seat. To try several players on one computer, use a separate tab for each user. To play from phones on the same Wi-Fi, run the server with `--urls http://0.0.0.0:5000` and open `http://<computer's IP address>:5000`.

## Running Tests

```
dotnet test
```
