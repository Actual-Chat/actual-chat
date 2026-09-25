# Speech coach v1 — design

Issue: #4829. Mocks: [Coach tab](https://www.figma.com/design/EJxXOzem02zhRvuqsJIool/Actual-chat-Desktop?node-id=27796-131026),
[hints and inline marking](https://www.figma.com/design/EJxXOzem02zhRvuqsJIool/Actual-chat-Desktop?node-id=29066-173115).

## Problem

Every voice message a user sends is already transcribed with word timings, yet the user gets no
feedback on how they speak. Non-native professionals and the users Poised leaves behind
(shutdown 2026-10-08) want to see their filler words, pace, weak words and progress, from real
conversations rather than practice sessions. Rivals (Poised, Yoodli, Speeko, Orai, Microsoft
Speaker Coach, the call-analytics tools) all do this for English only, from a meeting overlay or
a recording, and none show the flags inside the conversation itself.

The feature ships behind a feature flag. Whether it is switched on depends on the two-week
Poised test; coaching cannot be promised in copy until then.

## Goals

- Feedback on the user's **own** voice messages, in **any language we transcribe**.
- Four surfaces: the Coach tab, the Trends view, live tips after a finalized voice message,
  and inline marking of fillers and weak words in the user's own messages.
- Metrics computed in the background for everyone, so history exists on the day the tab opens
  and no backfill job is needed. Live tips and inline marking are opt-in.
- A design that lets the v1.1 LLM metrics (tone, then grammar, hedging, a "one thing to work
  on" summary) slot into the same pipeline without rework.
- Removable: turning the flag off leaves no visible trace; deleting the feature is two folders,
  a handful of tables and a migration.

## Non-goals

- Acoustic metrics: energy, pitch, monotone, pronunciation, articulation (Tier 4).
- Profanity masking, replay bleeps, live bleeps or the video "$%#?!" bubble. Profanity is only
  counted. Masking is a separate feature and would be display-only.
- Cross-user ranking ("better than 30% of users"). No rival ranks users against each other; the
  score is compared with the user's own trailing average. This also removes the profiling
  question from the privacy check.
- Practice or roleplay sessions, a conversational coach, custom filler lists, a "meters to
  watch" picker. Any of these can come later.
- Analysing other participants' text. Their entries contribute durations only.
- Backfilling history recorded before the flag was turned on.

## Decisions taken during brainstorming

| Topic | Decision |
|---|---|
| v1 metrics | pace, pauses, turn-taking, patience, interruptions, longest monologue, questions, sentence length, vocabulary variety, repetition, fillers (two classes), weak words with synonyms, profanity count, a 0-100 score |
| v1.1 slot | tone per message first; grammar, hedging and the "one thing to work on" summary reserved in the same tagger call |
| Languages | any transcribed language; word-level metrics come from an LLM tagger, not curated lists |
| Computation | one tagger prompt, two schedules: immediately per message for opted-in users, per conversation at close for everyone else; an LLM-generated per-language lexicon as the cost lever and offline fallback |
| Activation | metrics for everyone in the background; Coach tab visible to all; tips and marking behind the Coaching toggle |
| Score | weighted formula over the metrics, compared with the user's own trailing 30 days |
| Placement | no new projects: a `Coach` folder in Chat.Service (entry-level, chat-sharded) and a `Coach` folder in Users.Service (per-user rollups, settings, tips) |
| Extras taken from rivals | questions asked, patience, interruptions, repetition, jump-to-audio from a chip |

## Architecture

### Ownership and sharding

Entry-level data is chat-scoped, like entry languages, translations and summaries already are,
and lives in the Chat DB keyed by author id and entry lid. Per-user data lives in the Users DB.
The seam is one one-way record per analysed entry, from the chat shard to the user shard, in
the shape usage stats already uses (an append-only log deduplicated by source id, day rows
rebuilt from the log).

| Side | Folder | Owns |
|---|---|---|
| Chat.Service | `Coach/` | entry analysis rows with spans, per-conversation turn-taking rows, the tagger call, the marks read for inline marking |
| Users.Service | `Coach/` | per-entry log, day rollups, score, settings, tips, the lexicon cache |
| Chat.ML | | the speech tagger and its stub |
| Api / Api.Contracts | `Chat/Coach`, `Users/Coach` | models and the two frontend contracts |
| UI.Blazor.App | | Coach panel, Trends, tip bar, marking overlay |

Why not one owner in Users: the entry-changed event, the entry text and time map, the
conversation range (for turn-taking) and the chat tiles the marks are rendered into are all on
the chat shard. Keeping entry rows there makes analysis, cleanup and the marks lookup local, and
follows the rule that chat-scoped contracts key by author id, not user id. Why not one owner in
Chat: "today across all chats" and the tips need the user shard.

### Pipeline

**Trigger A — a voice entry of mine is finalized.** `ChatEntryChangedEvent`, filtered with the
same detection usage stats uses (`UsageEventSource.FromEntryChange`: a text entry with audio whose
end time was just set). The handler issues a durable `CoachAnalysis_AnalyzeEntry` self-command,
so redelivery and crashes cannot lose or double-count work. The command:

1. Reads text, time map and language from the entry (all carried by the event or read locally).
   Entries without text (transcription off) are skipped. A missing time map yields pace from
   duration and no pause metrics; the row records which inputs were missing.
2. Computes the code metrics (see *Metrics*).
3. Reads the author's `UserCoachSettings`. Coaching on: calls the tagger now, stores the row as
   *tagged*. Coaching off: stores the row as *pending*, no LLM call.
4. Emits the per-entry record to the user shard (`CoachBackend_Record`), which appends it to the
   log, rebuilds the day row and, for opted-in users, runs the tip rules.

**Trigger B — a conversation closes.** `ConversationChangedEvent` for a created conversation
marks the point where the split flow has fixed its entry range. For every author in the range
who is a tracked user the handler issues `CoachAnalysis_AnalyzeConversation`, which:

1. Tags that user's pending rows in the range in one batched call, chunked above a word limit,
   and re-emits their records (same source id, so the log replaces rather than duplicates).
2. Computes the per-conversation row from timings only: own speech time over everyone's speech
   time, own and total turns, longest monologue (longest run of own consecutive entries), patience
   (mean gap between another author's entry ending and the user's next entry starting, counting
   only gaps under a cap), interruptions (own entries starting before the previous other-author
   entry ended). No other author's text is read.
3. Emits a per-conversation record to the user shard.

Turn-taking and its siblings are therefore conversation-delayed even for opted-in users. They
are trends, not tips, which matches the mocks.

**Re-tagging.** Every row stores the tagger prompt version. A prompt bump re-tags only rows below
it, via the same conversation command, so there is never a full backfill. A nightly flow
re-batches rows still pending after a day and rows below the current prompt version, capped per
user per run.

**Tips**, opted-in users only, evaluated after each immediate record on the user shard:

- Pace: this entry over 170 wpm or under 100 wpm with at least 30 words.
- Filler: one filler's count today crossed 10, then every further 10.
- Weak word: one word's count today crossed 10, then every further 10, and the tagger returned
  synonyms for it.
- At most one tip per evaluation, word tips winning over pace; none inside the Display
  frequency window (last tip time lives with the tip state, not the settings). The tip is stored
  under the user with the chat it belongs to; `ICoach.GetPendingTip` exposes it until dismissed
  or replaced.

### LLM work

Two jobs, both through the existing keyed `IChatCompletionService` (Semantic Kernel, OpenAI,
Redis token-bucket rate limiter). Coach gets its own service key so its traffic cannot starve
translation or summaries.

**Job 1: the speech tagger** (`ISpeechTagger` in Chat.ML, prompt file `coach-tag-speech.md`).

- Input: the user's own message text, or all their messages of one conversation, plus the
  entry language when known.
- Output, JSON: one item per hit with `class`, `word`, `occurrence` (1-based index of that word
  in the text) and, for weak words, two or three `synonyms` in the same language. Classes in v1:
  `filledPause`, `filler`, `weak`, `profanity`. v1.1 adds a `tone` field (label + score) and a
  `grammar` class with a `correction` field. Same call, same prompt file.
- Our code locates every item in the text (`SpanLocator`, Core) and drops what it cannot find,
  so LLM offsets never reach storage. A response failing schema validation counts as a failed
  call. Temperature 0, lowest reasoning effort the model accepts
  (`OpenAIModels.GetLowestReasoningEffort`), JSON mode.
- Model: the fast tier (`gpt-5.6-luna`, the one realtime translation and language detection
  use), configurable in `CoachSettings`.

**Job 2: the per-language lexicon** (`coach-lexicon.md`). Filler, weak-word and profanity lists
for one language, generated the first time the language appears, cached in `CoachLexicons`,
regenerated only on a prompt-version bump. Model: the quality tier (`gpt-5.6-terra`); it runs a
few dozen times ever. With the tagger switched off (cost floor), code matches against these lists
and produces rows in the same shape, without synonyms.

**Not LLM work:** pace, pauses, turn-taking and its siblings, questions, sentence length,
vocabulary, repetition, the score, the rollups, and the tip text (templates over rollup numbers).
Language detection already exists per entry.

Cost shape per call:

| Path | Calls | Tokens per call (rough) |
|---|---|---|
| Immediate (opted-in) | one per finalized voice message | 400 prompt + ~200 text + ~100 output |
| Batch (everyone else) | one per user per conversation, chunked at ~2000 words | 400 prompt + up to ~3000 text + ~500 output |

Volume, production, measured 2026-09-25 from `usage_events_recorded_total{kind="Speech"}` (one
event per finalized voice entry; the counter is two days old, so re-check after a week) and
`app_audio_stream_count`:

| Figure | Value |
|---|---|
| finalized voice entries per day | 500–800, bursty (single hours of 300–400) |
| active users per day | ~240 |
| mean concurrent audio streams | ~1 (range 0.1–3.5), i.e. ~20 audio-hours/day |
| text messages per day, 14-day mean | ~450 |

Worst case, everyone on the immediate path: 800 calls × ~700 tokens ≈ 0.6 M tokens/day. The
batch path carries the same text in fewer calls, ≈ 0.3 M tokens/day. Both are far under the
2 M tokens/minute budget of the existing rate limiter, so at today's volume the caps exist for
abuse, not for budget. The lexicon fallback matters only if volume grows by two orders.

### Metrics

Words: whitespace-separated tokens with at least one letter or digit, using the same regex as
`PlayableTextMarkup.Words`, so server spans and client word indices agree by construction.
Sentences: split on terminal punctuation and line breaks (the offline pass punctuates). Pause: a
gap of at least 1 s between consecutive words inside one entry, leading and trailing silence
excluded. Languages without whitespace word boundaries (ja, zh, th) get no pace, sentence,
vocabulary or repetition numbers in v1; the tab says so.

| Metric | Definition | Bands |
|---|---|---|
| Talking pace | words / speech time (duration − pauses); a window is total words over total speech time | slow < 110, medium 110–160, fast > 160 wpm |
| Pauses | pauses per minute, mean pause length | informational |
| Filler rate | (filled pauses + lexical fillers) / words | good < 3%, medium 3–6%, high > 6% |
| Weak-word rate | weak words / words | good < 4%, high > 6% |
| Repetition | same word twice in a row / words; also emitted as a span class | good < 4% |
| Profanity | count and rate | informational |
| Questions | sentences ending in "?" per conversation | informational |
| Sentence length | words per sentence | short < 8, medium 8–20, long > 20 |
| Vocabulary variety | distinct / total words per entry (entries ≥ 20 words), words-weighted over the window | informational |
| Turn-taking | own speech time / all speech time per conversation, judged against fair share 1/participants | low < 0.5×, balanced, high > 1.5× fair share |
| Patience | mean gap after another author stops before the user starts | 0.5–1.5 s balanced (rivals' target) |
| Interruptions | own entries starting before the previous other-author entry ended | informational |
| Longest monologue | longest run of own consecutive entries | flag > 90 s |

All bands live in `CoachSettings` with per-language overrides for pace (defaults equal the
English band until Russian is measured). The same bands drive tab labels, trend colours and tip
rules, which fixes the mock where 145 wpm is both "recommended" and "too fast".

**Score, 0–100.** Each scored metric maps to a sub-score: 100 inside its good band, falling
linearly to 0 at twice the band-edge distance (fillers: 100 at ≤ 3%, 0 at ≥ 9%). Weights:
fillers 30, pace 25, weak words 20, turn-taking 15, sentence length 10. Unweighted in v1: pauses,
vocabulary, repetition, profanity, questions, patience, interruptions, monologue. A metric with
no data drops out and its weight spreads over the rest. The score shows only with ≥ 200 words
in the window. The badge compares the window with the trailing 30 days ending yesterday and
shows up/down only for a difference ≥ 3 points.

**Coverage.** Filler and weak-word numbers cover tagged entries only. The tab shows the tagged
share when below 100% ("8 of 10 messages analysed").

### Storage

Chat DB, migration in `Chat.Service.Migration`:

- **CoachEntries** — key (chat id, entry lid), author id, user id (for the emitted record), day,
  begins-at, language, duration ms, speech ms, words, sentences, distinct words, pauses, pause
  ms, questions, repetitions; spans JSON (class, word, start, length, synonyms); denormalised
  per-class counts; tag state (pending / tagged / skipped), prompt version, tagged-at, missing
  inputs. Index: (chat id, author id, entry lid) for the marks read.
- **CoachConversations** — key (chat id, author id, conversation start lid), day, own speech ms,
  total speech ms, own turns, total turns, longest monologue ms, responses, gap ms sum,
  interruptions, participants.

Users DB, migration in `Users.Service.Migration`:

- **CoachEvents** — the log: user id, source id (entry id or conversation id), kind, moment, the
  counts and per-word map from the record, spans without synonyms (for jump-to-audio). Deduped by
  (user id, source id); a re-emit replaces.
- **CoachDays** — key (user id, day), version, sums of every metric above, tagged-entry count, a
  per-word JSON map of filler and weak-word counts. Rebuilt from CoachEvents on every write.
- **CoachLexicons** — key language, prompt version, three JSON lists.

No tables for settings or tips: `UserCoachSettings` (stored-settings record: coaching on, live
tips on, tip interval) and `UserCoachTip` (kind, chat id, template values, dismissed, last tip
at) live in the user's key-value store under separate keys, so a settings edit does not
invalidate the tip and the reverse.

**Deletion.** A removed entry deletes its row (same entry event) and re-emits a removal record
so the day rebuilds. Chat removal takes the chat-side rows with the chat. Account deletion removes
the user-side rows through the existing account-removal path. All rows are derived personal
data; that is what the privacy check covers.

### Client API and surfaces

Contracts follow the usage pattern (`Api.Contracts/Users/IUsage.cs`): session-scoped compute
services, stored settings through the key-value client.

- **`ICoach`** (Users): `GetOwnSummary(session, window)` → score, comparison badge, metric values
  with band labels, chips with counts, coverage; `ListOwnDays(session, range)` for Trends;
  `GetPendingTip(session)`; `ListOwnOccurrences(session, word, window)` for jump-to-audio;
  commands `Coach_DismissTip` and admin-only `Coach_RebuildOwnDays`.
- **`IChatCoach`** (Chat): `GetOwnMarks(session, chatId, lidRange)` → spans per entry lid, only
  for lids authored by the caller, invalidated per chat on every entry-row write.
- **Feature flag, two layers.** The existing flags are the wrong kind: "incomplete UI" is
  admins plus a per-user setting, "experimental feature" is admins plus a focus group plus a
  per-user setting, and neither is visible to the backend. So: (1) a **server master switch**
  `CoachSettings.IsEnabled` (configuration, per environment, like `IsSummarizationEnabled`) —
  off means both event handlers return early and `ICoach`/`IChatCoach` report the feature
  disabled; on in dev, off in prod until the Poised decision. (2) a **client flag**
  `Features_EnableSpeechCoach` (`FeatureDef<bool>, IClientFeatureDef`) computed as the server
  switch AND a rollout rule: admins plus the focus group during the test, everyone at launch.
  The UI reads only this flag and hides every surface when it is false.

Surfaces:

1. **Coach tab** — a second side-panel mode behind the mock's "Chat | Coach" toggle, keyed by
   mode (survives chat switches). Score card with the badge, three toggles, window selector,
   one row per metric with value, band label and chips. Metrics without data in the window say
   so. Mobile: the same component as a full-screen page from the chat header.
2. **Trends** — second screen in the same panel/page: composition donut, weekly pace bars from
   the day rows, turn-taking with its band, patience, interruptions, longest monologue. The tone
   chart from the mock appears only with v1.1; v1 hides the slot.
3. **Tip bar** — a dedicated component in the chat column above the composer (not the global
   toast), bound to the pending tip, rendered only in the tip's chat on every open client of the
   user. Three templates (pace, filler, weak word); chips are display-only.
4. **Inline marking** — `PlayableTextMarkupView` gets a second per-word class from the marks
   lookup: strikethrough for filled pauses and fillers, dotted underline for weak words and
   repetitions, nothing for profanity in v1. Spans map to word indices on the client once per
   entry. One batched call per visible tile, own entries only, only with coaching on.
5. **Jump-to-audio** — a chip click in the tab lists the latest occurrences; each navigates to
   the entry and plays from that word through the existing play-from-word path.

Mock corrections carried into implementation: "sharpness of your writing" → speech; the pace
toast's band and the Coach tab's pace label use the same band; the header/body mismatches in the
exploratory toast variants are ignored; the dashed sentence underline in the desktop mock has no
defined meaning and is not implemented (open question for design).

## Failure handling and cost controls

- Tagger failure or timeout: the row keeps the code metrics, tag state stays pending; the
  conversation batch and the nightly flow retry. The tab never waits on the LLM.
- Garbage output: unlocatable items dropped, schema failures treated as call failures.
- Conversation re-split: the conversation row is keyed by start lid; a changed event for the
  new range recomputes it and deletes rows for start lids that no longer exist.
- Entry edited or re-transcribed: the entry is re-analysed and re-tagged (text changed).
- Redelivery: keyed, idempotent writes on the chat side; log dedupe on the user side.
- Per-user daily cap on tagger calls on both paths; over the cap, rows stay pending.
- Global switches: immediate path off (everyone batched, tips from the batch); tagger off
  (lexicon matching). Own rate-limiter key.

## Rollout and privacy

Flag off in production until the Poised test decides; on in dev from the first PR. Before the
production flag goes on, a privacy check covering: derived personal data and its deletion
paths, the tagger provider's data terms for transcript text, and the fact that other
participants contribute durations only. The percentile ranking is out, so no cross-user
profiling is involved.

## Risks

1. **Filler survival.** If the transcriber drops "um"/"эээ", the filled-pause class is empty and
   the headline metric is lexical fillers only. Soniox has no verbatim option and its benchmarks
   ignore fillers; Deepgram keeps seven English tokens; ElevenLabs Scribe is verbatim by default
   and multilingual. Spike before implementation: real audio with deliberate fillers through the
   current pipeline, comparing realtime and refined text. Fallbacks, in order: analyse the
   realtime text when the refined one strips fillers; re-transcribe own audio with the verbatim
   transcriber for opted-in users.
2. **LLM tagging quality across languages** is unproven anywhere. The golden set gates prompt
   edits; the lexicon fallback bounds the damage.
3. **Bands are borrowed.** Pace bands come from English-language products; Russian and others
   need measurement before the labels are trusted. Per-language overrides exist for that.
4. **Cost** scales with voice volume on the batch path. The caps and switches above are the
   stops; today's volume (see *LLM work*) makes it negligible.

## Testing

- Metric calculators: pure functions, unit-tested with hand-built entries: pace with and without
  pauses, sentence splitting, repetition, questions, vocabulary threshold, the no-spaces skip,
  every band edge, the sub-score mapping and weight redistribution.
- Turn-taking, patience, interruptions, monologue: a synthetic two- and three-author timed
  conversation with overlaps and gaps.
- Tagger contract: a stub tagger as the summarizers use; `SpanLocator` tested with a wrong
  occurrence and a word not in the text; schema-failure path.
- Golden set: a few English and Russian transcripts with hand-tagged fillers and weak words,
  run against the real model under `[LocalFact]` (skipped on the build agent). Gates prompt
  edits, not CI.
- Backend integration: finalize a voice entry for an opted-in user, assert the entry row, the
  log record, the day row and a tip; close the conversation, assert the conversation row and the
  batch tagging of a non-opted-in user's pending rows.
- UI: marks overlay renders classes for own entries only; tip bar shows in the right chat and
  clears on dismiss.

## Reuse

Existing abstractions:

| Need | Piece |
|---|---|
| finalized-voice-entry detection | `UsageEventSource.FromEntryChange` (Users.Contracts, already referenced by Chat.Service) |
| events | `ChatEntryChangedEvent`, `ConversationChangedEvent`, `LiveSessionEndedEvent` |
| word split with timings | `PlayableTextMarkup.Words` and its regex |
| time map | `LinearMap` (Core) |
| log + day rollup | `UsageBackend`, `DbUsageDay`, `UsageBackend_RebuildDays` |
| LLM call | keyed `IChatCompletionService`, `ThreadInsightExtractor` options pattern, prompt files, `OpenAIModels.GetLowestReasoningEffort`, `TokenEstimator`, `RedisTokenBucketRateLimiter` |
| background sweeps | `Flow<T>` (Chat.Service/Flows) |
| settings | `StoredSettings` + `IHasKvasKey` (`UserReplaySettings`) |
| feature flag | `ExperimentalFeature` (`Features_EnableTemplateChatUI`) |
| side panel | `ChatSidePanel` tabs, `ContentSwap` |
| play from a word | `PlayableTextMarkupView` click path |
| real-LLM tests | `[LocalFact]` |

New components and placement:

| Component | Local | Shared | Recommendation |
|---|---|---|---|
| text stats (word/sentence split, repetition, questions, vocabulary) | Chat.Service/Coach | Api, next to `PlayableTextMarkup` | shared: generic, and it must use that markup's regex |
| pause and pace from a time map | Chat.Service/Coach | Api, next to `Transcript` | shared |
| `SpanLocator` (nth occurrence, LLM span validation) | Chat.ML | Core | shared: every future span-returning LLM feature needs it |
| band + sub-score mapper | Users.Service/Coach | Core | shared: small, generic, used by tab, tips, trends |
| `ISpeechTagger` + stub | Chat.ML | already shared | Chat.ML |
| tip rules | Users.Service/Coach | — | local |
| timed-entry conversation stats | Chat.Service/Coach | Core.Server | local: input is the chat entry model |
| donut and bar chart | UI.Blazor.App | UI.Blazor | shared: inline SVG, no coaching knowledge |
| tip bar, Coach panel, Trends | UI.Blazor.App | — | local |
| per-word class overlay | UI.Blazor.App | — | local modification |

## Open questions

- Design: the meaning of the dashed sentence underline in the desktop mock; whether the dismiss
  cross is a plain dismiss or a snooze; chip tap behaviour beyond jump-to-audio.
- Outcome of the filler-survival spike.
