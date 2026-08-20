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

The **quoted snippet is the identity** — match on it first. The line number is
the original start line and only a hint, since it drifts as the file changes;
the rule is an abbreviated label, since its wording changes as the guide is
edited. The reason can be as short as whose decision it was.

## src/dotnet/UI.Blazor.App/Testing/VirtualListTestService.cs

- L3 `public class VirtualListTestService(IServiceProvider services) : IComputeService`
  — type not sealed — required: Fusion generates a proxy over the `[ComputeMethod]`
  members, so they must stay `virtual`, and a sealed type can't declare one (CS0549)

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
