| Pack | `./b.cmd app pack linux` - the Release folder as `artifacts/publish/App.Maui/<app id>-linux-x64.tar.gz`; CI uploads it on every `dev` and release push as the `<app id>.<version>.linux-x64.tar.gz` artifact of the "Build test and deploy" workflow |
# Linux GTK backend

The Linux app runs on the experimental GTK4 backend from
[dotnet/maui-labs](https://github.com/dotnet/maui-labs): a native GTK process with a WebKitGTK
view inside, built from the same `App.Maui` project as the other native apps. It is the Linux
twin of the [macOS AppKit backend](./macos-appkit.md), and it borrows most of that page's
approach: the web UI does the work, and the native side is as thin as the platform allows.

[[toc]]

## Building and running

There is no Linux workload and no Linux TFM, so the Linux app is the plain `net11.0` build of
`App.Maui`, and that target is opt-in.

| What | How |
|---|---|
| Enable the target | `-p:EnableLinuxGtk=true` |
| Build | `./b.cmd app build linux` - works on macOS and Windows too, as a cross-build |
| Run | `./b.cmd app run linux`, on Linux only |
| Output | `artifacts/publish/App.Maui/debug_net11.0_linux-x64/`, self-contained; start `ActualChat` |
| Logs | `~/.local/share/ActualChat/Logs/ActualChat.log`; a Debug build also writes them, and the page's console, to stdout |

The build is always a `dotnet publish`: the WebView serves the page from `wwwroot` next to the
executable, and only the publish output has it.

The target machine needs GTK 4.12+ and WebKitGTK 6 with the GStreamer plugins it plays media
through. On Ubuntu:

```bash
sudo apt install libgtk-4-1 libwebkitgtk-6.0-4 gstreamer1.0-plugins-good gstreamer1.0-plugins-bad gstreamer1.0-libav
```

The App.Maui and Maui projects compile Linux code by folder and file suffix, next to the ones
listed in [macOS AppKit backend](./macos-appkit.md#building-and-running):

| Folder / suffix | Compiled for |
|---|---|
| `Platforms/Linux`, `*.Linux.cs` | the GTK app only |

`LINUX` is the conditional compilation symbol, and `AppKind.Linux` the app kind.

The labs packages are pinned in
[Directory.Packages.props](https://github.com/Actual-Chat/actual-chat/blob/main/Directory.Packages.props):
`Microsoft.Maui.Platforms.Linux.Gtk4`, `.Essentials` and `.BlazorWebView`, at the same version
as the macOS ones.

### Testing on another machine

A cross-build runs from a copy of its output folder, so a Linux box on the network is enough:

```bash
./b.cmd app build linux
rsync -az --delete artifacts/publish/App.Maui/debug_net11.0_linux-x64/ user@linux-box:voxt-linux/
```

The CI artifact is the same folder as a tarball: `tar xzf chat.actual.dev.app-linux-x64.tar.gz`
and start `chat.actual.dev.app/ActualChat`.

Started over SSH, the app needs the desktop session's environment to find the display:

```bash
export XDG_RUNTIME_DIR=/run/user/$(id -u) WAYLAND_DISPLAY=wayland-0
export DBUS_SESSION_BUS_ADDRESS=unix:path=$XDG_RUNTIME_DIR/bus
~/voxt-linux/ActualChat
```

- **The page can be driven remotely.** With `WEBKIT_INSPECTOR_HTTP_SERVER=127.0.0.1:9230` in the
  environment a dev build serves the WebKit inspector there: its WebSocket speaks the inspector
  protocol (`Runtime.evaluate`, `Page.snapshotRect`), which is enough to script the UI and take
  page screenshots through an SSH tunnel. GNOME on Wayland offers no other way to do either.
- **A local server needs the mkcert root CA.** WebKitGTK and .NET both read the system CA
  bundle. Without root access, `bwrap --dev-bind / / --ro-bind <bundle with the CA>
  /etc/ssl/certs/ca-certificates.crt ./ActualChat` overlays it for the app alone. The server
  itself is selected by the `app_server_instance_override` key in
  `~/.config/ActualChat/preferences.json`.
- **A locked screen stalls the UI.** The compositor suspends the window, the app marks itself
  hidden and holds back renders, and WebKit leaves `getUserMedia` pending.

## Workarounds to remove when the backend moves on

Every place where Voxt works around the labs preview is marked `TODO(maui-labs)`, as on macOS:

```bash
git grep -n "TODO(maui-labs)" -- '*Linux*'
```

### Blazor is hosted by our own WebViewManager

The labs `GtkBlazorWebView` serves the page from `app://localhost/`, a constant. The server
accepts WebView connections from the unroutable `0.0.0.1` only (`Constants.Origins.AppWebView`),
which is the origin every other platform uses, so a page on `app://localhost` gets its RPC
WebSocket handshake rejected. The same class looks a request up with its query still attached
and answers 404 to `bundle.js?v=...`, and it sets a response's content type without its
headers, which `WebAssembly.compileStreaming` needs.

| Workaround | Goes away when |
|---|---|
| [LinuxWebViewManager](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/App.Maui/Platforms/Linux/LinuxWebViewManager.cs) is the Blazor Hybrid transport and the `app://0.0.0.1/` content source: the script message handler, the `window.external` shim with the `Blazor.start()` call, and the scheme handler | the labs origin is configurable; the query fix is already on the labs main branch |
| [LinuxCustomBlazorWebViewHandler](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/App.Maui/Platforms/Linux/LinuxCustomBlazorWebViewHandler.cs) derives from the labs handler for its GTK layout code, but builds its own WebKit view instead of calling the base `CreatePlatformView` | same |
| The `#if !LINUX` in [MauiWebView.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/App.Maui/WebView/MauiWebView.cs) skips the `BlazorWebViewInitializing`, `BlazorWebViewInitialized` and `UrlLoading` subscriptions, which nothing raises; [MauiWebView.Linux.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/App.Maui/WebView/MauiWebView.Linux.cs) configures the view and answers WebKit's `decide-policy`, `create` and `permission-request` signals instead | the labs handler raises the three events |

### Essentials is patched in, not implemented

The neutral `Microsoft.Maui.Essentials.dll` ships on the plain TFM, and every platform member
in it throws. Labs' `AddLinuxGtk4Essentials()` registers its implementations in DI, but swaps
only five of them into the static facades, and only once a `MauiAppBuilder` exists.

| Workaround | Goes away when |
|---|---|
| [LinuxEssentialsDefaults](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/App.Maui/Platforms/Linux/LinuxEssentialsDefaults.cs), called from [Program.Main](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/App.Maui/Platforms/Linux/Program.cs), sets the facades Voxt reads (`FileSystem`, `Preferences`, `SecureStorage`, `DeviceInfo`, `DeviceDisplay`, `Connectivity`, `Browser`, ...) through their private setters | labs sets them all, or Essentials ships a Linux implementation |
| [LinuxMainThread](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Maui/Platforms/Linux/LinuxMainThread.cs), aliased as `MainThread` in [MauiMainThread.cs](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/Maui/MauiMainThread.cs) | Essentials' `MainThread` works on the GTK backend |
| The web permission handlers and the web location tracker in [MauiAppModule](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/App.Maui/Module/MauiAppModule.cs): Essentials `Permissions` is unimplemented, and a Linux desktop has nothing to ask anyway | labs implements Permissions |

### The UI thread has no synchronization context

The pinned labs package runs the GTK application without one, so an `await` on the UI thread
resumes on the thread pool. The first casualty was the reload after a session replacement: it
recreated the WebView off the UI thread and took the process down.

| Workaround | Goes away when |
|---|---|
| `Program.Main` installs GirCore's `MainLoopSynchronizationContext` before the application starts | the next labs package; its main branch runs the application with one |

### Window and desktop integration

| Workaround | Goes away when |
|---|---|
| [WindowConfigurator](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/App.Maui/Platforms/Linux/WindowConfigurator.cs) resets the 800x600 minimum the backend pins its window at, which rules out the narrow layout | the backend stops setting it; its main branch has |
| [LinuxDesktopEntry](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/App.Maui/Platforms/Linux/LinuxDesktopEntry.cs) writes the launcher entry in place of the backend's, which has neither `%u` nor a `MimeType` | the backend's entry can carry both, or the app ships as a package with its own entry |
| `Program.Main` passes GApplication no arguments | the backend lets the app handle them |

::: warning Known quirks that are not worked around
- WebKit prints `Cannot register URI scheme app more than once` when the WebView is recreated:
  a web context takes a scheme once, and the handler stays registered across WebViews.
- A second activation of the same GApplication would make the backend build a second
  `MauiApp`. It never gets one: `LinuxActivation` ends a second launch before the
  backend's application runs.
:::

## What is deliberately not ported yet

- **Audio** runs on the web pipeline (`BrowserInfo.useWebAudio`), as in the AppKit app:
  `WebRecorderEngine`, the JS playback engine and `WebTuneUI`. Unlike the AppKit app, the GTK
  one registers no `IAudioCodec` and no `VoiceActivityDetector` - only `MauiRecorderEngine`
  needs them.
- **Notifications** are pull-based through `NotificationReconciler` and delivered only while
  the app runs: there is no push channel for a Linux desktop app.
- **Tray icon and autostart.** Closing the window quits the app.
- **Attachments, downloads and the clipboard** go the web way: `<input type=file>` and WebKit's
  own download and clipboard handling. No `IAttachmentFilePicker`, `IFileSaver` or native
  `ClipboardUI` is registered.
- **Passkeys** are unavailable, as in the Windows app: the page's origin can't target the RP id.
- **The `GCLB` cookie** that pins WebView WebSockets to the backend the native layer talks to
  isn't set: GirCore 0.7 has no binding for `webkit_cookie_manager_add_cookie`.
- **Packaging.** CI ships the publish folder as a tarball (`b app pack linux`). The labs
  package has `.deb`, AppImage and Flatpak targets; none is wired up.

## Behaviour worth knowing

- **One instance per user.** [LinuxActivation](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/App.Maui/Platforms/Linux/LinuxActivation.cs)
  registers a GApplication of its own in `Main`, which owns a name on the session bus. A second
  launch - from the launcher, or from the desktop handing over a `voxt://` link - finds the
  name taken, activates the owner's `open` action with its link and exits; the running instance
  presents its window. A link that arrives while the app starts waits on the bus for the main
  loop. Without a session bus every launch is an instance of its own.
- **A recreated WebView terminates the old page's web process.** Each WebKit view runs its page
  in a process of its own, which keeps running scripts for as long as anything references the
  view; `LinuxCustomBlazorWebViewHandler.DisconnectHandler` ends it explicitly.
- **Sign-in** uses the Windows and macOS flow: the default browser plus a `voxt-dev://`
  callback (`voxt://` in a prod-flavour build). The desktop entry the app writes to
  `~/.local/share/applications/` on start claims the scheme, so the entry always points at
  the build that ran last. The app also runs `update-desktop-database` there: a sandboxed
  browser (the Firefox snap) hands the link to the desktop portal, which offers only the apps
  that database lists for the scheme and otherwise shows "No Apps Available".
- **JS RPC authenticates with session tokens**, as in the Apple WebViews: the page's origin
  makes every cookie of the host a third-party one. See `SessionTokens.AutoRefresh`.
- **The hidden state follows `GtkWindow:suspended`**, and the app is never backgrounded (#5275):
  the OS doesn't suspend it, so the RPC client, the audio context and the on-disk store keep
  working with the window out of sight. A minimized, fully covered or other-workspace window
  stops rendering and auto-reading chats, a window in plain sight beside another app keeps both.
  A notification is posted unless the window is the active one.
- **Clicking a notification** activates the `app.open-notification` GAction, which the desktop
  reaches over D-Bus: it navigates to the chat and presents the window.
- **Media permissions have no prompt.** WebKitGTK hands every permission request to the
  embedder; the app grants microphone, camera, device list, location and clipboard requests
  while the WebView is on its own page, and denies the rest.
- **The device id is a salted hash of `/etc/machine-id`**
  ([LinuxDeviceIdProvider](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/App.Maui/Platforms/Linux/LinuxDeviceIdProvider.cs)),
  with a per-install id as the fallback.
- **Data locations**: `~/.config/ActualChat` (preferences), `~/.local/share/ActualChat`
  (local settings, logs, WebKit's site data), `~/.cache/ActualChat`; secrets go to the desktop
  keyring through libsecret, or to an encrypted file when no keyring answers.
- **Link navigation** is fail-closed, as in the AppKit app: an `http(s)` URL that is not the
  app's own origin or an allowed host opens in the default browser.
- **Ubuntu's WebKitGTK has no `RTCPeerConnection`.** Voxt doesn't need one: calls and video
  run on WebCodecs over the RPC connection, and the WebRTC loopback is a Chromium-only echo
  cancellation trick.
