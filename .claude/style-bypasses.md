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

## src/dotnet/UI.Blazor.App/Services/WalkieTalkiePlatform.cs

- L4 `Platform hooks for the walkie-talkie session:` — removed `<see cref="WalkieTalkieSession"/>` cross-reference (App.Maui type unreachable from UI.Blazor.App) per task-2-brief.md
