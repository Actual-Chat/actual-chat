# Sanitizer: Sensitive Data Masking for Logging

## Overview

Audit found ~29 locations across server-side (C#) and client-side (TypeScript) code that log user-generated content: chat message text, transcriptions, translations, notification bodies, SMS content, shared text, and editor history. These logs can leak personal user content in production via server logs, Crashlytics, device logs, and browser console.

## Design

### Core Types (`Core/Compliance/`)

**`Sanitizer`** — thread-static sanitizer with `static bool IsActive`. `Activate()` returns a `Scope` struct (implements `IDisposable`) that restores previous state on dispose. Supports nesting.
- `static string Mask(object?)` — dispatches to `MaskPrivate` for strings/`Sensitive.Private`, calls `ToString()` for `ISensitive`, passes through otherwise
- `static string MaskPrivate(string?)` — masks user content using `<<Ab* [16-31]>>` format

**`ISensitive`** — tagging interface for objects that change their representation based on sanitizer state.

**`Sensitive`** — readonly struct wrapping `object? Source`. `ToString()` calls `Sanitizer.Mask(Source)`.

**`Sensitive.Private`** — nested readonly struct wrapping `string? Source`. `ToString()` calls `Sanitizer.MaskPrivate(Source)`. Use for private user content (messages, transcriptions, etc.).

**`string.ToPrivate()`** — extension method in `StringExt` returning `Sensitive.Private`.

### Masking Format

All masked values are wrapped in `<<>>`:
- `""` → `""`
- `"Hi"` (2 chars) → `"<<* [2-3]>>"`
- `"Hey"` (3 chars) → `"<<* [2-3]>>"`
- `"Test"` (4 chars) → `"<<Te* [4-7]>>"`
- `"Hello world"` (11 chars) → `"<<He* [8-15]>>"`
- `"A longer message here"` (21 chars) → `"<<A * [16-31]>>"`

Length ranges use high-bit binary buckets: `[1]`, `[2-3]`, `[4-7]`, `[8-15]`, `[16-31]`, `[32-63]`, etc.

### Logging Integration (`Core/Logging/`)

**`SanitizingLogger`** — wraps `ILogger`, activates `Sanitizer.Activate()` around each `Log<TState>` call so `Sensitive` arguments self-mask via `ToString()`.

**`SanitizingLoggerFactory`** — wraps `ILoggerFactory`, creates `SanitizingLogger` instances. Takes `(ILoggerFactory innerFactory, bool mustSanitize = true)` — when `mustSanitize` is false, passes loggers through unwrapped.

**`LoggingBuilderExt.AddSanitizingLoggerFactory(Func<IServiceProvider, bool>)`** — registers `SanitizingLoggerFactory` as the `ILoggerFactory` implementation, resolving the predicate at DI resolution time.

### Usage Patterns

**Pattern 1: Wrap string in log call**
```csharp
// Before:
Log.LogError(e, "Failed to send message. Text='{Text}'", request.Text);

// After:
Log.LogError(e, "Failed to send message. Text='{Text}'", request.Text.ToPrivate());
```

**Pattern 2: Object implements ISensitive**
```csharp
public class NotificationData : ISensitive
{
    public override string ToString()
        => Sanitizer.IsActive ? $"Notification(Id={Id})" : $"Notification(Id={Id}, Body={Body})";
}
```

### Activation

Sanitization is activated per-host via `AddSanitizingLoggerFactory` in logging configuration:

- **Server** (`AppHost.Build.cs`): `logging.AddSanitizingLoggerFactory(c => c.HostInfo().IsProductionInstance)`
- **WASM** (`ClientStartup.cs`): `logging.AddSanitizingLoggerFactory(c => c.HostInfo().IsProductionInstance)`
- **MAUI** (`MauiDiagnostics.cs`): `logging.AddSanitizingLoggerFactory(_ => !MauiSettings.IsDevApp)`

### JavaScript Propagation

Pass sanitization flag through `BrowserInit.Initialize()` → `window.App.browserInit` → `browser-init.ts`. Create `Sensitive` class in `src/nodejs/src/sensitive.ts` mirroring the C# API.

### Global Using

`ActualChat.Compliance` added to both `src/dotnet/Directory.Build.props` and `tests/Directory.Build.props`.

---

## Progress

- [x] Create `Core/Compliance/` types: `Sanitizer`, `ISensitive`, `Sensitive`, `Sensitive.Private`
- [x] Add `string.ToPrivate()` extension in `StringExt`
- [x] Add global using for `ActualChat.Compliance` (src + tests)
- [x] Create `SanitizingLogger` and `SanitizingLoggerFactory` in `Core/Logging/`
- [x] Create `LoggingBuilderExt.AddSanitizingLoggerFactory`
- [x] Integrate into Server (`AppHost.Build.cs`)
- [x] Integrate into WASM (`ClientStartup.cs`)
- [x] Integrate into MAUI (`MauiDiagnostics.cs`)
- [x] Unit tests for `Sanitizer`, `Sensitive`, `Sensitive.Private`/`ToPrivate()`
- [x] Unit tests for `SanitizingLoggerFactory`
- [ ] Create TypeScript `Sensitive` module + propagate via BrowserInit
- [ ] Apply `Sensitive.Private(...)` / `.ToPrivate()` to all 26 active logging sites

---

## Logging Sites to Update

### CRITICAL — Message text (always active in prod at WARNING/ERROR)

| # | File | Line(s) | Level | Content |
|---|------|---------|-------|---------|
| 1 | `UI.Blazor.App/.../SendingMessages.cs` | 343-345 | Error | `request.Text` |
| 2 | `UI.Blazor.App/.../SendingMessages.cs` | 394 | Error | `request.Text` |
| 3 | `UI.Blazor.App/.../SendingMessages.Queue.cs` | 20-21 | Warning | `command.Request.Text` |
| 4 | `Users.Service/.../LogOnlyVerificationCodeSender.cs` | 10 | Warning | SMS text + phone |
| 5 | `App.Maui/.../IncomingShareHandler.cs` | 55 | Info | shared text |
| 6 | `App.Maui.IosShareExt/.../ShareUI.cs` | 179 | Info | shared text |
| 7 | `Chat.ML/EntryGroupExtractor.cs` | 197 | Warning | first 20 chars |
| 8 | `Chat.Service/.../LanguageDetector.cs` | 71 | Error | API response |
| 9 | `Users.Service/.../SMSToVerificationCodeSender.cs` | 44 | Error | SMS API response |

### HIGH — Message/transcription text (Debug level, but active on MAUI devices)

| # | File | Line(s) | Level | Content |
|---|------|---------|-------|---------|
| 10 | `UI.Blazor.App/.../SendingMessages.cs` | 98 | Debug | `cmd.Text` |
| 11 | `UI.Blazor.App/.../SendingMessages.cs` | 105 | Debug | `cmd.Text` |
| 12 | `UI.Blazor.App/.../SendingMessages.cs` | 111 | Debug | `cmd.Text` |
| 13 | `UI.Blazor.App/.../SendingMessages.cs` | 297-298 | Debug | `request.Text` |
| 14 | `UI.Blazor.App/.../SendingMessages.cs` | 387-389 | Debug | `chatEntry.Content` |
| 15 | `UI.Blazor.App/.../SendingMessages.Queue.cs` | 10, 16 | Debug | `command.Request.Text` |
| 16 | `Streaming.Service/.../DeepgramOfflineTranscriber.cs` | 105 | Debug | transcript text |
| 17 | `Streaming.Service/.../DeepgramTranscriber.cs` | 213 | Debug | transcript result object |
| 18 | `Streaming.Service/.../GoogleTranscriber.cs` | 290 | Debug | STT result object |
| 19 | `Streaming.Service/.../GoogleTranscriber.cs` | 293 | Debug | unstable transcript |
| 20 | `Chat.Service/ChatsBackend.cs` | 1892, 1896 | Debug | original + transcribed content |

### MEDIUM — Notification content

| # | File | Line(s) | Level | Content |
|---|------|---------|-------|---------|
| 21 | `App.Maui/.../FirebaseMessagingService.cs` | 65-67 | Debug | all notification data KVPs |
| 22 | `App.Maui/.../FirebaseMessagingService.cs` | 119 | Debug | notification body |

### Client-side TypeScript

| # | File | Line(s) | Level | Content |
|---|------|---------|-------|---------|
| 23 | `UI.Blazor/ServiceWorkers/service-worker.ts` | 82 | Debug | FCM payload with title/body |
| 24 | `UI.Blazor.App/notification-ui.ts` | 68 | Debug | FCM payload with title/body |
| 25 | `UI.Blazor.App/.../undo-stack.ts` | 59,84,112,118,126 | Debug | editor HTML history items |
| 26 | `UI.Blazor.App/.../markup-editor.ts` | 357 | Debug | mention search filter text |

### Not active (commented out, for reference)

| # | File | Line(s) | Status |
|---|------|---------|--------|
| — | `Chat.Service/.../Translator.cs` | 69, 113 | Commented out |
| — | `UI.Blazor.App/.../markup-editor.ts` | 467 | Commented out |
