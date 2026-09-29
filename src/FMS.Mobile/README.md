# FMS Mobile (Android Wrapper)

A lightweight .NET MAUI Android app that wraps the [FMS web frontend](https://muhammadabdullah3135.github.io/FarmManagementSystem/) in a native WebView. This gives Android users an installable app experience without maintaining a separate native UI.

## What It Does

- Loads the FMS web app in a full-screen WebView (URL: `https://muhammadabdullah3135.github.io/FarmManagementSystem/`)
- The web app communicates with the backend API at `https://fms-api-3ba95327d590.herokuapp.com/api`
- **Back button handling:** traverses web history first; only exits the app when there's no history left
- **Offline detection:** Microsoft removed deliberately. The web app registers a service worker that precaches its own shell, so the app keeps working when the network drops, and it reports its own offline state (with what the device has actually stored) in a banner. A native listener that hid the WebView on connectivity loss contradicted both. What remains is a "Couldn't load the app" overlay with a Retry button, shown only when the **app shell itself** fails to load — a first launch with no network, or a DNS failure — where there is nothing to show and Retry is the only useful action. A page that fails for a subresource never triggers it.
- **QR label scanning:** the scan screen opens the device camera through the WebView. `CAMERA`
  is declared in `AndroidManifest.xml` (with `uses-feature` camera `required="false"`, so a
  device without one can still install the app) and is granted at the moment the scan screen asks
  for it, by `FmsWebChromeClient` — see below for why MAUI's own chrome client is not enough.
- **Firebase Crashlytics:** unhandled exceptions are reported to the Firebase console (project: `fms-mobile-798f5`)
- **Local crash logging:** appends crash details to `crashlog.txt` and `latest-crash.txt` in the app's data directory

## Known Limitations

- **Requires a connection for data (not for the app shell):** the shell loads offline once it has been loaded on the device at least once, but reads and writes still go to the API. Cached reads and the offline write queue are designed but not built — see [../../docs/OFFLINE.md](../../docs/OFFLINE.md).
- **Android only** — the iOS, MacCatalyst and Windows targets were removed in `7188b41`, and the removal stands: an iOS target needs a macOS/Xcode machine this project does not have. See [Why Android only](#why-android-only-no-ios-target) for the reasons and for what a port would take.
- **arm64-only Debug build** — the Debug configuration forces the `android-arm64` ABI, so the APK produced by the build command below will not run on arm32 or x86 devices
- **Debug build** — current APK includes test-only buttons (Test Crash, Share Report) compiled out of Release builds
- **No push notifications inside the wrapped app** — the Android WebView implements neither the Push API nor a notification permission, so the app's push card reports that this app cannot receive push notifications instead of offering a switch that would do nothing. Web push works in any real browser (Chrome, Edge, Firefox on Android, or a desktop); reaching the APK means Firebase Cloud Messaging and its credentials, which is a second transport rather than a setting. See [../../docs/VERIFICATION.md](../../docs/VERIFICATION.md) §10.
- **Tightly coupled to GitHub Pages URL** — the WebView URL is hardcoded in `MainPage.cs`; changing the deployment URL requires a code change and rebuild
- **google-services.json is not in the repo** — Firebase config is gitignored; you must obtain your own from the Firebase console

## Why Android only (no iOS target)

This app wraps the web frontend in a WebView, and the same shape would work on iOS. There is no iOS target — by decision, not by oversight. What the tree records:

- `7188b41` ("Replace old mobile wrapper with minimal WebView shell + Firebase Crashlytics") **deleted** `Platforms/iOS/`, `Platforms/MacCatalyst/` and `Platforms/Windows/`.
- `FMS.Mobile.csproj` carries a single `<TargetFramework>net10.0-android</TargetFramework>`, and every conditioned property group in it tests for `android`.
- No iOS build has ever been produced from this tree, and nothing in it has been run on iOS.

Two facts about this project decide the question:

- **Nobody here can build or verify it.** A `net10.0-ios` target needs macOS and Xcode to compile, and a simulator or a signed device build to run. No machine on this project has either — the Android build below works only because the MAUI **Android** workload is installed. A target that cannot be compiled here cannot be kept honest, and this project's rule is that a claim with no run behind it is a handoff, not a result (`../../docs/VERIFICATION.md`).
- **Shipping needs an Apple Developer account.** Installing on a device beyond a short-lived free provisioning window, and App Store distribution, both require one.

What an iOS port would have to contain if it is ever revived — read off the Android implementation rather than guessed:

| Android (present) | iOS (would be needed) |
|---|---|
| `FmsWebViewHandler.ConnectHandler` — JavaScript, **DOM storage** (this is what makes IndexedDB, and therefore the offline shell, work), cookie acceptance, and `CacheModes.NoCache` so a deploy is picked up | The same set on `WKWebViewConfiguration`: `WkPreferences.JavaScriptEnabled`, a `WkWebsiteDataStore` for DOM storage, `NSHttpCookieStorage` acceptance, and a revalidating `NSURLRequestCachePolicy` |
| `FmsWebViewClient` — main-frame error interception for the "Couldn't load the app" Retry overlay | `WKNavigationDelegate`'s `DidFailProvisionalNavigation` / `DidFailNavigation`, with the same main-frame-only rule |
| `FmsWebChromeClient.OnPermissionRequest` — the camera and nothing else, granted only after the Android runtime permission | `WKUIDelegate`'s `WebView:requestMediaCapturePermissionForOrigin:initiatedByFrame:type:decisionHandler:` **plus** `NSCameraUsageDescription` in `Info.plist`; without both, the scan screen reports "no camera" on a device whose camera is fine |
| `MainActivity`'s `OnBackPressedCallback` walking web history | The edge-swipe gesture, which `WKWebView` provides itself |
| `AndroidManifest.xml` — `INTERNET`, `CAMERA`, and a camera `uses-feature` marked `required="false"` | `Info.plist`'s `NSCameraUsageDescription`; network access needs no declaration |

The runbooks that exercise the shell are Android-only with it: `../../docs/VERIFICATION.md` §6 (offline shell and write queue in a real WebView), §8 (Arabic RTL inside the WebView) and §9 (QR scanning with the camera). An iOS port restarts all three; none of their results transfers. §10 (push notifications) is a **browser** runbook by construction — this shell cannot be its subject, because the WebView has no Push API.

iPhone users are not shut out of the product — the frontend runs in any browser, Safari included — but there is no FMS app in the App Store, and there is no plan for one.
## Building

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (or .NET 9+ with MAUI workload)
- .NET MAUI Android workload: `dotnet workload install maui`
- Android SDK with platform-tools (API level 23+)

### Build the APK

```bash
dotnet build src/FMS.Mobile/FMS.Mobile.csproj -f net10.0-android -c Debug
```

The signed APK is output at:
```
src/FMS.Mobile/bin/Debug/net10.0-android/android-arm64/com.fms.mobile-Signed.apk
```

### Install on Device

1. Uninstall any previous FMS Mobile install
2. Transfer the APK to your device
3. Open the APK and allow installation from unknown sources
4. The app should open and load the FMS website

### Firebase Setup (Optional)

Without `google-services.json`, the app still builds and works — Firebase is gated behind a `FIREBASE` compile define that's only set when the config file exists. To enable Crashlytics:

1. Create a Firebase project at https://console.firebase.google.com/
2. Add an Android app with package name `com.fms.mobile`
3. Download `google-services.json`
4. Place it at `src/FMS.Mobile/Platforms/Android/google-services.json`
5. Rebuild — the `FIREBASE` define activates automatically

## Architecture

| File | Purpose |
|---|---|
| `App.cs` | DI entry point, creates `MainPage` |
| `MauiProgram.cs` | MAUI builder, handler registration, Firebase lifecycle init |
| `MainPage.cs` | WebView + first-load-failure overlay + debug chips |
| `FmsWebView.cs` | Partial WebView with `ReceivedError` event + static `Current` |
| `Services/CrashReporter.cs` | Local crash logging (AppDomain + TaskScheduler hooks) |
| `Platforms/Android/MainActivity.cs` | Back-press callback (AndroidX OnBackPressedCallback) |
| `Platforms/Android/FmsWebViewHandler.cs` | WebView config (JS, DOM storage, cookies) — DOM storage is what makes IndexedDB, and therefore the offline shell, available |
| `Platforms/Android/FmsWebChromeClient.cs` | Grants the WebView the camera for QR scanning, and only the camera — MAUI's own chrome client never answers the request, so without this the scanner reports "no camera" on a device whose camera is fine |
| `Platforms/Android/FmsWebViewClient.cs` | Main-frame error interception |

## Project Structure

```
src/FMS.Mobile/
├── App.cs
├── MainPage.cs
├── FmsWebView.cs
├── MauiProgram.cs
├── FMS.Mobile.csproj
├── Services/
│   └── CrashReporter.cs
├── Platforms/Android/
│   ├── MainActivity.cs
│   ├── MainApplication.cs
│   ├── FmsWebViewHandler.cs
│   ├── FmsWebChromeClient.cs
│   ├── FmsWebViewClient.cs
│   ├── AndroidManifest.xml
│   ├── google-services.json    (gitignored — your own Firebase config)
│   └── Resources/values/
│       ├── strings.xml          (Crashlytics mapping workaround)
│       └── colors.xml
├── Resources/
│   ├── AppIcon/
│   └── Splash/
└── Properties/
    └── launchSettings.json
```
