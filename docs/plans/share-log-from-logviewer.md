# Share Log from Log Viewer

**Status**: PLAN
**Issue**: #3730
**Branch**: `feat/3730-share-log-from-logviewer`

## Goal

Add a **Report** button to the in-app Log Viewer that lets the user
submit a short comment plus the captured log buffer as a diagnostic
report. v1 delivers this report via Sentry's User Feedback (only the
MAUI host has Sentry wired up today). The implementation is structured
behind a `ReportUI` abstraction so other hosts can plug in different
transports later (web Sentry, chat, etc.) without UI changes.

Hard constraints from the issue author:

- Keep it **simple** — no general-purpose issue-reporting infrastructure.
- Short text comment + the log file as an attachment.
- Pluggable transport, so v2 can switch to / add a chat target.

---

## Reuse

### Existing abstractions to reuse

| Need                                         | Reused type / file                                                                          |
| -------------------------------------------- | ------------------------------------------------------------------------------------------- |
| In-memory log ring buffer (10K entries)      | `LogUI` (`src/dotnet/UI.Blazor/Services/LogUI/LogUI.cs`)                                    |
| Log entry shape                              | `LogEntry` (`src/dotnet/UI.Blazor/Services/LogUI/LogEntry.cs`)                              |
| Log Viewer host UI                           | `LogView.razor` / `LogList.razor` in `src/dotnet/UI.Blazor.App/Components/LogView/`         |
| Modal UI pattern                             | `JoinVideoCallModal.razor` (DialogFrame / IModalView reference)                             |
| Sentry .NET SDK (MAUI)                       | `MauiDiagnostics.cs` (`SentrySdk.Init`), `App.Maui/Services/MauiSentryInitializer.cs`       |
| Sentry feedback API                          | `SentrySdk.CaptureFeedback(SentryFeedback, SentryHint?)` — version per `Maui.csproj`         |
| Settings toggle that enables the Log Viewer  | `LogUI.IsEnabled` (already gates the Log Viewer tab in `SettingsModal.razor`)                |
| `HostInfo.HostKind` for SSB detection        | Same check as `LogUI.OnRun` (line 149) — `IsServer()` excludes SSB                          |
| DI module override pattern                   | `MauiAppModule` already replaces / extends UI.Blazor services for MAUI                       |

### Reusability of new components

Two new pieces are introduced. For each, decide local vs shared:

1. **`ReportUI`** (no-op base) — placed in
   `src/dotnet/UI.Blazor/Services/ReportUI/ReportUI.cs` alongside `LogUI`.
   Lives in `UI.Blazor` so any UI host (WASM, MAUI, SSB) can override it
   with a host-specific implementation. **Shared**, default is no-op.

2. **`MauiReportUI`** (Sentry-backed) — placed in `src/dotnet/Maui/` next
   to the rest of the Sentry integration. **MAUI-specific**.

3. **`ReportLogBuilder`** (serialises `LogEntry[]` + env metadata into a
   single `.log` text payload). Reasonably reusable beyond the Log
   Viewer (e.g. future "auto-report on fatal error"). **Recommendation:
   place in `src/dotnet/UI.Blazor/Services/LogUI/`** so any UI host can
   build a report payload.

---

## Design

```
┌──────────────────────┐    1. click Report       ┌────────────────────┐
│   LogView.razor      │ ───────────────────────► │ ReportModal        │
│   [Report] (hidden   │                          │ (comment textarea, │
│   if !ReportUI       │                          │  Submit button)    │
│   .IsAvailable)      │                          │                    │
└──────────────────────┘                          └─────────┬──────────┘
                                                            │ 2. submit
                                                            ▼
                              ┌──────────────────────────────────────┐
                              │ ReportLogBuilder.BuildLogPayload     │
                              │   - snapshot LogUI ring buffer       │
                              │   - format as .log text              │
                              │   - add header: app, host, version   │
                              └────────────────┬─────────────────────┘
                                               │
                              ┌────────────────▼─────────────────────┐
                              │ Modal writes payload to a temp .log  │
                              │ file (Path.GetTempPath() | name).    │
                              │ Hub.ReportUI.Submit(comment, FilePath│
                              │  logFile, ct). Modal deletes file    │
                              │ in finally (Submit must finish read).│
                              │                                      │
                              │ MAUI: MauiReportUI reads bytes →     │
                              │   SentrySdk.CaptureFeedback(...)     │
                              │                                      │
                              │ WASM / SSB: ReportUI base → no-op    │
                              │   (button is hidden anyway)          │
                              └──────────────────────────────────────┘
```

---

## Component breakdown

### 1. `ReportUI` (base, no-op)

**File (new)**: `src/dotnet/UI.Blazor/Services/ReportUI/ReportUI.cs`

```csharp
public class ReportUI
{
    public virtual bool IsAvailable => false;
    // Caller owns logFile and may delete it once Submit returns.
    public virtual Task Submit(string comment, FilePath logFile, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
```

Registered as a singleton in the UI.Blazor DI module. Exposed via `UIHub.ReportUI`
(add a property to `UIHub` like the other UI services). With no override,
`IsAvailable` returns false and the Report button is hidden.

### 2. `MauiReportUI` (Sentry-backed)

**File (new)**: `src/dotnet/App.Maui/Services/MauiReportUI.cs`

Lives in `App.Maui/Services/` (alongside `MauiSentryInitializer`) because:
- `Maui.csproj` does not reference `UI.Blazor`, where `ReportUI` is defined;
- `App.Maui` already references both `Maui` (for Sentry types) and
  `UI.Blazor.AppPack` (transitively `UI.Blazor`).

```csharp
public sealed class MauiReportUI : ReportUI
{
    public override bool IsAvailable => SentrySdk.IsEnabled;

    public override async Task Submit(string comment, FilePath logFile, CancellationToken ct)
    {
        if (!SentrySdk.IsEnabled)
            return;

        var bytes = await File.ReadAllBytesAsync(logFile, ct).ConfigureAwait(false);
        var hint = new SentryHint();
        hint.Attachments.Add(new SentryAttachment(
            AttachmentType.Default,
            new ByteAttachmentContent(bytes),
            logFile.FileName,
            "text/plain"));
        SentrySdk.CaptureFeedback(new SentryFeedback(comment), hint: hint);
    }
}
```

Name / email are auto-attached via Sentry's user scope (already set by
`MauiSentryInitializer`). Sentry SDK version is 5.14.1 (pinned via
`SentryVersion` in `Directory.Packages.props`); `CaptureFeedback` is the
5.x API. The deprecated `CaptureUserFeedback` is intentionally avoided.

**Registration**: in `MauiAppModule.InjectServices`, register
`ReportUI` → `MauiReportUI` as scoped (replacing the base no-op via
re-registration — last `Add*` wins, same pattern as `ReloadUI` on line
47 of the same module).

> **Build-environment note.** `App.Maui` requires the `maui-android`
> (and on macOS, `maui-ios`) workloads. The standard `c` Docker
> container ships without them, so `App.Maui` does not compile inside
> Docker. Any change to `MauiReportUI` must be built/verified outside
> Docker (e.g. on the host with workloads installed, or via the
> `/server-loop` MAUI build skill). UI.Blazor (where `ReportUI` lives)
> builds fine in Docker.

### 3. `ReportLogBuilder`

**File (new)**: `src/dotnet/UI.Blazor/Services/LogUI/ReportLogBuilder.cs`

Single public method:

```csharp
public static byte[] BuildLogPayload(
    IReadOnlyList<LogEntry> entries,
    HostInfo hostInfo,
    AccountFull? account);
```

Output (UTF-8 plain text, one entry per line):

```
=== Voxt log report ===
Generated:    2026-05-13T10:34:00Z
App:          AppKind=Maui, OS=Android 14, Version=1.2.3 (build 4567)
Account:      <UserId>  <Display name>
Entries:      <count>

[2026-05-13T10:30:01.234Z] INFO  MyCategory: message…
[2026-05-13T10:30:01.245Z] ERROR Other.Category: message…
  System.Exception: …
   at …
```

Snapshot of `LogUI` taken via the existing public `GetIdRange` /
`GetTiles` compute methods and flattened to `IReadOnlyList<LogEntry>`.
No new API surface on `LogUI`.

### 4. UI: Report button + modal

**Modify** `src/dotnet/UI.Blazor.App/Components/LogView/LogView.razor`:

- Add a small toolbar above `<LogList />` with one button: `Report`.
- Render the button only when `Hub.ReportUI.IsAvailable` is true.
- (The Log Viewer tab itself is already gated by `LogUI.IsEnabled`; we
  don't need to change that.)
- On click, open a new modal `ReportModal`.

Additionally, the Log Viewer is empty in SSB by design (see
`LogUI.cs:149` — `TailLoggerSinks` not attached when
`HostInfo.HostKind.IsServer()`). The Report button being gated by
`ReportUI.IsAvailable` covers SSB too — since SSB has no `MauiReportUI`
override registered, it gets the no-op base, `IsAvailable=false`,
button hidden.

**New** `src/dotnet/UI.Blazor.App/Components/LogView/ReportModal.razor`:

- Follows the `JoinVideoCallModal` pattern: implements
  `IModalView<ReportModal.Model>`, uses `DialogFrame`.
- One `<textarea>` for an optional short comment (cap at 1000 chars,
  enforced client-side with a counter).
- Submit button:
  1. Calls `ReportLogBuilder.BuildLogPayload(...)`.
  2. Calls `Hub.ReportUI.Submit(comment, bytes, ct)`.
  3. Shows a toast "Report sent — thanks!" via existing `ToastUI`.
  4. Closes modal.
  5. On exception, shows an error toast with the message; modal stays
     open so the user can retry.
- Cancel button: `Modal.Close()`.

Mobile considerations: modal is already responsive in this codebase.

### 5. Testing

**Unit test**: `tests/UI.Blazor.IntegrationTests/ReportLogBuilderTest.cs`
— verify header lines, entry formatting, and that exceptions are
included with stack traces.

**`MauiReportUI`**: skip an automated test unless `SentrySdk` is
trivially mockable. Cover via the manual test step instead.

**No backend integration test needed** — v1 has no backend in this path.

---

## Security & abuse considerations

- **Spam / DoS**: skipped for v1. Sentry has its own ingestion quotas
  and rate-limits — abuse stays bounded by the Sentry plan.
- **Attachment size**: cap the uploaded log to 100 MB client-side
  before calling `Submit`. Sentry has its own per-attachment limit
  (typically 20 MB free / configurable per plan) — if the payload is
  larger than Sentry's cap we should truncate the buffer before sending
  and note "log truncated" in the report comment.
- **Privacy**: the log payload may contain user-identifiable strings
  the user typed (markup masking via `ToPrivate()` should already apply
  before anything reaches `LogUI` — verify via `Sanitizer.MaskPrivate`
  usage in the codebase before shipping). If we find unmasked content
  in `LogEntry.Message`, add a sanitisation pass in `ReportLogBuilder`.
  Sentry is a third-party SaaS, so this matters more than for a chat
  destination.
- **Identity**: the Sentry feedback carries `Name` and `Email`
  automatically from the existing Sentry user scope set by
  `MauiSentryInitializer`. We don't need to add identity to the comment
  text.

---

## Step-by-step implementation order

1. **[done]** Add `ReportUI` base class
   (`src/dotnet/UI.Blazor/Services/ReportUI/ReportUI.cs`). Expose via
   `UIHub.ReportUI` (`UI.Blazor/UIHub.cs`). Register the no-op base in
   `BlazorUICoreModule.cs` as `services.AddScoped<ReportUI>()`.
2. **[done]** Add `MauiReportUI` in
   `src/dotnet/App.Maui/Services/MauiReportUI.cs`. Register it in
   `MauiAppModule.InjectServices` as
   `services.AddScoped<ReportUI>(_ => new MauiReportUI())` to replace
   the base. Verified by `dotnet build -f net10.0-ios` on macOS host
   with `maui` workload installed.
3. **[done]** Add `ReportLogBuilder` in
   `src/dotnet/UI.Blazor/Services/LogUI/ReportLogBuilder.cs`. Static
   class, single method
   `BuildLogPayload(IReadOnlyList<LogEntry>, HostInfo, AccountFull?, Moment)`
   returning UTF-8 bytes.
4. **[done]** Add `ReportModal` + Report button on `LogView.razor`
   (gated by `Hub.ReportUI.IsAvailable`). Modal in
   `src/dotnet/UI.Blazor.App/Components/LogView/ReportModal.razor`,
   toolbar + modal styles in `log-view.css`.
5. **[pending — manual]** Manual test on MAUI Android (and iOS if
   practical). Verify the feedback lands in Sentry with the log
   attached. Verify the button is hidden on WASM and SSB.
6. **[done]** Unit tests for `ReportLogBuilder` in
   `tests/UI.Blazor.UnitTests/ReportLogBuilderTest.cs` — header,
   entries, exception formatting, UTF-8 encoding, null account. All 5
   tests pass.
7. **[done]** `dotnet build` of `UI.Blazor`, `UI.Blazor.App`,
   `UI.Blazor.UnitTests`, `UI.Blazor.IntegrationTests`, and
   `App.Maui -f net10.0-ios` all succeed. No `*.CI.slnf` exists in the
   repo root; the wider solution wasn't rebuilt. `npm run build:Verify`
   not run (no TypeScript files changed).

---

## Open questions

None — all resolved during plan review.

## Future versions (out of scope for v1)

- **`WebReportUI`** — Sentry browser SDK integration for WASM / SSB.
  Could share the same flow once `@sentry/browser` (or
  `sentry-blazor`) is wired up.
- **`ChatReportUI`** — alternative target that posts into a Voxt team
  Place chat instead of (or alongside) Sentry. The earlier plan
  iterations covered this in detail (see git history of this file) —
  preserved as a reference if/when we decide to bring it back.
