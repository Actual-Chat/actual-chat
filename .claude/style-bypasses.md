# Style-check bypasses

Violations the style hook must not report again.

The default is that **every** style violation gets fixed, including ones that
were already in a file before the edit that surfaced them. An entry belongs here
only when a human has explicitly decided to keep the code as it is.

## General

These hold everywhere and need no per-file entry.

- **A URL is reproduced verbatim.** Wherever it appears — a comment, a string, a
  doc — a link is never shortened, wrapped, re-anchored or otherwise rewritten to
  satisfy the line length limit or any other formatting rule. The limit yields to
  the link. Trimming `#section` off a URL to save 30 characters costs the reader
  the paragraph the author meant to point at, and nothing about formatting is
  worth that.

One `##` subheader per file, one bullet per bypassed violation:

```
## src/dotnet/Api/Chat/Markup/PreformattedTextMarkup.cs

- L11 `public static readonly PreformattedTextMarkup Empty = new("");`
  — blank lines around single-line member — Alex Yakunin's decision
```

## src/dotnet/Transcription.Service/Transcribers/OpenAITranscriber.cs

- L73 `AudioTranscription transcription = await _audioClient`
  — explicit type instead of `var` — required: the call returns
  `ClientResult<AudioTranscription>` and the target type drives the implicit
  conversion, so `var` doesn't compile

## tests/Chat.UnitTests/CommandMigrationSerializationTest.cs

- `ArgumentList args = ArgumentList.New(default(TstCmd_RemoveEntries)!);`
  — explicit type instead of `var` — required: `ArgumentList.New` returns a
  derived `ArgumentList1<T>`, but `Deserialize(ref ArgumentList, …)` needs the
  variable typed as the base `ArgumentList` for `ref args` to bind

The **quoted snippet is the identity** — match on it first. The line number is
the original start line and only a hint, since it drifts as the file changes;
the rule is an abbreviated label, since its wording changes as the guide is
edited. The reason can be as short as whose decision it was.

## src/dotnet/Api/Users/UserLanguageSettings.cs

- L24 `[DataMember, MemoryPackOrder(4), Key(4)]` on `UILanguage`
  — MemoryPack attribute on a new member — required: the type is `[MemoryPackable]`
  for legacy KVAS reads, and MemoryPack's generator rejects a partially annotated
  object outright (MEMPACK025), so a new member cannot opt out of `MemoryPackOrder`

## src/dotnet/UI.Blazor.App/Services/LanguageUI/LanguageUI.cs

- L26 `public LanguageUI(AppUIHub hub) : base(hub)`
  — not a primary constructor — required: the `Settings` initializer uses `StateFactory`
  (a base member) and the `CreateLanguageSettings` method group, and `this` isn't
  available in a field initializer

## src/dotnet/UI.Blazor.App/Services/LocalizationUI.cs

- L20 `private readonly AsyncTaskMethodBuilder _whenReadySource = AsyncTaskMethodBuilderExt.New();`
  — task-source field naming — matches `AccountUI.cs:13` and `WebShareInfo.cs:9`
  verbatim; this is the established idiom for a `WhenXxx` gate
- L30 `public LocalizationUI(AppUIHub hub) : base(hub)`
  — not a primary constructor — required: the `_localizations` initializer passes the
  `Localize` method group, and `this` isn't available in a field initializer
- L31 `hub.LogFor<ConcurrentProcessor<Key, string>>()`
  — `LogFor<T>()` instead of `LogFor(GetType())` — intended: the logger belongs to the
  `ConcurrentProcessor` it's handed to, not to `LocalizationUI`

## src/dotnet/UI.Blazor.App/Services/TranslationUI/ThrottledTranslations.cs

- L17 `public ThrottledTranslations(AppUIHub hub) : base(hub)`
  — explicit constructor instead of a primary one — required: both fields are built
  from the instance methods `WhenTranslated` / `WhenLanguageDetected`, which a field
  initializer can't reference (CS0236)

## src/nodejs/src/scroll-controller.ts

- L452 `switch (this.phase) {`
  — switch case labels not indented relative to `switch` — required: the
  repo's eslint `indent` rule is configured with `SwitchCase: 0` and fails the
  build on the indented form, so eslint wins over the style guide here

## tests/Benchmarks/MarkupParserBenchmarks.cs

- L18 `public class MarkupParserBenchmarks`
  — type not sealed — required: BenchmarkDotNet's in-process toolchain rejects a
  sealed benchmark class ("Declaring type must be unsealed") and refuses to run it

## src/dotnet/Api/Chat/Markup/MarkupParser.cs

- L450 `private class InternalParsers(bool useUnparsedTextMarkup)`
  — type not sealed — required: `IncompleteInternalParsers` derives from it and
  overrides its `protected virtual` factory methods, so sealing it is CS0509

## tests/Users.IntegrationTests/MauiAuthControllerTest.cs

- L21 `var response = await client.GetAsync(StartUrl(sessionToken, "/signIn/Google"));`
  — missing `.ConfigureAwait(false)` — required: the guide scopes that rule to
  service-layer code, `tests/Directory.Build.props:36-39` suppresses CA2007 /
  MA0004 / RCS1090 for test projects, and no other test in the repo uses it

## src/dotnet/UI.Blazor/Components/ContentSwap/content-swap.css

- L180 `var(--content-swap-wipe, 0.2s) linear both;`
  — `mask-position` animated without `steps(N)` — Alex Yakunin's decision: the
  wipe-dissolve is meant to read as a smooth ramp, and `steps()` turns it into
  the discrete bands the effect was moved away from
- L184 `mask-position: var(--c-wipe-from);`
  — same, in the `content-swap-wipe-out` keyframes the rule above runs

## src/dotnet/App.Maui/Platforms/Windows/Audio/WindowsAudioCapture.cs

- L20 `private ILogger Log { get; } = log;`
  — blank line after a single-line property — the hook alternates between
  demanding and forbidding the blank line after this property on successive
  runs; settled on no blank line, which is what "0 blank lines around
  single-line properties, fields, and methods" says literally

## src/dotnet/UI.Blazor.App/Components/AudioRecorder/AudioRecorder.cs

- L14 `private readonly MutableState<AudioRecorderState> _state;`
  — blank lines between members — the hook reads "0 blank lines inside types" as
  "strip every blank line between members"; every other type in the repo separates
  members with blank lines, so the file is left as it is. NEEDS ALEX'S CALL
- L19 `private readonly AudioFocusRequester _audioFocusRequester;`
  — readonly field after a mutable one — field order predates this branch and
  reordering it is unrelated churn. NEEDS ALEX'S CALL
- L37 `public AudioRecorder(AppUIHub hub)`
  — explicit constructor instead of a primary constructor — the ctor body assigns
  four fields including a MutableState built from `Hub`, so the conversion is a real
  refactor, not a style fix. NEEDS ALEX'S CALL
- L146 `throw new AudioRecorderException("Failed to start the recording.", e);`
  — missing blank line before the final throw — it follows a run of guard clauses,
  which the guide's own exemption covers
- L109 `await StopRecordingUnsafe().ConfigureAwait(false);`
  — blank line after an if/else-if chain ending in `return` — the chain ends in an
  `else if`, which the guide's exemption covers

## src/dotnet/UI.Blazor.App/Services/NotificationsPanelUI.cs

- L23 `public NotificationsPanelUI(AppUIHub hub) : base(hub)`
  — explicit constructor instead of a primary one — required: the body reads
  `StateFactory`, a base-class member, which a primary constructor's field
  initializers can't reach

## src/dotnet/UI.Blazor.App/Services/Video/services/recorder-preview-view.ts

- L53 `// Forces WebKit to recomposite a <video> that decodes but never paints.`
  — comment longer than the guide's limit — Alex Yakunin's decision: this is a
  four-line workaround for an upstream browser bug whose symptom (a healthy,
  advancing, correctly-sized `<video>` painting nothing) reads as a no-op, and
  whose cause lives in two CSS properties in another file. Without the reasoning
  written down the next reader deletes it. Records the WebKit bug id, the
  measured evidence, and why `contain`/`will-change` are nudged, not removed.

## src/dotnet/UI.Blazor.App/Components/Settings/SettingsModal.razor

- L44 `tabs.Add(new(SettingsTabId.CarAudio, "Android Auto") {`
  — hardcoded English tab title instead of `L.Settings_*` — Dmitrii's decision:
  "Android Auto" is a third-party product name, the same category
  CODING_STYLE.md's Localization section already exempts (`GIF`, `Google Play`,
  `Sentry`); it is the tab title itself here, not an incidental mention, but the
  exemption's rationale (brand names aren't translated) applies identically

## src/dotnet/UI.Blazor.App/Services/LiveFoldMath.cs

- L12 `public static long Advance(long lastFoldEndLid, long minVisibleLid, long streamingFloorLid, long tailFloorLid)`
  — multi-paragraph comment on a single-expression member, and two bullets that describe
  `LiveBlockUI` members rather than this one — Alex Yakunin's decision: the fold rule's
  accepted trade-offs must be listed together at the rule itself, not scattered across the
  call sites that happen to cause them

## src/dotnet/UI.Blazor.App/Services/Video/hevc-codec-selection.ts

- L42 `// Ordering: each simulcast layer ships its own HVCC with per-layer`
  — comment longer than the guide's limit — same category as the
  `recorder-preview-view.ts` entry: nine lines recording two distinct upstream
  browser bugs (Chrome's `isConfigSupported` not cross-checking the level against
  the description, so `decode()` drops chunks silently; Chrome's HEVC encoder
  writing HVCC tier=Low while the SPS says High) and the candidate ordering they
  force. Both symptoms are silent, so without the reasoning the next reader
  shortens the ladder and reintroduces them. NEEDS ALEX'S CALL
- L190 `// SPS layout: 2-byte NAL hdr, then RBSP. RBSP byte 1 high bit`
  — comment on a bit-twiddling expression — it names the bitstream field layout
  the shift-and-mask decodes, which the expression cannot carry. NEEDS ALEX'S CALL
