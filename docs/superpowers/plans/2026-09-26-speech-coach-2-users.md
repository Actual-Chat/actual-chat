# Speech Coach — Plan 2: Users side

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every `CoachEntryAnalyzedEvent` / `CoachConversationAnalyzedEvent` from the chat side lands in a per-user log, rebuilds that user's day row, can fire one live tip, and `ICoach` serves the score, the metric rows, the day series, the pending tip and jump-to-audio occurrences for the caller only.

**Architecture:** A `Coach/` folder in `Users.Service` mirroring the usage-stats subsystem: `CoachBackend` (sharded by user) handles the two events, appends a `CoachEvents` row keyed by (user, source id) with the analysis as a JSON payload, rebuilds the affected `CoachDays` row from the log, and evaluates tip rules outside the DB operation, storing the tip in the user's key-value store. Pure calculators (day aggregation, scoring, tip policy) are static classes with unit tests. `Coach : ICoach` is the session-scoped frontend. The client flag `Features_EnableSpeechCoach` combines the chat-side master switch with a rollout rule.

**Tech Stack:** .NET 11 / C#, ActualLab.Fusion (compute services, commander, queues, operations, KVAS), EF Core + PostgreSQL (snake_case, `C` collation on ids, `jsonb` payload), xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-09-25-speech-coach-design.md` (sections *Pipeline → Tips*, *Metrics*, *Storage → Users DB*, *Client API*, *Failure handling*, *Rollout*). Plan 1 (`2026-09-25-speech-coach-1-chat-analysis.md`, merged as PR #4856's branch) produces the events; Plan 3 is the UI.

## Global Constraints

- Everything in Plan 1's *Global Constraints* applies (style guide, no comments by default, serialization attributes, no `Async` suffix, `.ConfigureAwait(false)`, build by test project, commit per task, never push unless asked).
- Branch `feat/4829-speech-coach-2-users`, stacked on `feat/4829-speech-coach`, worktree `/home/undead/projects/actual-chat-4829-speech-coach`. Its PR targets `feat/4829-speech-coach` until #4856 merges, then `dev`.
- Every user-side write is idempotent under redelivery: the log is keyed by (user id, source id) and a re-emit **replaces** the row; day rows are **rebuilt from the log**, never incremented.
- All bands, weights and thresholds live in `UsersSettings.Coach`; none are literals in calculators.
- Nothing here reads chat text or another user's rows. Occurrences come from the caller's own log rows.
- Days are UTC calendar days via `UsageDay.DayOf(moment)`; windows are half-open `Range<Moment>`.

**Deviations from the spec, recorded here for the spec owner:**
1. The **per-language lexicon** (`CoachLexicons`, the "tagger off" cost floor) is deferred. The spec's own sizing puts tagging at well under a million tokens a day; the lexicon only matters two orders of magnitude up. Follow-up.
2. The **per-user daily tagger cap** stays deferred: the calls happen chat-side, and enforcing a user-side count there needs a cross-shard read on every message. Follow-up with the lexicon.
3. `ICoach.IsEnabled` is the **per-user verdict** (master switch from `IChatCoach.IsEnabled` AND the rollout rule), so the client flag is one call.

## Review Focus

1. The same entry event delivered twice, and a re-emit with different counts under the same source id: one log row, the latest counts, the day rebuilt once per write (Task 6 tests `RedeliveryShouldKeepOneRow`, `ReEmitShouldReplaceTheRow`).
2. A removal event (`IsRemoved = true`) for an entry whose row exists, and for one that doesn't: row gone, day rebuilt, no exception (Task 6 test `RemovalShouldDropTheRowAndRebuildTheDay`).
3. A window with fewer than 200 words: no score, no badge, metric rows still present with values (Task 3 test `ScoreShouldBeAbsentBelowMinWords`).
4. Tips for a user who is opted in but has live tips off, and for one not opted in: no tip stored either way (Task 6 test `TipsShouldRequireBothToggles`).
5. A guest or a signed-out session calling `ICoach`: empty summary, no tip, no throw (Task 7 test `GuestShouldGetEmptySummary`).

## File structure

| File | Responsibility |
|---|---|
| `src/dotnet/Api/Users/Coach/CoachRecord.cs` | Log record models: `CoachRecordKind`, `CoachEntryRecord`, `CoachRunRecord`, `CoachRecord` + mapping from the chat-side analyses |
| `src/dotnet/Api/Users/Coach/CoachDay.cs` | Per-day aggregate model + `CoachDayBuilder` (pure) |
| `src/dotnet/Api/Users/Coach/CoachSummary.cs` | `CoachWindow`, `CoachMetricKind`, `CoachMetric`, `CoachChip`, `CoachSummary`, `CoachOccurrence` |
| `src/dotnet/Api/Users/StoredSettings/UserCoachTip.cs` | The pending tip, KVAS record |
| `src/dotnet/Users.Service/Module/UsersSettings.cs` | `CoachScoringSettings` (bands, weights, thresholds, rollout) |
| `src/dotnet/Users.Service/Coach/CoachScoring.cs` | Pure: bands, sub-scores, score, badge, metric rows, chips |
| `src/dotnet/Users.Service/Coach/CoachTipPolicy.cs` | Pure: which tip, if any, a fresh record earns |
| `src/dotnet/Users.Service/Db/DbCoachEvent.cs`, `DbCoachDay.cs` | Entities |
| `src/dotnet/Users.Service/Db/UsersDbContext.cs` + migration | Tables |
| `src/dotnet/Users.Contracts/ICoachBackend.cs` | Backend contract + commands |
| `src/dotnet/Users.Service/Coach/CoachBackend.cs` | Log, rebuild, events, tips |
| `src/dotnet/Api.Contracts/Users/ICoach.cs`, `src/dotnet/Users.Service/Coach/Coach.cs` | Frontend |
| `src/dotnet/Users.Service/AccountsBackend.cs` | Delete coach rows on account deletion |
| `src/dotnet/UI.Blazor/Services/Features/Features_EnableSpeechCoach.cs` | Client flag |
| `tests/Users.UnitTests/Coach/*`, `tests/Users.IntegrationTests/CoachTest.cs` | Tests |

---

### Task 1: Log record models and mapping (Api)

**Files:**
- Create: `src/dotnet/Api/Users/Coach/CoachRecord.cs`
- Test: `tests/Users.UnitTests/Coach/CoachRecordTest.cs`

**Interfaces:**
- Consumes: `CoachEntryAnalysis`, `CoachConversationAnalysis`, `SpeechSpan` (Plan 1, namespace `ActualChat.Chat`).
- Produces (namespace `ActualChat.Users`, all `[DataContract, MessagePackObject]` with `[DataMember, Key(N)]`):
  ```csharp
  public enum CoachRecordKind { Entry = 0, Run = 1 }
  public sealed partial record CoachEntryRecord(
      long EntryLid, string? Language, double DurationSeconds, double? SpeechSeconds,
      int? Words, int? Sentences, int? Questions, int? Repetitions, int? DistinctWords,
      int? Pauses, double? PauseSeconds, bool IsTagged,
      int FilledPauses, int Fillers, int WeakWords, int Profanities,
      ApiArray<SpeechSpan> Spans);                       // synonyms stripped
  public sealed partial record CoachRunRecord(
      long StartEntryLid, double OwnSpeechSeconds, double TotalSpeechSeconds, int OwnTurns, int TotalTurns,
      int Participants, double LongestMonologueSeconds, int Responses, double ResponseGapSeconds, int Interruptions);
  public sealed partial record CoachRecord(
      CoachRecordKind Kind, string SourceId, UserId UserId, ChatId ChatId, Moment OccurredAt) {
      CoachEntryRecord? Entry; CoachRunRecord? Run;
      public Moment Day => UsageDay.DayOf(OccurredAt);
      public static CoachRecord FromEntry(CoachEntryAnalysis a);   // SourceId = a.Id.Value, OccurredAt = a.BeginsAt
      public static CoachRecord FromRun(CoachConversationAnalysis a); // SourceId = $"{a.Id}:{a.AuthorId}", OccurredAt = a.EndsAt
  }
  ```

- [ ] **Step 1: Write the failing tests**

```csharp
using ActualChat.Chat;
using ActualChat.Hashing;

namespace ActualChat.Users.UnitTests.Coach;

public class CoachRecordTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void FromEntryShouldCarryCountsAndStripSynonyms()
    {
        // arrange
        var chatId = GroupChatId.New();
        var analysis = new CoachEntryAnalysis(ChatEntryId.New(chatId, 7), 1) {
            AuthorId = AuthorId.New(chatId, 1),
            UserId = UserId.New(),
            BeginsAt = new DateTime(2026, 9, 26, 10, 30, 0, DateTimeKind.Utc),
            Language = Languages.English,
            DurationSeconds = 12,
            Words = 20,
            Spans = ApiArray.New(new SpeechSpan(SpeechSpanKind.Weak, "awesome", 3, 7, ApiArray.New("excellent"))),
            WeakWords = 1,
            TagState = CoachTagState.Tagged,
            ContentHash = ChatEntryHashExt.GetContentHashString("x"),
        };

        // act
        var record = CoachRecord.FromEntry(analysis);

        // assert
        record.Kind.Should().Be(CoachRecordKind.Entry);
        record.SourceId.Should().Be(analysis.Id.Value);
        record.ChatId.Should().Be(chatId);
        record.Day.Should().Be(new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc));
        record.Entry!.Words.Should().Be(20);
        record.Entry.IsTagged.Should().BeTrue();
        record.Entry.Spans.Should().ContainSingle().Which.Synonyms.Should().BeEmpty("synonyms stay chat-side");
        record.Run.Should().BeNull();
    }

    [Fact]
    public void FromRunShouldKeyByRunAndAuthor()
    {
        // arrange
        var chatId = GroupChatId.New();
        var analysis = new CoachConversationAnalysis(ConversationId.New(chatId, 100), AuthorId.New(chatId, 2), 1) {
            UserId = UserId.New(),
            EndsAt = new DateTime(2026, 9, 26, 23, 59, 0, DateTimeKind.Utc),
            OwnSpeechSeconds = 30,
            TotalSpeechSeconds = 90,
            Participants = 3,
        };

        // act
        var record = CoachRecord.FromRun(analysis);

        // assert
        record.Kind.Should().Be(CoachRecordKind.Run);
        record.SourceId.Should().Be($"{analysis.Id}:{analysis.AuthorId}");
        record.Run!.OwnSpeechSeconds.Should().Be(30);
        record.Entry.Should().BeNull();
    }

    [Fact]
    public void RecordShouldPassThroughAllSerializers()
        => CoachRecord.FromEntry(new CoachEntryAnalysis(ChatEntryId.New(GroupChatId.New(), 1), 1) {
                AuthorId = AuthorId.New(GroupChatId.New(), 1), UserId = UserId.New(),
                ContentHash = ChatEntryHashExt.GetContentHashString("x"),
            })
            .AssertPassesThroughSerializers(Out);
}
```
`AssertPassesThroughSerializers` is the helper `ContentIndexPageCountsSerializationTest` uses (grep `AssertPassesThroughSerializers` in `tests/Testing` for its exact signature; it may need an equality callback).

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Users.UnitTests --filter FullyQualifiedName~CoachRecordTest`
Expected: build errors.

- [ ] **Step 3: Implement**

```csharp
using ActualChat.Chat;

namespace ActualChat.Users;

public enum CoachRecordKind
{
    Entry = 0,
    Run = 1,
}

[DataContract, MessagePackObject]
public sealed partial record CoachEntryRecord(
    [property: DataMember, Key(0)] long EntryLid,
    [property: DataMember, Key(1)] string? Language,
    [property: DataMember, Key(2)] double DurationSeconds,
    [property: DataMember, Key(3)] double? SpeechSeconds,
    [property: DataMember, Key(4)] int? Words,
    [property: DataMember, Key(5)] int? Sentences,
    [property: DataMember, Key(6)] int? Questions,
    [property: DataMember, Key(7)] int? Repetitions,
    [property: DataMember, Key(8)] int? DistinctWords,
    [property: DataMember, Key(9)] int? Pauses,
    [property: DataMember, Key(10)] double? PauseSeconds,
    [property: DataMember, Key(11)] bool IsTagged,
    [property: DataMember, Key(12)] int FilledPauses,
    [property: DataMember, Key(13)] int Fillers,
    [property: DataMember, Key(14)] int WeakWords,
    [property: DataMember, Key(15)] int Profanities,
    [property: DataMember, Key(16)] ApiArray<SpeechSpan> Spans
);

[DataContract, MessagePackObject]
public sealed partial record CoachRunRecord(
    [property: DataMember, Key(0)] long StartEntryLid,
    [property: DataMember, Key(1)] double OwnSpeechSeconds,
    [property: DataMember, Key(2)] double TotalSpeechSeconds,
    [property: DataMember, Key(3)] int OwnTurns,
    [property: DataMember, Key(4)] int TotalTurns,
    [property: DataMember, Key(5)] int Participants,
    [property: DataMember, Key(6)] double LongestMonologueSeconds,
    [property: DataMember, Key(7)] int Responses,
    [property: DataMember, Key(8)] double ResponseGapSeconds,
    [property: DataMember, Key(9)] int Interruptions
);

/// <summary>
/// One row of a user's speech-coach log: a voice entry's analysis or a run's turn-taking,
/// as the chat side emitted it. Day rows are rebuilt from these.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record CoachRecord(
    [property: DataMember, Key(0)] CoachRecordKind Kind,
    [property: DataMember, Key(1)] string SourceId,
    [property: DataMember, Key(2)] UserId UserId,
    [property: DataMember, Key(3)] ChatId ChatId,
    [property: DataMember, Key(4)] Moment OccurredAt)
{
    [DataMember, Key(5)] public CoachEntryRecord? Entry { get; init; }
    [DataMember, Key(6)] public CoachRunRecord? Run { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public Moment Day => UsageDay.DayOf(OccurredAt);

    public static CoachRecord FromEntry(CoachEntryAnalysis a)
        => new (CoachRecordKind.Entry, a.Id.Value, a.UserId, a.Id.ChatId, a.BeginsAt) {
            Entry = new CoachEntryRecord(
                a.Id.LocalId,
                a.Language?.Value,
                a.DurationSeconds,
                a.SpeechSeconds,
                a.Words,
                a.Sentences,
                a.Questions,
                a.Repetitions,
                a.DistinctWords,
                a.Pauses,
                a.PauseSeconds,
                a.TagState == CoachTagState.Tagged,
                a.FilledPauses,
                a.Fillers,
                a.WeakWords,
                a.Profanities,
                a.Spans.Select(s => s with { Synonyms = ApiArray<string>.Empty }).ToApiArray()),
        };

    public static CoachRecord FromRun(CoachConversationAnalysis a)
        => new (CoachRecordKind.Run, $"{a.Id}:{a.AuthorId}", a.UserId, a.Id.ChatId, a.EndsAt) {
            Run = new CoachRunRecord(
                a.Id.StartEntryLid,
                a.OwnSpeechSeconds,
                a.TotalSpeechSeconds,
                a.OwnTurns,
                a.TotalTurns,
                a.Participants,
                a.LongestMonologueSeconds,
                a.Responses,
                a.ResponseGapSeconds,
                a.Interruptions),
        };
}
```
`Api` already references the chat models (they are in the same project), so `using ActualChat.Chat;` resolves.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Users.UnitTests --filter FullyQualifiedName~CoachRecordTest`
Expected: green.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Api/Users/Coach/CoachRecord.cs tests/Users.UnitTests/Coach/CoachRecordTest.cs
git commit -m "feat(coach): user-side log record models mapped from the chat-side analyses"
```

---

### Task 2: `CoachDay` and `CoachDayBuilder` (Api, pure)

**Files:**
- Create: `src/dotnet/Api/Users/Coach/CoachDay.cs`
- Test: `tests/Users.UnitTests/Coach/CoachDayBuilderTest.cs`

**Interfaces:**
- Produces:
  ```csharp
  [DataContract, MessagePackObject]
  public sealed partial record CoachDay(Moment Day) {
      int Entries, TaggedEntries, Words, Sentences, Questions, Repetitions, Pauses,
          FilledPauses, Fillers, WeakWords, Profanities, Runs, OwnTurns, TotalTurns, Responses, Interruptions;
      double DurationSeconds, SpeechSeconds, PauseSeconds, VocabularyWords, VocabularyDistinct,
          OwnSpeechSeconds, TotalSpeechSeconds, FairShareSeconds, LongestMonologueSeconds, ResponseGapSeconds;
      ApiMap<string, int> FillerCounts, WeakWordCounts;   // lowercase word → count, from spans
      public static readonly CoachDay None = new(default);
  }
  public static class CoachDayBuilder {
      public static CoachDay Build(Moment day, IEnumerable<CoachRecord> records, int minVocabularyWords);
      public static CoachDay Merge(Moment day, IEnumerable<CoachDay> days);   // for windows
  }
  ```
  Vocabulary: only entries with `Words >= minVocabularyWords` contribute `Words` to `VocabularyWords` and `DistinctWords` to `VocabularyDistinct`, so the window ratio is words-weighted. `FairShareSeconds += TotalSpeechSeconds / Participants` per run. `LongestMonologueSeconds` is a max, `ResponseGapSeconds` a sum (mean = sum / Responses). `ApiMap<,>` is `ActualLab.Api.ApiMap`; if it doesn't exist as a serializable map use `ApiArray<CoachWordCount>` with `record CoachWordCount(string Word, int Count)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using ActualChat.Chat;

namespace ActualChat.Users.UnitTests.Coach;

public class CoachDayBuilderTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Moment Day = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);
    private static readonly UserId User = UserId.New();
    private static readonly ChatId Chat = GroupChatId.New();

    private static CoachRecord Entry(long lid, int words, int distinct, bool isTagged, params SpeechSpan[] spans)
        => new (CoachRecordKind.Entry, $"e{lid}", User, Chat, Day + TimeSpan.FromHours(lid)) {
            Entry = new CoachEntryRecord(lid, "en-US", 60, 50, words, 4, 1, 0, distinct, 2, 10, isTagged,
                spans.Count(s => s.Kind == SpeechSpanKind.FilledPause),
                spans.Count(s => s.Kind == SpeechSpanKind.Filler),
                spans.Count(s => s.Kind == SpeechSpanKind.Weak),
                0, spans.ToApiArray()),
        };

    private static SpeechSpan Span(SpeechSpanKind kind, string word)
        => new (kind, word, 0, word.Length, ApiArray<string>.Empty);

    private static CoachRecord Run(long start, double own, double total, int participants)
        => new (CoachRecordKind.Run, $"r{start}", User, Chat, Day + TimeSpan.FromHours(5)) {
            Run = new CoachRunRecord(start, own, total, 2, 5, participants, 40, 1, 0.8, 0),
        };

    [Fact]
    public void BuildShouldSumEntriesAndCountWords()
    {
        // arrange
        var records = new[] {
            Entry(1, 100, 80, true, Span(SpeechSpanKind.Filler, "you know"), Span(SpeechSpanKind.Filler, "you know"), Span(SpeechSpanKind.Weak, "awesome")),
            Entry(2, 10, 9, false),
        };

        // act
        var day = CoachDayBuilder.Build(Day, records, minVocabularyWords: 20);

        // assert
        day.Entries.Should().Be(2);
        day.TaggedEntries.Should().Be(1);
        day.Words.Should().Be(110);
        day.Fillers.Should().Be(2);
        day.WeakWords.Should().Be(1);
        day.FillerCounts["you know"].Should().Be(2);
        day.WeakWordCounts["awesome"].Should().Be(1);
        day.VocabularyWords.Should().Be(100, "entries under the vocabulary minimum do not count");
        day.VocabularyDistinct.Should().Be(80);
        day.SpeechSeconds.Should().Be(100);
    }

    [Fact]
    public void BuildShouldAggregateRunsWithFairShare()
    {
        // act
        var day = CoachDayBuilder.Build(Day, [Run(1, 30, 90, 3), Run(50, 20, 40, 2)], 20);

        // assert
        day.Runs.Should().Be(2);
        day.OwnSpeechSeconds.Should().Be(50);
        day.TotalSpeechSeconds.Should().Be(130);
        day.FairShareSeconds.Should().Be(50, "90/3 + 40/2");
        day.LongestMonologueSeconds.Should().Be(40);
        day.Responses.Should().Be(2);
        day.ResponseGapSeconds.Should().BeApproximately(1.6, 0.001);
    }

    [Fact]
    public void MergeShouldAddDaysAndWordMaps()
    {
        // arrange
        var a = CoachDayBuilder.Build(Day, [Entry(1, 100, 80, true, Span(SpeechSpanKind.Filler, "like"))], 20);
        var b = CoachDayBuilder.Build(Day + TimeSpan.FromDays(1), [Entry(2, 50, 40, true, Span(SpeechSpanKind.Filler, "like"))], 20);

        // act
        var merged = CoachDayBuilder.Merge(Day, [a, b]);

        // assert
        merged.Words.Should().Be(150);
        merged.FillerCounts["like"].Should().Be(2);
        merged.Entries.Should().Be(2);
    }

    [Fact]
    public void BuildShouldIgnoreRecordsOfOtherDays()
    {
        // arrange
        var other = Entry(1, 100, 80, true) with { OccurredAt = Day + TimeSpan.FromDays(2) };

        // act
        var day = CoachDayBuilder.Build(Day, [other], 20);

        // assert
        day.Entries.Should().Be(0);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Users.UnitTests --filter FullyQualifiedName~CoachDayBuilderTest`
Expected: build errors.

- [ ] **Step 3: Implement**

```csharp
using ActualChat.Chat;

namespace ActualChat.Users;

/// <summary>
/// One user's speech-coach numbers for one UTC day, rebuilt from the log; windows are merges of days.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record CoachDay([property: DataMember, Key(0)] Moment Day)
{
    public static readonly CoachDay None = new (default);

    [DataMember, Key(1)] public int Entries { get; init; }
    [DataMember, Key(2)] public int TaggedEntries { get; init; }
    [DataMember, Key(3)] public int Words { get; init; }
    [DataMember, Key(4)] public int Sentences { get; init; }
    [DataMember, Key(5)] public int Questions { get; init; }
    [DataMember, Key(6)] public int Repetitions { get; init; }
    [DataMember, Key(7)] public int Pauses { get; init; }
    [DataMember, Key(8)] public int FilledPauses { get; init; }
    [DataMember, Key(9)] public int Fillers { get; init; }
    [DataMember, Key(10)] public int WeakWords { get; init; }
    [DataMember, Key(11)] public int Profanities { get; init; }
    [DataMember, Key(12)] public int Runs { get; init; }
    [DataMember, Key(13)] public int OwnTurns { get; init; }
    [DataMember, Key(14)] public int TotalTurns { get; init; }
    [DataMember, Key(15)] public int Responses { get; init; }
    [DataMember, Key(16)] public int Interruptions { get; init; }
    [DataMember, Key(17)] public double DurationSeconds { get; init; }
    [DataMember, Key(18)] public double SpeechSeconds { get; init; }
    [DataMember, Key(19)] public double PauseSeconds { get; init; }
    [DataMember, Key(20)] public double VocabularyWords { get; init; }
    [DataMember, Key(21)] public double VocabularyDistinct { get; init; }
    [DataMember, Key(22)] public double OwnSpeechSeconds { get; init; }
    [DataMember, Key(23)] public double TotalSpeechSeconds { get; init; }
    [DataMember, Key(24)] public double FairShareSeconds { get; init; }
    [DataMember, Key(25)] public double LongestMonologueSeconds { get; init; }
    [DataMember, Key(26)] public double ResponseGapSeconds { get; init; }
    [DataMember, Key(27)] public ApiMap<string, int> FillerCounts { get; init; } = ApiMap<string, int>.Empty;
    [DataMember, Key(28)] public ApiMap<string, int> WeakWordCounts { get; init; } = ApiMap<string, int>.Empty;
}

public static class CoachDayBuilder
{
    public static CoachDay Build(Moment day, IEnumerable<CoachRecord> records, int minVocabularyWords)
    {
        var d = new CoachDay(day);
        var fillers = new Dictionary<string, int>();
        var weak = new Dictionary<string, int>();
        foreach (var r in records.Where(r => r.Day == day)) {
            if (r.Entry is { } e) {
                d = d with {
                    Entries = d.Entries + 1,
                    TaggedEntries = d.TaggedEntries + (e.IsTagged ? 1 : 0),
                    Words = d.Words + (e.Words ?? 0),
                    Sentences = d.Sentences + (e.Sentences ?? 0),
                    Questions = d.Questions + (e.Questions ?? 0),
                    Repetitions = d.Repetitions + (e.Repetitions ?? 0),
                    Pauses = d.Pauses + (e.Pauses ?? 0),
                    FilledPauses = d.FilledPauses + e.FilledPauses,
                    Fillers = d.Fillers + e.Fillers,
                    WeakWords = d.WeakWords + e.WeakWords,
                    Profanities = d.Profanities + e.Profanities,
                    DurationSeconds = d.DurationSeconds + e.DurationSeconds,
                    SpeechSeconds = d.SpeechSeconds + (e.SpeechSeconds ?? e.DurationSeconds),
                    PauseSeconds = d.PauseSeconds + (e.PauseSeconds ?? 0),
                    VocabularyWords = d.VocabularyWords + (e.Words >= minVocabularyWords ? e.Words.Value : 0),
                    VocabularyDistinct = d.VocabularyDistinct + (e.Words >= minVocabularyWords ? e.DistinctWords ?? 0 : 0),
                };
                foreach (var s in e.Spans) {
                    var map = s.Kind switch {
                        SpeechSpanKind.FilledPause or SpeechSpanKind.Filler => fillers,
                        SpeechSpanKind.Weak => weak,
                        _ => null,
                    };
                    if (map is not null)
                        map[s.Word] = map.GetValueOrDefault(s.Word) + 1;
                }
            }
            if (r.Run is { } run) {
                d = d with {
                    Runs = d.Runs + 1,
                    OwnTurns = d.OwnTurns + run.OwnTurns,
                    TotalTurns = d.TotalTurns + run.TotalTurns,
                    Responses = d.Responses + run.Responses,
                    Interruptions = d.Interruptions + run.Interruptions,
                    OwnSpeechSeconds = d.OwnSpeechSeconds + run.OwnSpeechSeconds,
                    TotalSpeechSeconds = d.TotalSpeechSeconds + run.TotalSpeechSeconds,
                    FairShareSeconds = d.FairShareSeconds + (run.Participants > 0 ? run.TotalSpeechSeconds / run.Participants : 0),
                    LongestMonologueSeconds = Math.Max(d.LongestMonologueSeconds, run.LongestMonologueSeconds),
                    ResponseGapSeconds = d.ResponseGapSeconds + run.ResponseGapSeconds * run.Responses,
                };
            }
        }
        return d with { FillerCounts = fillers.ToApiMap(), WeakWordCounts = weak.ToApiMap() };
    }

    public static CoachDay Merge(Moment day, IEnumerable<CoachDay> days)
    {
        var d = new CoachDay(day);
        var fillers = new Dictionary<string, int>();
        var weak = new Dictionary<string, int>();
        foreach (var x in days) {
            d = d with {
                Entries = d.Entries + x.Entries,
                TaggedEntries = d.TaggedEntries + x.TaggedEntries,
                Words = d.Words + x.Words,
                Sentences = d.Sentences + x.Sentences,
                Questions = d.Questions + x.Questions,
                Repetitions = d.Repetitions + x.Repetitions,
                Pauses = d.Pauses + x.Pauses,
                FilledPauses = d.FilledPauses + x.FilledPauses,
                Fillers = d.Fillers + x.Fillers,
                WeakWords = d.WeakWords + x.WeakWords,
                Profanities = d.Profanities + x.Profanities,
                Runs = d.Runs + x.Runs,
                OwnTurns = d.OwnTurns + x.OwnTurns,
                TotalTurns = d.TotalTurns + x.TotalTurns,
                Responses = d.Responses + x.Responses,
                Interruptions = d.Interruptions + x.Interruptions,
                DurationSeconds = d.DurationSeconds + x.DurationSeconds,
                SpeechSeconds = d.SpeechSeconds + x.SpeechSeconds,
                PauseSeconds = d.PauseSeconds + x.PauseSeconds,
                VocabularyWords = d.VocabularyWords + x.VocabularyWords,
                VocabularyDistinct = d.VocabularyDistinct + x.VocabularyDistinct,
                OwnSpeechSeconds = d.OwnSpeechSeconds + x.OwnSpeechSeconds,
                TotalSpeechSeconds = d.TotalSpeechSeconds + x.TotalSpeechSeconds,
                FairShareSeconds = d.FairShareSeconds + x.FairShareSeconds,
                LongestMonologueSeconds = Math.Max(d.LongestMonologueSeconds, x.LongestMonologueSeconds),
                ResponseGapSeconds = d.ResponseGapSeconds + x.ResponseGapSeconds,
            };
            foreach (var (w, c) in x.FillerCounts)
                fillers[w] = fillers.GetValueOrDefault(w) + c;
            foreach (var (w, c) in x.WeakWordCounts)
                weak[w] = weak.GetValueOrDefault(w) + c;
        }
        return d with { FillerCounts = fillers.ToApiMap(), WeakWordCounts = weak.ToApiMap() };
    }
}
```
Note `ResponseGapSeconds` in `CoachDay` is a **sum** (the run record carries a mean, so Build multiplies back); mean = `ResponseGapSeconds / Responses`. Check `ApiMap<TKey,TValue>`, `.ToApiMap()` and `ApiMap.Empty` exist in `ActualLab.Api`; if the extension is missing, construct with `new ApiMap<string,int>(dictionary)`.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Users.UnitTests --filter FullyQualifiedName~CoachDayBuilderTest`
Expected: green. (`BuildShouldAggregateRunsWithFairShare` expects `ResponseGapSeconds` ≈ 1.6 = 0.8·1 + 0.8·1.)

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Api/Users/Coach/CoachDay.cs tests/Users.UnitTests/Coach/CoachDayBuilderTest.cs
git commit -m "feat(coach): per-day aggregate and its builder"
```

---

### Task 3: Scoring settings and `CoachScoring` (pure)

**Files:**
- Modify: `src/dotnet/Users.Service/Module/UsersSettings.cs` (add `CoachScoringSettings Coach`)
- Create: `src/dotnet/Api/Users/Coach/CoachSummary.cs`, `src/dotnet/Users.Service/Coach/CoachScoring.cs`
- Test: `tests/Users.UnitTests/Coach/CoachScoringTest.cs`

**Interfaces:**
- Settings (on `UsersSettings.Coach`, section `UsersSettings:Coach`):
  ```csharp
  public class CoachScoringSettings {
      public int MinScoreWords { get; set; } = 200;
      public int MinVocabularyWords { get; set; } = 20;
      public int BadgeMinDelta { get; set; } = 3;
      public int TrailingDays { get; set; } = 30;
      public double PaceSlowWpm { get; set; } = 110; public double PaceFastWpm { get; set; } = 160;
      public Dictionary<string, (double Slow, double Fast)> PaceByLanguage { get; set; } = new();  // iso code → band; empty = English band everywhere
      public double FillerGoodRate { get; set; } = 0.03; public double FillerHighRate { get; set; } = 0.06;
      public double WeakGoodRate { get; set; } = 0.04; public double WeakHighRate { get; set; } = 0.06;
      public double RepetitionGoodRate { get; set; } = 0.04;
      public double SentenceShort { get; set; } = 8; public double SentenceLong { get; set; } = 20;
      public double TurnLowFactor { get; set; } = 0.5; public double TurnHighFactor { get; set; } = 1.5;
      public double PatienceLowSeconds { get; set; } = 0.5; public double PatienceHighSeconds { get; set; } = 1.5;
      public double MonologueFlagSeconds { get; set; } = 90;
      public int WeightFillers { get; set; } = 30; public int WeightPace { get; set; } = 25; public int WeightWeakWords { get; set; } = 20;
      public int WeightTurnTaking { get; set; } = 15; public int WeightSentenceLength { get; set; } = 10;
      public int TipPaceFastWpm { get; set; } = 170; public int TipPaceSlowWpm { get; set; } = 100; public int TipMinWords { get; set; } = 30;
      public int TipWordStep { get; set; } = 10;
      public CoachRollout Rollout { get; set; } = CoachRollout.AdminsAndFocusGroup;
      public string[] FocusGroupEmails { get; set; } = [];
  }
  public enum CoachRollout { AdminsAndFocusGroup = 0, Everyone = 1 }
  ```
  `Dictionary<string,(double,double)>` may not bind from configuration; use a nested class `PaceBand { double Slow; double Fast; }` and `Dictionary<string, PaceBand>`.
- Models (Api):
  ```csharp
  public enum CoachWindow { Today = 0, Week = 1, Month = 2, AllTime = 3 }
  public enum CoachMetricKind { Pace, Pauses, Fillers, WeakWords, Repetition, Profanity, Questions, SentenceLength, Vocabulary, TurnTaking, Patience, Interruptions, Monologue }
  public enum CoachBand { None = 0, Good = 1, Medium = 2, High = 3, Low = 4 }   // label chosen by the UI per kind
  public sealed partial record CoachChip(string Word, int Count);
  public sealed partial record CoachMetric(CoachMetricKind Kind, double? Value, double? Rate, CoachBand Band, ApiArray<CoachChip> Chips);
  public sealed partial record CoachSummary(CoachWindow Window, int? Score, int? ScoreDelta, int Words, int Entries, int TaggedEntries, ApiArray<CoachMetric> Metrics)
  { public static readonly CoachSummary None = new(CoachWindow.Today, null, null, 0, 0, 0, ApiArray<CoachMetric>.Empty); }
  public sealed partial record CoachOccurrence(ChatId ChatId, long EntryLid, int Start, int Length, Moment At);
  ```
- Scoring:
  ```csharp
  public static class CoachScoring {
      public static CoachSummary Summarize(CoachWindow window, CoachDay windowDay, CoachDay? trailingDay, CoachScoringSettings s, string? language = null);
      public static int? Score(CoachDay d, CoachScoringSettings s, string? language);     // null below MinScoreWords
      public static double SubScore(double value, double goodLow, double goodHigh);        // 100 inside, linear to 0 at 2× band-edge distance; goodLow may be 0
      public static CoachBand PaceBand(double wpm, CoachScoringSettings s, string? language);
  }
  ```
  Metric values: pace = `Words*60/SpeechSeconds`; filler rate = `(FilledPauses+Fillers)/Words` over **tagged entries' words only** — the day row has no "tagged words" sum, so Task 2's `CoachDay` gets one more field `TaggedWords` (add it there now: `Key(29)`, summed for `IsTagged` entries) and the rate uses it; weak rate likewise; repetition rate over `Words`; sentence length = `Words/Sentences`; vocabulary = `VocabularyDistinct/VocabularyWords`; turn ratio = `OwnSpeechSeconds/FairShareSeconds` (band: Low < TurnLowFactor, High > TurnHighFactor); patience = `ResponseGapSeconds/Responses`; monologue = `LongestMonologueSeconds` (High when ≥ flag); pauses = `Pauses / (SpeechSeconds/60)`. A metric whose denominator is 0 has `Value = null` and `Band = None`. Chips: top 5 of the word maps by count, desc.

  Sub-score bands: fillers good ≤ FillerGoodRate (edge distance = FillerGoodRate → 0 at 3×? **No**: "falling linearly to 0 at twice the band-edge distance" means: distance from the band edge equal to the band width beyond it. Define for a one-sided band `[0, good]`: 100 at ≤ good, 0 at ≥ 3·good (twice the edge value beyond it), linear between — fillers: 100 at 3%, 0 at 9%, matching the spec's example. For a two-sided band `[low, high]`: width = high−low; 100 inside, 0 at low−width and high+width.) Weights redistribute over metrics with a value.

- [ ] **Step 1: Write the failing tests**

```csharp
using ActualChat.Users.Coach;
using ActualChat.Users.Module;

namespace ActualChat.Users.UnitTests.Coach;

public class CoachScoringTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Moment Day = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);
    private static readonly CoachScoringSettings S = new();

    private static CoachDay DayWith(int words, int fillers = 0, int weak = 0, double speechSeconds = 100, int sentences = 10)
        => new CoachDay(Day) {
            Entries = 1, TaggedEntries = 1, Words = words, TaggedWords = words, Fillers = fillers, WeakWords = weak,
            SpeechSeconds = speechSeconds, DurationSeconds = speechSeconds, Sentences = sentences,
        };

    [Theory]
    [InlineData(0.03, 100)]
    [InlineData(0.06, 50)]
    [InlineData(0.09, 0)]
    [InlineData(0.20, 0)]
    public void SubScoreShouldFallLinearlyToZeroAtTwiceTheEdgeDistance(double rate, double expected)
        => CoachScoring.SubScore(rate, 0, S.FillerGoodRate).Should().BeApproximately(expected, 0.01);

    [Theory]
    [InlineData(135, 100)]
    [InlineData(160, 100)]
    [InlineData(185, 50)]
    [InlineData(210, 0)]
    [InlineData(85, 50)]
    public void TwoSidedSubScoreShouldUseTheBandWidthAsTheFallOff(double wpm, double expected)
        => CoachScoring.SubScore(wpm, S.PaceSlowWpm, S.PaceFastWpm).Should().BeApproximately(expected, 0.01);

    [Fact]
    public void ScoreShouldBeAbsentBelowMinWords()
    {
        // act
        var summary = CoachScoring.Summarize(CoachWindow.Today, DayWith(199), null, S);

        // assert
        summary.Score.Should().BeNull();
        summary.ScoreDelta.Should().BeNull();
        summary.Metrics.Should().Contain(m => m.Kind == CoachMetricKind.Pace && m.Value != null, "metric rows still show");
    }

    [Fact]
    public void ScoreShouldBe100ForASpeakerInsideEveryBand()
    {
        // arrange: 300 words in 120 s = 150 wpm, 2% fillers, 2% weak, 15 words/sentence, no runs
        var day = DayWith(300, fillers: 6, weak: 6, speechSeconds: 120, sentences: 20);

        // act
        var score = CoachScoring.Score(day, S, null);

        // assert
        score.Should().Be(100, "turn-taking has no data and its weight is redistributed");
    }

    [Fact]
    public void ScoreShouldWeightFillersAt30()
    {
        // arrange: fillers at 9% → sub-score 0; everything else perfect; turn-taking absent
        var day = DayWith(300, fillers: 27, weak: 6, speechSeconds: 120, sentences: 20);

        // act
        var score = CoachScoring.Score(day, S, null);

        // assert
        // weights without turn-taking: fillers 30, pace 25, weak 20, sentence 10 = 85; 55/85 ≈ 64.7
        score.Should().Be(65);
    }

    [Fact]
    public void BadgeShouldNeedAtLeastThreePoints()
    {
        // arrange
        var now = DayWith(300, fillers: 6, weak: 6, speechSeconds: 120, sentences: 20);
        var trailing = DayWith(300, fillers: 9, weak: 6, speechSeconds: 120, sentences: 20);

        // act
        var summary = CoachScoring.Summarize(CoachWindow.Week, now, trailing, S);

        // assert
        summary.Score.Should().Be(100);
        summary.ScoreDelta.Should().BeGreaterThanOrEqualTo(S.BadgeMinDelta);
        CoachScoring.Summarize(CoachWindow.Week, now, now, S).ScoreDelta.Should().BeNull("no change is no badge");
    }

    [Fact]
    public void ChipsShouldListTopWordsByCount()
    {
        // arrange
        var day = DayWith(300, fillers: 12) with {
            FillerCounts = new Dictionary<string, int> { ["you know"] = 7, ["like"] = 4, ["um"] = 1 }.ToApiMap(),
        };

        // act
        var fillers = CoachScoring.Summarize(CoachWindow.Today, day, null, S).Metrics.Single(m => m.Kind == CoachMetricKind.Fillers);

        // assert
        fillers.Chips.Select(c => c.Word).Should().Equal("you know", "like", "um");
        fillers.Band.Should().Be(CoachBand.Medium, "12/300 = 4%");
    }

    [Fact]
    public void TurnTakingShouldBeJudgedAgainstFairShare()
    {
        // arrange: own 10 s of 100 s with 2 participants → fair share 50 s → ratio 0.2 → Low
        var day = DayWith(300) with { Runs = 1, OwnSpeechSeconds = 10, TotalSpeechSeconds = 100, FairShareSeconds = 50 };

        // act
        var turn = CoachScoring.Summarize(CoachWindow.Today, day, null, S).Metrics.Single(m => m.Kind == CoachMetricKind.TurnTaking);

        // assert
        turn.Value.Should().BeApproximately(0.1, 0.001, "the value is the raw share");
        turn.Band.Should().Be(CoachBand.Low);
    }

    [Fact]
    public void PaceBandShouldUseTheLanguageOverride()
    {
        // arrange
        var s = new CoachScoringSettings { PaceByLanguage = { ["ru"] = new PaceBand { Slow = 90, Fast = 140 } } };

        // assert
        CoachScoring.PaceBand(150, s, "ru-RU").Should().Be(CoachBand.High);
        CoachScoring.PaceBand(150, s, "en-US").Should().Be(CoachBand.Good);
    }
}
```
`CoachBand.High` doubles as "fast"/"long"; `Low` as "slow"/"short". The UI labels per kind (Plan 3).

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Users.UnitTests --filter FullyQualifiedName~CoachScoringTest`
Expected: build errors.

- [ ] **Step 3: Implement.** Add `TaggedWords` to `CoachDay` (Key 29) and its summing in `CoachDayBuilder` (both `Build` and `Merge`). Add `CoachScoringSettings`/`PaceBand`/`CoachRollout` to `UsersSettings.cs` and `public CoachScoringSettings Coach { get; set; } = new ();` on `UsersSettings`. Create the models in `CoachSummary.cs`. Then:

```csharp
using ActualChat.Users.Module;

namespace ActualChat.Users.Coach;

/// <summary>
/// Bands, sub-scores and the 0-100 score over a merged day; the same bands serve the tab, the
/// trends and the tip rules.
/// </summary>
public static class CoachScoring
{
    private const int MaxChips = 5;

    public static CoachSummary Summarize(
        CoachWindow window, CoachDay d, CoachDay? trailing, CoachScoringSettings s, string? language = null)
    {
        var score = Score(d, s, language);
        int? delta = null;
        if (score is { } current && trailing is not null && Score(trailing, s, language) is { } previous) {
            var diff = current - previous;
            delta = Math.Abs(diff) >= s.BadgeMinDelta ? diff : null;
        }
        return new CoachSummary(window, score, delta, d.Words, d.Entries, d.TaggedEntries, Metrics(d, s, language));
    }

    public static int? Score(CoachDay d, CoachScoringSettings s, string? language)
    {
        if (d.Words < s.MinScoreWords)
            return null;

        var weighted = 0d;
        var weights = 0;
        Add(FillerRate(d), 0, s.FillerGoodRate, s.WeightFillers);
        Add(Pace(d), Band(s, language).Slow, Band(s, language).Fast, s.WeightPace);
        Add(WeakRate(d), 0, s.WeakGoodRate, s.WeightWeakWords);
        Add(TurnRatio(d), s.TurnLowFactor, s.TurnHighFactor, s.WeightTurnTaking);
        Add(SentenceLength(d), s.SentenceShort, s.SentenceLong, s.WeightSentenceLength);
        return weights == 0 ? null : (int)Math.Round(weighted / weights);

        void Add(double? value, double low, double high, int weight) {
            if (value is null)
                return;

            weighted += SubScore(value.Value, low, high) * weight;
            weights += weight;
        }
    }

    public static double SubScore(double value, double goodLow, double goodHigh)
    {
        if (value >= goodLow && value <= goodHigh)
            return 100;

        var fallOff = goodLow <= 0 ? 2 * goodHigh : goodHigh - goodLow;
        var distance = value < goodLow ? goodLow - value : value - goodHigh;
        return Math.Clamp(100 * (1 - distance / fallOff), 0, 100);
    }

    public static CoachBand PaceBand(double wpm, CoachScoringSettings s, string? language)
    {
        var band = Band(s, language);
        return wpm < band.Slow ? CoachBand.Low : wpm > band.Fast ? CoachBand.High : CoachBand.Good;
    }

    // Private methods

    private static ApiArray<CoachMetric> Metrics(CoachDay d, CoachScoringSettings s, string? language)
    {
        var fillerRate = FillerRate(d);
        var weakRate = WeakRate(d);
        var pace = Pace(d);
        var turn = TurnRatio(d);
        var patience = d.Responses > 0 ? d.ResponseGapSeconds / d.Responses : (double?)null;
        var sentence = SentenceLength(d);
        var speechMinutes = d.SpeechSeconds / 60;
        return ApiArray.New(
            new CoachMetric(CoachMetricKind.Pace, pace, null, pace is { } p ? PaceBand(p, s, language) : CoachBand.None, ApiArray<CoachChip>.Empty),
            new CoachMetric(CoachMetricKind.Pauses, speechMinutes > 0 ? d.Pauses / speechMinutes : null, null, CoachBand.None, ApiArray<CoachChip>.Empty),
            new CoachMetric(CoachMetricKind.Fillers, d.FilledPauses + d.Fillers, fillerRate,
                fillerRate is { } f ? (f < s.FillerGoodRate ? CoachBand.Good : f <= s.FillerHighRate ? CoachBand.Medium : CoachBand.High) : CoachBand.None,
                Chips(d.FillerCounts)),
            new CoachMetric(CoachMetricKind.WeakWords, d.WeakWords, weakRate,
                weakRate is { } w ? (w < s.WeakGoodRate ? CoachBand.Good : w <= s.WeakHighRate ? CoachBand.Medium : CoachBand.High) : CoachBand.None,
                Chips(d.WeakWordCounts)),
            new CoachMetric(CoachMetricKind.Repetition, d.Repetitions, d.Words > 0 ? (double)d.Repetitions / d.Words : null,
                d.Words > 0 ? ((double)d.Repetitions / d.Words < s.RepetitionGoodRate ? CoachBand.Good : CoachBand.High) : CoachBand.None, ApiArray<CoachChip>.Empty),
            new CoachMetric(CoachMetricKind.Profanity, d.Profanities, d.TaggedWords > 0 ? (double)d.Profanities / d.TaggedWords : null, CoachBand.None, ApiArray<CoachChip>.Empty),
            new CoachMetric(CoachMetricKind.Questions, d.Questions, null, CoachBand.None, ApiArray<CoachChip>.Empty),
            new CoachMetric(CoachMetricKind.SentenceLength, sentence, null,
                sentence is { } l ? (l < s.SentenceShort ? CoachBand.Low : l > s.SentenceLong ? CoachBand.High : CoachBand.Good) : CoachBand.None, ApiArray<CoachChip>.Empty),
            new CoachMetric(CoachMetricKind.Vocabulary, d.VocabularyWords > 0 ? d.VocabularyDistinct / d.VocabularyWords : null, null, CoachBand.None, ApiArray<CoachChip>.Empty),
            new CoachMetric(CoachMetricKind.TurnTaking, d.TotalSpeechSeconds > 0 ? d.OwnSpeechSeconds / d.TotalSpeechSeconds : null, turn,
                turn is { } t ? (t < s.TurnLowFactor ? CoachBand.Low : t > s.TurnHighFactor ? CoachBand.High : CoachBand.Good) : CoachBand.None, ApiArray<CoachChip>.Empty),
            new CoachMetric(CoachMetricKind.Patience, patience, null,
                patience is { } g ? (g < s.PatienceLowSeconds ? CoachBand.Low : g > s.PatienceHighSeconds ? CoachBand.High : CoachBand.Good) : CoachBand.None, ApiArray<CoachChip>.Empty),
            new CoachMetric(CoachMetricKind.Interruptions, d.Interruptions, d.OwnTurns > 0 ? (double)d.Interruptions / d.OwnTurns : null, CoachBand.None, ApiArray<CoachChip>.Empty),
            new CoachMetric(CoachMetricKind.Monologue, d.Runs > 0 ? d.LongestMonologueSeconds : null, null,
                d.Runs > 0 ? (d.LongestMonologueSeconds >= s.MonologueFlagSeconds ? CoachBand.High : CoachBand.Good) : CoachBand.None, ApiArray<CoachChip>.Empty));
    }

    private static ApiArray<CoachChip> Chips(ApiMap<string, int> counts)
        => counts.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Take(MaxChips).Select(x => new CoachChip(x.Key, x.Value)).ToApiArray();

    private static double? Pace(CoachDay d) => d.SpeechSeconds > 0 && d.Words > 0 ? d.Words * 60 / d.SpeechSeconds : null;
    private static double? FillerRate(CoachDay d) => d.TaggedWords > 0 ? (double)(d.FilledPauses + d.Fillers) / d.TaggedWords : null;
    private static double? WeakRate(CoachDay d) => d.TaggedWords > 0 ? (double)d.WeakWords / d.TaggedWords : null;
    private static double? SentenceLength(CoachDay d) => d.Sentences > 0 ? (double)d.Words / d.Sentences : null;
    private static double? TurnRatio(CoachDay d) => d.FairShareSeconds > 0 ? d.OwnSpeechSeconds / d.FairShareSeconds : null;

    private static PaceBand Band(CoachScoringSettings s, string? language)
    {
        var iso = language is null ? "" : Language.GetIsoCode(language);
        return s.PaceByLanguage.TryGetValue(iso, out var band) ? band : new PaceBand { Slow = s.PaceSlowWpm, Fast = s.PaceFastWpm };
    }
}
```
Wrap the long `new CoachMetric(...)` lines to the 120-char limit in the real file (one argument per line). Verify the theory numbers by hand: fillers 6% → distance 0.03, fall-off 0.06 → 50 ✓; pace 185 → distance 25, width 50 → 50 ✓; 85 → 50 ✓. `ScoreShouldWeightFillersAt30`: (0·30 + 100·25 + 100·20 + 100·10)/85 = 64.7 → 65 ✓ (`Math.Round` banker's rounding on 64.7 is fine).

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Users.UnitTests --filter "FullyQualifiedName~CoachScoringTest|FullyQualifiedName~CoachDayBuilderTest"`
Expected: green (the day builder tests keep passing with `TaggedWords` added).

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Api/Users/Coach src/dotnet/Users.Service/Coach/CoachScoring.cs src/dotnet/Users.Service/Module/UsersSettings.cs tests/Users.UnitTests/Coach
git commit -m "feat(coach): bands, sub-scores, score and metric rows over a merged day"
```

---

### Task 4: `UserCoachTip` and `CoachTipPolicy` (pure)

**Files:**
- Create: `src/dotnet/Api/Users/StoredSettings/UserCoachTip.cs`, `src/dotnet/Users.Service/Coach/CoachTipPolicy.cs`
- Modify: `src/dotnet/Api/StoredSettings.cs` (`[Union(56, typeof(UserCoachTip))]`), `src/dotnet/Users.Contracts/UserScopedKvasBackendExt.cs` and `src/dotnet/Api/Users/UserSettingsUIExt.cs` (accessors)
- Test: `tests/Users.UnitTests/Coach/CoachTipPolicyTest.cs`, plus a round-trip in `tests/Users.UnitTests/StoredSettingsSerializationTest.cs`

**Interfaces:**
```csharp
public enum CoachTipKind { None = 0, SlowDown = 1, SpeedUp = 2, Filler = 3, WeakWord = 4 }
[DataContract, MessagePackObject]
public sealed partial record UserCoachTip : StoredSettings, IHasOrigin, IHasKvasKey<UserCoachTip> {
    string Origin; CoachTipKind Kind; ChatId? ChatId; long EntryLid; string Word; int Count; int Wpm;
    ApiArray<string> Synonyms; Moment ShownAt; bool IsDismissed; Moment LastTipAt;
    public bool IsPending => Kind != CoachTipKind.None && !IsDismissed;
}
public static class CoachTipPolicy {
    // The record just written, the day before it was applied and after, the entry's spans with synonyms (from the event), the previous tip state
    public static UserCoachTip? Evaluate(CoachRecord record, CoachDay before, CoachDay after, ApiArray<SpeechSpan> spansWithSynonyms,
        UserCoachTip previous, UserCoachSettings settings, CoachScoringSettings s, Moment now, string? language);
}
```
Rules (spec): word tips win over pace; a filler's or weak word's count crossing a multiple of `TipWordStep` today fires; weak words need synonyms; pace tips need `Words >= TipMinWords` and wpm outside `[TipPaceSlowWpm, TipPaceFastWpm]`; nothing inside `settings.TipInterval` since `previous.LastTipAt`; nothing unless `settings is { IsCoachingEnabled: true, AreLiveTipsEnabled: true }`. Returns the new tip record (with `LastTipAt = now`, `IsDismissed = false`) or `null` for "no change".

- [ ] **Step 1: Write the failing tests**

```csharp
using ActualChat.Chat;
using ActualChat.Users.Coach;
using ActualChat.Users.Module;

namespace ActualChat.Users.UnitTests.Coach;

public class CoachTipPolicyTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Moment Now = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Moment Day = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);
    private static readonly CoachScoringSettings S = new();
    private static readonly UserCoachSettings OptedIn = new() { IsCoachingEnabled = true, AreLiveTipsEnabled = true };
    private static readonly UserCoachTip NoTip = new();

    private static CoachRecord Entry(int words, double speechSeconds, int fillers = 0, int weak = 0)
        => new (CoachRecordKind.Entry, "e1", UserId.New(), GroupChatId.New(), Now) {
            Entry = new CoachEntryRecord(1, "en-US", speechSeconds, speechSeconds, words, 2, 0, 0, words, 0, 0, true, 0, fillers, weak, 0, ApiArray<SpeechSpan>.Empty),
        };

    private static CoachDay DayWithFillers(int youKnow)
        => new CoachDay(Day) { FillerCounts = new Dictionary<string, int> { ["you know"] = youKnow }.ToApiMap() };

    [Fact]
    public void FillerCountCrossingTenShouldTip()
    {
        // act
        var tip = CoachTipPolicy.Evaluate(Entry(40, 20), DayWithFillers(9), DayWithFillers(11), ApiArray<SpeechSpan>.Empty, NoTip, OptedIn, S, Now, "en-US");

        // assert
        tip!.Kind.Should().Be(CoachTipKind.Filler);
        tip.Word.Should().Be("you know");
        tip.Count.Should().Be(11);
        tip.LastTipAt.Should().Be(Now);
        tip.IsDismissed.Should().BeFalse();
    }

    [Fact]
    public void FillerCountStayingBetweenStepsShouldNotTip()
        => CoachTipPolicy.Evaluate(Entry(40, 20), DayWithFillers(11), DayWithFillers(13), ApiArray<SpeechSpan>.Empty, NoTip, OptedIn, S, Now, "en-US")
            .Should().BeNull();

    [Fact]
    public void WeakWordShouldTipOnlyWithSynonyms()
    {
        // arrange
        var before = new CoachDay(Day) { WeakWordCounts = new Dictionary<string, int> { ["awesome"] = 9 }.ToApiMap() };
        var after = new CoachDay(Day) { WeakWordCounts = new Dictionary<string, int> { ["awesome"] = 10 }.ToApiMap() };
        var spans = ApiArray.New(new SpeechSpan(SpeechSpanKind.Weak, "awesome", 0, 7, ApiArray.New("excellent", "superb")));

        // act
        var with = CoachTipPolicy.Evaluate(Entry(40, 20, weak: 1), before, after, spans, NoTip, OptedIn, S, Now, "en-US");
        var without = CoachTipPolicy.Evaluate(Entry(40, 20, weak: 1), before, after, ApiArray<SpeechSpan>.Empty, NoTip, OptedIn, S, Now, "en-US");

        // assert
        with!.Kind.Should().Be(CoachTipKind.WeakWord);
        with.Synonyms.Should().Equal("excellent", "superb");
        without.Should().BeNull();
    }

    [Theory]
    [InlineData(60, 20, CoachTipKind.SlowDown)]   // 180 wpm
    [InlineData(30, 20, CoachTipKind.SpeedUp)]    // 90 wpm
    [InlineData(50, 20, CoachTipKind.None)]       // 150 wpm
    [InlineData(20, 5, CoachTipKind.None)]        // 240 wpm but under TipMinWords
    public void PaceTipShouldNeedEnoughWordsAndAnOutOfBandRate(int words, double seconds, CoachTipKind expected)
    {
        // act
        var tip = CoachTipPolicy.Evaluate(Entry(words, seconds), new CoachDay(Day), new CoachDay(Day), ApiArray<SpeechSpan>.Empty, NoTip, OptedIn, S, Now, "en-US");

        // assert
        (tip?.Kind ?? CoachTipKind.None).Should().Be(expected);
        if (tip is not null)
            tip.Wpm.Should().Be((int)Math.Round(words * 60 / seconds));
    }

    [Fact]
    public void WordTipShouldWinOverPace()
        => CoachTipPolicy.Evaluate(Entry(60, 20), DayWithFillers(9), DayWithFillers(10), ApiArray<SpeechSpan>.Empty, NoTip, OptedIn, S, Now, "en-US")!
            .Kind.Should().Be(CoachTipKind.Filler);

    [Fact]
    public void TipsShouldRespectTheInterval()
    {
        // arrange
        var recent = new UserCoachTip { Kind = CoachTipKind.Filler, LastTipAt = Now - TimeSpan.FromMinutes(2), IsDismissed = true };

        // act
        var tip = CoachTipPolicy.Evaluate(Entry(60, 20), DayWithFillers(9), DayWithFillers(10), ApiArray<SpeechSpan>.Empty, recent, OptedIn, S, Now, "en-US");

        // assert
        tip.Should().BeNull("5 minutes have not passed");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void TipsShouldRequireBothToggles(bool coaching, bool liveTips)
        => CoachTipPolicy.Evaluate(Entry(60, 20), DayWithFillers(9), DayWithFillers(10), ApiArray<SpeechSpan>.Empty, NoTip,
                new UserCoachSettings { IsCoachingEnabled = coaching, AreLiveTipsEnabled = liveTips }, S, Now, "en-US")
            .Should().BeNull();
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Users.UnitTests --filter FullyQualifiedName~CoachTipPolicyTest`
Expected: build errors.

- [ ] **Step 3: Implement**

`UserCoachTip.cs`:
```csharp
using ActualChat.Kvas;

namespace ActualChat.Users;

public enum CoachTipKind
{
    None = 0,
    SlowDown = 1,
    SpeedUp = 2,
    Filler = 3,
    WeakWord = 4,
}

/// <summary>
/// The user's latest live coaching tip and when the last one fired; the client shows it in its
/// chat until dismissed or replaced.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record UserCoachTip : StoredSettings, IHasOrigin, IHasKvasKey<UserCoachTip>
{
    public static string KvasKey => nameof(UserCoachTip);

    [DataMember, Key(0)] public string Origin { get; init; } = "";
    [DataMember, Key(1)] public CoachTipKind Kind { get; init; }
    [DataMember, Key(2)] public ChatId ChatId { get; init; }
    [DataMember, Key(3)] public long EntryLid { get; init; }
    [DataMember, Key(4)] public string Word { get; init; } = "";
    [DataMember, Key(5)] public int Count { get; init; }
    [DataMember, Key(6)] public int Wpm { get; init; }
    [DataMember, Key(7)] public ApiArray<string> Synonyms { get; init; }
    [DataMember, Key(8)] public Moment ShownAt { get; init; }
    [DataMember, Key(9)] public bool IsDismissed { get; init; }
    [DataMember, Key(10)] public Moment LastTipAt { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsPending => Kind != CoachTipKind.None && !IsDismissed;
}
```
Follow `UserCoachSettings.cs` for the attribute set; `ChatId` is a `StringIdentifier` and serializes like `AppReviewPromptState.PendingChatId` (use `ChatId?` if the non-nullable form does not round-trip in the KVAS test).

`CoachTipPolicy.cs`:
```csharp
using ActualChat.Chat;
using ActualChat.Users.Module;

namespace ActualChat.Users.Coach;

/// <summary>
/// Decides whether the record just logged earns a live tip: a word count crossing a step today
/// wins over pace, and nothing fires inside the user's tip interval.
/// </summary>
public static class CoachTipPolicy
{
    public static UserCoachTip? Evaluate(
        CoachRecord record,
        CoachDay before,
        CoachDay after,
        ApiArray<SpeechSpan> spansWithSynonyms,
        UserCoachTip previous,
        UserCoachSettings settings,
        CoachScoringSettings s,
        Moment now,
        string? language)
    {
        if (settings is not { IsCoachingEnabled: true, AreLiveTipsEnabled: true } || record.Entry is not { } entry)
            return null;
        if (previous.LastTipAt != default && now - previous.LastTipAt < settings.TipInterval)
            return null;

        var step = s.TipWordStep;
        var tip = CrossedStep(before.FillerCounts, after.FillerCounts, step) is { } filler
            ? new UserCoachTip { Kind = CoachTipKind.Filler, Word = filler.Word, Count = filler.Count }
            : CrossedStep(before.WeakWordCounts, after.WeakWordCounts, step) is { } weak
                && Synonyms(spansWithSynonyms, weak.Word) is { Count: > 0 } synonyms
                ? new UserCoachTip { Kind = CoachTipKind.WeakWord, Word = weak.Word, Count = weak.Count, Synonyms = synonyms }
                : PaceTip(entry, s, language);
        if (tip is null)
            return null;

        return tip with {
            ChatId = record.ChatId,
            EntryLid = entry.EntryLid,
            ShownAt = now,
            LastTipAt = now,
            IsDismissed = false,
        };
    }

    // Private methods

    private static (string Word, int Count)? CrossedStep(ApiMap<string, int> before, ApiMap<string, int> after, int step)
    {
        foreach (var (word, count) in after.OrderByDescending(x => x.Value)) {
            var previous = before.GetValueOrDefault(word);
            if (count / step > previous / step)
                return (word, count);
        }
        return null;
    }

    private static ApiArray<string> Synonyms(ApiArray<SpeechSpan> spans, string word)
        => spans.FirstOrDefault(sp => sp.Kind == SpeechSpanKind.Weak && sp.Word == word && sp.Synonyms.Count > 0)?.Synonyms
            ?? ApiArray<string>.Empty;

    private static UserCoachTip? PaceTip(CoachEntryRecord entry, CoachScoringSettings s, string? language)
    {
        if (entry.Words is not { } words || words < s.TipMinWords)
            return null;

        var seconds = entry.SpeechSeconds ?? entry.DurationSeconds;
        if (seconds <= 0)
            return null;

        var wpm = (int)Math.Round(words * 60 / seconds);
        if (wpm > s.TipPaceFastWpm)
            return new UserCoachTip { Kind = CoachTipKind.SlowDown, Wpm = wpm };
        if (wpm < s.TipPaceSlowWpm)
            return new UserCoachTip { Kind = CoachTipKind.SpeedUp, Wpm = wpm };

        return null;
    }
}
```
Pace tip thresholds use the tip settings (170/100), not the language band; per-language tip thresholds are a follow-up. Add the union tag, the two accessors (`UserCoachTip()` on both extension classes) and a KVAS round-trip test `UserCoachTipUnionRoundTrip` in `StoredSettingsSerializationTest` like `UserCoachSettingsUnionRoundTrip`.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Users.UnitTests --filter "FullyQualifiedName~CoachTipPolicyTest|FullyQualifiedName~StoredSettingsSerializationTest"`
Expected: green.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Api/Users/StoredSettings/UserCoachTip.cs src/dotnet/Api/StoredSettings.cs src/dotnet/Users.Contracts/UserScopedKvasBackendExt.cs src/dotnet/Api/Users/UserSettingsUIExt.cs src/dotnet/Users.Service/Coach/CoachTipPolicy.cs tests/Users.UnitTests
git commit -m "feat(coach): live tip record and the rules that fire it"
```

---

### Task 5: Entities, DbContext, migration, backend contract

**Files:**
- Create: `src/dotnet/Users.Service/Db/DbCoachEvent.cs`, `DbCoachDay.cs`
- Modify: `src/dotnet/Users.Service/Db/UsersDbContext.cs`
- Create: migration `Add_Coach` in `src/dotnet/Users.Service.Migration/Migrations/`
- Create: `src/dotnet/Users.Contracts/ICoachBackend.cs`
- Test: `tests/Users.UnitTests/Coach/DbCoachEventTest.cs`

**Interfaces:**
```csharp
[Table("CoachEvents")] public class DbCoachEvent : IRequirementTarget {
    string UserId; string SourceId; CoachRecordKind Kind; string ChatId; DateTime Day; DateTime OccurredAt;
    [Column(TypeName = "jsonb")] string Payload;   // SystemJsonSerializer of CoachRecord
    DbCoachEvent(); DbCoachEvent(CoachRecord r); CoachRecord ToModel(); void UpdateFrom(CoachRecord r);
}   // key (UserId, SourceId); index (UserId, Day)
[Table("CoachDays")] public class DbCoachDay : IHasVersion<long> {
    string UserId; DateTime Day; [ConcurrencyCheck] long Version; [Column(TypeName = "jsonb")] string Data;  // SystemJsonSerializer of CoachDay
    CoachDay ToModel(); void UpdateFrom(CoachDay d);
}   // key (UserId, Day)

[BackendService(nameof(HostRole.UsersBackend), ServiceMode.Distributed)]
public interface ICoachBackend : IComputeService, IBackendService {
    [ComputeMethod] Task<ApiArray<CoachDay>> ListDays(UserId userId, Range<Moment> dayRange, CancellationToken ct);   // inclusive start, exclusive end, by Day
    [ComputeMethod] Task<ApiArray<CoachOccurrence>> ListOccurrences(UserId userId, string word, Range<Moment> range, int limit, CancellationToken ct);
    [CommandHandler] Task OnRecord(CoachBackend_Record command, CancellationToken ct);
    [CommandHandler] Task OnRebuildDays(CoachBackend_RebuildDays command, CancellationToken ct);
    [EventHandler] Task OnCoachEntryAnalyzedEvent(CoachEntryAnalyzedEvent eventCommand, CancellationToken ct);
    [EventHandler] Task OnCoachConversationAnalyzedEvent(CoachConversationAnalyzedEvent eventCommand, CancellationToken ct);
}
CoachBackend_Record(UserId UserId, CoachRecord Record, bool IsRemoved) : ICommand<Unit>, IBackendCommand, IHasShardKey  // ShardKey = UserId.ShardKey
CoachBackend_RebuildDays(UserId UserId) : same shape
```
The day JSON blob avoids a 30-column table that changes with every metric; `CoachDay` is `[DataContract]` with STJ-friendly properties. `ApiMap` must serialize through `SystemJsonSerializer` — verify in the round-trip test; fall back to `ApiArray<CoachWordCount>` if not.

- [ ] **Step 1: Write the failing round-trip tests**

```csharp
using ActualChat.Chat;
using ActualChat.Users.Db;

namespace ActualChat.Users.UnitTests.Coach;

public class DbCoachEventTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void RecordShouldSurviveDbRoundTrip()
    {
        // arrange
        var chatId = GroupChatId.New();
        var record = new CoachRecord(CoachRecordKind.Entry, "src", UserId.New(), chatId, new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc)) {
            Entry = new CoachEntryRecord(5, "ru-RU", 12, 10, 20, 3, 1, 0, 18, 1, 2.5, true, 1, 2, 1, 0,
                ApiArray.New(new SpeechSpan(SpeechSpanKind.Filler, "ну", 0, 2, ApiArray<string>.Empty))),
        };

        // act
        var restored = new DbCoachEvent(record).ToModel();

        // assert
        restored.Should().BeEquivalentTo(record);
    }

    [Fact]
    public void DayShouldSurviveDbRoundTrip()
    {
        // arrange
        var day = new CoachDay(new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc)) {
            Entries = 3, Words = 120, TaggedWords = 100, Fillers = 4,
            FillerCounts = new Dictionary<string, int> { ["you know"] = 3, ["um"] = 1 }.ToApiMap(),
        };
        var dbDay = new DbCoachDay { UserId = "u1", Version = 1 };

        // act
        dbDay.UpdateFrom(day);
        var restored = dbDay.ToModel();

        // assert
        restored.Should().BeEquivalentTo(day);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Users.UnitTests --filter FullyQualifiedName~DbCoachEventTest`
Expected: build errors.

- [ ] **Step 3: Implement the entities**

```csharp
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.Db;

[Table("CoachEvents")]
[Index(nameof(UserId), nameof(Day))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbCoachEvent : IRequirementTarget
{
    public string UserId { get; set; } = "";
    public string SourceId { get; set; } = "";
    public CoachRecordKind Kind { get; set; }
    public string ChatId { get; set; } = "";

    public DateTime Day {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime OccurredAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    [Column(TypeName = "jsonb")]
    public string Payload { get; set; } = "{}";

    public DbCoachEvent() { }
    public DbCoachEvent(CoachRecord record) => UpdateFrom(record);

    public CoachRecord ToModel()
        => SystemJsonSerializer.Default.Read<CoachRecord>(Payload);

    public void UpdateFrom(CoachRecord record)
    {
        UserId = record.UserId.Value;
        SourceId = record.SourceId;
        Kind = record.Kind;
        ChatId = record.ChatId.Value;
        Day = record.Day.ToDateTimeClamped();
        OccurredAt = record.OccurredAt.ToDateTimeClamped();
        Payload = SystemJsonSerializer.Default.Write(record);
    }
}
```
`SystemJsonSerializer.Default.Write/Read` are what `DbUsageEvent` uses for attributes (check the exact member names there). `DbCoachDay` is the same shape with `UserId`, `Day`, `[ConcurrencyCheck] long Version`, `[Column(TypeName = "jsonb")] string Data`, `ToModel()`/`UpdateFrom(CoachDay)`.

In `UsersDbContext.cs`: `DbSet<DbCoachEvent> CoachEvents`, `DbSet<DbCoachDay> CoachDays`; in `OnModelCreating` after the usage block:
```csharp
        var coachEvent = model.Entity<DbCoachEvent>();
        coachEvent.HasKey(e => new { e.UserId, e.SourceId });
        coachEvent.Property(e => e.UserId).UseCollation("C");
        coachEvent.Property(e => e.SourceId).UseCollation("C");
        coachEvent.Property(e => e.ChatId).UseCollation("C");
        var coachDay = model.Entity<DbCoachDay>();
        coachDay.HasKey(e => new { e.UserId, e.Day });
        coachDay.Property(e => e.UserId).UseCollation("C");
```

- [ ] **Step 4: Migration**

```bash
cd src/dotnet/Users.Service.Migration && dotnet ef migrations add Add_Coach
```
Rename the class in both files to `_<timestamp>_Add_Coach` (keep the `[Migration("…")]` string). Inspect `Up`: `coach_events` with composite key and `ix_coach_events_user_id_day`, `coach_days` with composite key, `jsonb` columns, `C` collations. Needs local PostgreSQL (`ac_dev_users`).

- [ ] **Step 5: The contract**, `src/dotnet/Users.Contracts/ICoachBackend.cs`, modelled on `IUsageBackend.cs` (same attributes and usings; the commands carry `[DataContract, MessagePackObject]`, `[property: DataMember, Key(N)]`, the four ignore attributes on `ShardKey`).

- [ ] **Step 6: Run the round-trip tests and build the migration project**

Run: `dotnet test tests/Users.UnitTests --filter FullyQualifiedName~DbCoachEventTest && dotnet build src/dotnet/Users.Service.Migration`
Expected: green, build succeeded.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/Users.Service/Db src/dotnet/Users.Service.Migration/Migrations src/dotnet/Users.Contracts/ICoachBackend.cs tests/Users.UnitTests/Coach/DbCoachEventTest.cs
git commit -m "feat(coach): user-side log and day tables, migration and backend contract"
```

---

### Task 6: `CoachBackend`

**Files:**
- Create: `src/dotnet/Users.Service/Coach/CoachBackend.cs`
- Modify: `src/dotnet/Users.Service/Module/UsersServiceModule.cs` (`rpcHost.AddBackend<ICoachBackend, CoachBackend>();` next to the usage lines)
- Test: `tests/Users.IntegrationTests/CoachTest.cs`

**Interfaces:**
- Consumes: Tasks 1–5; `IServerKvasBackend.ForUser(userId).UserCoachSettings()` / `.UserCoachTip()`; `Clocks`; `Commander`.
- Produces: the registered `ICoachBackend`; `UserCoachTip` written to KVAS when a tip fires.

- [ ] **Step 1: Write the failing integration tests**

```csharp
using ActualChat.Chat;
using ActualChat.Hashing;
using ActualChat.Queues;
using ActualChat.Testing.Host;
using ActualChat.Users.Coach;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class CoachTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly Moment T0 = new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc);

    private ICoachBackend Backend => AppHost.Services.GetRequiredService<ICoachBackend>();
    private IServerKvasBackend Kvas => AppHost.Services.GetRequiredService<IServerKvasBackend>();

    private static CoachEntryAnalysis Entry(UserId userId, ChatId chatId, long lid, int words, double seconds, Moment at, int fillers = 0, string? word = null)
        => new (ChatEntryId.New(chatId, lid), 1) {
            AuthorId = AuthorId.New(chatId, 1),
            UserId = userId,
            BeginsAt = at,
            Language = Languages.English,
            DurationSeconds = seconds,
            SpeechSeconds = seconds,
            Words = words,
            Sentences = 2,
            DistinctWords = words,
            Fillers = fillers,
            TagState = CoachTagState.Tagged,
            PromptVersion = 1,
            Spans = word is null ? ApiArray<SpeechSpan>.Empty
                : Enumerable.Range(0, fillers).Select(i => new SpeechSpan(SpeechSpanKind.Filler, word, i * 10, word.Length, ApiArray<string>.Empty)).ToApiArray(),
            ContentHash = ChatEntryHashExt.GetContentHashString($"{lid}"),
        };

    private static CoachConversationAnalysis Run(UserId userId, ChatId chatId, long start, Moment endsAt)
        => new (ConversationId.New(chatId, start), AuthorId.New(chatId, 1), 1) {
            UserId = userId, ConversationVersion = start, EndsAt = endsAt,
            OwnSpeechSeconds = 30, TotalSpeechSeconds = 90, OwnTurns = 2, TotalTurns = 5, Participants = 3,
            LongestMonologueSeconds = 20, Responses = 1, ResponseGapSeconds = 0.9, Interruptions = 0,
        };

    private Task<CoachDay> WhenDay(UserId userId, Moment day, Func<CoachDay, bool> ready)
        => TestWait.When(async ct => {
            var days = await Backend.ListDays(userId, new Range<Moment>(day, day + TimeSpan.FromDays(1)), ct);
            days.Should().ContainSingle();
            ready(days[0]).Should().BeTrue();
            return days[0];
        });

    [Fact]
    public async Task EntryEventShouldBuildTheDay()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();

        // act
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 40, 20, T0, fillers: 2, word: "you know"), false));

        // assert
        var day = await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Entries == 1);
        day.Words.Should().Be(40);
        day.TaggedWords.Should().Be(40);
        day.Fillers.Should().Be(2);
        day.FillerCounts["you know"].Should().Be(2);
    }

    [Fact]
    public async Task RedeliveryShouldKeepOneRow()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        var e = new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 40, 20, T0), false);

        // act
        await Queues.Enqueue(e);
        await Queues.Enqueue(e);
        await Queues.WhenProcessing(TimeSpan.FromSeconds(1), default);

        // assert
        var day = await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Entries == 1);
        day.Words.Should().Be(40);
    }

    [Fact]
    public async Task ReEmitShouldReplaceTheRow()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 40, 20, T0), false));
        await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Words == 40);

        // act
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 55, 20, T0) with { Version = 2 }, false));

        // assert
        var day = await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Words == 55);
        day.Entries.Should().Be(1, "same source id replaces, never duplicates");
    }

    [Fact]
    public async Task RemovalShouldDropTheRowAndRebuildTheDay()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 40, 20, T0), false));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 2, 10, 5, T0), false));
        await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Entries == 2);

        // act
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 40, 20, T0), true));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 99, 1, 1, T0), true));

        // assert
        var day = await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Entries == 1);
        day.Words.Should().Be(10);
    }

    [Fact]
    public async Task RunEventShouldAddTurnTaking()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();

        // act
        await Queues.Enqueue(new CoachConversationAnalyzedEvent(Run(account.Id, chatId, 1, T0)));

        // assert
        var day = await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Runs == 1);
        day.OwnSpeechSeconds.Should().Be(30);
        day.FairShareSeconds.Should().Be(30);
    }

    [Fact]
    public async Task RebuildShouldReproduceTheDays()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 40, 20, T0), false));
        var before = await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Entries == 1);

        // act
        await Commander.Call(new CoachBackend_RebuildDays(account.Id));

        // assert
        var after = await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Entries == 1);
        after.Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task FillerStepShouldStoreATipForAnOptedInUser()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        await Kvas.ForUser(account.Id, isOutermost: true).UserCoachSettings().Set(new UserCoachSettings { IsCoachingEnabled = true });
        var now = Clocks.SystemClock.Now;

        // act
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 100, 60, now, fillers: 10, word: "like"), false));

        // assert
        var tip = await TestWait.When(async ct => {
            var t = await Kvas.ForUser(account.Id).UserCoachTip().Get(ct);
            t.IsPending.Should().BeTrue();
            return t;
        });
        tip.Kind.Should().Be(CoachTipKind.Filler);
        tip.Word.Should().Be("like");
        tip.ChatId.Should().Be(chatId);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task TipsShouldRequireBothToggles(bool coaching, bool liveTips)
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        await Kvas.ForUser(account.Id, isOutermost: true).UserCoachSettings()
            .Set(new UserCoachSettings { IsCoachingEnabled = coaching, AreLiveTipsEnabled = liveTips });

        // act
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 100, 60, Clocks.SystemClock.Now, fillers: 10, word: "like"), false));
        await WhenDay(account.Id, UsageDay.DayOf(Clocks.SystemClock.Now), d => d.Entries == 1);

        // assert
        (await Kvas.ForUser(account.Id).UserCoachTip().Get(default)).IsPending.Should().BeFalse();
    }

    [Fact]
    public async Task ListOccurrencesShouldReturnLatestFirst()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 40, 20, T0, fillers: 2, word: "like"), false));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 2, 40, 20, T0 + TimeSpan.FromHours(1), fillers: 1, word: "like"), false));
        await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Entries == 2);

        // act
        var occurrences = await Backend.ListOccurrences(account.Id, "like", new Range<Moment>(T0 - TimeSpan.FromDays(1), T0 + TimeSpan.FromDays(1)), 10, default);

        // assert
        occurrences.Should().HaveCount(3);
        occurrences[0].EntryLid.Should().Be(2, "latest first");
        occurrences.Should().OnlyContain(o => o.ChatId == chatId);
    }
}
```
`AppHostFixture` is `tests/Users.IntegrationTests/Collections/UserCollection.cs`'s fixture; `Queues`, `Commander`, `Clocks` come from `SharedAppHostTestBase`. `SignInAsUniqueBob` yields a non-guest account.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Users.IntegrationTests --filter FullyQualifiedName~CoachTest`
Expected: build errors (`CoachBackend_RebuildDays` exists from Task 5; `ICoachBackend` has no registered implementation → runtime failures once it builds).

- [ ] **Step 3: Implement**

```csharp
using ActualChat.Chat;
using ActualChat.Users.Db;
using ActualChat.Users.Module;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.Coach;

/// <summary>
/// The user side of the speech coach: an append-or-replace log of the chat-side analyses keyed by
/// source id, day rows rebuilt from it, and the live tip decided after each entry record.
/// </summary>
public class CoachBackend(IServiceProvider services)
    : ShardedDbServiceBase<UsersDbContext>(services), ICoachBackend
{
    private UsersSettings Settings { get; } = services.GetRequiredService<UsersSettings>();
    private IServerKvasBackend ServerKvasBackend => field ??= Services.GetRequiredService<IServerKvasBackend>();

    // [ComputeMethod]
    public virtual async Task<ApiArray<CoachDay>> ListDays(UserId userId, Range<Moment> dayRange, CancellationToken cancellationToken)
    {
        var days = await ListAllDays(userId, cancellationToken).ConfigureAwait(false);
        return days.Where(d => d.Day >= dayRange.Start && d.Day < dayRange.End).ToApiArray();
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<CoachOccurrence>> ListOccurrences(
        UserId userId, string word, Range<Moment> range, int limit, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var start = range.Start.ToDateTimeClamped();
        var end = range.End.ToDateTimeClamped();
        var rows = await dbContext.CoachEvents
            .Where(e => e.UserId == userId.Value && e.Kind == CoachRecordKind.Entry && e.OccurredAt >= start && e.OccurredAt < end)
            .OrderByDescending(e => e.OccurredAt)
            .Take(limit * 5)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows
            .Select(r => r.ToModel())
            .SelectMany(r => r.Entry!.Spans
                .Where(s => s.Word == word)
                .Select(s => new CoachOccurrence(r.ChatId, r.Entry.EntryLid, s.Start, s.Length, r.OccurredAt)))
            .OrderByDescending(o => o.At).ThenByDescending(o => o.Start)
            .Take(limit)
            .ToApiArray();
    }

    // [CommandHandler]
    public virtual async Task OnRecord(CoachBackend_Record command, CancellationToken cancellationToken)
    {
        var (userId, record, isRemoved) = command;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            if (context.Operation.Items.KeylessGet<bool>())
                _ = ListAllDays(userId, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var dbEvent = await dbContext.CoachEvents.ForUpdate()
            .FirstOrDefaultAsync(e => e.UserId == userId.Value && e.SourceId == record.SourceId, cancellationToken)
            .ConfigureAwait(false);
        var hasChanges = false;
        if (isRemoved) {
            if (dbEvent is not null) {
                dbContext.Remove(dbEvent);
                hasChanges = true;
            }
        }
        else if (dbEvent is null) {
            dbContext.Add(new DbCoachEvent(record));
            hasChanges = true;
        }
        else if (dbEvent.Payload != SystemJsonSerializer.Default.Write(record)) {
            dbEvent.UpdateFrom(record);
            hasChanges = true;
        }
        if (!hasChanges)
            return;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await RebuildDay(dbContext, userId, record.Day, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(true);
    }

    // [CommandHandler]
    public virtual async Task OnRebuildDays(CoachBackend_RebuildDays command, CancellationToken cancellationToken)
    {
        var userId = command.UserId;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            _ = ListAllDays(userId, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var days = await dbContext.CoachEvents
            .Where(e => e.UserId == userId.Value)
            .Select(e => e.Day)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var stale = await dbContext.CoachDays.ForUpdate()
            .Where(d => d.UserId == userId.Value && !days.Contains(d.Day))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        dbContext.RemoveRange(stale);
        foreach (var day in days)
            await RebuildDay(dbContext, userId, day, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(true);
    }

    // [EventHandler]
    public virtual async Task OnCoachEntryAnalyzedEvent(CoachEntryAnalyzedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var analysis = eventCommand.Analysis;
        if (!IsTrackedUser(analysis.UserId))
            return;

        var record = CoachRecord.FromEntry(analysis);
        var day = record.Day;
        var before = await GetDay(analysis.UserId, day, cancellationToken).ConfigureAwait(false);
        await Commander.Call(new CoachBackend_Record(analysis.UserId, record, eventCommand.IsRemoved), true, cancellationToken)
            .ConfigureAwait(false);
        if (eventCommand.IsRemoved)
            return;

        await EvaluateTip(analysis, record, before, cancellationToken).ConfigureAwait(false);
    }

    // [EventHandler]
    public virtual async Task OnCoachConversationAnalyzedEvent(CoachConversationAnalyzedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var analysis = eventCommand.Analysis;
        if (!IsTrackedUser(analysis.UserId))
            return;

        await Commander.Call(new CoachBackend_Record(analysis.UserId, CoachRecord.FromRun(analysis), false), true, cancellationToken)
            .ConfigureAwait(false);
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<ApiArray<CoachDay>> ListAllDays(UserId userId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var rows = await dbContext.CoachDays
            .Where(d => d.UserId == userId.Value)
            .OrderBy(d => d.Day)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(r => r.ToModel()).ToApiArray();
    }

    // Private methods

    private async Task<CoachDay> GetDay(UserId userId, Moment day, CancellationToken cancellationToken)
    {
        var days = await ListDays(userId, new Range<Moment>(day, day + TimeSpan.FromDays(1)), cancellationToken).ConfigureAwait(false);
        return days.Count > 0 ? days[0] : new CoachDay(day);
    }

    private async Task RebuildDay(UsersDbContext dbContext, UserId userId, Moment day, CancellationToken cancellationToken)
    {
        var dbDay = day.ToDateTimeClamped();
        var events = await dbContext.CoachEvents
            .Where(e => e.UserId == userId.Value && e.Day == dbDay)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var model = CoachDayBuilder.Build(day, events.Select(e => e.ToModel()), Settings.Coach.MinVocabularyWords);
        var row = await dbContext.CoachDays.ForUpdate()
            .FirstOrDefaultAsync(d => d.UserId == userId.Value && d.Day == dbDay, cancellationToken)
            .ConfigureAwait(false);
        if (events.Count == 0) {
            if (row is not null)
                dbContext.Remove(row);
            return;
        }
        if (row is null) {
            row = new DbCoachDay { UserId = userId.Value, Day = dbDay };
            dbContext.Add(row);
        }
        row.UpdateFrom(model);
        row.Version = VersionGenerator.NextVersion(row.Version);
    }

    // Runs after the record command, outside any DB operation, like the review prompt does
    private async Task EvaluateTip(CoachEntryAnalysis analysis, CoachRecord record, CoachDay before, CancellationToken cancellationToken)
    {
        try {
            var kvas = ServerKvasBackend.ForUser(analysis.UserId);
            var settings = await kvas.UserCoachSettings().Get(cancellationToken).ConfigureAwait(false);
            if (settings is not { IsCoachingEnabled: true, AreLiveTipsEnabled: true })
                return;

            var after = await GetDay(analysis.UserId, record.Day, cancellationToken).ConfigureAwait(false);
            var tipAccessor = kvas.UserCoachTip();
            var previous = await tipAccessor.Get(cancellationToken).ConfigureAwait(false);
            var tip = CoachTipPolicy.Evaluate(
                record, before, after, analysis.Spans, previous, settings, Settings.Coach, Clocks.SystemClock.Now, analysis.Language?.Value);
            if (tip is not null)
                await tipAccessor.Set(tip with { Origin = previous.Origin }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Tip evaluation failed for {UserId}", analysis.UserId);
        }
    }

    private static bool IsTrackedUser(UserId userId)
        => userId is { IsGuest: false } && !userId.Value.IsNullOrEmpty();
}
```
Notes: `GetDay` before the record is a compute read; the record command invalidates `ListAllDays`, so the `after` read is fresh. The tip's KVAS `Set` from an event handler is outside the DB operation, so `isOutermost` is not required (`ForUser(userId)` default), matching `MarkReviewPromptPending`. Wrap long lines. Register: `rpcHost.AddBackend<ICoachBackend, CoachBackend>();` after the usage lines in `UsersServiceModule.cs`.

- [ ] **Step 4: Run the integration tests**

Run: `dotnet test tests/Users.IntegrationTests --filter FullyQualifiedName~CoachTest`
Expected: all green. If `RedeliveryShouldKeepOneRow` sees two rows, the composite key is missing; if `FillerStepShouldStoreATipForAnOptedInUser` never sees a tip, check that `GetDay` after the command observes the rebuilt row (invalidation of `ListAllDays`).

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Users.Service/Coach/CoachBackend.cs src/dotnet/Users.Service/Module/UsersServiceModule.cs tests/Users.IntegrationTests/CoachTest.cs
git commit -m "feat(coach): user-side log, day rebuild and live tips"
```

---

### Task 7: `ICoach` frontend, account deletion cleanup

**Files:**
- Create: `src/dotnet/Api.Contracts/Users/ICoach.cs`, `src/dotnet/Users.Service/Coach/Coach.cs`
- Modify: `src/dotnet/Users.Service/Module/UsersServiceModule.cs` (`rpcHost.AddApi<ICoach, Coach>()`), `src/dotnet/Api.Contracts/Module/ApiContractsModule.cs` (`fusion.AddClient<ICoach>()`), `src/dotnet/Users.Service/AccountsBackend.cs` (delete coach rows in `OnDelete`)
- Test: add to `tests/Users.IntegrationTests/CoachTest.cs`

**Interfaces:**
```csharp
public interface ICoach : IComputeService {
    [ComputeMethod] Task<bool> IsEnabled(Session session, CancellationToken ct);            // master switch AND rollout rule
    [ComputeMethod] Task<CoachSummary> GetOwnSummary(Session session, CoachWindow window, CancellationToken ct);
    [ComputeMethod] Task<ApiArray<CoachDay>> ListOwnDays(Session session, Range<Moment> dayRange, CancellationToken ct);
    [ComputeMethod] Task<UserCoachTip?> GetPendingTip(Session session, CancellationToken ct);
    [ComputeMethod] Task<ApiArray<CoachOccurrence>> ListOwnOccurrences(Session session, string word, CoachWindow window, CancellationToken ct);
    [CommandHandler] Task OnDismissTip(Coach_DismissTip command, CancellationToken ct);
    [CommandHandler] Task OnRebuildOwnDays(Coach_RebuildOwnDays command, CancellationToken ct);   // admin-only
}
public sealed partial record Coach_DismissTip : ApiCommand<Unit>;
public sealed partial record Coach_RebuildOwnDays : ApiCommand<Unit>;
```
Windows: Today = [today, tomorrow); Week = [today−6d, tomorrow); Month = [today−29d, tomorrow); AllTime = [epoch, tomorrow). Trailing = [windowStart−TrailingDays, windowStart), only for Today/Week/Month. "Today" is the UTC day of `Clocks.SystemClock.Now`; the client's local day is a Plan 3 refinement.

- [ ] **Step 1: Write the failing tests** (append to `CoachTest`):

```csharp
    private ICoach Coach => AppHost.Services.GetRequiredService<ICoach>();

    [Fact]
    public async Task SummaryShouldScoreTheWindowAndDismissTheTip()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        await Kvas.ForUser(account.Id, isOutermost: true).UserCoachSettings().Set(new UserCoachSettings { IsCoachingEnabled = true });
        var now = Clocks.SystemClock.Now;
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 300, 120, now, fillers: 10, word: "like"), false));
        await WhenDay(account.Id, UsageDay.DayOf(now), d => d.Entries == 1);

        // act
        var summary = await Coach.GetOwnSummary(tester.Session, CoachWindow.Today, default);
        var tip = await TestWait.When(async ct => {
            var t = await Coach.GetPendingTip(tester.Session, ct);
            t.Should().NotBeNull();
            return t!;
        });
        await tester.Commander.Call(new Coach_DismissTip { Session = tester.Session });

        // assert
        summary.Words.Should().Be(300);
        summary.Score.Should().NotBeNull();
        summary.Metrics.Single(m => m.Kind == CoachMetricKind.Fillers).Chips.Should().ContainSingle(c => c.Word == "like" && c.Count == 10);
        tip.Kind.Should().Be(CoachTipKind.Filler);
        await TestWait.When(async ct => (await Coach.GetPendingTip(tester.Session, ct)).Should().BeNull());
    }

    [Fact]
    public async Task GuestShouldGetEmptySummary()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);

        // act
        var summary = await Coach.GetOwnSummary(tester.Session, CoachWindow.Week, default);
        var tip = await Coach.GetPendingTip(tester.Session, default);

        // assert
        summary.Should().Be(CoachSummary.None with { Window = CoachWindow.Week });
        tip.Should().BeNull();
    }

    [Fact]
    public async Task ListOwnOccurrencesShouldStayWithinTheWindow()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        var now = Clocks.SystemClock.Now;
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 40, 20, now, fillers: 1, word: "like"), false));
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 2, 40, 20, now - TimeSpan.FromDays(40), fillers: 1, word: "like"), false));
        await WhenDay(account.Id, UsageDay.DayOf(now), d => d.Entries == 1);

        // act
        var month = await Coach.ListOwnOccurrences(tester.Session, "like", CoachWindow.Month, default);
        var all = await Coach.ListOwnOccurrences(tester.Session, "like", CoachWindow.AllTime, default);

        // assert
        month.Should().ContainSingle().Which.EntryLid.Should().Be(1);
        all.Should().HaveCount(2);
    }

    [Fact]
    public async Task DeletingTheAccountShouldRemoveCoachRows()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var chatId = GroupChatId.New();
        await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 40, 20, T0), false));
        await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Entries == 1);

        // act
        await tester.Commander.Call(new Accounts_DeleteOwn(tester.Session));

        // assert
        var dbHub = AppHost.Services.GetRequiredService<DbHub<UsersDbContext>>();
        await using var dbContext = await dbHub.CreateDbContext(false);
        (await dbContext.CoachEvents.CountAsync(e => e.UserId == account.Id.Value)).Should().Be(0);
        (await dbContext.CoachDays.CountAsync(d => d.UserId == account.Id.Value)).Should().Be(0);
    }
```
Check the exact delete-own command name in `Api.Contracts/Users/IAccounts.cs` (grep `DeleteOwn`); there may be a test that already deletes an account to copy from.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Users.IntegrationTests --filter FullyQualifiedName~CoachTest`
Expected: build errors on `ICoach`.

- [ ] **Step 3: Implement**

`ICoach.cs` in `Api.Contracts/Users`, namespace `ActualChat.Users`, modelled on `IUsage.cs` (the two commands are `[DataContract, MessagePackObject] public sealed partial record Coach_DismissTip : ApiCommand<Unit>;`).

`Coach.cs`:
```csharp
using ActualChat.Chat;
using ActualChat.Users.Module;

namespace ActualChat.Users.Coach;

public class Coach(IServiceProvider services) : ICoach
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IChatCoach ChatCoach { get; } = services.GetRequiredService<IChatCoach>();
    private ICoachBackend Backend { get; } = services.GetRequiredService<ICoachBackend>();
    private IServerKvasBackend ServerKvasBackend { get; } = services.GetRequiredService<IServerKvasBackend>();
    private UsersSettings Settings { get; } = services.GetRequiredService<UsersSettings>();
    private MomentClockSet Clocks { get; } = services.Clocks();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    public virtual async Task<bool> IsEnabled(Session session, CancellationToken cancellationToken)
    {
        if (!await ChatCoach.IsEnabled(session, cancellationToken).ConfigureAwait(false))
            return false;

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return false;

        return Settings.Coach.Rollout == CoachRollout.Everyone
            || account.IsAdmin
            || Settings.Coach.FocusGroupEmails.Contains(account.Email ?? "", StringComparer.OrdinalIgnoreCase);
    }

    // [ComputeMethod]
    public virtual async Task<CoachSummary> GetOwnSummary(Session session, CoachWindow window, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return CoachSummary.None with { Window = window };

        var (range, trailing) = Ranges(window);
        var days = await Backend.ListDays(account.Id, range, cancellationToken).ConfigureAwait(false);
        var merged = CoachDayBuilder.Merge(range.Start, days);
        CoachDay? trailingDay = null;
        if (trailing is { } t) {
            var trailingDays = await Backend.ListDays(account.Id, t, cancellationToken).ConfigureAwait(false);
            trailingDay = trailingDays.Count > 0 ? CoachDayBuilder.Merge(t.Start, trailingDays) : null;
        }
        var languageSettings = await ServerKvasBackend.ForUser(account.Id).UserLanguageSettings().Get(cancellationToken).ConfigureAwait(false);
        return CoachScoring.Summarize(window, merged, trailingDay, Settings.Coach, languageSettings.Primary.Value);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<CoachDay>> ListOwnDays(Session session, Range<Moment> dayRange, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return account.IsGuestOrNull()
            ? ApiArray<CoachDay>.Empty
            : await Backend.ListDays(account.Id, dayRange, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<UserCoachTip?> GetPendingTip(Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return null;

        var tip = await ServerKvasBackend.ForUser(account.Id).UserCoachTip().Get(cancellationToken).ConfigureAwait(false);
        return tip.IsPending ? tip : null;
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<CoachOccurrence>> ListOwnOccurrences(
        Session session, string word, CoachWindow window, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull() || word.IsNullOrWhiteSpace())
            return ApiArray<CoachOccurrence>.Empty;

        var (range, _) = Ranges(window);
        return await Backend.ListOccurrences(account.Id, word.Trim().ToLower(), range, 20, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnDismissTip(Coach_DismissTip command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeActive);
        var accessor = ServerKvasBackend.ForUser(account.Id, isOutermost: true).UserCoachTip();
        var tip = await accessor.Get(cancellationToken).ConfigureAwait(false);
        if (tip.IsPending)
            await accessor.Set(tip with { IsDismissed = true }, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRebuildOwnDays(Coach_RebuildOwnDays command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeAdmin);
        await Commander.Call(new CoachBackend_RebuildDays(account.Id), true, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private (Range<Moment> Window, Range<Moment>? Trailing) Ranges(CoachWindow window)
    {
        var today = UsageDay.DayOf(Clocks.SystemClock.Now);
        var tomorrow = today + TimeSpan.FromDays(1);
        var start = window switch {
            CoachWindow.Today => today,
            CoachWindow.Week => today - TimeSpan.FromDays(6),
            CoachWindow.Month => today - TimeSpan.FromDays(29),
            _ => Moment.EpochStart,
        };
        var trailing = window == CoachWindow.AllTime
            ? (Range<Moment>?)null
            : new Range<Moment>(start - TimeSpan.FromDays(Settings.Coach.TrailingDays), start);
        return (new Range<Moment>(start, tomorrow), trailing);
    }
}
```
Whether `Users.Service` may take a dependency on `IChatCoach` (defined in `Api.Contracts`, which it references): yes, the same way `Usage` uses `IAccounts`. `AccountFull.Email` — check the property name (`Email`? `Emails`?). Register the API and the client. In `AccountsBackend.OnDelete`, next to the `UserSessions` delete: `CoachEvents` and `CoachDays` `.Where(x => x.UserId == userId.Value).ExecuteDeleteAsync(...)`, and in its invalidation block nothing extra (the account is gone; `ListAllDays` for a deleted user goes stale harmlessly).

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Users.IntegrationTests --filter FullyQualifiedName~CoachTest && dotnet test tests/Users.UnitTests`
Expected: green.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Api.Contracts/Users/ICoach.cs src/dotnet/Users.Service/Coach/Coach.cs src/dotnet/Users.Service/Module/UsersServiceModule.cs src/dotnet/Api.Contracts/Module/ApiContractsModule.cs src/dotnet/Users.Service/AccountsBackend.cs tests/Users.IntegrationTests/CoachTest.cs
git commit -m "feat(coach): ICoach serves the caller's score, days, tip and occurrences"
```

---

### Task 8: Client flag, docs, hand-off

**Files:**
- Create: `src/dotnet/UI.Blazor/Services/Features/Features_EnableSpeechCoach.cs`
- Modify: `docs/api-index.md`, `docs/api-index-full.md` (Users sections), `docs/superpowers/specs/2026-09-25-speech-coach-design.md` (note the two deferrals from *Global Constraints* under *Failure handling* / *LLM work*)

- [ ] **Step 1: The flag**

```csharp
namespace ActualChat.UI.Blazor.Services;

// ReSharper disable once InconsistentNaming
public sealed class Features_EnableSpeechCoach : FeatureDef<bool>, IClientFeatureDef
{
    public override Task<bool> Compute(IServiceProvider services, CancellationToken cancellationToken)
        => services.GetRequiredService<ICoach>().IsEnabled(services.Session(), cancellationToken);
}
```
`ICoach` is the RPC client on the client side (`fusion.AddClient<ICoach>()` from Task 7). No test beyond compilation: the rule it reads is tested in Task 7.

- [ ] **Step 2: Docs.** Add `ICoach`, `ICoachBackend`, `CoachRecord`, `CoachDay`, `CoachSummary`, `UserCoachTip`, `CoachScoring`, `CoachTipPolicy`, `CoachBackend`, `Coach`, `Features_EnableSpeechCoach` to both index files in their Users / Api / Api.Contracts / UI.Blazor sections, following Plan 1's wording. In the spec, add one line under *LLM work* ("The lexicon job is deferred to a follow-up; see plan 2.") and one under *Failure handling* ("The per-user daily cap is deferred; see plan 2.").

- [ ] **Step 3: Build the server and run the Users suites**

Run: `dotnet build src/dotnet/App.Server && dotnet test tests/Users.UnitTests && dotnet test tests/Users.IntegrationTests --filter FullyQualifiedName~CoachTest`
Expected: success, green.

- [ ] **Step 4: Commit**

```bash
git add src/dotnet/UI.Blazor/Services/Features/Features_EnableSpeechCoach.cs docs/api-index.md docs/api-index-full.md docs/superpowers/specs/2026-09-25-speech-coach-design.md
git commit -m "feat(coach): client rollout flag; index the user-side coach types"
```

---

## Deferred to a follow-up

- The per-language lexicon (`CoachLexicons`) and the "tagger off" matching path.
- The per-user daily tagger cap (`MaxTaggerCallsPerUserPerDay`), which needs a chat-side check against user-side counts.
- Per-language tip thresholds (tips use the English 100/170 wpm thresholds; the tab uses per-language bands).
- The client's local day instead of the UTC day for "Today" (Plan 3 can pass a day offset).
- Usage-stats rows are not deleted on account deletion either; out of scope here, worth its own fix.
