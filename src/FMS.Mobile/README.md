# FMS Mobile (Android Wrapper)

A lightweight .NET MAUI Android app that wraps the [FMS web frontend](https://muhammadabdullah3135.github.io/FarmManagementSystem/) in a native WebView. This gives Android users an installable app experience without maintaining a separate native UI.

## What It Does

- Loads the FMS web app in a full-screen WebView (URL: `https://muhammadabdullah3135.github.io/FarmManagementSystem/`)
- The web app communicates with the backend API at `https://fms-api-3ba95327d590.herokuapp.com/api`
- **Back button handling:** traverses web history first; only exits the app when there's no history left
- **Offline detection:** shows a "No internet connection" overlay with a Retry button when network drops (via both `Connectivity` listener and native WebView error handling)
- **Firebase Crashlytics:** unhandled exceptions are reported to the Firebase console (project: `fms-mobile-798f5`)
- **Local crash logging:** appends crash details to `crashlog.txt` and `latest-crash.txt` in the app's data directory

## Known Limitations

- **Requires internet connection** — no offline data caching or native data storage
- **Android only** — iOS, MacCatalyst, and Windows platform targets have been removed
- **arm64 only** — built for `android-arm64` ABI; does not produce arm32 or x86 builds
- **Debug build** — current APK includes test-only buttons (Test Crash, Share Report) compiled out of Release builds
- **No push notifications** — not yet implemented
- **Tightly coupled to GitHub Pages URL** — the WebView URL is hardcoded in `MainPage.cs`; changing the deployment URL requires a code change and rebuild
- **google-services.json is not in the repo** — Firebase config is gitignored; you must obtain your own from the Firebase console

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
| `MainPage.cs` | WebView + offline overlay + debug chips |
| `FmsWebView.cs` | Partial WebView with `ReceivedError` event + static `Current` |
| `Services/CrashReporter.cs` | Local crash logging (AppDomain + TaskScheduler hooks) |
| `Platforms/Android/MainActivity.cs` | Back-press callback (AndroidX OnBackPressedCallback) |
| `Platforms/Android/FmsWebViewHandler.cs` | WebView config (JS, DOM storage, cookies) |
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
