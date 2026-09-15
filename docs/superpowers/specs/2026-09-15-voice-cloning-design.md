# Voice cloning ("Use my own voice") — design

Date: 2026-09-15. Branch: `feat/voice-dubbing` (PR #4514). Extends live + replay dubbing
(`docs/live-audio/12-dubbing.md`) so that an opted-in speaker is dubbed in a clone of their own
voice instead of a stock voice.

## Decisions (from the brainstorm)

- **Sample**: auto from the speaker's own recordings, with an optional explicit sample that
  replaces it.
- **Lifetime**: a transient pool — a Soniox clone exists only while the speaker is being dubbed;
  idle clones are deleted; a full pool means the stock voice for that speaker.
- **Replay** uses the clone too, acquiring it on demand.
- **Rollout**: the UI is visible to admins with "incomplete UI" enabled (`Features_EnableIncompleteUI`),
  the server side is ungated. The gate is removed once Soniox raises the 20-voice quota.
- Fallback everywhere is the speaker's stock voice (`SpeakerVoices` as today); a listener never
  sees an error, the speaker sees a status line.

## Soniox facts

`POST https://api.soniox.com/v1/voices` (multipart `file` ≤ 2 min / ≤ 35 MB single-speaker audio,
`name` unique per project) → `{id, name, ...status per model}`; processing is async, usually
seconds; `GET /v1/voices/{id}`, `GET /v1/voices` (paged), `DELETE /v1/voices/{id}`. A clone is used
as `voice: <uuid>` and speaks every language. Quota: 20 voices per organization. No extra cost.

## Data

- `UserLanguageSettings` (array-form, append only): key 8 `IsOwnVoiceEnabled` (`bool`) — the
  consent; key 9 `OwnVoiceSampleMediaId` (`MediaId?`) — the explicit sample, `null` = auto.
- `DbUserVoice` in Users.Service (new table `user_voices`, migration): `UserId` (key),
  `SampleHash` (hash of the sample media id or of the auto sample's entry-id list), `SonioxVoiceId`
  (`string?`), `Status` (`None | Creating | Ready | Failed`), `FailedUntil` (`DateTime?`),
  `LastUsedAt`, `CreatedAt`, `ModifiedAt`, `Version`. Exposed as `UserVoice` (array-form record)
  through `IUserVoicesBackend` (Users.Contracts): `Get(UserId)`, `OnChange` command.
- Blob: the reference clip lives in `BlobScope.AudioRecord` as `voice-sample/<userId>/<hash>.wav`
  (WAV 16 kHz mono PCM - `Constants.Audio.RecordingSampleRate` - ≤ 60 s) — rebuilt when the hash changes, deleted with the record.

## Sample

`VoiceSampleBuilder` (Streaming.Service, next to `AudioSegmentSaver`):

- Explicit: `OwnVoiceSampleMediaId` → download the media blob (webm), decode Opus → PCM
  (`OpusToPcmDecoder`), trim to 60 s, write WAV. Recording it: the "Record a sample" action opens
  the user's **Notes** chat with a prompt card (the localized paragraph to read), the recording
  becomes an ordinary voice entry there, and its `MediaId` is stored as the sample. Re-record =
  record again + store the new id; Remove = clear the setting (the entry stays in Notes).
- Auto: the speaker's own voice entries (chats from the user's recency list, author = the
  user's author in each chat, `HasAudio`, not removed, last 90 days), longest first, each ≥ 5 s,
  concatenated to ≤ 60 s. Requires ≥ 30 s in total, otherwise `Status = None` with reason
  `NotEnoughRecordings`. The hash is Blake3 of the ordered entry-id list, so new recordings
  change the sample only when the selection changes.

## Pool — `VoicePool` (Streaming.Service)

- `Task<string?> Acquire(UserId, CancellationToken)`:
  1. `UserVoice` `Ready` with the current sample hash → touch `LastUsedAt` (throttled to once a
     minute), return `SonioxVoiceId`.
  2. `Failed` and `FailedUntil` in the future → `null`.
  3. Otherwise, if the number of `Ready | Creating` records < `Constants.Audio.VoiceCloneQuota`
     (20, configurable via `TranscriptionSettings.SonioxVoiceQuota`): mark `Creating`, build the
     sample if missing, `POST /v1/voices` with `name = "voxt-<userId>-<hash8>"`, poll
     `GET /v1/voices/{id}` every 500 ms until ready or `VoiceCloneReadyTimeout` (30 s), store
     `Ready`, return the id. Any failure → `Failed`, `FailedUntil = now + VoiceCloneFailureCooldown`
     (10 min), delete the Soniox voice if it was created, return `null`.
  4. Pool full → `null`.
  Single-flight per user (TCS map, like `ReplayDubs`); callers wait at most
  `VoiceCloneAcquireTimeout` (15 s) — a live dub that can't wait gets `null` this utterance and the
  clone next time.
- `Release` is implicit: `VoicePoolSweeper` (hosted service, every minute) deletes Soniox voices
  whose `LastUsedAt` is older than `VoiceCloneIdleTimeout` (10 min) and resets the record to
  `None`; at startup it reconciles with `GET /v1/voices`: voices named `voxt-*` with no
  `Ready` record are deleted (crash leftovers), records whose voice is gone are reset.
- Opt-out (`IsOwnVoiceEnabled` → false) or a sample change (hash mismatch) → the next `Acquire`
  deletes the old Soniox voice and starts over; the sweeper also drops clones of opted-out users.

## Dub integration

`SpeakerVoices.Get(chatId, authorId)` (the single place live and replay ask for a voice):
opted-in user → `VoicePool.Acquire` → clone id; `null` → the stock voice as today. Live: the
acquire runs inside the existing dub decision window; replay: inside the 20 s dub wait. The replay
dub hash already includes the voice id, so a stored dub is regenerated when the clone appears,
disappears or changes.

`SonioxSpeechSynthesizer` passes a UUID voice id through unchanged (`SpeechSynthesisOptions.VoiceId`);
the catalog validation in `SpeakerVoices` accepts UUIDs owned by `UserVoice` records.

## UI (admin + incomplete UI)

In Transcription settings, Translated Voice section, above the stock-voice tile:
- Toggle **Use my own voice** — caption: "Others hear your translated speech in a clone of your
  voice, made from your recordings. Turn off to delete it." Turning on requires the consent
  caption to be shown; no extra confirmation.
- Status line under it: `Ready` / `Preparing your voice…` / `Needs about N more seconds of your
  recordings` / `Temporarily using a standard voice` (pool full / failed) / `Off`.
- Actions: **Record a sample** (opens Notes with the prompt card), **Re-record**, **Remove sample**.
- The stock-voice tile caption becomes "Used when your own voice isn't available" while own voice
  is on.
- Gate: rendered only when `account.IsAdmin && IsIncompleteUIEnabled`.

## Tests

- `VoiceSampleBuilderTest` (unit): selection (longest first, ≥ 5 s each, ≤ 60 s total, ≥ 30 s
  required, 90-day window), deterministic hash, WAV header/length.
- `VoicePoolTest` (integration, fake `ISonioxVoices` client): acquire creates + returns; second
  acquire reuses; quota full → null; failure → `Failed` + cool-down + Soniox delete; idle sweep
  deletes and resets; startup reconcile deletes orphans; opt-out deletes.
- `SpeakerVoicesTest`: opted-in + acquired → clone id; not acquired → stock voice.
- Replay/live dub tests: an opted-in speaker's dub is synthesized with the clone id
  (`RecordingSpeechSynthesizer` records `VoiceId`).
- Live (self-skipping): create a clone from a 30 s WAV, wait ready, synthesize one sentence with
  it, delete it.
- Settings serialization for keys 8/9.

## Out of scope

Gender detection from the sample; cloning on non-Soniox synthesizers; a dedicated sample
recorder outside Notes; per-chat opt-in.
