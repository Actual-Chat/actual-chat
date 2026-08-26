# Style-check bypasses

Violations the style hook must not report again.

The default is that **every** style violation gets fixed, including ones that
were already in a file before the edit that surfaced them. An entry belongs here
only when a human has explicitly decided to keep the code as it is.

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

## src/dotnet/UI.Blazor.App/Testing/VirtualListTestService.cs

- L3 `public class VirtualListTestService(IServiceProvider services) : IComputeService`
  — type not sealed — required: Fusion generates a proxy over the `[ComputeMethod]`
  members, so they must stay `virtual`, and a sealed type can't declare one (CS0549)

## src/dotnet/Api/Users/UserLanguageSettings.cs

- L24 `[DataMember, MemoryPackOrder(4), Key(4)]` on `UILanguage`
  — MemoryPack attribute on a new member — required: the type is `[MemoryPackable]`
  for legacy KVAS reads, and MemoryPack's generator rejects a partially annotated
  object outright (MEMPACK025), so a new member cannot opt out of `MemoryPackOrder`

## src/dotnet/UI.Blazor.App/Services/LanguageUI/LanguageUI.cs

- L11 `public class LanguageUI : UIWorkerBase<AppUIHub>, IComputeService, IDisposable`
  — type not sealed — required: Fusion generates a proxy over the `[ComputeMethod]`
  members, so they must stay `virtual`, and a sealed type can't declare one (CS0549)
- L26 `public LanguageUI(AppUIHub hub) : base(hub)`
  — not a primary constructor — required: the `Settings` initializer uses `StateFactory`
  (a base member) and the `CreateLanguageSettings` method group, and `this` isn't
  available in a field initializer

## src/dotnet/UI.Blazor.App/Services/LocalizationUI.cs

- L14 `public class LocalizationUI : UIServiceBase<AppUIHub>, IUITextLocalizer, IComputeService, IAsyncDisposable`
  — type not sealed — required: Fusion generates a proxy over the `[ComputeMethod]`
  members, so they must stay `virtual`, and a sealed type can't declare one (CS0549)
- L20 `private readonly AsyncTaskMethodBuilder _whenReadySource = AsyncTaskMethodBuilderExt.New();`
  — task-source field naming — matches `AccountUI.cs:13` and `WebShareInfo.cs:9`
  verbatim; this is the established idiom for a `WhenXxx` gate
- L30 `public LocalizationUI(AppUIHub hub) : base(hub)`
  — not a primary constructor — required: the `_localizations` initializer passes the
  `Localize` method group, and `this` isn't available in a field initializer
- L31 `hub.LogFor<ConcurrentProcessor<Key, string>>()`
  — `LogFor<T>()` instead of `LogFor(GetType())` — intended: the logger belongs to the
  `ConcurrentProcessor` it's handed to, not to `LocalizationUI`

## src/dotnet/UI.Blazor.App/Components/ChatView/Items/TranscriptUI.cs

- L5 `public class TranscriptUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService`
  — type not sealed — required: Fusion generates a proxy over the `[ComputeMethod]`
  members, so they must stay `virtual`, and a sealed type can't declare one (CS0549)

## src/dotnet/UI.Blazor.App/Services/TranslationUI/ThrottledTranslations.cs

- L5 `public class ThrottledTranslations : UIWorkerBase<AppUIHub>, IComputeService, IAsyncDisposable`
  — type not sealed — required: Fusion generates a proxy over the `[ComputeMethod]`
  members, so they must stay `virtual`, and a sealed type can't declare one (CS0549)
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

## src/dotnet/Invite.Service/InvitesBackend.cs

- L8 `public class InvitesBackend(IServiceProvider services)`
  — type not sealed — required: Fusion generates a proxy over the `[ComputeMethod]`
  and `[CommandHandler]` members, so they must stay `virtual`, and a sealed type
  can't declare one (CS0549)

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

- L10 `public class NotificationsPanelUI : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized`
  — type not sealed — required: Fusion generates a proxy over the `[ComputeMethod]`
- L23 `public NotificationsPanelUI(AppUIHub hub) : base(hub)`
  — explicit constructor instead of a primary one — required: the body reads
  `StateFactory`, a base-class member, which a primary constructor's field
  initializers can't reach
