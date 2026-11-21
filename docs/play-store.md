# Google Play submission

Setting up Google Play and Firebase for the GamesLane Android app (`GamesLane.Mobile/android`), and everything Play Console asks for when submitting it, ready to copy. Keep it in step with the app: the Data safety answers must match the [privacy policy](../GamesLane.Server/wwwroot/privacy.html) and the App Store's [App Privacy](app-store.md#app-privacy) answers.

- [Setup](#setup)
- [Before submitting](#before-submitting)
- [Store listing](#store-listing)
- [Graphics](#graphics)
- [App content](#app-content)
- [Data safety](#data-safety)
- [Content rating](#content-rating)
- [Releases and testing](#releases-and-testing)
- [Review notes](#review-notes)

## Setup

Once, in this order. Nothing here is a secret except the keystore, its passwords and the Firebase service account key: keep those out of the repository, chat and command lines.

### 1. Google Play Console account

1. https://play.google.com/console/signup, signed in with the Google account that will own the app. Choose **Yourself** (a personal account) or **An organization** (needs a D-U-N-S number), pay the one-time fee ($25) and verify your identity and contact details. Verification can take a few days.
2. A new personal account must run a **closed test** with at least 12 testers for 14 days in a row before it can publish to production (see [Releases and testing](#releases-and-testing)). Check the current rule in Play Console.
3. **Create app**: name `GamesLane`, default language English (United States), **Game**, **Free**; accept the declarations. The package name `com.gameslane.app` is set by the first upload and cannot change.

### 2. Firebase (turn notifications)

1. https://console.firebase.google.com → **Create a project** → choose the existing Google Cloud project **GamesLane** (the one with the Google sign-in client). Turn **Google Analytics off**.
2. **Add app → Android**: package `com.gameslane.app`, nickname `GamesLane Android` (the SHA-1 can be left out). Download `google-services.json` and save it as `GamesLane.Mobile/android/app/google-services.json` (ignored by Git). Skip the SDK steps; the app already has them.
3. **Project settings → Cloud Messaging**: check that **Firebase Cloud Messaging API (V1)** is enabled.
4. **Project settings → Service accounts → Generate new private key**. On Render: the service's **Environment** tab → **Secret Files** → add `firebase-key.json` with the file's content, and save (the service restarts). Then delete the downloaded file.

### 3. Upload key

On the Windows machine (JDK 21 is installed). `keytool` asks for a password and the name to put in the certificate; type them yourself:

```
& "$env:LOCALAPPDATA\Programs\jdk-21\bin\keytool.exe" -genkeypair -v -keystore "$env:USERPROFILE\gameslane-upload.jks" -alias upload -keyalg RSA -keysize 2048 -validity 10000
```

Keep `gameslane-upload.jks` and its password safe, outside the repository (e.g. in a password manager, with a backup). If it is lost, Play Console can reset the upload key, but that takes a request to Google.

### 4. GitHub Actions secrets

The Android build (`.github/workflows/android.yml`) signs the app bundle with the upload key. In PowerShell, in the repository folder; each value goes straight to GitHub without being shown, and the passwords are typed at `gh`'s prompt:

```
[Convert]::ToBase64String([IO.File]::ReadAllBytes("$env:USERPROFILE\gameslane-upload.jks")) | gh secret set ANDROID_UPLOAD_KEYSTORE_BASE64
gh secret set ANDROID_UPLOAD_KEYSTORE_PASSWORD
gh secret set ANDROID_UPLOAD_KEY_PASSWORD
"upload" | gh secret set ANDROID_UPLOAD_KEY_ALIAS
Get-Content -Raw GamesLane.Mobile\android\app\google-services.json | gh secret set ANDROID_GOOGLE_SERVICES_JSON
```

`keytool` uses the keystore's password for the key too unless you chose another, so both password secrets are usually the same.

### 5. First upload and the certificates' fingerprints

1. GitHub → **Actions → Android app → Run workflow** (or push a change to `GamesLane.Mobile/`). Download the run's artifact, `app-release.aab`.
2. Play Console → **Test and release → Testing → Internal testing → Create new release**. Accept **Play App Signing** (Google keeps the app signing key), upload the `.aab`, and add yourself as a tester.
3. **Test and release → App integrity → App signing**: copy the **SHA-256 certificate fingerprint** of the **app signing key** and of the **upload key**.
4. On Render, set the environment variable `Android__CertificateFingerprints` to both, separated by a comma. To try a debug build from Android Studio too, add the debug key's (see the developer guide's [Sign-in setup](developer-guide.md#sign-in-setup)). Then https://gameslane.onrender.com/.well-known/assetlinks.json lists them.
5. In Firebase → **Project settings → Your apps → the Android app → Add fingerprint**: the same SHA-256 fingerprints.
6. Install the app from the internal testing link and check it (the developer guide's [Next steps](developer-guide.md#next-steps)).

## Before submitting

- [ ] Setup done, and `assetlinks.json` lists the app signing key.
- [ ] The checks on a phone in the developer guide's [Next steps](developer-guide.md#next-steps): sign-in, join links, notifications, the back button.
- [ ] The closed test (12 testers, 14 days) done, if Play Console asks for it.
- [ ] Graphics and screenshots.
- [ ] **App content** and **Data safety** filled in, then the store listing; then a production release.

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
| Device or other IDs | The Firebase registration token for turn notifications, if allowed | Optional | App functionality |

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
