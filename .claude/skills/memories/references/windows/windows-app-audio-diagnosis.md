The Windows (MSIX) Voxt app has **no usable diagnostics of its own**, so recording
bugs must be diagnosed from outside the app:

- **Its log file is dead.** `MauiDiagnostics.AddPlatformLoggerSinks` writes to
  `%LOCALAPPDATA%\Packages\ActualChatInc.ActualChat_kpmvmkx3s0ak6\LocalState\Logs\ActualChat.log`
  with `fileSizeLimitBytes: 10_000_000` and **no `rollOnFileSizeLimit`** — Serilog
  stopped writing at the cap. As of 2026-08-25 the file is frozen at May 2024.
  "Open log file" in Developer Tools opens that stale file.
- **Sentry ingestion stopped 2026-08-14** (quota, blown by telemetry logged at
  Warning: 118k `PushStream is delayed`, 35k `send-cadence`, 25k `process-cadence`).
  See [[sentry-api-access]].
- In-app Log Viewer (Settings → Developer Tools) only captures while its toggle is on —
  `LogUI.Log` drops everything when disabled, so a dump taken later has an empty buffer.

Ground truth that *does* work:

- **Did the mic actually open?** `HKCU:\...\CapabilityAccessManager\ConsentStore\microphone\ActualChatInc.ActualChat_kpmvmkx3s0ak6`
  → `LastUsedTimeStart` / `LastUsedTimeStop` (`[DateTime]::FromFileTime(...)`).
  A missing `LastUsedTimeStop` means the mic is open right now. This distinguishes
  "never reached the device" from "opened but pipeline broke".
- The app is **CoreCLR, not NativeAOT**, so `dotnet-dump` / `dotnet-stack` (already
  installed in `~/.dotnet/tools`) attach over the diagnostics pipe. But
  `dotnet-stack report` only shows threads with managed frames — it **cannot see an
  `await` that never returns**; use `dotnet-dump collect` + `dumpasync` for that.
- WebView2 has **no** remote-debugging port. On 2026-09-11 neither documented way to add one
  worked for the unpackaged Debug build launched directly: `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`
  in the child's environment and the HKCU policy `Software\Policies\Microsoft\Edge\WebView2\AdditionalBrowserArguments`
  (value name `ActualChat.exe`). The `msedgewebview2` browser process (parent = app pid) had no
  switch either way. Untested remaining suspect: a leftover browser process for the same
  `ActualChat.exe.WebView2` user-data folder. The deterministic route would be an `#if DEBUG`
  `EnvironmentOptions` hook in `MauiWebView.Windows.cs` `OnInitializing`. The policy value
  name also matches the store app's exe, so remove it right after use.
  Recording is native anyway (`MauiRecorderEngine`/`WindowsAudioCapture`), not WebView.

- **WebView page reloads are timestamped** in `LocalState\EBWebView\Breadcrumbs`: each
  block starts at `Startup` (= process start time) and lines carry offsets, so
  `0:53:23 Reload` + `#reload` nav = a same-tab document reload at start+53:23.
  `MauiReloadUI` recreates the WebView instead (a new `Startup` block).
- **Map the app to its prod session** without logs: grep the dump (UTF-16) for the
  `rpc.server.msgpack6c-lz4f://<clientId>` ids seen in prod logs; the matching one's
  `PushStream ... Session = Xxxx:yyyy` lines are this app's recordings.
- **dotnet-dump 10.x can't open a .NET 11 heap** ("Unable to create a ClrHeap"), so
  `dumpasync`/`dumpheap` are out. Scan the dump for UTF-16 strings instead: exception
  JSON with full stack traces survives there. `rg` with multi-KB context windows
  silently returns nothing; use an `Add-Type` C# scanner (`File.ReadAllBytes` + span
  `IndexOf`) instead.

**Never run `procdump` under an external `timeout`/kill** — it attaches as a debugger,
and killing it takes the app down with it. (Did exactly this on 2026-08-25 and
killed Alex's running app.) Use `dotnet-dump collect`, which needs no debugger attach.
