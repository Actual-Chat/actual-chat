---
title: "Multi-provider transcription"
description: Plan for a pluggable transcription provider registry with per-language preference ranking, health-based auto-ejection, chat-context prefixes, and new/upgraded providers (Soniox, Google Chirp 3, OpenAI gpt-transcribe, Gemini 3).
---

# Multi-provider transcription

[[toc]]

## Status (2026-08-04)

Work lives on branch `feat/more-transcribers`, PR
[#4111](https://github.com/Actual-Chat/actual-chat/pull/4111).

### Shipped on the branch

- **Phase 0** - `Transcription.Contracts` / `Transcription.Service` projects,
  `TranscriberId` / `TranscriberInfo` / `TranscriberKind`, registry + ranking +
  per-language overrides, `ITranscriberSelector` with built-in failover.
  `TranscriptionEngine`, `ITranscriberFactory`, `TranscriberFactory` deleted.
- **Key consolidation** - every AI provider key now lives in
  `CoreServerSettings` (section `CoreSettings`): `OpenAIKey`, `OpenAIProxy`,
  `AnthropicKey`, `DeepgramKey`, `SonioxKey`. See the commit for the full
  old -> new env-var table.
- **Soniox** - `SonioxTranscriber` (stt-rt-v5, WebSocket) and
  `SonioxOfflineTranscriber` (stt-async-v5, REST), `SonioxTranscriptBuilder`,
  `SonioxContext`. `Language.IsoCode` added for ISO-639-1 mapping.

### Verified against the live API

| | |
|---|---|
| `SonioxOfflineTranscriber` | **Works.** Upload -> create -> poll -> transcript, real text returned. |
| `SonioxTranscriber` (streaming) | **Works.** Was failing with HTTP 408 - see below. |

Test entry point: `tests/Transcription.IntegrationTests/SonioxTranscriberTest.cs`
(self-skips when `CoreSettings__SonioxKey` is unset). The account is funded.

#### The streaming 408, resolved

Audio and transcription were fine all along: tokens came back correctly and
the whole file was pushed within ~5s. The 408 landed exactly 20s later - the
server's inactivity timeout - because the end-of-audio signal was sent as an
empty **Binary** frame, which Soniox ignores. The official example sends
`ws.send("")`, an empty **Text** frame; switching to that ends the session
cleanly with a `finished` response.

Two things came out of the same investigation:

- `enable_endpoint_detection` is now on. Without it nothing is finalized until
  the stream ends, so `SonioxTranscriptBuilder.Complete()` would drop the whole
  transcript as an unfinalized tail.
- Soniox got its own `Constants.Transcription.Soniox` block with **zero**
  silent prefix/suffix - it was borrowing Deepgram's. The end-of-audio frame
  finalizes everything, so padding only adds billed stream time.

### Provider economics (list prices, normalised)

| provider / mode | $/hour |
|---|---|
| Google STT V2 `long`, streaming | 0.96 |
| Deepgram Nova-3 mono streaming (PAYG) | 0.288 |
| Deepgram Nova-3 multilingual streaming | 0.348 |
| **Soniox real-time** | **0.12** |
| Soniox async | 0.10 |
| OpenAI `gpt-transcribe` (offline) | 0.27 |
| OpenAI `gpt-live-transcribe` (streaming) | 1.02 |

Context is billed separately as input text tokens: Soniox $4.00/1M real-time,
$3.50/1M async. Both work out to **~8 context tokens per second of audio** at
cost parity, which is what `MaxTokensPerAudioSecond = 8` encodes.

Measured from a real chat tail (133 transcribed messages): **mean 15 words,
median ~7**, so a typical message is 3-6s. That puts a "don't more than double
the cost" context budget at **~25-45 tokens** for streaming - hence
per-message author labels were dropped in favour of one `Participants:` line.
19% of messages (>=20 words) carry 56% of the words, so a duration threshold
on the retranscription pass drops ~80% of passes while keeping most content.

Soniox being 2.4x under Deepgram and 8x under Google means switching to it
pays for a few hundred tokens of context and still lowers the bill.

### Google model

Stays on `long`. `chirp_2` fails with *'The model "chirp_2" does not exist in
the location named "us"'* - Chirp needs a specific region (`us-central1`,
`europe-west4`, ...) and `CoreSettings:GoogleRegionId` is the `us`
multi-region shared with storage and the transcoder. Moving it is not a
transcription-local change; a Speech-specific region setting would be needed.
The model is now a constant and part of the recognizer id, so a future switch
provisions a fresh recognizer rather than reusing a stale one.

### Next, in order

1. **The context source.** Nothing populates `TranscriptionContext` yet, so
   Soniox's context fields go out empty - this is what makes its main
   advantage do anything. See section 4.
2. **The settings UI** (`TranscriptionEngineSettings.razor`) still offers only
   Deepgram and Google tiles, and knows nothing about pairs - so Soniox leads
   the ranking but cannot be selected.
3. **OpenAI `gpt-live-transcribe`** - deferred on economics: at $1.02/hour it
   is 8.5x Soniox and dearer than Google, so it only makes sense as a premium
   tier, not a default.
4. **Gemini** offline.
5. Google still prepends a **3s silent prefix** to every segment
   (`Constants.Transcription.Google.SilentPrefixDuration`), pushed at
   `Speed = 2`, so ~1.5s of wall-clock lead-in before real audio flows.

### Provider option decisions

- `language_hints`: use them; do **not** set `language_hints_strict`.
- `translation`: not now, possibly later.
- Transcriber-driven language detection: a transcriber that detects language
  could signal it early, letting common logic persist the detected language
  sooner than today's post-message detection. **Check the existing LLM-based
  `LanguageDetector` (`Chat.Service/Translation/`) first** - if it already
  covers this, postpone.

## Goal

Make transcription providers pluggable, so that:

1. A provider implements **streaming**, **offline**, or **both** — independently.
2. Providers are ranked by **preference per stage**, with **per-language
   overrides**.
3. A **text prefix** (recent chat messages + conversation summary + speaker
   names) is passed to the provider to improve accuracy.
4. A provider that starts failing or slowing down is **temporarily ejected**
   and traffic moves to the next-ranked one automatically.
5. When the streaming provider is good enough, the offline re-transcription
   pass is **skipped** rather than always run.

Target provider set after this work: **Soniox** (new, streaming + offline),
**Google** (upgraded to Chirp 3, streaming + offline), **OpenAI** (upgraded to
`gpt-transcribe`, plus new streaming `gpt-live-transcribe`), **Gemini 3**
(new, offline only), **Deepgram** (kept, streaming).

## Current state

### What exists

The streaming/offline split already exists, but asymmetrically.

- `ITranscriber` — `Streaming.Contracts/ITranscriber.cs:9` — push-style
  streaming: `Transcribe(audioStreamId, AudioSource, TranscriptionOptions,
  ChannelWriter<Transcript>, ct)`.
  Implementations: `GoogleTranscriber`, `DeepgramTranscriber`, `FakeTranscriber`
  (all under `Streaming.Service/Services/Transcribers/`), plus a
  **dead, unregistered** `DeepgramOfflineTranscriber.cs:12`.
- `IRefineTranscriber` — `Streaming.Contracts/IRefineTranscriber.cs:9` —
  one-shot: `Transcribe(AudioSource, TranscriptionOptions, ct) -> Transcript?`.
  Single implementation: `Chat.ML/OpenAITranscriber.cs:11`.
- `ITranscriberFactory.Get(TranscriptionEngine)` —
  `Streaming.Contracts/ITranscriberFactory.cs:8` — a `switch` over a
  two-value enum (`Api/Transcription/TranscriptionEngine.cs:6`).
- `TranscriptionOptions` — `Api/Transcription/TranscriptionOptions.cs:6` —
  `Language`, `DetectLanguage`, `LanguageCandidates`,
  `LanguageDetectedCallback`. Nothing else.
- Selection — `Streaming.Service/Backend/AudioStreamingBackend.ProcessAudio.cs:431`:
  auto-detect forces Deepgram, otherwise a per-user KVAS setting decides.
- Refine dispatch — `.ProcessAudio.cs:255-286` (`DispatchRefineTranscription`),
  consumed in `FinalizeTextEntry` (`.ProcessAudio.cs:565-608`) behind
  `TranscriptRefineExt.ShouldUseOriginalTranscript`.

### Gaps

| Gap | Detail |
|---|---|
| Provider identity is closed | `TranscriptionEngine` is a 2-value enum. It cannot name a provider that only exists at runtime, which is what user-supplied transcribers (§8) require. |
| Offline side has no registry | No factory, no engine enum, no id — `IRefineTranscriber` is a single optionally-registered singleton (`ChatServiceModule.cs:164-170`). |
| No ranking, no fallback | One `switch`. A provider that throws for a language (`DeepgramLanguage.cs:43-47`) fails the transcription instead of falling through. |
| No health signal | No error/latency tracking, no ejection, no recovery. |
| No context/prefix anywhere | Zero hits for `prompt`, `keyterm`, `keywords`, `speechContexts`, `adaptation` across the transcription path. `OpenAITranscriber.cs:44-47` doesn't set the SDK's `Prompt`. |
| Offline pass is live-only | `DispatchRefineTranscription` uses the **in-memory** `closedSegment.Audio` with a 20s budget. Nothing re-transcribes a stored entry later. |
| Language knowledge is scattered | Per-provider maps inside providers (`DeepgramLanguage.cs`, `GoogleTranscriber.cs:461-475`, `OpenAITranscriber.cs:62`), not declared. |
| UI is hardcoded | `Settings/TranscriptionEngineSettings.razor:12-49` — two literal tiles. |

Doc drift found while mapping: `docs/live-audio/05-server-publish-and-transcribe.md:224-232`
claims Google is the default (it is Deepgram) and names a method that no longer
exists. Fix as part of this work.

## Reuse

### Existing abstractions to reuse

- **Transcription core** — `Transcript`, `TranscriptDiff`, `StringDiff`,
  `LinearMap`, `LinearMapDtwRemapper` (+ `LinearMapAlignmentMode.RetranscribeSameAudio`),
  `TranscriptDiffStreamExt`, `TranscriberExt.Transcribe` channel adapter. No
  changes needed to any of these.
- **Accept/reject gate** — `Streaming.Service/Backend/TranscriptRefineExt.cs:5`
  (`ShouldUseOriginalTranscript`). Keep as-is; it becomes the gate for every
  offline provider, not just OpenAI.
- **Audio** — `AudioSource`, `OggOpusStreamConverter`, `WebMStreamConverter`,
  `ActualOpusStreamConverter`.
- **Stored audio read-back** — `Core.Server/Blobs/AudioSourceDownloader.cs:13`
  (`Download(blobId, skipTo, ct)`). This is the input for durable
  re-transcription; today only `ReplayStreamMuxer` uses it.
- **Batch job template** — `Chat.Service/Flows/ChatMediaIndexingFlow.cs:15`, a
  `BatchedIndexingFlow<ChatEntry, ChatEntryId>` with cursors, quota and
  self-resume. The re-transcription flow is the same shape.
- **Context sizing precedent** — `Chat.Service/Translation/Translator.cs:45,83,177-225`
  already passes prior messages as context, sized by
  `TranslationSettings.ContextMessageCount = 5` and
  `ContentMinLengthWithoutContext = 150` (`ChatSettings.cs:28-29`). Mirror this
  shape and these defaults rather than inventing new ones.
- **Conversation summary** — `LiveSessionSummary` (`Api/Live/LiveSessionSummary.cs:7`:
  `Title`, `Description`, `Summary`, `EndEntryLid`, `AuthorIds`), written by
  `Chat.Service/Flows/LiveConversationSummaryFlow.cs` via
  `ILiveSessionsBackend.UpdateSummary` (`Streaming.Contracts/ILiveSessionsBackend.cs:48`).
  This is the summary the prefix should carry — no new summarizer.
- **Composite identifier pattern** — `PrincipalId` + `PrincipalKind`
  (`Api/Identifiers/PrincipalId.cs:18`, `PrincipalKind.cs`) and `AuthorId`
  (`AuthorId.cs:18`) are the template for `TranscriberId`: `StringIdentifier`
  base, `IStringIdentifier<T>` `Format`/`Parse`, the
  `StringLike{Json,NewtonsoftJson,MessagePack,TypeConverter}` attribute set,
  `ParameterComparer(ByValueParameterComparer)`, and `ILruCache<string, T>`
  interning.
- **Language types** — `Language` (`Api/Identifiers/Language.cs:18`),
  `Languages` registry, `UserLanguageSettings.ListSpoken()`,
  `ChatUserSettings.Language`, `AudioSegmentLanguage`.
- **Lag signal, already measured** — `GoogleTranscribeState` /
  `DeepgramTranscribeState` both track `ProcessedAudioDuration`. That is exactly
  the "is this provider falling behind" signal the health monitor needs; no new
  instrumentation inside providers.
- **Redis + resilience infrastructure** (for §5) —
  `RedisModule.AddRedisDb<InfrastructureDbContext>` and the already-registered
  `RedisDb<InfrastructureDbContext>` (`AppServerModule.cs:209-217`);
  `Redis/RedisMeshLocks.cs` for the single-probe token;
  `Redis/RedisSlidingWindowRateLimiter.cs` as the reference for a Lua rolling
  window; `Redis/RedisRateLimitPolicy.cs` + `Core.Server/Resilience/RateLimitPolicy.cs`
  as the exact abstraction-vs-implementation split to copy.
  Note `Core.Server/Diagnostics/IHealthState.cs` is about **our own** CPU, not
  external services — related name, unrelated concern; don't overload it.
- **Reserved sharding scaffolding** — `HostRole.TranscriptionBackend`
  (`Core/Hosting/HostRole.cs:38`) and `ShardScheme.TranscriptionBackend`
  (`Backend/Sharding/ShardScheme.cs:34`). Reserved but unused.
  Correction to an earlier draft of this plan: `src/dotnet/Transcription.Contracts/`
  was **not** an empty shell assembly — it was a folder with a single
  `AssemblyAttributes.cs` and no `.csproj`, referenced by no solution or filter,
  so it never compiled and its `BackendService` attributes never took effect.
  Phase 0 creates the project for real and drops those attributes until there is
  an actual backend to declare.
- **ActualLab / Fusion** — `RetryDelaySeq` for the ejection backoff schedule,
  `BackgroundTask.Run`, `Memoize`, `ApiArray<T>` / `ApiSet<T>` for serializable
  collections, `StringIdentifier` base for the new id type, `StreamStore<T>`,
  `ConfigurationExt.Settings<T>` binding with `__` env overrides.
- **Test harness** — `TranscriberTestBase.GetAudio(...)`
  (`tests/Transcription.IntegrationTests/TranscriberTestBase.cs:10-36`) and the
  27 fixtures in `data/`; the `internal ProcessResponses` unit-test seam
  (`GoogleTranscriber.cs:253`); `UseFakeTranscriber`;
  `RefinePipelineDiagnosticTest.cs` as the A/B template.

### New components and placement

| Component | Placement | Rationale |
|---|---|---|
| `TranscriberId`, `TranscriberKind`, `TranscriberInfo`, `TranscriptionContext`, `TranscriberRanking`, `ITranscriber`, `IOfflineTranscriber`, `ITranscriberRegistry`, `ITranscriptionContextSource` | **`Transcription.Contracts`** (move the two existing interfaces here from `Streaming.Contracts`) | Shared, no server deps. `docs/plans/on-prem-instances.md` explicitly wants customers to plug in their own transcription providers — that requires the abstraction to sit in a contracts assembly, not in `Streaming.Service`. |
| All provider implementations | **new `Transcription.Service`** project | Five providers × two kinds does not belong in `Streaming.Service/Services/Transcribers/`, and `OpenAITranscriber` currently sits in `Chat.ML` purely by accident. Also unblocks `HostRole.TranscriptionBackend` later. |
| `TranscriptionContextSource` implementation | **`Chat.Service`** | Needs `IChatsBackend`, `IAuthorsBackend` and live-session summary access. The interface stays in contracts so `Streaming.Service` depends on the abstraction only. |
| `IExternalServiceHealth` + `ExternalServiceHealth` + trip logic | **`Core.Server/Resilience/`** | Not transcription-specific — see §5. Sits next to `RateLimitPolicy` / `LocalRateLimiter`, which follow the same abstraction-here / Redis-there split. |
| `RedisExternalServiceHealth` | **`Redis`** project, registered from `AppServerModule` | Mirrors `RedisRateLimitPolicy` exactly. `Core.Server` has no `ActualLab.Redis` reference and should not gain one. |

**Recommendation: shared placement for all of the above.** The on-prem plan and
the desire to A/B providers per language both push this into contracts.
Promoting later is harder than placing correctly now.

## Design

### 1. Provider identity

`TranscriberId` is a **parseable composite identifier**, not an opaque string —
because user-supplied transcribers (§8) are identified by the API key the user
enlists, so the id has to carry a source discriminator alongside a value.

Format: `<source>:<value>`, following the existing `PrincipalId` /
`PrincipalKind` pattern (`Api/Identifiers/PrincipalId.cs:18`,
`Api/Identifiers/AuthorId.cs:18`) — a `StringIdentifier` with a `Kind` parsed
out of the prefix, `IStringIdentifier<T>` for `Format`/`Parse`, the
`StringLike{Json,MessagePack,TypeConverter}` attribute set, and the
`ILruCache<string, T>` interning those files use.

```
soniox-stream    soniox-offline    google-stream    deepgram-stream
openai-stream    openai-offline    gemini-offline
u:<transcriberKeyId>
```

```csharp
public enum TranscriberSource { Builtin = 0, User }

public sealed partial class TranscriberId : StringIdentifier, IStringIdentifier<TranscriberId>
{
    public TranscriberSource Source { get; }
    public Symbol Key { get; }   // "soniox" — or the enlisted key's id
}
```

#### A `TranscriberId` names a *configuration*, not a vendor

This distinction has to be right from the start, because it is what makes
per-configuration failover possible. The same vendor can be present many times
over: our Soniox key, a user's own Soniox key, a second key for a different
billing account, a customer's on-prem endpoint. Those are **different
transcribers** — they fail independently, they get ejected independently, and
one is a valid fallback for another.

So the model is **driver × configuration**, not one singleton per vendor:

- A **driver** is the code that speaks a vendor's protocol — one
  `SonioxTranscriber` class, one `GeminiTranscriber` class.
- A **transcriber** is a driver bound to a configuration (key, endpoint, model,
  limits), carrying its own `TranscriberId` and its own `TranscriberInfo`.

`TranscriberInfo` therefore carries a `DriverId` alongside `Id`: `Id` is
`user:k7f2…`, `DriverId` is `soniox`. Registration moves from
`services.AddSingleton<SonioxTranscriber>()` to a driver registry plus a
configuration list, with instances created by a factory. Built-in
configurations come from settings at startup; user configurations are created
on demand and cached.

Expect the total configuration count to grow large once users can supply keys —
including **their own keys for built-in providers**, not just wholly external
transcribers. Nothing in the design may enumerate all configurations on a hot
path; see the scale notes in §3 and §5.

**No back-compat burden.** `UserTranscriptionEngineSettings` is rendered only
when `Features.IsIncompleteUIEnabled` is true
(`Settings/TranscriptionSettings.razor:74`) — it is an admin/testing affordance,
not a user-facing setting, so there is no installed base to migrate. Keep the
type name and its `MemoryPackUnion(7)` / `Union(7)` slot in
`Api/StoredSettings.cs:19,42` stable (changing a union index *is* breaking), and
replace its fields outright — see §7. The `TranscriptionEngine` enum is deleted.

> Why not an enum: user-supplied providers are identified by a key known only at
> runtime. An enum cannot express that, and neither can a flat string constant —
> hence the parseable composite.

### 2. Two interfaces, symmetric

```csharp
public interface ITranscriber                 // streaming — unchanged signature
{
    TranscriberInfo Info { get; }
    Task Transcribe(string audioStreamId, AudioSource audioSource,
        TranscriptionOptions options, ChannelWriter<Transcript> output, CancellationToken cancellationToken = default);
}

public interface IOfflineTranscriber          // was IRefineTranscriber
{
    TranscriberInfo Info { get; }
    Task<Transcript?> Transcribe(AudioSource audioSource,
        TranscriptionOptions options, CancellationToken cancellationToken = default);
}
```

`IRefineTranscriber` is renamed to `IOfflineTranscriber` — it is no longer only
"refine the tail of a live recording"; it also serves durable batch
re-transcription. A provider class may implement one or both.

`TranscriberInfo` is the declarative capability descriptor that ranking and
context-budgeting read:

```csharp
public sealed record TranscriberInfo
{
    public TranscriberId Id { get; init; } = TranscriberId.None;  // the configuration
    public Symbol DriverId { get; init; }                        // "soniox", "gemini", …
    public TranscriberKind Kind { get; init; }                   // see below
    public ApiSet<Language> Languages { get; init; } = new();    // "" / empty = any
    public ApiSet<Language> DetectLanguages { get; init; } = new();
    public bool IsLanguageDetectionSupported { get; init; }
    public TranscriptionContextKind ContextKind { get; init; }   // None | Terms | Text
    public int MaxContextChars { get; init; }
    public int MaxTerms { get; init; }
}
```

This kills the scattered per-language knowledge: `DeepgramLanguage`'s throw-on-unmapped
becomes a declared `Languages` set that the ranker filters on *before* dispatch.

#### `TranscriberKind` — which stages, and whether a second pass is needed

Rather than a stage flag plus a separate quality flag, one enum covers every
case — the "is an offline pass worth running" question only ever has a
meaningful answer *per stream transcriber*, so it belongs in the same value:

| `Kind` | Meaning | Pipeline |
|---|---|---|
| `StreamOnly` | Live-only, needs correcting | stream, then resolve and run the offline chain |
| `OfflineOnly` | Batch-only | never chosen for stage 1; usable as stage 2 and by the durable re-transcribe flow |
| `StreamSelfRefined` | Streams, and refines itself — typically by re-running a stronger model on a delay | stream; **skips the offline stage only when picked explicitly**. "Automatic" still runs the offline chain — measured on real recordings, a second pass fixes words the live stream fused |

The values are powers of two so a future filter can express a set, but the enum
is deliberately **not** `[Flags]`: a transcriber has exactly one kind.

The pipeline rule is then one line: run the resolved stream transcriber `S`;
if `S.Info.IsOfflinePassNeeded` is false, finalize; otherwise resolve the offline chain and
run it. This replaces the earlier `SkipOfflineAfter` settings list — it belongs
on the provider, not in config.

It stays **overridable**, per transcriber and per language, because "as good as
offline" is rarely uniform across languages — a model may be offline-grade for
English and clearly not for a long-tail language:

```json
"KindOverrides": { "soniox-stream/hy-AM": "StreamOnly" }
```

Note this is also what decides whether a *user-supplied* transcriber
(§8) needs a second pass — and for E2EE chats there is no second provider to
fall back to, so a BYO transcriber that isn't offline-grade simply gets no
refinement.

### 3. Registry and per-language ranking

Configuration-driven, so ranking is tunable per environment without a deploy:

```json
"TranscriptionSettings": {
  "StreamRanking":  "soniox,google,deepgram",
  "OfflineRanking": "soniox,gemini,openai",
  "StreamRankingOverrides":  { "ru-RU": "soniox,google", "en-US": "soniox,openai" },
  "OfflineRankingOverrides": { "en-US": "gemini,openai" },
  "KindOverrides": { "soniox-stream/hy-AM": "StreamOnly" }
}
```

Resolution, in `ITranscriberRegistry.Resolve(kind, language | candidates)`:

0. If the caller is scoped to a user-supplied transcriber (§8), that is the
   whole chain — no fallback to built-ins, ever.
1. Start from `Overrides[language]` if present, else the default ranking for the kind.
2. Drop entries not registered, or whose `Info.Kind` excludes the stage.
3. Drop entries whose `Info.Languages` excludes the language — or, in
   auto-detect mode, whose `IsLanguageDetectionSupported` is false or whose
   `DetectLanguages` does not cover the candidate set.
4. Drop entries the health monitor currently has **ejected** (§5).
5. Return the remaining list — an ordered fallback chain, not a single pick.

If every candidate is ejected, the least-recently-failed one is used rather than
failing outright — a degraded transcript beats none. The one exception is step
0: a user-supplied transcriber has no substitute, so if it is ejected the
recording fails rather than silently falling back to a provider the user did not
consent to.

The offline chain is resolved **only if** the stream transcriber that actually
produced the text has `Kind == StreamOnly` — see §2.

#### Failover is built into the chain, not into callers

Call sites must not walk the chain themselves. If they did, every consumer —
the live path, the refine dispatch, the durable re-transcribe flow, and every
future one — would have to reimplement "try, detect failure, report health,
advance, retry", and they would each get it subtly wrong.

Instead, resolution returns a **composite transcriber that *is* the chain**:

```csharp
public interface ITranscriberSelector
{
    ITranscriber GetStream(TranscriptionScope scope, TranscriptionOptions options);
    IOfflineTranscriber? GetOffline(TranscriptionScope scope, TranscriptionOptions options, TranscriberInfo streamUsed);
}
```

The returned object implements the ordinary `ITranscriber` / `IOfflineTranscriber`
interface and internally: picks the first healthy candidate, times the call,
reports the outcome to `IExternalServiceHealth`, and on failure advances to the
next candidate and retries — transparently to the caller. `GetOffline` returns
`null` unless `streamUsed.IsOfflinePassNeeded`, which is how the skip
decision reaches callers without any of them knowing the rule.

`AudioStreamingBackend.ProcessAudio.cs:431-436` therefore *shrinks*: the engine
`switch` and `TranscriberFactory.Get` disappear, replaced by one selector call.
The existing `TranscriberFactory` / `ITranscriberFactory` is deleted.

Failover semantics worth pinning down now:

- **Offline / batch** — unrestricted retry down the chain; the audio is at rest
  and nothing is user-visible until it succeeds.
- **Streaming, before first transcript** — free failover; the client has seen
  nothing yet.
- **Streaming, mid-session** — bounded (see §5): the memoized `AudioSource` is
  replayed into the next candidate, at most once per segment.
- **Ejection is reported, not just consumed** — a failover event is a health
  signal for the failed configuration, so a config that fails at the start of
  every recording ejects itself quickly rather than being retried forever.

### 4. The context prefix

```csharp
public sealed record TranscriptionContext
{
    public ApiArray<TranscriptionContextEntry> Prefix { get; init; } = [];  // recent entries
    public string Summary { get; init; } = "";                              // LiveSessionSummary
    public ApiArray<string> SpeakerNames { get; init; } = [];
}

public sealed record TranscriptionContextEntry
{
    public long AuthorLocalId { get; init; }
    public string AuthorName { get; init; } = "";
    public string Text { get; init; } = "";
}
```

Added to `TranscriptionOptions` as `Context`, defaulting to
`TranscriptionContext.None`. That record is the single chokepoint every provider
already receives, so no signature churn.

The prefix is **structured, not pre-flattened**: an ordered set of message-like
entries, each carrying the author's local id, display name, and text. Providers
differ in how they want context framed — a speaker-labelled dialogue for Gemini,
a flat blob for Soniox's `context.text`, names into `general.speakers` — and
flattening in the source would throw away the structure each of them needs.
`AuthorLocalId` rather than `AuthorId` because the whole context belongs to one
chat, so the chat id would repeat on every entry.

**Entry text is rendered plain.** Each `Text` is produced with
`MarkupFormatter.ReadableUnstyled` (`Api/Chat/Markup/Visitors/MarkupFormatter.cs:154`),
which combines `MentionMarkup.ReadableFormatter` — turning `@a:<chatId>:<localId>`
into `@Name` (`MentionMarkup.cs:21-25`) — with style tokens stripped. Raw ids in
a prompt are worse than useless: they are unpronounceable noise that can only
mislead the model, whereas the readable name is exactly the kind of proper noun
we want it to bias toward.

**Source.** `ITranscriptionContextSource.GetContext(chatId, authorId, repliedEntryId, budget, ct)`,
implemented in `Chat.Service`:
- Last *N* entries of the chat, newest last, with author names —
  matching `Translator`'s existing `ContextMessageCount = 5` default.
- The current `LiveSessionSummary` (`Title` + `Description` + `Summary`) when
  one exists; it is already maintained by `LiveConversationSummaryFlow`, so this
  is a read, not new work.
- `SpeakerNames` — the roster, which doubles as the keyterm list for providers
  that accept nothing richer.

Populated at `AudioStreamingBackend.ProcessAudio.cs:421-459`, where `chatId`,
`authorId` and `repliedEntryId` are already locals and `IChatsBackend` is
already injected (`AudioStreamingBackend.cs:35`).

**Budgets differ per stage**, as requested:

```csharp
public sealed record TranscriptionContextBudget(int MaxChars, int MaxEntries, bool IncludeSummary)
{
    public static readonly TranscriptionContextBudget Stream = new(1_500, 3, true);
    public static readonly TranscriptionContextBudget Offline = new(8_000, 10, true);
}
```

The effective budget is `min(stage budget, provider Info.MaxContextChars)`.
Rationale: the streaming budget is small because prompting adds latency and,
with some vendors, per-hour cost; the offline budget is large because it is off
the critical path and the whole point of the second pass is the extra context.

**Provider downcast** — one context object, three shapes:

| `ContextKind` | Providers | What is sent |
|---|---|---|
| `Text` | Soniox (`context`, ≤8k tokens), OpenAI (`prompt` + `keywords` + `languages`), Gemini (prompt) | `Summary` + `Prefix` rendered in the provider's preferred framing + `SpeakerNames` |
| `Terms` | Deepgram (`keyterm`, ~100 words) | `SpeakerNames` only, truncated to `MaxTerms` |
| `Terms` | Google Chirp 3 (`adaptation` phrase sets) | `SpeakerNames` only — **and see the caveat in §6** |

There is deliberately **no mined-terms field**. An earlier draft had one
(capitalised/rare tokens harvested from recent messages); it was dropped because
the harvesting heuristic is unreliable and the names we actually care about are
already in `SpeakerNames`. Term-only providers are a fallback tier anyway — the
providers we care about take the full prefix.

### 5. Health monitoring and auto-ejection

This is **not transcription-specific** and should not be built as such. We call
several external services that can degrade independently — ASR providers, LLM
providers (`ConversationSummarizer`, `Translator`), TTS, GCS — and the on-prem
plan multiplies them. So the mechanism lands in `Core.Server/Resilience/` as a
general external-service health API, and transcription becomes its first
consumer.

#### API

```csharp
public interface IExternalServiceHealth : IComputeService
{
    [ComputeMethod(AutoInvalidationDelay = 5)]
    Task<ExternalServiceHealth> Get(Symbol serviceKey, CancellationToken cancellationToken = default);
    [ComputeMethod(AutoInvalidationDelay = 5)]
    Task<ApiSet<Symbol>> ListEjected(Symbol serviceGroup, CancellationToken cancellationToken = default);

    ValueTask Report(Symbol serviceKey, ExternalCallResult result, CancellationToken cancellationToken = default);
    ValueTask<bool> TryAcquireProbe(Symbol serviceKey, CancellationToken cancellationToken = default);
}
```

`serviceKey` is a hierarchical `group/instance` symbol. Transcription keys it by
**configuration**, not vendor — `transcriber/soniox-stream`,
`transcriber/u:k7f2…` — so a user whose own Soniox key is revoked or
rate-limited ejects only their configuration, while ours keeps serving.

The group prefix is what `ListEjected` filters on, so the registry can fetch an
ejection set in one cached call rather than one call per candidate. **Groups
must stay small**, which means scoping them rather than using one global
`transcriber` group once configuration counts grow:

- `transcriber/builtin` — the platform's own configurations. Small, bounded,
  shared by every node; one cached `ListEjected` per node per 5s covers all of
  them.
- `transcriber/user/<userId>` — that user's configurations. A user has one or
  two, and the set is only consulted while that user is recording, so it is
  fetched per scope on demand and never enumerated globally.

Nothing in the resolution path may enumerate all configurations. Redis holds one
aggregate key per (configuration, kind) with a few-minute TTL, so only
*recently active* configurations occupy keys — idle ones cost nothing. Likewise,
Fusion's computed count per node is bounded by the configurations that node
actually touches, not by the number registered.

```csharp
public sealed record ExternalCallResult(
    bool IsSuccess, TimeSpan Latency, ExternalCallFailureKind Kind = ExternalCallFailureKind.None);
// FailureKind: None | Timeout | Transient | Fatal | Empty

public sealed record ExternalServiceHealth
{
    public ExternalServiceStatus Status { get; init; }   // Healthy | Degraded | Ejected | Probing
    public double FailureRatio { get; init; }
    public TimeSpan LatencyMean { get; init; }
    public TimeSpan LatencyBaseline { get; init; }
    public Moment? EjectedUntil { get; init; }
    public int EjectionCount { get; init; }
}
```

#### Storage and caching

**Redis holds the state; Fusion holds the reads.**

- Writes — `Report` accumulates into a per-node buffer and flushes to Redis
  about once a second per `serviceKey`, so a busy node does one round-trip per
  second per service rather than one per call. The flush is a single Lua script
  that atomically folds the delta into a rolling-window aggregate (success
  count, failure count by kind, latency sum, window start) with a few-minute
  TTL, and applies the trip rule in the same script — so `EjectedUntil` and
  `EjectionCount` are written exactly once cluster-wide, not once per node.
- Reads — `Get` / `ListEjected` are Fusion compute methods with
  `AutoInvalidationDelay = 5` (Fusion's unit here is **seconds** —
  `ComputeMethodAttribute.cs:48`; note some existing call sites in this repo
  pass milliseconds and are effectively "never", so don't copy them). Each node
  therefore hits Redis at most once per 5s per key no matter how many callers
  ask, and every local caller shares one cached value. Tune to 10s if Redis load
  warrants; the cost of acting on 5-second-stale health data is negligible next
  to the ejection cooldowns.

**Why shared rather than per-node:** a provider outage is global. Per-node
breakers would each rediscover it independently, each burn their own probe
traffic against a service that is already failing, and disagree about the
backoff stage. Shared state means the first node's failures protect the rest.

#### Trip rules and recovery

Evaluated inside the flush script, so all nodes agree by construction:

- **Healthy → Ejected** when, over the rolling window and past a minimum sample
  count (default 5): failure ratio > 30% (with `Fatal` weighted above
  `Transient`), **or** latency mean exceeds either an absolute per-service
  ceiling or 3× the recorded baseline. `Degraded` is the same condition at half
  the threshold — reported and logged, but still routed to.
- **Ejected** — the registry filters it out (step 4 of §3 resolution). Cooldown
  follows `RetryDelaySeq` (30s → 1m → 5m → 15m, capped), lengthening per
  consecutive re-trip and resetting after a clean interval.
- **Ejected → Probing** once `EjectedUntil` passes. `TryAcquireProbe` takes a
  short-lived lock via `RedisMeshLocks` so exactly **one** caller cluster-wide
  sends the probe; everyone else keeps treating the service as ejected until the
  probe's `Report` lands. Success closes and resets the backoff; failure
  re-ejects at the next cooldown step.

Per-service thresholds live in an `ExternalServiceHealthSettings` section keyed
by service key prefix, so a slow-by-design service (Gemini) gets a different
latency ceiling than a fast one (Soniox) without code changes.

#### Transcription's use of it

**Signals reported:**
- *Hard failure* — connection refused, auth error, 5xx, protocol/parse error,
  or an exception out of `Transcribe`.
- *Timeout* — no first transcript within a budget; for offline, exceeding the
  existing 20s `RetranscriptionTimeout`.
- *Elevated latency* — EWMA of time-to-first-transcript, and, for streaming,
  the **processing lag**: `wallClockElapsed - ProcessedAudioDuration`, which
  `GoogleTranscribeState` / `DeepgramTranscribeState` already track. Every new
  provider's state type must expose the same field.
- *Empty result* — a completed call that produced no text on non-silent audio
  counts as a soft failure.

Streaming lag deserves emphasis: `wallClockElapsed - ProcessedAudioDuration` is
the signal that catches a provider that is *up but falling behind*, which is the
failure mode that actually hurts a live transcript and which plain error-rate
monitoring misses entirely. Every new provider's state type must expose
`ProcessedAudioDuration` the way the two existing ones already do.

The registry consumes this through one cached `ListEjected` call per scope per
resolution — not one `Get` per candidate.

**Mid-stream failover.** A streaming provider dying mid-session is recoverable
here, unlike in most systems: the audio fan-out is already memoized
(`.ProcessAudio.cs:155-169`), so the `AudioSource` can be replayed from the
start of the segment into the next-ranked provider. The transcript is rewritten
rather than truncated — `TranscriptDiff` already expresses full replacement, and
the entry is still in streaming state, so the client handles it as a normal
update. Guard it: at most one mid-stream failover per segment, and only if the
segment is under some duration cap, to avoid pathological re-billing.

**Observability.** Log at Warning on every trip/untrip with the deciding metric;
counters per (provider, kind, language) for success, failure, first-token
latency, lag. These are also what settles the ranking empirically.

### 6. Providers

| Id | Streaming | Offline | Context support | Languages |
|---|---|---|---|---|
| `soniox` | **new** — `stt-rt-v5`, WebSocket, sub-200ms | **new** — `stt-async-v5` | `context` object: `general` + `terms` + `text`, ≤8k tokens (~10k chars) — richest of any provider, and identical on both stages | 60+, native mid-sentence code-switching |
| `google` | **upgrade** — per-language v2 recognizers → `chirp_3` | **new** — `BatchRecognize` | `adaptation` phrase sets (terms) | 24 GA + 77 preview |
| `openai` | **new** — `gpt-live-transcribe` | **upgrade** — `gpt-4o-transcribe` → `gpt-transcribe` | `prompt` (free text) + `keywords` + `languages` | 99+ |
| `gemini` | — | **new** — `gemini-3-flash` / `gemini-3-pro` | full prompt; effectively unbounded — the actual preceding messages, verbatim | 100+ |
| `deepgram` | keep — `nova-3` | — (delete the dead `DeepgramOfflineTranscriber`) | `keyterm`, ≤500 tokens (~100 words) | 30+ |

**Soniox** is the priority — it is the only provider giving the same rich
context object on *both* stages, at roughly ¼ the cost of the alternatives, and
early manual testing was strong. If its streaming quality holds up, it earns
`Kind = StreamSelfRefined` and the second pass disappears for most traffic —
a cost win on top of a quality win.

**Google upgrade caveat — this is a real correctness issue, not a detail.**
`GoogleTranscriber.cs:90-93` names its recognizer
`projects/{proj}/locations/us/recognizers/{languageCode}` and auto-creates
missing ones — i.e. **one recognizer shared by every user and every chat**.
Per-chat `adaptation` therefore *cannot* live on the recognizer; it must move to
the per-request `RecognitionConfig`. Moving to `chirp_3` also lets the
per-language recognizer zoo collapse toward a single auto-detecting recognizer.

**OpenAI upgrade details.** `OpenAITranscriber.cs:44-47` sets only `Language`
and `TimestampGranularities` — set `Prompt` too. Default model moves from the
legacy `gpt-4o-transcribe` to `gpt-transcribe`. Also fix
`GetSupportedLanguage` (`:62-63`), which truncates to the first two characters
and so collapses `zh-Hans` / `zh-Hant`.

### 7. Admin override UI

`Settings/TranscriptionEngineSettings.razor:12-49` is admin/testing-only — it
renders only under `Features.IsIncompleteUIEnabled`
(`Settings/TranscriptionSettings.razor:74`). So it can be reshaped freely; its
job is letting an admin pin a provider to test it, not letting end users choose.

It becomes a list rendered from `ITranscriberRegistry` with **two independent
overrides** — stream and offline — each defaulting to **Auto**, meaning "use the
ranking". Each entry shows its `Kind`, so the
consequence of a pick is visible: choosing an offline-grade stream provider
greys out the offline override, because no second pass will run.
`UserTranscriptionEngineSettings` keeps its type name and union slot and becomes:

```csharp
public sealed partial record UserTranscriptionEngineSettings : StoredSettings, ...
{
    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)] public string StreamTranscriberId { get; init; } = "";
    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)] public string OfflineTranscriberId { get; init; } = "";
}
```

Empty means Auto. Providers whose keys are unconfigured are not listed.

### 8. User-supplied transcribers (BYO) — out of scope

> **Not being built now.** This section exists so that the identifier shape, the
> driver × configuration split, the two-layer registry and the persisted types
> are ready for it — the bar is that adding BYO later requires no migration of
> anything already stored, not that any of it ships in this effort.

The endgame, and the reason §1 uses a parseable id: a user enlists their own
transcriber by registering an **API key**, and that key *is* the configuration's
identity — `u:<transcriberKeyId>`.

Two flavours, and the design must cover both:

1. **Own key for a built-in provider** — the user supplies their own Soniox or
   Gemini key. Same driver, different configuration. This is the cheap, common
   case and it needs no new transport at all — it falls straight out of the
   driver × configuration model in §1.
2. **Wholly external transcriber** — the user runs their own service. This is
   what needs the attach modes below.

**Two attach modes** for flavour 2, both keyed the same way:

1. **Reverse / provider-initiated** — the transcriber connects *to our API*
   authenticating with its key, and we invoke it over that connection using
   Fusion's server-to-client RPC. No inbound URL, no firewall holes; the
   transcriber can run on the user's own laptop or on-prem box.
2. **Forward / server-initiated** — the transcriber announces a URL and our
   servers open an RPC connection to it. Suitable for a customer-hosted service
   with a stable address.

**Why this matters for E2EE.** `docs/plans/e2ee.md` currently makes E2E chats
text-only and blocks audio recording outright (Phase 8.1–8.2), because voice
would mean shipping plaintext audio through our servers to a third-party ASR
vendor. A user-supplied transcriber moves transcription outside our trust
boundary, so it is the mechanism that could **lift** that restriction — voice in
E2E chats becomes possible exactly when the user brings their own transcriber.
That also retires the privacy risk logged below for the context prefix: for a
BYO transcriber, the prefix never leaves the user's own infrastructure.

**What this demands of the registry.** Built-in providers are DI singletons
resolved at startup; BYO providers are **dynamic and scoped** — registered,
revoked, and rotated at runtime, and visible only to the enlisting user (or
place). So `ITranscriberRegistry` needs two layers: a static built-in set and a
dynamic per-scope set, with resolution consulting the scope first (step 0 of §3)
and never falling back across the boundary. Health tracking works unchanged —
`u:<key>` is just another `serviceKey` — with the caveat that a BYO
transcriber's ejection means "recording unavailable", not "use someone else",
so it needs to surface to that user rather than being silently absorbed.

This section is **design intent, not phase-1 scope.** It is recorded here so the
identifier shape, the registry's two-layer structure, and the `TranscriberKind`
semantics are right the first time; the actual BYO implementation lands after
the built-in providers do, and alongside E2EE.

## Phases

Phase 0 is a prerequisite; 1–5 are largely independent and can be reordered.

| # | Scope | Ships |
|---|---|---|
| **0** | Contracts move to `Transcription.Contracts`; new `Transcription.Service` project; parseable `TranscriberId`, `TranscriberInfo` (incl. `TranscriberKind`), `IOfflineTranscriber` rename, two-layer registry + ranking + settings; existing three providers re-registered through it. | No behavior change. Pure refactor. |
| **0b** | `IExternalServiceHealth` in `Core.Server/Resilience/` + `RedisExternalServiceHealth` in `Redis`. Reporting wired from the transcription call sites; ejection **observe-only** (logs and metrics, never filters). | Independent of the rest — reviewable and shippable on its own, and lets us watch real trip rates before they can affect routing. |
| **1** | `TranscriptionContext` + `ITranscriptionContextSource` + budgets; wired into OpenAI (`Prompt`) and Deepgram (`keyterm`). | First quality win on existing providers. |
| **2** | **Soniox** streaming + offline, full context on both. | The main event. |
| **3** | Google → Chirp 3, per-request `adaptation`, `BatchRecognize` offline. | |
| **4** | OpenAI → `gpt-transcribe` + new `gpt-live-transcribe` streaming. | |
| **5** | Gemini 3 offline. | |
| **6** | Ejection switched from observe-only to enforcing (registry filtering, half-open probes, mid-stream failover); durable `ChatEntryRetranscribeFlow` over stored audio; `StreamSelfRefined` skip logic; admin override UI. | |
| **7** | User-supplied transcribers (§8) — key enlistment, both attach modes, dynamic scoped registry layer. | **Out of scope for this effort.** Gated on E2EE; earlier phases only have to not preclude it, and must not persist anything that would need migrating. |

## Testing

- **Unit, no network** — table-driven ranking resolution (overrides, capability
  filtering, detect-mode, all-ejected fallback); context builder budget
  truncation. These carry most of the risk and none of the cost.
- **Health service** — the trip rule must be a pure function over the aggregate
  (counts, latency sums, window start, ejection count) so it is testable without
  Redis: thresholds, minimum sample count, cooldown escalation and reset, and
  the `Degraded` band. Then one integration test against a real Redis for
  atomicity — concurrent `Report` flushes from two "nodes" must produce exactly
  one ejection and one `TryAcquireProbe` winner.
- **Provider parse tests** — follow the existing seam:
  `Transcription.UnitTests/GoogleTranscriberTest.cs:9-19` drives
  `internal ProcessResponses(...)` with canned responses and no audio. Add the
  equivalent for Soniox (canned WebSocket frames) and the OpenAI streaming path.
- **Integration** — extend `TranscriberTestBase`; keep every provider test
  `Skip`-gated for manual runs as today, since they need real keys.
- **A/B harness** — generalize `RefinePipelineDiagnosticTest.cs` into a matrix
  run: every registered provider × the `data/` fixtures × language, printing WER
  and latency. **This, not vendor benchmarks, is what sets the default ranking**
  — the published numbers are marketing and disagree with each other.
- **Regression** — `UseFakeTranscriber` and the existing retranscribe flow tests
  (`RetranscribeTranslationFlowTest`, `RetranscribeNotifyFlowTest`,
  `RetranscribeDisabledNotifyFlowTest`, `StreamingEntryNetworkLossTest`) must
  keep passing unchanged through phase 0.

## Risks and open questions

1. **`Languages` registry is too small.** `Api/Identifiers/Languages.cs` has 41
   entries with reference-equality `Language`. Soniox (60+), Google Chirp 3
   (100+) and Gemini (100+) all exceed it, so `Info.Languages` cannot express
   their real coverage. Decide whether to expand the registry or to treat an
   empty `Languages` set as "any". Expanding touches the language-picker UI and
   `UserLanguageSettings`.
2. **Privacy.** Sending chat context to a third-party ASR vendor widens what
   leaves our infrastructure — materially more than sending audio alone. Needs a
   per-chat or per-place opt-out. The structural answer for E2E chats is
   user-supplied transcribers (§8), not a policy toggle: with BYO, neither audio
   nor prefix leaves the user's own infrastructure. Until §8 exists, E2E chats
   simply have no voice, as `docs/plans/e2ee.md` already specifies.
3. **Cost.** Prompting is a billed add-on with several vendors; Gemini 3 Pro is
   ~$1.09/hr against Soniox's ~$0.12/hr. `StreamSelfRefined` is the main lever —
   validate early that Soniox streaming really removes the need for a second
   pass, because that assumption carries a large share of the cost model, and
   the flag's value per language should come from the A/B harness rather than
   from a vendor's claim.
4. **Union slot stability.** `UserTranscriptionEngineSettings` may change its
   fields freely (admin-only, no installed base), but its
   `MemoryPackUnion(7)` / `Union(7)` index in `Api/StoredSettings.cs:19,42` must
   not move — that index is shared by every stored setting type.
5. **Mid-stream failover re-billing.** Replaying a segment into a second
   provider pays for the same audio twice. Capped as described, but worth a
   metric.
7. **Configuration count growth.** Today there are ~5 configurations; once users
   can supply keys there may be many thousands. The design keeps every hot path
   free of global enumeration (scoped `ListEjected`, TTL'd Redis keys, per-node
   computed sets bounded by actual traffic) — but this is an invariant to hold,
   not a one-time fix. Any future "list all transcribers" call is a regression;
   the admin UI lists **built-in** configurations only.
6. **Google recognizer sharing.** See §6 — per-request adaptation is mandatory,
   not optional, given the shared recognizer.
