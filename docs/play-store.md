# Google Play submission

Setting up Google Play and Firebase for the GamesLane Android app (`GamesLane.Mobile/android`), and everything Play Console asks for when submitting it, ready to copy. Keep it in step with the app: the Data safety answers must match the [privacy policy](../GamesLane.Server/wwwroot/privacy.html) and the App Store's [App Privacy](app-store.md#app-privacy) answers.

- [Setup](#setup)
- [Before submitting](#before-submitting)
- [Google TV and Android TV](#google-tv-and-android-tv)
- [Store listing](#store-listing)
- [Graphics](#graphics)
- [App content](#app-content)
- [Data safety](#data-safety)
- [Content rating](#content-rating)
- [Releases and testing](#releases-and-testing)
- [Review notes](#review-notes)
- [Review notes for Google TV](#review-notes-for-google-tv)

## Setup

Once, in this order. Nothing here is a secret except the keystore, its passwords and the Firebase service account key: keep those out of the repository, chat and command lines.

### Which Google account owns what

GamesLane uses two Google accounts:

| Account | Owns |
| --- | --- |
| **The Android account** (new, made for Google Play) | The Google Play Console developer account and the app `com.gameslane.app`; the Firebase project for the Android app's notifications |
| **The original account** | The Google Cloud project "GamesLane" with the OAuth client for **Sign in with Google** on the website and in the apps |

They do not need to be the same account: the Android app finds its Firebase project through `google-services.json`, the server through `firebase-key.json`, and Google sign-in through the OAuth client's ID and secret on Render. Leave Google sign-in where it is; moving it would mean a new OAuth client, new `Authentication__Google__*` values on Render and its redirect URIs registered again, for no gain.

### Keeping access

A Google Play developer account closed for being unused cannot be reopened, and an app cannot leave the developer account that published it without a transfer request to Google. So:

1. **Give each account access to the other's services**, so losing one account (forgotten password, lost phone, account closed) does not lose the service:
   - **Play Console** (Android account): **Users and permissions → Invite new users** → the original account, with **Admin (all permissions)**. Accept the invitation from the original account. Only the Android account stays the owner (the account holder), but the admin can do everything else, including releases.
   - **Firebase** (Android account): **Project settings → Users and permissions → Add member** → the original account, role **Owner**.
   - **Google Cloud project "GamesLane"** (original account): https://console.cloud.google.com/iam-admin/iam, with the project chosen → **Grant access** → the Android account, role **Owner**.
2. **Secure both accounts:** 2-Step Verification on, with a recovery email and phone that you keep, and the backup codes stored in a password manager. Keep each account's password there too.
3. **Keep the accounts in use:** sign in to each at least every few months. Google may delete a personal Google account unused for 2 years, and Google Play may close a developer account that has published no app and not signed in for a long time. Publishing the app, and an update now and then, keeps the developer account active.
4. **Keep Play Console's contact details current** (Play Console → **Settings → Developer account → Account details**): Google sends warnings about closing an account, policy problems and verification there. Use an email you read, and check it.
5. **Keep the upload key's backup** (step 3 below) with the passwords; the app signing key is kept by Google (Play App Signing), so it cannot be lost.

### 1. Google Play Console account

1. https://play.google.com/console/signup, signed in with the Android account. Choose **Yourself** (a personal account) or **An organization** (needs a D-U-N-S number), pay the one-time fee ($25) and verify your identity and contact details. Verification can take a few days.
2. A new personal account must run a **closed test** with at least 12 testers for 14 days in a row before it can publish to production (see [Releases and testing](#releases-and-testing)). Check the current rule in Play Console.
3. **Create app**: name `GamesLane`, default language English (United States), **Game**, **Free**; accept the declarations. The package name `com.gameslane.app` is set by the first upload and cannot change.

### 2. Firebase (turn notifications)

1. https://console.firebase.google.com, signed in with the Android account → **Create a project** → a new project, e.g. `GamesLane Android` (not the original account's "GamesLane" Cloud project). Turn **Google Analytics off** (the app does not use it, and the Data safety answers assume it is off). Then add the original account as Owner ([Keeping access](#keeping-access)).
2. **Add app → Android**: package `com.gameslane.app`, nickname `GamesLane Android` (the SHA-1 can be left out). Download `google-services.json` and save it as `GamesLane.Mobile/android/app/google-services.json` (ignored by Git). Skip the SDK steps; the app already has them. Then give it to the GitHub Actions build, in PowerShell in the repository folder:

   ```
   Get-Content -Raw GamesLane.Mobile\android\app\google-services.json | gh secret set ANDROID_GOOGLE_SERVICES_JSON
   ```
3. **Project settings → Cloud Messaging**: check that **Firebase Cloud Messaging API (V1)** is enabled.
4. **Project settings → Service accounts → Generate new private key**. On Render: the service's **Environment** tab → **Secret Files** → add `firebase-key.json` with the file's content, and save (the service restarts). Then delete the downloaded file.

### 3. Upload key

On the Windows machine (JDK 21 is installed). `keytool` asks for a password and the name to put in the certificate; type them yourself:

```
& "$env:LOCALAPPDATA\Programs\jdk-21\bin\keytool.exe" -genkeypair -v -keystore "$env:USERPROFILE\gameslane-upload.jks" -alias upload -keyalg RSA -keysize 2048 -validity 10000
```

Keep `gameslane-upload.jks` and its password safe, outside the repository (e.g. in a password manager, with a backup). If it is lost, Play Console can reset the upload key, but that takes a request to Google.

### 4. GitHub Actions secrets

The Android build (`.github/workflows/android.yml`) signs the app bundle with the upload key (`ANDROID_GOOGLE_SERVICES_JSON` was set in [step 2](#2-firebase-turn-notifications)). In PowerShell, in the repository folder; each value goes straight to GitHub without being shown, and the passwords are typed at `gh`'s prompt:

```
[Convert]::ToBase64String([IO.File]::ReadAllBytes("$env:USERPROFILE\gameslane-upload.jks")) | gh secret set ANDROID_UPLOAD_KEYSTORE_BASE64
gh secret set ANDROID_UPLOAD_KEYSTORE_PASSWORD
gh secret set ANDROID_UPLOAD_KEY_PASSWORD
"upload" | gh secret set ANDROID_UPLOAD_KEY_ALIAS
```

`keytool` uses the keystore's password for the key too unless you chose another, so both password secrets are usually the same.

### 5. First upload and the certificates' fingerprints

1. GitHub → **Actions → Android app → Run workflow** (or merge a change to `GamesLane.Mobile/`). Download the run's artifact, `app-release.aab`.
2. Play Console → **Test and release → Testing → Internal testing → Create new release**: upload the `.aab` (Play App Signing is on by default for new apps: Google keeps the app signing key and signs what users install), write the release notes between the `<en-US>` tags, then **Next** and **Save and publish**.
3. **Testers** tab of Internal testing → **Create email list** with the Google account on your phone, saved and ticked → **Copy link** under "How testers join your test". Open the link in a browser signed in with that account (the phone or the computer), **Accept invitation**, then **Download it on Google Play** (from a computer: **Install** and choose the phone). A new release can take up to an hour to appear.
4. **Protected with Play → Play Store protection → Protect app signing key → Manage Play app signing**: the page shows each fingerprint as a copy button. Copy **App signing key → Classical key → SHA-256 certificate fingerprint** (the one Android checks), and, further down, the upload key's SHA-256 (it matches `keytool -list -v` on `gameslane-upload.jks`). Listing more fingerprints does no harm, so also add the **Post-quantum cryptography key**'s SHA-256 (beta) and any key under **Previous app signing keys** (its ⋮ menu). Do not use **Change key**. Fingerprints are public, not secrets.
5. On Render, set the environment variable `Android__CertificateFingerprints` to all of them, separated by commas. To try a debug build from Android Studio too, add the debug key's (see the developer guide's [Sign-in setup](developer-guide.md#sign-in-setup)). Then https://gameslane.onrender.com/.well-known/assetlinks.json lists them. Android checks the site when the app is installed or updated, so if the app was installed before they were listed, reinstall it, or turn on `gameslane.onrender.com` under **Settings → Apps → GamesLane → Open by default → Add link**.
6. Check the app ([Before submitting](#before-submitting)). Firebase needs no fingerprints: turn notifications work without them.

## Before submitting

- [ ] Setup done, and `assetlinks.json` lists the app signing key.
- [ ] Checked on a phone, in the app installed from Play: sign-in with Apple, Google, Microsoft and Discord; a join link opened from another app opens the game in the app (**Settings → Apps → GamesLane → Open by default** lists `gameslane.onrender.com` as verified); turn notifications (allowed when first seated; shown with the phone locked, with another app in front and with the app closed; none while the game is on screen; a tap opens the game); the back button; the screen staying on during a game.
- [ ] The closed test (12 testers, 14 days) done, if Play Console asks for it.
- [ ] Graphics and screenshots.
- [ ] **App content** and **Data safety** filled in, then the store listing; then a production release.
- [ ] Google TV: see [Google TV and Android TV](#google-tv-and-android-tv).

## Google TV and Android TV

The same app and listing (`com.gameslane.app`) runs on TVs, where it shows the web TV app (see the developer guide's [Web TV app](developer-guide.md#web-tv-app-google-tv-lg-samsung)). Google reviews the TV version separately, against its [TV app quality](https://developer.android.com/docs/quality-guidelines/tv-app-quality) checklist.

1. **Try it:** Android Studio → Device Manager → **Create device** → **TV** → Google TV (1080p), API 34 or later; then `npm run emulator` and choose it. Check: the banner on the TV's home screen, the pairing screen, pairing from a phone, a game of each kind showing in full on the screen, the sounds, the screen staying on, and the remote's Back button leaving the app.
2. **Turn on the form factor:** Play Console → the app → **Test and release → Advanced settings → Form factors → + Add form factor → Google TV and Android TV**, and accept its terms.
3. **TV graphics** (Store presence → Main store listing → the Google TV tab): the TV banner, 1280 × 720 PNG or JPEG (the splash picture with the GamesLane name, like the app's banner), and 1 to 8 TV screenshots, 1920 × 1080 (the emulator's camera button): the pairing screen, a lobby, and three or four games.
4. **Release** a build with the TV changes to a test track, then **Test and release → Advanced settings → Form factors → Google TV → Manage → opt in to Google TV**, which sends it for TV review (it can take a few weeks). The review notes for the TV are under [Review notes for Google TV](#review-notes-for-google-tv).
5. **Data safety:** the TV's ID and name (the same as the Apple TV app's) are in [Data safety](#data-safety); update the answers in Play Console before the TV review.

## Store listing

**Main store listing** (Grow users → Store presence):

| Field | Value |
| --- | --- |
| App name (30 characters) | `GamesLane` — if the name is taken: `GamesLane: Board & Card Games` |
| Short description (80) | `Backgammon, Hearts, Dominoes and more, to play together with family and friends.` |
| App category | Game → **Board** (tags: Board, Card, Multiplayer, Family) |
| Email | gameslane@yahoo.com |
| Website | https://gameslane.onrender.com |
| Privacy policy | https://gameslane.onrender.com/privacy.html |

**Full description** (4000 characters): the App Store's [description](app-store.md#version-page-the-listing), with "sign in with Apple" changed to "sign in with Google, Apple, Microsoft or Discord", and "An iPad or TV shows the table" to "A tablet or TV shows the table". Google Play's listing can be translated too (Store presence → Store listings → Manage translations).

## Graphics

| Asset | Size |
| --- | --- |
| App icon | 512 × 512 PNG (32-bit, from `GamesLane.Mobile/assets/logo.png` on the page background, `#f5f1ea`) |
| Feature graphic | 1024 × 500 PNG or JPEG: the logo and name on the page background (`docs/` has the originals) |
| Phone screenshots | 2–8, 9:16 or 16:9, at least 1080 px on the short side (e.g. 1080 × 1920); from the emulator (the camera button in its toolbar) or a phone |
| Tablet screenshots (optional) | 7" and 10" tablets, to be shown to tablet users |

Suggested screens: the same as the App Store's [Screenshots](app-store.md#screenshots), with the turn notification in Android's notification shade.

## App content

Policy and programs → App content:

| Declaration | Answer |
| --- | --- |
| Privacy policy | https://gameslane.onrender.com/privacy.html |
| App access | All functionality is available without special access (everyone plays as a guest) |
| Ads | No, the app does not contain ads |
| Content rating | See [Content rating](#content-rating) |
| Target audience | 13 and over (not designed for children; avoids the Families policy's extra requirements). Appeals to children: No |
| News app | No |
| Data safety | See [Data safety](#data-safety) |
| Government app | No |
| Financial features | None |
| Health | None |
| Account deletion | Yes, accounts can be created. In the app: Profile → Delete account. On the web: https://gameslane.onrender.com, signed in, Profile → Delete account (the privacy policy describes it). Data deleted: the account, its picture, saved game options, saved games and notification devices, at once |

## Data safety

**Does the app collect or share user data?** Yes, collects; shares none (no third parties receive data for their own use; Firebase delivers notifications on GamesLane's behalf, which counts as a service provider, not sharing). **Encrypted in transit:** Yes (HTTPS). **Users can request deletion:** Yes (Delete account; guests' data goes after 30 days unused).

| Google's data type | What it is in GamesLane | Required or optional | Purpose |
| --- | --- | --- | --- |
| Personal info → Name | The display name; with Google, Apple, Microsoft or Discord, the name given by the service as the first display name | Required (a guest gets a random name) | App functionality, Account management |
| Personal info → User IDs | The account ID, and the account ID at Google, Apple, Microsoft or Discord | Required | App functionality, Account management |
| Photos and videos → Photos | An uploaded profile picture | Optional | App functionality |
| App activity → Other user-generated content | Chat messages in a game | Optional | App functionality |
| App activity → App interactions | The games created or joined and the moves made; anonymous daily counts (visits, games opened, created, joined, finished) | Required | App functionality, Analytics |
| Device or other IDs | The Firebase registration token for turn notifications, if allowed; on a TV, the TV's ID, saved with the accounts paired with it | Optional | App functionality |
| App info and performance → Other | On a TV, the TV's name (e.g. "Living Room TV"), saved with the accounts paired with it and shown to them | Optional | App functionality |

Not collected: email address, phone number, location, contacts, financial information, web browsing history, crash logs or diagnostics. Passwords are kept only as a salted hash.

## Content rating

The IARC questionnaire (App content → Content rating), category **Game**:

| Question | Answer |
| --- | --- |
| Violence, fear, sexuality, language, controlled substances, crude humour | No |
| Gambling: simulated gambling | No (card and dice games played for points; no betting, no money, no prizes) |
| Interactive elements: users interact | Yes: chat between the players of a private game, who invited each other by link or code; public games have no chat |
| Users share personal information | No (display names only) |
| Shares the user's location | No |
| Digital purchases | No |

Expect a low rating (e.g. PEGI 3 or Everyone, with "Users interact").

## Releases and testing

- **Internal testing:** up to 100 testers by email, available within minutes; for the first checks (setup step 5).
- **Closed testing:** a list of testers (email addresses or a Google Group). A new personal account needs at least 12 testers opted in for 14 days in a row before it can apply for production access (Dashboard → Apply for production).
- **Production:** Test and release → Production → Create new release, with the `.aab` from GitHub Actions (each run has a higher `versionCode`). Raise `versionName` in `android/app/build.gradle` for each new public version (1.0.1, 1.1 …). Countries: all, or a chosen list.
- The app shows the live site, so most changes need no new release; only changes to the app itself (`GamesLane.Mobile/android`) do.

## Review notes

Play Console → App content → App access lets you add instructions; since everything works without signing in, only the notes below are needed. Paste them where Play Console asks for reviewer instructions, or keep them for an appeal:

```
GamesLane is a free app for playing ten classic board and card games (Backgammon, Hearts, Spades, Dominoes, Yaniv and more) with family and friends in real time. No account is needed: the app starts as a guest. No ads, no purchases, no tracking.

TRYING A GAME ON ONE DEVICE
1. On the main screen, under Private games, tap Create a game and choose a game (for example Backgammon or Hearts). You are seated at once.
2. In the lobby, tap Sit here on the other seats until enough players are seated.
3. Tap Start. You now play for every seat you hold.

TURN NOTIFICATIONS
The first time you sit in a game, the app asks to send notifications. Create a Tic-tac-toe game on the phone, join it from a second device or a computer's browser at https://gameslane.onrender.com with the game code (so each holds one seat), start, make the phone's move, lock the phone, then make the move on the other device: the phone shows "Your turn". Tapping it opens the game.

ACCOUNT DELETION
Profile (your name at the top right) > Delete account deletes the account and its data at once.

CONTENT
Chat is available only in private games, played with people one invites by link or code; public games have no chat. Comments and reports: Send feedback, at the bottom of the main screen and in every game's menu.

CONTACT
gameslane@yahoo.com
```

## Review notes for Google TV

For the TV review, with a review account (a username and password account made for it on the website, its password only in Play Console, never in the repository):

```
On Google TV and Android TV, GamesLane shows a player's games on the TV, as someone watching them sees them, for a family or group playing together in one room. The games are played on phones, tablets or computers; the TV only shows them, so it needs nothing but the remote to open it. No ads, no purchases, no tracking.

TO TRY IT (needs a second device, e.g. a phone or computer)
1. Open GamesLane on the TV. It shows a QR code and a pairing code.
2. On the phone or computer, open https://gameslane.onrender.com and log in with the review account (Log in, at the top right).
3. Scan the QR code, or open the profile (the name at the top right) and enter the code under TVs, then Pair TV.
4. In the profile, turn on Watch on TV for this TV. The TV says it is waiting for the player.
5. On the phone or computer, create a game (for example Backgammon), sit on enough seats and Start. The TV shows the game and follows every move, with sounds.

The remote's Back button leaves the app.
```
