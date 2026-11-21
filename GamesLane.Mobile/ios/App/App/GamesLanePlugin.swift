import AudioToolbox
import AuthenticationServices
import Capacitor
import UIKit
import UserNotifications

/// The app's own features for the site, which calls them as `window.Capacitor.Plugins.GamesLane` (see common.js):
/// signing in with a provider in Apple's sign-in window, Sign in with Apple, vibrating when the user's turn comes, turn
/// notifications, and keeping the screen on during a game. Join links (universal links, https://<server>/join/...) and tapped notifications open their page
/// in the app's web view.
@objc(GamesLanePlugin)
public class GamesLanePlugin: CAPPlugin, CAPBridgedPlugin, ASWebAuthenticationPresentationContextProviding,
    ASAuthorizationControllerDelegate, ASAuthorizationControllerPresentationContextProviding, NotificationHandlerProtocol {
    public let identifier = "GamesLanePlugin"
    public let jsName = "GamesLane"
    public let pluginMethods: [CAPPluginMethod] = [
        CAPPluginMethod(name: "signIn", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "signInWithApple", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "vibrate", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "registerForNotifications", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "keepAwake", returnType: CAPPluginReturnPromise)
    ]

    private var session: ASWebAuthenticationSession?
    private var appleCall: CAPPluginCall?
    // The registerForNotifications calls waiting for the device token (see AppDelegate), used on the main thread only.
    private var tokenCalls: [CAPPluginCall] = []

    /// The page of the notification the app was launched with (set by SceneDelegate), opened once the plugin loads.
    static var launchPath: String?
    // The launch notification's page once opened, so the same tap reported again to didReceive is not opened twice.
    private var openedLaunchPath: String?

    override public func load() {
        NotificationCenter.default.addObserver(self, selector: #selector(openUniversalLink(_:)),
                                               name: .capacitorOpenUniversalLink, object: nil)
        NotificationCenter.default.addObserver(self, selector: #selector(didRegisterForNotifications(_:)),
                                               name: .capacitorDidRegisterForRemoteNotifications, object: nil)
        NotificationCenter.default.addObserver(self, selector: #selector(didFailToRegisterForNotifications(_:)),
                                               name: .capacitorDidFailToRegisterForRemoteNotifications, object: nil)
        bridge?.notificationRouter.pushNotificationHandler = self
        // Opened after the web view starts loading the first page, so the game's page replaces it.
        if let path = Self.launchPath {
            Self.launchPath = nil
            openedLaunchPath = path
            open(notificationPath: path)
        }
    }

    /// The page a turn notification opens (`path` in the notification, e.g. /backgammon.html?session=ABC123).
    static func notificationPath(_ response: UNNotificationResponse) -> String? {
        response.notification.request.content.userInfo["path"] as? String
    }

    /// Opens a notification's page, if it is a path on the server the app shows.
    private func open(notificationPath path: String) {
        guard let link = URLComponents(string: path), link.host == nil, link.path.hasPrefix("/") else {
            return
        }
        open(path: link.path, query: link.query)
    }

    /// Opens a join link the app was opened with in the web view, on the server the app shows.
    @objc private func openUniversalLink(_ notification: Notification) {
        guard let link = (notification.object as? [String: Any])?["url"] as? URL else {
            return
        }
        open(path: link.path, query: link.query)
    }

    /// Opens `path` (with `query`) in the web view, on the server the app shows.
    private func open(path: String, query: String?) {
        guard let server = bridge?.config.serverURL,
              var components = URLComponents(url: server, resolvingAgainstBaseURL: false) else {
            return
        }
        components.path = path
        components.query = query
        guard let url = components.url else {
            return
        }
        DispatchQueue.main.async {
            self.bridge?.webView?.load(URLRequest(url: url))
        }
    }

    /// Registers the device for notifications and resolves with `{ token, sandbox }`: Apple's device token for the app
    /// (hexadecimal), and whether it is for Apple's development servers (see `usesSandbox`). With `ask`, the user is
    /// asked to allow notifications if they have not answered yet. Rejects with the code DENIED when notifications are
    /// not allowed.
    @objc func registerForNotifications(_ call: CAPPluginCall) {
        let ask = call.getBool("ask") ?? false
        let center = UNUserNotificationCenter.current()
        center.getNotificationSettings { settings in
            switch settings.authorizationStatus {
            case .authorized, .provisional, .ephemeral:
                self.requestDeviceToken(call)
            case .notDetermined where ask:
                center.requestAuthorization(options: [.alert, .sound]) { granted, _ in
                    if granted {
                        self.requestDeviceToken(call)
                    } else {
                        call.reject("Notifications are not allowed.", "DENIED")
                    }
                }
            default:
                call.reject("Notifications are not allowed.", "DENIED")
            }
        }
    }

    private func requestDeviceToken(_ call: CAPPluginCall) {
        DispatchQueue.main.async {
            self.tokenCalls.append(call)
            UIApplication.shared.registerForRemoteNotifications()
        }
    }

    @objc private func didRegisterForNotifications(_ notification: Notification) {
        guard let deviceToken = notification.object as? Data else {
            return
        }
        let token = deviceToken.map { String(format: "%02x", $0) }.joined()
        DispatchQueue.main.async {
            self.tokenCalls.forEach { $0.resolve(["token": token, "sandbox": Self.usesSandbox]) }
            self.tokenCalls.removeAll()
        }
    }

    /// Whether the device token is for Apple's development (sandbox) servers. iOS chooses them by the signing profile,
    /// not the build configuration: an app signed for development embeds a profile whose `aps-environment` is
    /// `development`. TestFlight and App Store builds embed none, and use the production servers. The Simulator uses
    /// the development servers.
    private static let usesSandbox: Bool = {
        #if targetEnvironment(simulator)
        return true
        #else
        guard let url = Bundle.main.url(forResource: "embedded", withExtension: "mobileprovision"),
              let data = try? Data(contentsOf: url),
              let text = String(data: data, encoding: .isoLatin1),
              let start = text.range(of: "<?xml"),
              let end = text.range(of: "</plist>", range: start.upperBound..<text.endIndex) else {
            return false
        }
        // The profile is a signed message around a property list; Latin-1 keeps each byte as one character.
        let plist = Data(text[start.lowerBound..<end.upperBound].unicodeScalars.map { UInt8($0.value) })
        guard let profile = try? PropertyListSerialization.propertyList(from: plist, format: nil) as? [String: Any],
              let entitlements = profile["Entitlements"] as? [String: Any] else {
            return false
        }
        return entitlements["aps-environment"] as? String == "development"
        #endif
    }()

    @objc private func didFailToRegisterForNotifications(_ notification: Notification) {
        let error = notification.object as? Error
        DispatchQueue.main.async {
            self.tokenCalls.forEach { $0.reject("Could not register for notifications.", nil, error) }
            self.tokenCalls.removeAll()
        }
    }

    /// A notification that comes while the app is in front shows as usual: the server sends one only when the game's
    /// page is not in view.
    public func willPresent(notification: UNNotification) -> UNNotificationPresentationOptions {
        [.banner, .list, .sound]
    }

    /// A tapped turn notification opens its game, unless it is the one the app was launched with, already opened.
    public func didReceive(response: UNNotificationResponse) {
        guard let path = Self.notificationPath(response) else {
            return
        }
        if path == openedLaunchPath {
            openedLaunchPath = nil
            return
        }
        open(notificationPath: path)
    }

    /// Opens `url` (the server's /auth/signin/{provider}?app=true...) in Apple's sign-in window, and resolves with
    /// `{ url }`, the address the server ended the sign-in with: `callback`, an https address on the site, which only
    /// this app can receive (its associated domains, and the site's apple-app-site-association). Rejects with the code
    /// CANCELLED when the user closes the window.
    @objc func signIn(_ call: CAPPluginCall) {
        guard let address = call.getString("url"), let url = URL(string: address), url.scheme == "https",
              let callback = call.getString("callback").flatMap(URL.init(string:)), callback.scheme == "https",
              let host = callback.host else {
            call.reject("An https sign-in address and callback are required.")
            return
        }

        DispatchQueue.main.async {
            let completion: ASWebAuthenticationSession.CompletionHandler = { [weak self = self] callbackURL, error in
                self?.session = nil
                if let callbackURL {
                    call.resolve(["url": callbackURL.absoluteString])
                } else if let error = error as? ASWebAuthenticationSessionError, error.code == .canceledLogin {
                    call.reject("Sign-in was cancelled.", "CANCELLED")
                } else {
                    call.reject("Sign-in did not complete.", nil, error)
                }
            }
            let session = ASWebAuthenticationSession(url: url, callback: .https(host: host, path: callback.path),
                                                     completionHandler: completion)
            session.presentationContextProvider = self
            self.session = session
            if !session.start() {
                self.session = nil
                call.reject("Sign-in could not start.")
            }
        }
    }

    /// Signs in with Apple in Apple's own sheet, asking for the user's name only. `nonce` is put in the identity token
    /// (the page sends it hashed, and the server checks it). Resolves with `{ identityToken, name, authorizationCode }`;
    /// Apple gives the name only on the first sign-in, and the server redeems the code for a refresh token to revoke
    /// when the account is deleted. Rejects with the code CANCELLED when the user closes the sheet.
    @objc func signInWithApple(_ call: CAPPluginCall) {
        guard let nonce = call.getString("nonce"), !nonce.isEmpty else {
            call.reject("A nonce is required.")
            return
        }

        DispatchQueue.main.async {
            let request = ASAuthorizationAppleIDProvider().createRequest()
            request.requestedScopes = [.fullName]
            request.nonce = nonce
            let controller = ASAuthorizationController(authorizationRequests: [request])
            controller.delegate = self
            controller.presentationContextProvider = self
            self.appleCall = call
            controller.performRequests()
        }
    }

    public func authorizationController(controller: ASAuthorizationController, didCompleteWithAuthorization authorization: ASAuthorization) {
        guard let call = appleCall else { return }
        appleCall = nil
        guard let credential = authorization.credential as? ASAuthorizationAppleIDCredential,
              let tokenData = credential.identityToken, let token = String(data: tokenData, encoding: .utf8) else {
            call.reject("Sign-in did not complete.")
            return
        }
        let name = credential.fullName.map { PersonNameComponentsFormatter().string(from: $0) } ?? ""
        let code = credential.authorizationCode.flatMap { String(data: $0, encoding: .utf8) } ?? ""
        call.resolve(["identityToken": token, "name": name, "authorizationCode": code])
    }

    public func authorizationController(controller: ASAuthorizationController, didCompleteWithError error: Error) {
        guard let call = appleCall else { return }
        appleCall = nil
        if let error = error as? ASAuthorizationError, error.code == .canceled {
            call.reject("Sign-in was cancelled.", "CANCELLED")
        } else {
            call.reject("Sign-in did not complete.", nil, error)
        }
    }

    public func presentationAnchor(for controller: ASAuthorizationController) -> ASPresentationAnchor {
        bridge?.webView?.window ?? ASPresentationAnchor()
    }

    /// Vibrates the phone once.
    @objc func vibrate(_ call: CAPPluginCall) {
        AudioServicesPlaySystemSound(kSystemSoundID_Vibrate)
        call.resolve()
    }

    /// Keeps the screen on (`on: true`) or lets it dim and lock as usual (`on: false`). iOS applies it only while the
    /// app is in front.
    @objc func keepAwake(_ call: CAPPluginCall) {
        let on = call.getBool("on") ?? false
        DispatchQueue.main.async {
            UIApplication.shared.isIdleTimerDisabled = on
            call.resolve()
        }
    }

    public func presentationAnchor(for session: ASWebAuthenticationSession) -> ASPresentationAnchor {
        bridge?.webView?.window ?? ASPresentationAnchor()
    }
}

/// The app's screen: Capacitor's web view, with the GamesLane plugin.
class GamesLaneViewController: CAPBridgeViewController {
    override open func capacitorDidLoad() {
        bridge?.registerPluginInstance(GamesLanePlugin())
    }
}
