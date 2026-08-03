# Streaming markup messages

## Goal

Let a regular text message (one with markup) stream its content in, the way a
transcribed message already does — so an LLM can post a reply that appears
progressively instead of landing whole. Four properties define "done":

- Streaming works for regular text messages, i.e. messages with markup.
- Transcribed messages keep rendering as `PlayableTextMarkup` at the top level.
- A translation streams while the source message is still streaming.
- A streamed translation retains the top-level playable markup for transcribed
  entries and the regular markup styling for text entries.

The load-bearing prerequisite is partial-markup parsing: while text arrives, the
tail is always mid-token, and today that renders as literal `**` characters that
snap into styled text only when the closing token shows up.

## Why now

Two things already in the tree point at this:

- The streaming machinery is not audio-specific. `ChatEntry.ContentStreamId` and
  `IsContentStreaming` (`ChatEntry.cs:57,121`) are entry-kind agnostic; only the
  *producer* is audio-only (`AudioStreamingBackend.ProcessAudio.cs:550`).
- The client streaming branch renders raw text spans with no markup parsing
  (`ChatEntryMessageInternalView.razor:99-119`). For a spoiler this is a live
  defect, not just cosmetics: `||secret||` in a message that is being translated
  is displayed **in the clear** for the duration of the stream, and only becomes
  a spoiler once the stream finalizes and the markup is parsed.

## Findings: how the parser handles unfinished markup today

Checked directly against the parser. Only **code blocks** auto-close:
`CodeBlockEndOrEof` (`MarkupParser.cs:245-246`) falls back to end-of-input, with
three tests covering it (`UnclosedCodeBlockExtendsToEndOfMessage`,
`UnclosedCodeBlockWithLanguage`, `UnclosedCodeBlockAfterParagraph`).

Inline markup does **not** pretend to close:

| Input | Parses as |
|---|---|
| `Hello **bold` | plain text — not bold |
| `Hello *ital` | plain text |
| `Hello \|\|spoi` | plain text |
| `` Hello `cod `` | plain text |
| `**bold with \`code` | plain text, whole thing |
| `# Head`, `> quo`, `- item` | correct — line-based, no closer needed |
| `Hello https://exa` | already a `UrlMarkup` |
| ``Hello `` `` | an empty `PreformattedTextMarkup` |

This behaviour is deliberate and asserted for finished messages —
`UnterminatedSpoilerIsLiteralTest` (`MarkupParserTest.cs:336`) and
`UnterminatedPipeTest` lock it in, and they should stay: in a *completed*
message `a || b` must not become a spoiler. Hence the partial behaviour is a
**mode**, not a change of default. There is no existing coverage of partial
inline markup, because nothing consumed partial markup until now.

Block-level constructs already behave correctly while streaming. Two extra
sources of flicker to fix along the way: a partial URL links early, and a
half-typed `` ` `` pair renders as an empty code span.

## Phase 0 — incomplete markup in the grammar (done)

Rewriting the text before parsing was rejected: it duplicates the grammar's
open/close rules in a second place, and the two inevitably drift. Instead the
grammar itself **matches** the unterminated form and marks the result.

- `Markup.IsIncomplete` — an init-only flag on the base type, excluded from
  every serializer, since it describes a transient parse of a prefix rather
  than the message. `Format()` on an incomplete element omits the closing
  token, so it round-trips back to the received text.
- `MarkupParser.AllowIncompleteMarkup` selects a second grammar variant. A
  parser without it **never** produces an element with the flag set — that is
  the contract, and it has its own test.
- The variant is built by `IncompleteInternalParsers`, a subclass of the
  existing grammar builder overriding `CreateStylized` and
  `CreatePreformattedText` to add an alternative alongside the complete one.
  This required moving the inline chain (stylized spans, preformatted, text
  blocks, list items) from static fields into the builder so it can be
  overridden; everything else stayed static.

The incomplete alternative is: opening token, non-empty content, then end of
input. Two constraints emerged from the tests and both are load-bearing:

- **Content must be non-empty.** With an empty match allowed, a nested span
  consumes the *closing* token of the span enclosing it, turning `**bold**`
  into an incomplete bold wrapping an incomplete empty one. The cost is that a
  bare trailing token (`Hello **`) stays literal until its first content
  character arrives.
- **A two-character closing token may be half-arrived** (`**bold*`,
  `||secret|`). That half is consumed as part of the incomplete match.
  Otherwise the span degrades to literal text — which for a spoiler means
  displaying exactly what it is supposed to hide.

Not done: suppressing linkification of a still-growing URL. It can't be
expressed as an alternative rule the way the others can, and needs the URL
parser itself to become variant-aware.

**Tests** (this is the part that has to be thorough):

- Each inline token unterminated, alone and mid-sentence.
- Nesting: `**a ||b`, `||**b`, `*a **b`, and closing order on completion.
- Split mid-delimiter: `**`, `*`, `|`, `` ` ``, `@` as the final character(s).
- Interaction with code blocks and with block constructs (header, quote, list).
- Incomplete URL and the empty-`` `` `` case.
- Prefix sweep: for a set of known-good messages, assert that **every prefix**
  parses without throwing, and that the readable text of each prefix is a
  prefix of the readable text of the whole. This is the property that actually
  matters for streaming and catches cases nobody enumerated.
- The existing full-parser tests must stay green unchanged — that is the proof
  the mode is opt-in.

## Phase 1 — streaming text entries

`ContentStreamId` / `IsContentStreaming` are already generic, so this is about
adding a non-audio producer: create an entry with a `ContentStreamId`, push
chunks, then finalize (`Content = finalText`, `ContentStreamId = ""`) — the same
lifecycle `ProcessAudio` drives at `AudioStreamingBackend.ProcessAudio.cs:550`
and `:592`.

Open: transport and producer surface (see Open questions).

## Phase 2 — client rendering refactor

This needs real refactoring, not a patch. Today the streaming branch carries four
flat strings — `RetainedText` / `ChangedText` / `AnimatedText` / `Tail`
(`TranscriptStreamReaderState`) — rendered as sibling `<span>`s with CSS
transition delays (`ChatEntryMessageInternalView.razor:99-119`). Markup is a
tree, so "first N characters retained, the rest animated" no longer maps onto
the rendered structure.

Direction: make the split markup-aware. A rewriter built on the existing
`MarkupRewriter` (`Visitors/Generic/`) splits the parsed partial markup at a
character offset, so the animated tail stays *inside* whatever span contains it —
newly arriving characters inside an unterminated `**` render bold immediately.

A cheaper first increment, if we want to stage it: split only the trailing text
node, leaving everything before it as ordinary markup. Streamed text always grows
at the tail, so this covers the common case; its one visible flaw is that
characters arriving inside an unterminated `**` render unstyled for a frame.

Transcribed entries keep `PlayableTextMarkup` at the top level throughout.

## Phase 3 — when a translation streams

Keep the existing length-based mechanic; do **not** replace it with
"stream only while the source streams". A long message that is already complete
should still translate progressively — the current branch simulates streaming by
streaming one LLM response, and that behaviour is worth keeping.

Changes:

- Raise `StreamingMinContentLength` from 50 to ~100. The point is efficiency and
  rate-limit headroom: at 50 we open a stream for messages short enough that the
  overhead is not repaid.
- Add "source entry is still content-streaming" as an *additional* trigger, so a
  streaming source always yields a streaming translation regardless of length.

Net gating: stream the translation when the source is streaming **or** the
content is long. The final one-shot re-translation after finalization
(`SkipRealtimeTranslation: true`) stays as it is.

The existing streaming plumbing in `StreamTranslation()`
(`TranslationsBackend.cs:203`) is reusable for the streaming-source case.

## Phase 4 — streamed translation keeps its markup

Feed the translated stream through the same partial-markup path as Phase 2:
`PlayableTextMarkup` at the top level for transcribed entries — the time map is
already rescaled by the source/target length ratio at
`TranslationsBackend.cs:526` — and partial-mode markup for text entries.

This is also what closes the spoiler leak: `||secret` renders masked while the
translation streams instead of exposing its content.

## Reuse

**Existing abstractions to use.** `MarkupParser`'s init-option pattern for the
new flag; `IMarkupParser` and the `CachingMarkupParser` decorator (partial
results must not poison the shared cache — see Risks); `MarkupRewriter` /
`MarkupVisitor` (`Visitors/Generic/`) for the Phase 2 tree split;
`Transcript` / `TranscriptDiff` / `StringDiff` plus
`IAudioStreamingBackend.PushTranscript` and
`ILiveAudioStreams.GetTranscriptStream` for transport; `TranscriptStreamReader`
and `TranscriptUI.GetStreamingState` keep their shape;
`ChatEntryDiff.ContentStreamId` for the entry lifecycle;
`StreamTranslation()` for the streaming-translation path. Nothing existing does
partial-markup completion or markup-tree splitting — those two are genuinely new.

**Placement of new components.** The partial-mode logic goes into `MarkupParser`
itself. The tree splitter belongs in `ActualChat.Api`'s markup folder next to the
other visitors, **not** in the UI project: it depends on `Markup` types (so
`ActualChat.Core` is not an option), and it is useful beyond streaming — the
message-editor live preview wants partial parsing, and any progressive-reveal
surface wants the splitter.

## Risks and things to watch

- **Caching.** `CachingMarkupParser` keys on text alone. A partial parse of
  `"Hello **bo"` must not be served later as the full parse of the same string,
  and the cache should not fill with thousands of one-shot prefixes.
- **Animation fidelity vs. tree correctness** — the Phase 2 trade-off above.
- **Cost.** Re-parsing the full text on every chunk is O(n²) over a message.
  Measure before optimising, but it is the obvious hot spot.
- **Preview surfaces.** Chat list, quotes and notifications flatten markup via
  `MarkupFormatter.ReadableUnstyled` (which masks spoilers by design). Check how
  they behave for an entry mid-stream.
- **Naming drift.** Every transport type says "transcript"/"audio". If text
  streaming reuses it, the naming gets misleading — worth one mechanical rename
  pass afterwards.

## Open questions

1. **Transport** — reuse the `Transcript`-shaped stream with a degenerate
   `TimeMap`, or introduce a text-specific stream type alongside it? Reuse costs
   nothing to build; the naming is the price.
2. **Producer surface** — internal backend command only, or a public API / MCP
   surface so bots can stream? This decides whether Phase 1 is small or needs
   auth and rate-limit design.
