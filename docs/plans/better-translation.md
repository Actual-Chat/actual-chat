---
title: Better translation
description: Throttle the translation streams — one path is unthrottled per LLM chunk, the other is throttled but tuned for latency rather than cost.
---

# Better translation

Translation publishes two independent streams. One of them has no rate limit at
all and emits one wire item per LLM chunk; the other is throttled, but at a
period chosen for latency rather than for LLM spend. This plan covers both.

Every line reference below was read from source on 2026-08-21.

## Why it matters

Each item a translation stream publishes becomes an RPC message, then a
`TranscriptStreamReader` state write on every viewing client, then a Fusion
recompute, then a Blazor render batch. It was one of the producers behind the
main-thread stall investigated in PR #4220.

The client half of that is already fixed: `ChatEntryMessageInternalView` now
uses `FixedDelayer.Get(0.2)`, capping rendering at 5/sec regardless of arrival
rate. So throttling the server no longer changes client *render* cost much —
what it saves is **wire traffic, RPC serialization, server CPU, and (on the
realtime path) LLM spend**. The two fixes are complementary, not redundant.

## The two paths

### Path A — realtime translation of live speech

`TranslationsBackend.TranslateTranscriptStream` (`TranslationsBackend.cs:444`),
using the keyed `RealtimeTranslator` (`:31`,
`Constants.Translation.RealtimeServiceKey`).

**Already throttled on input** (`:472-476`):

```csharp
await foreach (var transcriptDiffBatch in originalStream.Replay(cancellationToken)
                   .Buffer(TranslateThrottleDelay, Clocks.CpuClock, cancellationToken: cancellationToken)
```

`TranslateThrottleDelay = 500ms` (`:23`, `private static readonly`). Each window
emits at most two output diffs — the stable diff (`:490`) and the diff-since-stable
(`:497`) — so roughly 2-4 wire items/sec, not per token.

**The real cost here is LLM calls, not renders.** Every window costs up to two
`RealtimeTranslator.Translate` round-trips (`:514`), i.e. up to **4 LLM calls per
second per active realtime translation**. There is an early-exit when the text is
unchanged (`:507`), so the saving from a larger window is real but not exactly
linear.

**Proposed work:**

1. Raise `TranslateThrottleDelay`. 1000-1500ms roughly halves-to-thirds the LLM
   spend. The cost is translated-text lag — but the ASR transcript underneath
   stays at 200ms (see Path A's source below), so the original text remains
   responsive and only the translated overlay trails. For live speech, which
   already lags by a sentence, 1-1.5s is defensible.
2. Promote it from a hardcoded `private static readonly` to a `ChatSettings`
   knob, so it can be calibrated against real cost without a deploy.

The number is a product/cost call, not a technical one.

### Path B — whole-message translation streaming

`TranslationsBackend.StreamTranslation` (`:202-211`):

```csharp
using var stream = Translator
    .Stream(translationSource.Content, id.Language, context, cancellationToken)
    .ToTranscriptDiffs()
    .Memoize(cancellationToken);
var rpcStream = RpcStream.New(stream.Replay(cancellationToken));
var publishStreamTask = StreamingBackend.PushTranscript(streamId, rpcStream, cancellationToken);
```

**No throttle. One wire item per LLM chunk.** This is the path behind the
`div.chat-message-markup.streaming` churn in the incident log.

Contrast the ASR path, `AudioStreamingBackend.ProcessAudio.cs:540-543`, which
throttles at `Constants.Transcription.ThrottlePeriod` = 200ms (`Constants.cs:226`).

## The fix for Path B

Three existing operators in `TranscriptDiffStreamExt` compose into exactly the
coalescing this needs — no new machinery:

```csharp
using var stream = Translator
    .Stream(translationSource.Content, id.Language, context, cancellationToken)
    .ToTranscriptDiffs()                                  // StringDiff -> TranscriptDiff
    .ToTranscripts()                                      // accumulate: transcript += diff
    .ThrottleTranscript(throttlePeriod, Clocks.CpuClock, cancellationToken)
    .ToTranscriptDiffs()                                  // re-diff vs last EMITTED snapshot
    .Memoize(cancellationToken);
```

Why each step is safe:

- **Throttling is applicable at all** because `ToTranscripts` (`:39-46`) turns the
  incremental diffs into cumulative *snapshots*. Dropping a superseded snapshot is
  lossless; dropping a diff would not be.
- **The throttle actually engages.** `ToTranscriptDiffs(IAsyncEnumerable<StringDiff>)`
  (`:31-37`) leaves `IsStable` at its default `false`, and `TranscriptDiff.Apply`
  (`TranscriptDiff.cs:43`) propagates that to the accumulated transcript. Stable
  transcripts bypass the throttle (`TranscriptDiffStreamExt.cs:89`); these are not
  stable, so they are throttled.
- **First-paint latency is unchanged.** The first transcript passes through
  immediately — `isFirstTranscript` (`:69`) is only cleared in `ResetPendingState()`
  (`:121`).
- **The final translation is never swallowed.** `pendingTranscript` is flushed both
  on stream end (`:83-85`) and after the loop (`:113-114`).
- **The dropped text is not lost.** The trailing `ToTranscriptDiffs(IAsyncEnumerable<Transcript>)`
  (`:8-16`) diffs each transcript against the previously *yielded* one, so every
  emitted diff is the merge of everything dropped. That is the coalescing.

### Period

Start at 500ms. Translation of an already-complete message does not need the
200ms smoothness the ASR path targets. Add it as
`Constants.Translation.StreamThrottlePeriod` rather than reusing
`Constants.Transcription.ThrottlePeriod`, so the two can diverge —
`Constants.Translation` (`Constants.cs:438-443`) currently holds only service keys
and `NoTranslationNeededText`.

## Corrections to earlier analysis

Recorded so they are not repeated:

- **"Translation is an unthrottled producer"** — only half true. Path A has been
  throttled at 500ms all along; only Path B is unthrottled.
- **"The `IsStable` bypass makes any throttle a no-op here"** — false. That applies
  to the *synchronous* `ToTranscriptDiffs(IEnumerable<StringDiff>)` overload
  (`:28-29`), which sets `IsStable = true`. `Translator.Stream` returns
  `IAsyncEnumerable<StringDiff>` (`Translator.cs:80`), which binds to the async
  overload at `:31`, and that one does not set it.
- **"Fixing it needs a new coalescing helper"** — false. `ToTranscripts` +
  `ThrottleTranscript` + `ToTranscriptDiffs` already compose into it.

## Dead code found on the way

`ToTranscriptDiffs(IEnumerable<StringDiff>)` (`:28-29`) — the overload that sets
`IsStable = true` — has **zero call sites**. Only two call sites exist for the
whole family: `TranslationsBackend.cs:208` and
`AudioStreamingBackend.ProcessAudio.cs:572`. Worth deleting, and worth noting that
its `IsStable = true` is what made the analysis above easy to get wrong.

## Risks and open questions

- **Time maps.** The `StringDiff` overload sets `LinearMapDiff.None`, so time maps
  stay empty through the round-trip. Re-diffing empty maps should be inert, but
  `Transcript.operator -` (`Transcript.cs:116`) is worth a look to confirm it does
  not misbehave on an empty `TimeMap`.
- **Visible UX change.** Translated text arrives in ~500ms steps rather than
  token-by-token. Probably smoother; still worth eyeballing once.
- **`Memoize`/`Replay` ordering.** The throttle sits upstream of `Memoize`, so
  replay consumers get the throttled sequence too. That is intended — the final
  state is complete — but it does mean a late joiner cannot reconstruct the
  fine-grained stream.
- **No test coverage.** Neither translation path is tested, and `ThrottleTranscript`
  itself appears untested. A test that feeds a known diff sequence through the
  round-trip and asserts (a) the concatenated output equals the input text and
  (b) the item count drops, would cover the risky part cheaply.
