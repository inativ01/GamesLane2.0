# App Store submission

Everything App Store Connect asks for when submitting the GamesLane iPhone app (`GamesLane.Mobile/ios`), ready to copy. The Android app's submission is in [play-store.md](play-store.md). Keep it in step with the app: the privacy answers must match the app's privacy manifest (`ios/App/App/PrivacyInfo.xcprivacy`) and the [privacy policy](../GamesLane.Server/wwwroot/privacy.html).

- [Before submitting](#before-submitting)
- [App information](#app-information)
- [Version page: the listing](#version-page-the-listing)
- [Screenshots](#screenshots)
- [App Privacy](#app-privacy)
- [Age rating](#age-rating)
- [Pricing and availability](#pricing-and-availability)
- [App Review information](#app-review-information)
- [Review notes](#review-notes)

## Before submitting

Decided:

- **Chat (guideline 1.2, user-generated content):** public games, which seat people who do not know each other, have no chat. Only private games, played with people one invited, have it.
- **Game names (guideline 5.2, intellectual property):** every game has its own name in GamesLane. Poker Heist's challenge and specialist cards follow a published game's cards; make sure their texts and pictures may be used.

Checklist:

- [ ] The checks on a phone in the developer guide's [Next steps](developer-guide.md#next-steps) (sign-in, join links, notifications), also on an iPad.
- [ ] A TestFlight build from Xcode Cloud, tested: it uses Apple's production push service, like the App Store version.
- [ ] The version is right (`MARKETING_VERSION`, 1.0 for the first release; see the developer guide's [iPhone app](developer-guide.md#iphone-app)).
- [ ] Screenshots for iPhone and iPad.
- [ ] Everything below filled in, then **Add for Review**.

## App information

| Field | Value |
| --- | --- |
| Name (30 characters) | `GamesLane` — if the name is taken: `GamesLane: Board & Card Games` |
| Subtitle (30) | `Board & card games with family` |
| Bundle ID | `com.gameslane.app` |
| SKU | `gameslane-ios` (any text; never shown) |
| Primary language | English (U.S.) |
| Category | Games; subcategories **Board** and **Card** |
| Secondary category | Entertainment (optional) |
| Content rights | Does not contain, show or access third-party content (the sounds are Kenney's Casino Audio, CC0) |
| Privacy policy URL | https://gameslane.onrender.com/privacy.html |
| Copyright | `2026 Isaac Nativ` |

**Languages:** the app declares the site's seven languages (`CFBundleLocalizations` in `Info.plist`: `en`, `he`, `es`, `fr`, `pt-BR`, `zh-Hans`, `zh-Hant`), so the App Store can list them. After uploading a build, check that the version page shows them. The listing itself can also be translated in App Store Connect (each language's name, subtitle, description and keywords).

## Version page: the listing

**Promotional text** (170 characters; can be changed at any time without a new review):

> Ten classic board and card games to play together, in the same room or far apart. Free, no ads, and no account needed.

**Description** (4000 characters):

> GamesLane brings classic board and card games to the family table, and to friends and family wherever they are.
>
> Create a private game, send the invite link or the game code, and play together in real time. A favourite setup: an iPad or TV shows the table while each player plays on their own phone. No one nearby? Join a public game and play with others on GamesLane, or take several seats and play on one device.
>
> TEN GAMES
> • Backgammon: matches with the doubling cube
> • Hearts: passing cards, to 100 points
> • Spades: two teams, bidding with Nil and Blind Nil
> • Dominoes: Block, Draw, All Fives and Mexican Train
> • Yaniv: call Yaniv with the lowest hand, but beware of Assaf
> • Final Card: the fast shedding game, in US or Israeli style
> • Rich man, poor man: the shedding game for 4 to 12 players
> • Poker Heist: the cooperative poker game
> • Wazoo: the classic five-dice game, alone or with up to 8 players
> • Tic-tac-toe
>
> MADE FOR PLAYING TOGETHER
> • Real-time play on phones, tablets and computers
> • A notification when it is your turn, so you can put the phone down
> • The screen stays on while a game is played
> • Leave and come back: your seat waits for you
> • Game chat, and options for each game's house rules
> • Help on every screen, with the full rules of every game
> • English, Hebrew, Spanish, French, Portuguese and Chinese
>
> SIMPLE AND PRIVATE
> • Play straight away as a guest, or sign in with Apple to keep your profile on all your devices
> • Free, with no ads, no in-app purchases and no tracking

**Keywords** (100 characters, commas without spaces; the name and category need not be repeated):

```
board games,card games,backgammon,hearts,spades,dominoes,yaniv,family,multiplayer,friends
```

| Field | Value |
| --- | --- |
| Support URL | https://github.com/inativ01/GamesLane2.0/issues |
| Marketing URL (optional) | https://gameslane.onrender.com |
| Version | 1.0 |
| What's New | Not shown for the first version |
| Build | The TestFlight build to release, chosen from the list |
| Release | Manually release this version (to choose the day), or automatically after approval |

## Screenshots

Up to 10 per device size; the first three show in search results. Take them in the Simulator (**File → Save Screen**, or ⌘S) on the largest iPhone and the 13-inch iPad Pro, with the app built for the production site.

| Device | Size (portrait) |
| --- | --- |
| iPhone 6.9" | 1320 × 2868 (or 1290 × 2796) |
| iPad 13" | 2064 × 2752 (or 2048 × 2732) |

Suggested screens, each in a game in progress with a few players seated (several seats on one device work):

1. The main screen with the list of games.
2. Backgammon.
3. A card game with the hand showing (Hearts, Yaniv or Final Card).
4. Dominoes, Mexican Train.
5. The lobby with the invite link and the players seated.
6. A turn notification on the lock screen.
7. The same game in Hebrew or Chinese, to show the languages.

An optional app preview video (15–30 seconds) can be recorded in the Simulator (**File → Record Screen**).

## App Privacy

**Data used to track you:** none. **Tracking:** No (no advertising, no third-party analytics, no data brokers).

Data collected (the same as `PrivacyInfo.xcprivacy`):

| Apple's data type | What it is in GamesLane | Linked to the user | Purpose |
| --- | --- | --- | --- |
| Contact Info → Name | The display name; with Apple, Google, Microsoft or Discord, the name given by the service as the first display name | Yes | App Functionality |
| Identifiers → User ID | The account ID, and the account ID at Apple, Google, Microsoft or Discord | Yes | App Functionality |
| Identifiers → Device ID | The device token for turn notifications, if allowed | Yes | App Functionality |
| User Content → Photos or Videos | An uploaded profile picture (optional) | Yes | App Functionality |
| User Content → Gameplay Content | The games created or joined and the moves made | Yes | App Functionality |
| User Content → Other User Content | Chat messages in a game | Yes | App Functionality |
| Usage Data → Product Interaction | Anonymous daily counts (visits, games opened, created, joined, finished) | No | Analytics |

Not collected: email address, phone number, location, contacts, payment information, browsing history, diagnostics. Passwords are kept only as a salted hash.

## Age rating

Answer the questionnaire as follows; App Store Connect then computes the rating.

| Question | Answer |
| --- | --- |
| Violence, sexual content, profanity, horror, drugs, alcohol, tobacco | None |
| Simulated gambling | None (card and dice games played for points; no betting and no money) |
| Contests | None |
| Unrestricted web access | No (other sites open in Safari) |
| User-generated content | Yes: display names and profile pictures, seen by the other players, and chat in private games |
| Messaging and chat | Yes: chat between the players of a private game, who invited each other by link or code. Public games have no chat |
| Advertising | No |
| Parental controls, age assurance | No |
| Medical or wellness topics | No |

The questionnaire decides the rating; with chat limited to invited players, expect a low one (likely 9+).

## Pricing and availability

- Price: **Free**. No in-app purchases, no subscriptions.
- Availability: all countries and regions (or a chosen list).
- iPhone and iPad. Not offered on Mac with Apple silicon or on Apple Vision Pro, unless tested there.

## App Review information

| Field | Value |
| --- | --- |
| Sign-in required | No. Everyone plays as a guest; no account is needed |
| Demo account | None needed |
| Contact | Isaac Nativ, gameslane@yahoo.com, and a phone number (App Store Connect requires all four fields: first name, last name, phone and email; Apple uses them only during the review) |
| Notes | The text under [Review notes](#review-notes) |
| Attachment (optional) | A short screen recording of a game with turn notifications helps |

## Review notes

Paste into **Notes** (4000 characters):

```
GamesLane is a free app for playing ten classic board and card games (Backgammon, Hearts, Spades, Dominoes, Yaniv and more) with family and friends in real time. No account is needed: the app starts as a guest. No ads, no purchases, no tracking.

TRYING A GAME ON ONE DEVICE
A game can be played on one device by taking several seats:
1. On the main screen, under Private games, tap Create a game and choose a game (for example Backgammon or Hearts). You are seated at once.
2. In the lobby, tap Sit here on the other seats until enough players are seated.
3. Tap Start. You now play for every seat you hold.
Wazoo (the dice game) can also be played by a single player.

PLAYING WITH A SECOND DEVICE
Create a private game on the iPhone, then on a second device open the invite link, or https://gameslane.onrender.com and enter the game code shown in the lobby (the key icon). Both devices join the same game.

FEATURES OF THE APP (beyond the website)
- Turn notifications: the first time you sit in a game, the app asks to send notifications. When it becomes your turn while the game is not on the screen (phone locked, another app open, or the app closed), a notification says "Your turn" with the game's name; tapping it opens the game. To try it: create a Tic-tac-toe game on the iPhone, join it from a second device or a computer's browser (so each holds one seat), start, make the iPhone's move, lock the iPhone, then make the move on the other device. The iPhone shows the notification. (A player holding every seat on one device is never notified, since every turn is theirs.)
- Sign in with Apple, in Apple's own sign-in sheet (profile, then Log in or create account). Google, Microsoft and Discord sign-in open in Apple's secure sign-in window.
- Invite links (https://gameslane.onrender.com/join/...) open the game in the app.
- The phone vibrates when your turn comes, and the screen stays on while a game is played.

ACCOUNT DELETION
Profile (your name at the top right) > Delete account deletes the account and its data at once; for Sign in with Apple, the app also revokes its Apple token.

CONTENT
Players choose a display name. Chat is available only in private games, which are played with people one invites by link or code; public games, which match people who do not know each other, have no chat. Each player can also hide the chat in their profile. Comments and reports can be sent from the Send feedback link at the bottom of the main screen and the menu of every game.

The server is always on; games are played live over the internet.

CONTACT
gameslane@yahoo.com
```
