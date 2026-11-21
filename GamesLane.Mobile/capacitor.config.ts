import type { CapacitorConfig } from "@capacitor/cli";

// The server the app shows, chosen when the app is built (npm run sync) by the GAMESLANE_SERVER environment variable:
// unset for production, a pull request's number (e.g. 42) for its preview server, or a full address such as
// http://localhost:5171 for a server running locally.
function serverUrl(value: string | undefined): string {
    const server = value?.trim() ?? "";
    if (server === "") {
        return "https://gameslane.onrender.com";
    }
    if (/^\d+$/.test(server)) {
        return `https://gameslane-pr-${server}.onrender.com`;
    }
    return server.replace(/\/+$/, "");
}

const url = serverUrl(process.env.GAMESLANE_SERVER);

const config: CapacitorConfig = {
    appId: "com.gameslane.app",
    appName: "GamesLane",
    webDir: "www",
    backgroundColor: "#f5f1ea",
    server: {
        url,
        // Shown when the server cannot be reached.
        errorPath: "index.html",
        // Android allows plain http only for a server running locally.
        cleartext: url.startsWith("http:"),
        // Other sites open in the browser. Sign-in with a provider opens in Apple's sign-in window
        // (ios/App/App/GamesLanePlugin.swift) or a Chrome Auth Tab (android/app/src/main/java/com/gameslane/app/GamesLanePlugin.java).
    },
    ios: {
        // The site does not draw under the status bar or the home indicator.
        contentInset: "always",
    },
};

export default config;
