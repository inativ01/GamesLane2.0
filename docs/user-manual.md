# GamesLane user manual

How to use GamesLane and the rules of each game. For how it is built, see the [developer guide](developer-guide.md).

- [Getting started](#getting-started)
- [Users and logging in](#users-and-logging-in)
- [Main screen](#main-screen)
- [Languages](#languages)
- [Sessions](#sessions)
- [Session lobby](#session-lobby)
- [Game screen](#game-screen)
- [Disconnections and seats](#disconnections-and-seats)
- [Game options](#game-options)
- [Games](#games): [Tic-tac-toe](#tic-tac-toe), [Rock-paper-scissors](#rock-paper-scissors), [Backgammon](#backgammon), [The Gang](#the-gang), [Rich man, poor man](#rich-man-poor-man), [Yatzee](#yatzee)

## Getting started

1. Open the site (e.g. https://gameslane.onrender.com). You are logged in automatically as a guest; to use an account, open your profile (your icon and name in the top bar) and log in.
2. On the main screen, choose a game. A new **session** opens, with its game code and a join link.
3. Send the link (or the code) to the others. Anyone who opens it can watch, or tap **Sit here** to take a seat.
4. Once enough seats are taken, any seated player presses **Start**.

A typical setup is a shared screen (such as a TV or laptop) connected as a spectator to show the board, while each player uses their own phone to view their hand and play. To try several players on one computer, use a separate browser tab for each user.

## Users and logging in

- A **user** is a person using the site. There is always exactly one user per browser tab; different tabs may have different users.
- A user has a **user ID**, a **display name** and an **icon** (a built-in emoji, or an uploaded picture).
- There are three kinds of user:
  - **Guest:** anyone who has not logged in with an account. A new visitor automatically becomes a guest named `Guest-` and a random 3-digit number (e.g. `Guest-042`), with a random built-in icon. A guest's information is stored only in their browser's local storage, never on the server.
  - **Registered user** (stored in the server's database, like Google users): a unique username (3–20 letters, digits or `_`, any case) and a password (at least 8 characters), plus a display name and icon. Passwords cannot be reset, since no email is collected.
  - **Google** (or Microsoft): the account is saved by provider and account ID. The display name and icon default to the Google name and picture; changing them affects only GamesLane.
- The profile (the user's icon and name in the top bar) lets every user change their display name and icon. Guests can also **Log in**, **Create account** (which keeps the display name and icon chosen in the profile), or sign in with Google. Registered and Google users can upload a picture as their icon (the page shrinks it to 128×128 before uploading) and **Log out**, which makes them a new guest with a random name and icon.
- Registered and Google users stay logged in for 30 days after their last visit. Their information is deleted from the database automatically after 30 days of inactivity.
- Guests do not expire: a guest keeps the same name and icon for as long as the browser keeps its local storage. A guest exists only in the browser where they were created; clearing the browser's site data ends them, and the next visit creates a new guest.
- Changing the display name or icon also updates it on any seat the user holds.
- Logging in, creating an account or logging out mid-game keeps your seats: they move to the new user, with the new name and icon. Other tabs that still use the previous user become spectators. Only sessions that are open on the server at the time move; a seat in a session nobody has open stays with the previous user.

## Main screen

- A splash screen (the portrait or landscape picture, to suit the screen) is shown for 5 seconds when the app is first opened in a browser tab and whenever the GamesLane name in the top bar is used (on every page, the main screen too); then the main screen appears. It is not shown again when the page reloads (e.g. after logging in).
- The top bar of every page shows the GamesLane name picture and, on the right, the user's icon and name, which open their profile (see [Users and logging in](#users-and-logging-in)).
- A list of games. Choosing a game starts a new session.
- Users can reorder the games by dragging a game by its ⋮⋮ handle (mouse or touch; Alt+↑/↓ with the keyboard). A saved user's order (registered or Google) is saved with their account; a guest's is remembered in the browser. The order is applied whenever the main screen opens. Games not in a saved order (e.g. new ones) keep their usual place after the others.
- A way to join a session by entering its code.
- A link to the privacy policy (`/privacy.html`).

## Languages

- English and Hebrew. Every page has a Hebrew translation (including the profile dialog, the session lobby, the game options and the server's messages); the top bar shows the current language's flag; tapping it opens a menu of the languages, and choosing one reloads the page in it.
- Hebrew pages read from right to left (the backgammon board stays as it is).
- The choice is remembered in the browser; until the user chooses, Hebrew if the browser prefers it, otherwise English.
- Pages without a translation (The Gang, Rock-paper-scissors, Tic-tac-toe) stay in English and show no flag.

## Sessions

- A **session** is one instance of a specific game, identified by a unique code.
- A session can be created from the main screen, joined by entering its code, or opened directly from its URL.
- A session's status is **Lobby** (the session lobby), **Playing** (the game screen), **Paused** (the game screen, waiting for an absent player) or **Over** (the game has ended by itself and shows its result). Each game also has its own phases inside Playing, e.g. the exchange in Rich man, poor man or the reveal in The Gang. See [Disconnections and seats](#disconnections-and-seats).
- The top of a session's page stays in place while the page scrolls. Under the top bar, the **session bar** (as tall as the top bar) shows the game's picture and name, the user's role in words (e.g. "You: ⚪ White", "You: X", "You: Player 3", or "👁 Watching"), the game code (🔑; clicking it copies it to the clipboard), and during a game a menu button (☰). The menu shows the invite link with **Copy** (and **Share** where the browser supports it), how many players are seated, and for players during a game **Leave my seat (watch)** and **End game for everyone** (after confirming). When it does not all fit on one line (e.g. on a phone), the session bar takes two: the game's picture and name, then the rest. The picture and name are on the left (on the right in Hebrew), and the other items on the right (on the left in Hebrew), on either line. During a game, a **status bar** under it shows the game's status and messages to the user, with the buttons of the turn where the game has them (e.g. Backgammon's 🎲 **Roll** and **Done**). While the user waits for others, the status bar is grey; when they have something to do (e.g. their move, or a button waiting for them such as **Next round**, **Next game**, **Next heist** or **Play again**), it turns gold, with a larger text, and flashes once. The colour alone shows whose turn it is, so the text has no "waiting" or "your turn" icons. The buttons between rounds and games (**Next round**, **Next game**, **Next heist**, **Play again**, **Back to the lobby**) are in the status bar. In games that keep a score (Tic-tac-toe, Rock-paper-scissors, Backgammon, Yatzee), a score row under the status, in the same colour, shows the round or game number (and the draws, or the match length), and each player's score (and pip count in Backgammon), with the player to act outlined. The round or match line is on its own; the players share the line under it when both fit, otherwise each has a line. The game options are shown and changed only in the session lobby.
- Sessions are saved and kept for 30 days after their last activity, so players can come back to a game later, even after the server restarts. After that, its code no longer works.

## Session lobby

- A screen shared by all games, showing the game's **seats**.
- Any user can take a seat. Users do not choose a seat: the taken seats are always the first ones, followed by a single empty seat (**Sit here**), until all seats are taken. A user may take more than one seat (and then sees the private information of all of them).
- A user can leave a seat by tapping it. The seats after it then move up one, so there are no gaps.
- On every page except the main screen, the GamesLane name in the top bar goes back to the main screen (for players, this gives up their seats).
- A user who loses their connection (closed tab, phone asleep, network lost) keeps their seats for 60 seconds, and gets them back if they return in time; their seat shows a spinner meanwhile. In the lobby, the seats are then freed, as if they had left. During a game, see [Disconnections and seats](#disconnections-and-seats).
- Creating a session does not seat the creator; they join the session and can then take a seat.
- Each game has its own number of seats and minimum to start:

  | Game | Seats | Needed to start |
  | --- | --- | --- |
  | Tic-tac-toe, Rock-paper-scissors, Backgammon | 2 | 2 |
  | The Gang | 8 | 3 |
  | Rich man, poor man | 12 | 6 |
  | Yatzee | 8 | 1 (it can be played alone) |

- When the minimum number of seats is taken, a **Start** button appears (**New game** after the first game). Any seated user can start the game.
- While the session is in the lobby, an **Invite friends** panel shows the join link (`/join/{code}`), with **Copy link** and **Share…** buttons where the browser supports them.
- In the lobby, everyone sees the chosen [game options](#game-options) on a big **Game options:** button; anyone in the session can change them.
- Anyone not seated is a **spectator**.

## Game screen

- Once the game starts, the session lobby is replaced by a game-specific screen.
- Seated players may see a different version of the screen than spectators (for example, their own cards in hand). Spectators never see any player's private information.
- Anyone can watch an active game. During a game, the only seats that can be taken are those of absent players (see below).
- A seated user can **leave their seat** (**Leave my seat (watch)** in the ☰ menu; they become a spectator) or **leave** (go back to the main screen, giving up their seats).
- A player can **End game for everyone** (in the ☰ menu) at any time during a game (after confirming): everyone goes back to the session lobby, the seats move up, others can join, and **Start** begins a new game.
- When a game ends (The Gang, a backgammon match, and Yatzee), anyone seated can choose **Play again** while at least the minimum number of players are seated, or go **Back to the lobby** (for everyone). A new game is played by whoever is seated (any number between the minimum and the maximum). If fewer than the minimum remain, the session goes back to the session lobby automatically.
- Tic-tac-toe and rock-paper-scissors never end by themselves; players leave their seats or the game when they are done.

## Disconnections and seats

- **Seat states:**

  | State | Meaning | Starts | Ends |
  | --- | --- | --- | --- |
  | **Present** | The holder is connected | Taking the seat, or reconnecting | The holder disconnects or leaves |
  | **Reconnecting** | The holder lost their connection less than 60 seconds ago; the seat shows a spinner. For the game, the seat is still present: nothing else changes | Disconnecting | Coming back (Present), or 60 seconds (Away) |
  | **Away** | The holder has been disconnected for 60 seconds or more; the seat is still theirs | 60 seconds after disconnecting | The holder comes back (at any time), someone takes the seat, or the game ends |
  | **Vacant** | Nobody holds the seat | **Leave my seat**, going back to the main screen, or an Away seat freed (below) | Someone takes it |

  An **absent** seat is one that is Away or Vacant.
- **Only while a game is in progress** (Playing or Paused) are Away seats kept. In the session lobby, and once a game is Over, an Away seat is freed.
- **Coming back:** after a refresh or a dropped connection, the page reconnects by itself and the player keeps their seats: in the session lobby, as long as they return within 60 seconds; during a game, at any time, unless someone has taken their seat.
- **Taking an absent seat:** during a game, anyone can take an absent seat of the game with **Take this seat** (in the banner in the status bar, which names the Away players and counts the empty seats). A player in the game can also take it as an additional seat (**Take additional seat**), and then plays both seats. Whoever takes it continues that player's game (their cards, checkers or chips). If the seat was Away, its holder loses it: when they come back, they are a spectator.
- **What a game does with absent seats** depends on the game:

  | Game | With an absent seat | Effect |
  | --- | --- | --- |
  | Tic-tac-toe, Rock-paper-scissors, Backgammon, The Gang, Yatzee | **Wait:** the session is Paused | The status bar shows "Game paused: …" (the players waited for) with **Take this seat** (or **Take additional seat** for players); players can **End game for everyone** from the ☰ menu. Present players can still do what does not need the absent seat (e.g. finish their own turn, pick, move their chip); the game stops where it needs the absent player |
  | Rich man, poor man | **Act automatically:** the seat passes | The game goes on and never pauses; the seat keeps its cards. If every seat is absent, the game waits for a player to come back or take a seat. At the card exchange, absent seats are removed (see the game's rules) |

- **Ending:** any one present player can **End game for everyone** during a game (after confirming), which takes everyone back to the session lobby. The session also goes back to the lobby with **Back to the lobby** after a game is Over, and when there are too few players for the next game (**Play again** with fewer than the minimum seated, or Rich man, poor man's exchange with fewer than 6).
- **Reopening a session later:** a session nobody has had open for a while counts everyone as disconnected since then. In the lobby, their seats are freed; during a game, their seats are Away and they get them back by opening the session again.

## Game options

A game can offer **options**, i.e. rule variants (so far The Gang's and backgammon's). A **choice** option is one of its choices. A **set** option (e.g. The Gang's challenge cards) is a list of choices, all on by default (for cards, each with its picture: tap a card to turn it on or off; a card that is off is greyed out). Some options are shown only when another option has a certain value (e.g. the card options only in Advanced mode).

- In the session lobby, the chosen options are shown on a big button starting with **Game options:**. Anyone in the session can press it and change the options in the dialog it opens, spectators too. They apply to the next new game; a paused game keeps the options it started with.
- Saved users (registered or Google, not guests) can **save** the options as their preferences for that game, and **load** them back in the dialog. A session starts with its creator's saved preferences.

## Games

### Tic-tac-toe

2 players. After each board, the wins and draws are kept and players start the next round. The starting player alternates: X starts the odd rounds, O the even ones.

### Rock-paper-scissors

2 players. Both players pick secretly. When both have picked, the picks are revealed and the winner scores a point. Play continues for as many rounds as you like.

### Backgammon

2 players, standard rules. The first seat plays white, the second black.

- **A match** to 1, 3, 5, 7 or 9 points (option **Match length**, default 5). A single game scores 1 point, a gammon (the loser bore off no checker) 2, a backgammon (and still has a checker on the bar or in the winner's home board) 3, times the cube. When the match ends, **Play again** starts a new one; between games, **Next game**.
- **Opening roll:** at the start of each game, each player rolls their own die (tap the board, or 🎲 **Roll** in the status bar); each die stays on its player's side. On a tie, both dice stay on view until a player rolls again, and both roll again. The player with the higher die starts: they roll both dice for their first turn (and may not double before it).
- **A turn:** tap the board (or press 🎲, the **Roll** button) to roll, move the checkers, then press **Done**. 🎲 **Roll**, **Double**, **Take**, **Drop**, **Undo**, **Done** and **Next game** are buttons in the status bar, shown when they can be used. The dice tumble for one second when rolled; between turns, the last roll stays on the board, faded. **Undo** takes back the last checker moved in the turn (a combined move counts as one checker moved per die). Tap a checker and then one of its highlighted destinations; a checker that can only go to one place moves at once.
- **Combined moves:** a checker can also move by both dice at once (e.g. 13 to 5 with 5-3, or by two, three or four dice with doubles), as one of its destinations. This is offered only when the checker can get there, and only when every way of getting there gives the same result (e.g. not when going by the 3 first hits an opposing checker and going by the 5 first does not; then the dice are played one at a time). With doubles, the fewest dice are used.
- **Last move:** a **Last move** button in the status bar draws the last turn's checker moves on the board as numbered arrows; pressing it again hides them. The rules are enforced: a checker on the bar must enter first, a point with two or more opposing checkers is blocked, a single opposing checker is hit, bearing off only once all checkers are home (with a higher number only when no checker is on a higher point), as many dice as possible must be used (all four with doubles), and if only one die can be used, it must be the larger one when possible.
- **Automatic turns:** if no checker can move, the turn passes; if there is only one way to play the dice, the game plays it and the turn ends. **Last move** shows what was played.
- **Doubling cube** (option, on by default; not in 1-point matches): before rolling, the player may **Double** (the button in the status bar, or tap the cube, which is outlined when they may); the other player **Take**s (the cube doubles and they own it) or **Drop**s (losing the game at the cube's value). Crawford rule: no doubling in the game after a player first comes within one point of winning the match.
- **The board** looks like a wooden board: a dark walnut frame and bar, a light maple playing field with dark and ivory inlaid points, and a dark tray, with wood grain. It is seen from each player's own side of the table: the board is turned around (rotated, not mirrored) between the players. Black's home board is at the bottom right, bearing off into the tray on the right, with its two back checkers at the top right; white's home board is at the bottom left, bearing off into the tray on the left, with its two back checkers at the top left. Spectators see white's side, and a user holding both seats sees the side of the seat that must act. It shows the dice on the mover's side (used dice faded) and the cube; the match length, game number, scores and pip counts are in the status bar's score row. Moves are not highlighted on the board (the previous turn's are shown only with **Last move**); the checker just moved drops into place. The board scales to fit the screen, in portrait or landscape, and on a TV: it shrinks so that the whole page (scores, board and buttons) is visible without scrolling.

### The Gang

3–8 players. The cooperative poker game by John Cooper and Kory Heath. Each heist:

- Every player gets 2 pocket cards, shown only on their own screen. Five shared cards are dealt: none at first, then 3, 4 and 5 as the stages go on.
- Four betting stages (white, yellow, orange, red). In each, every player takes a numbered chip, from 1 (weakest hand) up to the number of players (strongest), from the middle or from another player. Tapping your own chip returns it to the middle. Players are numbered 1 to the number of players in seat order: each hand is titled "Player N", and the table's chip list shows the numbers. Next to the player's cards are the chips they took in this heist's earlier stages. Once every player holds a chip, a **Confirm** button appears in the status bar; the stage ends when everyone has pressed it. Once pressed it shows **✓ Confirmed** (tap it again to undo), and the table marks confirmed players with 👍. Any chip change clears everyone's confirmation (and hides the button again while a chip is in the middle). A user with several seats confirms once, for all their hands.
- **Chip taken:** when another player takes one of your chips, your device plays a short alert sound (after you have tapped the page once, as browsers require). Nothing plays when you take a chip between your own seats.
- Players work out their hand's strength themselves; its value is only shown when the hands are revealed. With the option **My hand's ranking** (Hidden by default, or Shown), each player sees next to their own cards the ranking their pocket cards make with the shared cards so far (e.g. "Pair", then "Two pair" once more shared cards appear); nobody else sees it.
- Hands are then revealed one at a time, from the lowest red chip to the highest, each with its ranking and its five cards, most important first, in bold (e.g. "Two pair: 9, 9, 5, 5, K"). A hand at least as strong as the one before it has a thick green frame; a weaker one has a thick red frame, with the first reason in red: the ranking if it is lower, otherwise the first lower card values (e.g. "Two pair: 9, 9, 5, 5, **7**" after "Two pair: 9, 9, 5, 5, K"). If any hand is weaker than the one before it, the alarm goes off; otherwise the vault opens. Equal hands may be in either order.
- 3 vaults win the game; 3 alarms lose it.
- **Jokers** (option, off by default, in both modes): the deck has two jokers.
  - **In a hand:** before the betting, a player dealt a joker gets two cards and taps the one to keep; the other is discarded. If one of the two is a joker, they either keep the other card, or tap the joker to swap it for two more cards (each choice is between two cards). With two jokers in hand, they are swapped one after the other. The betting starts once every joker is swapped. Everyone sees that a player swapped a joker (🃏 next to their name in the chip table), but not which cards.
  - **Cards drawn later** (Motion Detector, Laser Tripwires, Hacker): a joker is swapped the same way, and the stage cannot end until it is.
  - **On the table:** a joker is replaced by two cards, shown side by side in a frame; if one of them is the other joker, it is replaced too, and all three cards show. Each hand uses at most one card of the frame; the game always counts the best hand it allows.
- **Advanced mode** (option **Mode**; Basic is the default) adds challenge and specialist cards. The first heist gets 0–3 challenge cards (option, default 1); after a successful heist, one challenge card is added, and after a failed heist, one specialist card. Cards stay in play for the rest of the game. They are drawn at random from the enabled ones (all by default; players can turn cards off), each at most once per game. As in the original game, any combination can come up; with Quick Access (no white chips), Motion Detector and Laser Tripwires have no effect. The option **Cards that cancel each other** (Allow by default, or Avoid) keeps such pairs from being in play together. The cards in play lie on the right of the table (at the bottom on narrow screens), each showing a picture (drawn for this site from the card's name, not taken from the original game), its kind and its name, with the ones added for this heist marked **New**; tapping a card opens it on the table: it grows, with a bigger picture and what it does, and tapping it again closes it. Each user's open cards stay open as the game goes on. With both Investor and Math Whiz in play, both facts are shown. The full card texts are in [`the-gang/`](the-gang/).
- All 10 challenge cards and all 10 specialist cards are included. Motion Detector and Laser Tripwires are checked when the first 3 shared cards appear; the player who gets new pocket cards is shown to everyone. That player gets a panel saying their cards were replaced, with a **Got it** button; the stage cannot end until they press it. Their white chip no longer counts: it is crossed out in red wherever it is shown (the chip table, next to their hand). Locked chips (Noise Sensors, Ventilation Shaft) stay with whoever takes them first: they cannot be taken, swapped or returned. With Blackout, earlier stages' chips are hidden: the chip table shows each as a chip of its stage's colour with "?" instead of the number. The specialist cards that start "Once the pocket cards have been dealt" (Coordinator, Con Artist, Investor, Math Whiz) are not optional and happen by themselves in every heist, before the betting: first Coordinator (everyone picks a card to pass to the next player; it moves once all have picked), then Con Artist (everyone confirms they have seen their cards, then all pocket cards are shuffled and dealt again), then Investor and Math Whiz show every player's count or total to everyone, in the chip table.
- **The "Decide which of you…" specialist cards are optional.** The group may use any of them, in any order, at any time during the betting, each at most once per heist; when everyone is ready in the red stage, the hands are revealed however many are left unused. Next to a player's hand, each unused one is a folded panel.
- **Group decisions** (Informant, Getaway Driver, Mastermind, Hacker, Jack, Muscle, Retina Scan, Fingerprint Scan): any player proposes a player and/or a value; everyone sees it, and it takes effect once every voter has pressed the 👍 (any change clears the agreement). Each user votes once, for all their seats, and the count is per user (e.g. "👍 1 of 3" with 3 users); the same goes for Con Artist's "I've seen my cards". Informant's player then secretly shows one card to one other player; Hacker's player draws a card and Jack's player gets the suitless Jack card (never part of a flush), and each then discards one; the stage cannot end until they have. Getaway Driver and Mastermind show the answer to everyone. Muscle's hand beats every hand of the same ranking at the reveal; a green **💪 Muscle** badge marks that player's own hand (next to their pocket cards) and, at the reveal, their revealed hand. The scans are decided just before the strongest hand is revealed, by everyone except the user who holds that hand's seat, for all of their seats, since that hand is on their screen (if one user plays every seat, only the strongest hand's seat sits out); a wrong guess fails the heist. If the hands are already out of order (including the strongest hand, not yet revealed), the heist fails anyway, so the scans are skipped.
- **What the cards did is shown in the chip table, not on the cards.** Under each player's name, one line per card, starting with the card's name: e.g. "Motion Detector: new pocket cards were dealt", "Investor: 2 face cards", "Math Whiz: 18 total", "Informant: showed a card to …", "Hacker: drew a card and discarded one", "Getaway Driver: High card", "Mastermind: one 9", and the scans' guesses about the strongest hand with a green **PASS** or red **FAIL** badge. A note under the chip table shows group decisions under way (e.g. "Mastermind: deciding, 1 of 3 agreed").
- **Unused cards are faded** (grey and see-through): Motion Detector or Laser Tripwires once it is known that it did not go off (when the first 3 shared cards appear, or at once with Quick Access), optional specialist cards the group has not used yet in this heist, and scans that are not needed.
- **Spectator screen** (e.g. a TV): the whole table fits on the screen without scrolling. The table is sized to the largest that fits, so it grows on large screens and shrinks on small ones; on landscape screens the heist's cards are in a column on the right, and on very wide screens (or without heist cards) the chips and the revealed hands sit side by side.
- Players can **Hide table** (and **Show table** again), e.g. when a shared screen shows it; the browser remembers the choice. Until they choose, the table is hidden on phones and tablets and shown on computers.
- Not yet included: in-game chat.

### Rich man, poor man

עשיר ועני, 6–12 players. A shedding card game, in English and Hebrew.

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
- **Between games:** a seated player presses **Next game** to start it; there is no long-term score: each game only decides the seats and the exchange of the next one.
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
- **An absent player during a game** (Away or Vacant, see [Disconnections and seats](#disconnections-and-seats)): their cards are held until the end of the game. They become inactive and pass automatically; if they are to start a round, the next active player in turn starts it. An Away player who comes back plays on. Anyone can take an absent seat (**Take this seat** in the banner, no approval needed) and continue the game in their place, with their cards and their place. If every player is absent, the game waits until someone comes back or takes a seat. An inactive player gets no finishing place. When the game ends and **Next game** is pressed, absent seats are removed, and the exchange is for the remaining number of players (the others keep their order, by the ranking); a player who becomes absent during the exchange is removed too.
- **Fewer than 6 players:** a game in progress continues with however many active players are left, but there can be no exchange with fewer than 6 players: **Next game** then takes everyone back to the session lobby (a new game can start there once 6 are seated). Likewise, if players leave during the exchange and fewer than 6 remain, everyone goes back to the lobby.
- **Joining:** during a game, nobody can take a seat other than a seat whose player left. During the exchange, an observer can **Ask to join**; any player sees the request and can **Accept** it, choosing the newcomer's place (#1 to the last; the players from that place down move down one), or **Decline** it. A player who leaves during the exchange is dropped at once. Each time a player is added or dropped, one set of 2 to A is added to or removed from the deck (so each number still appears once per player), all the cards are shuffled and dealt again, any cards already given are cancelled, and the exchange starts again for the new number of players.
- **Screens:**
  - **A player's phone** shows their hand sorted from low to high, with equal numbers together, overlapping as needed so the whole hand and the table fit on the screen without scrolling. They tap one or more cards of the same value, then **Select** to put them on the table, or **Pass**. Tapping a card when there is a throw to beat picks as many cards of that number as are needed. A user holding several seats sees one hand per seat.
  - **The table** (shown to everyone, and to observers such as a TV) shows the cards to beat and who threw them, then the round's winner; and the players in place order (#1, the rich man, to the last, the poor man), one line each, with their number of cards, whose turn it is, their last call in this round (**Pass**, or what they threw, e.g. "2×9"), who left, the finishing places so far, and during the exchange, how many cards each player gives and to whom (e.g. "Gives 4 cards to #8", then "✓ Gave 4 cards to #8" once given). After a game, it shows the next game's places.
  - **Read throws aloud:** on a screen without a seat, this button says each throw out loud (e.g. "3 nines"), in the page's language, using the browser's speech; the choice is remembered in the browser. Hebrew needs a Hebrew voice on the device: without one, the throws are read in English (on Windows, add a Hebrew voice in Settings > Time & language > Speech).

### Yatzee

יאצי, 1–8 players. The classic dice game, in English and Hebrew. Players take turns in seat order for 13 rounds.

- **A turn:** press **Roll** (in the status bar) to roll the five dice, up to three times; the button then says how many rolls are left. Between rolls, tap dice to keep them (a kept die is lifted, outlined in gold and marked **Kept**); tap again to let it go. Then tap an open box of your scorecard to score the dice there, which ends the turn. The dice tumble for a moment when rolled.
- **The boxes:** Ones to Sixes score the dice of that number. Three of a kind and Four of a kind score the total of all the dice; a full house (three of one number and two of another) scores 25, a small straight (four in a row) 30, a large straight (five in a row) 40, a Yatzee (five of a kind) 50, and Chance the total of all the dice. A box whose pattern is not there scores 0 (the page asks before scoring 0).
- **Bonuses:** 35 when Ones to Sixes add up to 63 or more; 100 for each extra Yatzee rolled while the Yatzee box holds 50.
- **Joker rule** (official): a Yatzee rolled once the Yatzee box is filled (with 50 or 0) must go in the upper box of its number if that box is open; otherwise in any open lower box, where a full house and the straights score in full; otherwise in any open upper box, for 0.
- **The end:** once every box is filled, the highest total wins (several players on a tie); alone, the status bar shows the final score. **Play again** starts a new game with clean scorecards.
- **Screens:** everyone sees the dice and the scorecard, with a column per player, the section totals and bonuses, and the player to play highlighted. Their open boxes show what the dice would score there (on their own screen, as buttons to tap). The status bar's score row shows the round and each player's total; a message under the dice says what the last player scored.
