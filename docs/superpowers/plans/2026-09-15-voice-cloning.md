# Voice Cloning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An opted-in speaker is dubbed (live and replay) in a Soniox clone of their own voice, built from their recordings, held in a transient pool; the stock voice is the fallback everywhere.

**Architecture:** `UserLanguageSettings` carries the consent + explicit sample; a `user_voices` table (Users.Service, `IUserVoicesBackend`) holds the pool entry per user; `VoiceSampleBuilder` makes a ≤ 60 s WAV from the user's own entries (or the explicit sample); `VoicePool` acquires/creates Soniox clones with quota, single-flight, ready-polling, failure cool-down; `VoicePoolSweeper` evicts idle clones and reconciles with Soniox; `SpeakerVoices.Get` returns the clone id for opted-in users so live and replay dubs pick it up unchanged; the settings UI (admin + incomplete UI) exposes the toggle, status and sample actions.

**Tech Stack:** .NET 11, ActualLab.Fusion (compute services, commands, `IServerKvasBackend`), EF Core migration (`ef-migrations.cmd Users.Service add …`), MessagePack array-form records, OpusSharp (`OpusToPcmDecoder`), Soniox voices REST API, Blazor Server, xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-09-15-voice-cloning-design.md`

## Global Constraints

- `docs/CODING_STYLE.md` binds every edit (no `Async` suffix, no XML docs on members, 120-char lines, comments only where the code can't say it; the style hook runs on Edit/Write — use those tools; no `.claude/style-bypasses.md` entries). Reuse existing abstractions (`docs/api-index.md`) over new helpers.
- Array-form records append only: `UserLanguageSettings` keys 8 (`IsOwnVoiceEnabled`, `bool`) and 9 (`OwnVoiceSampleMediaId`, `MediaId?`), `MemoryPackOrder` matching; nothing renumbered.
- Constants (in `Constants.Audio`, next to the dub constants): `VoiceCloneQuota = 20`, `VoiceCloneReadyTimeout = 30 s`, `VoiceCloneAcquireTimeout = 15 s`, `VoiceCloneIdleTimeout = 10 min`, `VoiceCloneFailureCooldown = 10 min`, `VoiceSampleMinDuration = 30 s`, `VoiceSampleMaxDuration = 60 s`, `VoiceSampleMinEntryDuration = 5 s`, `VoiceSampleWindow = 90 days`. `TranscriptionSettings.SonioxVoiceQuota` (int?, null → the constant) overrides the quota.
- Soniox voice names are `voxt-<userId>-<hash8>`; the sweeper only ever deletes voices with that prefix.
- A listener never sees an error: every failure path in the pool/sample/synthesizer returns `null` (stock voice) and logs once.
- Never push; commit locally with the trailer:
  `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>` / `Claude-Session: https://claude.ai/code/session_01A697SeSrV5La5UsZE9oFRE`.
- Don't touch or stage the uncommitted `src/dotnet/Api/Constants.DebugMode.cs`.
- Integration test hosts take minutes to build; timeouts up to 10 min; infra is running. The Soniox key is in the env (`CoreSettings__SonioxKey`); live tests self-skip without it.
- UI gate: the own-voice tile renders only when `account.IsAdmin && IsIncompleteUIEnabled` (see `DeveloperTools.razor:79` and `FeaturesExt.IsIncompleteUIEnabled`).

## File Structure

| File | Responsibility |
|---|---|
| `src/dotnet/Api/Users/UserLanguageSettings.cs` | keys 8/9 |
| `src/dotnet/Api/Users/UserVoice.cs`, `UserVoiceStatus.cs` | pool entry record + status enum |
| `src/dotnet/Users.Contracts/IUserVoicesBackend.cs` | `Get(UserId)`, `ListActive()`, `OnChange` |
| `src/dotnet/Users.Service/UserVoicesBackend.cs`, `Db/DbUserVoice.cs`, `Db/UsersDbContext.cs`, `Users.Service.Migration/Migrations/*` | persistence |
| `src/dotnet/Transcription.Service/Transcribers/SonioxVoicesClient.cs` | REST: create/get/list/delete voices (`ISonioxVoices`) |
| `src/dotnet/Streaming.Service/Services/VoiceSampleBuilder.cs` | auto/explicit sample → WAV blob |
| `src/dotnet/Streaming.Service/Services/VoicePool.cs`, `VoicePoolSweeper.cs` | acquire/create/evict/reconcile |
| `src/dotnet/Streaming.Service/Services/SpeakerVoices.cs` | clone id for opted-in users |
| `src/dotnet/Api.Contracts/.../ITranslations.cs` (+ `Translations`) | `GetOwnVoiceStatus(Session)` for the UI |
| `src/dotnet/UI.Blazor.App/Components/Settings/TranscriptionSettings.razor` (+ strings) | toggle, status, sample actions |
| `docs/live-audio/12-dubbing.md` | "Own voice" section |

---

### Task 1: Settings keys, `UserVoice` record, `IUserVoicesBackend` + table

**Files:** `src/dotnet/Api/Users/UserLanguageSettings.cs`; create `src/dotnet/Api/Users/UserVoice.cs`, `UserVoiceStatus.cs`; create `src/dotnet/Users.Contracts/IUserVoicesBackend.cs`; create `src/dotnet/Users.Service/UserVoicesBackend.cs`, `Users.Service/Db/DbUserVoice.cs`; modify `Users.Service/Db/UsersDbContext.cs`, `Users.Service/Module/UsersServiceModule.cs` (register backend like `IChatUsagesBackend`); migration via `./ef-migrations.cmd Users.Service add Add_UserVoices` (build `src/dotnet/Users.Service.Migration` first).
**Tests:** `tests/Users.UnitTests/StoredSettingsSerializationTest.cs` (extend the `UserLanguageSettings` round-trip for keys 8/9 as done for key 7), `tests/Users.IntegrationTests/UserVoicesBackendTest.cs`.

**Interfaces produced:**
```csharp
public enum UserVoiceStatus { None = 0, Creating, Ready, Failed }
[DataContract, MessagePackObject] public sealed partial record UserVoice(
    [property: DataMember, Key(0)] UserId UserId, [property: DataMember, Key(1)] long Version = 0) : IHasVersion<long>
{
    [DataMember, Key(2)] public HashString SampleHash { get; init; }
    [DataMember, Key(3)] public string SonioxVoiceId { get => field ?? ""; init; } = "";
    [DataMember, Key(4)] public UserVoiceStatus Status { get; init; }
    [DataMember, Key(5)] public Moment? FailedUntil { get; init; }
    [DataMember, Key(6)] public Moment LastUsedAt { get; init; }
    [DataMember, Key(7)] public Moment CreatedAt { get; init; }
    [DataMember, Key(8)] public Moment ModifiedAt { get; init; }
}
public interface IUserVoicesBackend : IComputeService, IBackendService {
    [ComputeMethod] Task<UserVoice?> Get(UserId userId, CancellationToken ct);
    // Not a compute method: the sweeper's snapshot
    Task<ApiArray<UserVoice>> ListActive(CancellationToken ct); // Status is Creating or Ready
    [CommandHandler] Task<UserVoice?> OnChange(UserVoicesBackend_Change command, CancellationToken ct); // Change<UserVoiceDiff>, ExpectedVersion
}
```
`UserVoiceDiff : RecordDiff` mirrors the fields (nullable / `Option<>`). `UserLanguageSettings`: `[DataMember, MemoryPackOrder(8), Key(8)] public bool IsOwnVoiceEnabled { get; init; }` and `[DataMember, MemoryPackOrder(9), Key(9)] public MediaId? OwnVoiceSampleMediaId { get; init; }`.

- [ ] Write the failing serialization test (keys 8/9 round-trip through MessagePack + MemoryPack, absent keys → defaults) and the backend test (create → Get; update with wrong `ExpectedVersion` throws `VersionMismatchException`; `ListActive` excludes `None`/`Failed`).
- [ ] Implement records, contract, `DbUserVoice` (+ `UsersDbContext` set + `UseCollation("C")` on the id like siblings), backend modelled on `ChatUsagesBackend`/`ChatPositionsBackend` (Invalidation block invalidates `Get(userId)`), migration.
- [ ] Run `Users.UnitTests` + the new integration test. Commit `feat(dubbing): own-voice settings and the user voice record`.

---

### Task 2: `SonioxVoicesClient`

**Files:** create `src/dotnet/Transcription.Service/Transcribers/SonioxVoicesClient.cs` (+ `ISonioxVoices` in the same file or `Transcription.Contracts` if Streaming.Service must not depend on the implementation), models in `SonioxModels.cs`; register in `ServiceCollectionExt.AddSoniox` (uses `SonioxClient.HttpClientName`); a `FakeSonioxVoices` in `tests/Testing.Host` (in-memory dictionary, `ReadyAfter` delay knob, `FailCreate` knob) registered when `UseFakeTranscriber`.
**Tests:** `tests/Transcription.IntegrationTests/SonioxVoicesClientTest.cs` (live, self-skip): create from a 30 s WAV of the fake pump's silence? — no: Soniox rejects silence; use `tests/Transcription.IntegrationTests/data/long-ru-en-1.webm` decoded to WAV (see `OpusToPcmDecoder` + a WAV writer helper you add to `VoiceSampleBuilder` in Task 3 — so order Task 3's WAV writer before this test, or put `WavWriter` here in `Transcription.Service/Audio/WavWriter.cs` and reuse it in Task 3); poll until ready; synthesize "Hello" with the clone via `SonioxTtsClient.Generate`; delete; assert `Get` returns 404/null afterwards. Unit: request field mapping with a fake `HttpMessageHandler` (multipart `file` + `name`).

**Interface:**
```csharp
public sealed record SonioxVoice(string Id, string Name, bool IsReady, bool IsFailed);
public interface ISonioxVoices {
    Task<SonioxVoice> Create(string name, Stream wav, CancellationToken ct);        // POST /v1/voices
    Task<SonioxVoice?> Get(string id, CancellationToken ct);                        // GET /v1/voices/{id}, null on 404
    Task<ApiArray<SonioxVoice>> List(CancellationToken ct);                        // GET /v1/voices (paged, cursor)
    Task Delete(string id, CancellationToken ct);                                  // DELETE, 404 ignored
}
```
`IsReady`: every model status `ready`; `IsFailed`: any `failed` (read the response's per-model status shape from a real `Get` during the live test and encode it in the model class; document the shape in a comment).

- [ ] Commit `feat(dubbing): Soniox voices client`.

---

### Task 3: `VoiceSampleBuilder`

**Files:** create `src/dotnet/Streaming.Service/Services/VoiceSampleBuilder.cs`; `Constants.Audio` sample constants.
**Tests:** `tests/Streaming.UnitTests/VoiceSampleBuilderTest.cs` (pure selection logic, made `internal static`), `tests/Chat.IntegrationTests/VoiceSampleBuilderTest.cs` (real entries via `RecordVoiceEntry` ×N with `frameCount` sized to ≥ 5 s each; assert the WAV blob exists, its duration ≤ 60 s, ≥ 30 s; fewer entries → `NotEnoughRecordings`).

**Interface:**
```csharp
public sealed record VoiceSample(HashString Hash, string BlobId, TimeSpan Duration);
public enum VoiceSampleFailure { None, NotEnoughRecordings, SampleMissing }
public sealed class VoiceSampleBuilder(IServiceProvider services) {
    // Explicit sample (settings.OwnVoiceSampleMediaId) or auto from the user's own entries
    public Task<(VoiceSample? Sample, VoiceSampleFailure Failure)> Build(UserId userId, UserLanguageSettings settings, CancellationToken ct);
    internal static IReadOnlyList<ChatEntry> SelectEntries(IEnumerable<ChatEntry> ownAudioEntries, Moment now); // longest first, ≥ MinEntryDuration, ≤ MaxDuration total, within Window
    internal static HashString HashOf(IEnumerable<ChatEntryId> ids);  // Blake3 of the ordered id list (ChatEntryHashExt style)
}
```
Auto path: `IChatUsagesBackend.GetRecencyList(userId, ChatUsageListKind.Default?)` → per chat `IAuthorsBackend.GetByUserId(chatId, userId, …)` → `ChatEntryReader`/`IChatsBackend` tile reads filtered by author + `HasAudio` + not removed; decode each entry's blob (`AudioSourceDownloader.Download` → frames → `OpusToPcmDecoder`) and write PCM into a WAV (48 kHz mono 16-bit; header helper); upload to `BlobScope.AudioRecord` at `voice-sample/<userId>/<hash8>.wav` via `IBlobStorages`. Skip a rebuild when a blob for the same hash exists.

- [ ] Commit `feat(dubbing): build a voice sample from the speaker's recordings`.

---

### Task 4: `VoicePool` + `VoicePoolSweeper`

**Files:** create `src/dotnet/Streaming.Service/Services/VoicePool.cs`, `VoicePoolSweeper.cs` (hosted `WorkerBase`, registered in `StreamingServiceModule` on the backend role only); `Constants.Audio` pool constants; `TranscriptionSettings.SonioxVoiceQuota`.
**Tests:** `tests/Chat.IntegrationTests/VoicePoolTest.cs` with `FakeSonioxVoices`: acquire creates + returns the id (record `Ready`, `SampleHash` set); second acquire reuses (fake `Create` called once); a changed sample (new explicit `OwnVoiceSampleMediaId`) deletes the old voice and creates a new one; quota full (`SonioxVoiceQuota = 1`, two users) → second gets `null`; `FailCreate` → `Failed`, `FailedUntil` set, `null`, no retry within the cool-down; sweeper: `LastUsedAt` older than idle → fake `Delete` called, record `None`; reconcile: a `voxt-*` voice at the fake with no `Ready` record is deleted, a `Ready` record whose voice is gone is reset; opt-out → next acquire deletes the voice and returns `null`. Use `MomentClockSet` test clocks where the codebase does (grep `TestClock`/`ClockSet` in tests) rather than sleeping.

**Interface:**
```csharp
public sealed class VoicePool(IServiceProvider services) {
    public Task<string?> Acquire(UserId userId, CancellationToken ct);      // clone id or null; waits ≤ VoiceCloneAcquireTimeout
    internal int InFlightCount { get; }
}
```
Implementation per the spec's numbered rule; single-flight TCS map keyed by user (the `ReplayDubs` shape: publish before work, remove by identity, always complete); work runs on a host-stop-linked token; touch `LastUsedAt` at most once a minute per user (`UserVoicesBackend_Change` update); Soniox name `voxt-<userId>-<hash8>`.

- [ ] Commit `feat(dubbing): a transient pool of cloned voices`.

---

### Task 5: `SpeakerVoices` uses the clone; dub tests

**Files:** `src/dotnet/Streaming.Service/Services/SpeakerVoices.cs` (after reading settings: `if (settings.IsOwnVoiceEnabled && userId is known) { var clone = await VoicePool.Acquire(userId, ct); if (clone != null) return clone; }` then the stock-voice path; the catalog validation must not reject UUIDs the pool returned); `ReplayDubs`/live path unchanged.
**Tests:** `tests/Chat.IntegrationTests/ReplayDubsTest.cs` — an opted-in speaker with ≥ 30 s of recordings gets a dub whose `RecordingSpeechSynthesizer` `VoiceId` is the fake clone id; opt-out → stock voice and the stored dub regenerates (hash changed); `DubbingTranslationFlowTest` — live dub passes the clone id. Add `tests/Testing.Host` helpers as needed (`RecordVoiceEntries(chatId, language, count, frames)`).

- [ ] Commit `feat(dubbing): dub opted-in speakers in their own voice`.

---

### Task 6: `GetOwnVoiceStatus` + settings UI + strings

**Files:** `ITranslations.GetOwnVoiceStatus(Session, ct)` → `OwnVoiceStatus` record `(bool IsEnabled, UserVoiceStatus Status, VoiceSampleFailure Failure, TimeSpan? MissingDuration, bool HasExplicitSample)` (compute method depending on `IUserVoicesBackend.Get` + settings; `MissingDuration` from `VoiceSampleBuilder` selection without building); `TranscriptionSettings.razor`: the own-voice tile group (toggle + caption + status line + actions) gated by `account.IsAdmin && IsIncompleteUIEnabled` (read how `DeveloperTools.razor` gets both), stock-voice tile caption switch; "Record a sample" navigates to the Notes chat (`ChatsBackend_CreateNotesChat` exists; find how the UI opens Notes — grep `IsNotes` in UI.Blazor.App) and shows a prompt card with `Transcription_OwnVoicePromptText` (a ~30 s paragraph) — the simplest card that fits the chat header/banner patterns; after the user records there, "Use this recording" on the entry menu (or a "Use latest Notes recording" action in settings — pick the smaller one and say which) stores `OwnVoiceSampleMediaId`. Strings per docs/i18n.md in all catalogs + derive scripts + `LocalizedStringsLocalizerExt`.
**Tests:** `Chat.UI.Blazor.UnitTests` (`AppLocalizationTest` + full), `Chat.IntegrationTests` `GetOwnVoiceStatus` cases, `npm run build:Verify`; browser check with the chrome MCP (toggle on as admin with incomplete UI, status line, sample actions).

- [ ] Commit `feat(dubbing): "Use my own voice" settings`.

---

### Task 7: Docs + full pass

- `docs/live-audio/12-dubbing.md`: "Own voice" section (data, sample, pool, sweeper, integration, gate, follow-ups: gender detection, dedicated sample recorder, quota raise); delete the spec and this plan (`git rm`), commit `docs(dubbing): describe own-voice cloning`.
- Full pass: `Users.UnitTests`, `Streaming.UnitTests`, `Chat.UnitTests`, `Chat.UI.Blazor.UnitTests`, `Transcription.UnitTests` (excl. `SonioxTranscriberTest`), `Chat.IntegrationTests --filter "Dub|Voice|Translation"`, `Users.IntegrationTests --filter "Voice"`, `Streaming.IntegrationTests --filter "Dub"`, `Transcription.IntegrationTests --filter "Soniox"` (live). Report exact counts.
