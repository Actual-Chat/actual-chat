# Coach Panel Redesign (v1.1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the shipped v1 Coach panel into Recent / Progress / Skills with per-language skills, a focus skill, conversation cards, positive reinforcement, chat and place scope, and a settings page built from the app's settings vocabulary.

**Architecture:** The event log (`CoachEvents`) stays the single source of truth. Day rows gain a language key and are rebuilt from the log; conversations, weekly deltas, milestones, focus and the score explanation are pure functions over records or days in `Api/Users/Coach`, exposed through new `ICoach` compute methods and commands on the existing `Coach` / `CoachBackend` services. Scope (per chat, per place, peer skip) is decided on the chat shard before analysis. The UI is rebuilt in `UI.Blazor.App/Components/Coach` from `Card`, `CardItem`, `Tile`, `TileItem`, `TileTopic`, `Toggle` and `TabPanel`.

**Tech Stack:** .NET / ActualLab.Fusion compute services and commands, EF Core + PostgreSQL migrations, MessagePack array-form records, KVAS stored settings, Blazor (server + WASM) with bUnit 2 tests, xUnit + FluentAssertions, JSONC string catalogs.

**Spec:** `docs/superpowers/specs/2026-09-29-coach-panel-redesign-design.md` (read it first; the v1 spec it builds on is on `feat/4829-speech-coach-bak`).

## Global Constraints

- Follow `docs/CODING_STYLE.md`: 120-char lines, no `Async` suffix, no XML docs on members, comments only for non-obvious invariants, `field ??=` lazy DI, sealed by default, invariant globalization, tests named `<Subject>Should<Behavior>` with `// arrange / act / assert`, FluentAssertions.
- Waits in tests only through `ActualChat.Testing.TestWait` (`docs/testing/waiting.md`).
- Every user-visible string is a `Coach_*` key in all 19 hand-written catalogs under `src/dotnet/Localization/Resources/Strings.<iso>.json` (en, bg, bs, cs, de, es, fr, hi, id, it, ja, ko, pl, pt, ru, tr, uk, vi, zh) plus a typed member in `src/dotnet/Localization/Resources/LocalizedStringsLocalizerExt.cs`; then run `scripts/derive-bcms.cmd` and `scripts/derive-max.cmd`. `AppLocalizationTest` gates it.
- MessagePack records are array-form: only append keys, never move or reuse one (`serialization-schema-evolution-rules`).
- Work stays on `feat/4829-speech-coach`; commit per task; never push unless asked.
- The user's `server-loop` owns the dev server: restart with `curl -sk https://local.voxt.ai/health/stop`; CSS/TS-only changes with `touch tmp/server-loop-rebundle`; never use server-start/stop.
- Build by test project, not by `CI.slnf` (it is stale locally).
- Bands, weights and thresholds live in `CoachScoringSettings`; nothing numeric is hard-coded in the UI except the `MinScoreWords` copy already there.

## Review Focus

1. A user whose spoken languages changed (English added last week) opens Skills: the chip row must list only languages with words in the last 30 days, and the selected language falls back to the most spoken when the remembered one has no words. Test in Task 6 (`ListOwnLanguages`) and Task 10.
2. A conversation of one 45-minute monologue with no gap: grouping must not split it and must not loop; a chat with two own entries 31 minutes apart must yield two conversations. Test in Task 4.
3. An entry in a place chat where the place is switched off but the chat itself is switched on: the chat flag wins, the entry is analysed. Test in Task 3.
4. A bilingual user's Russian day rows and English day rows on the same date: `ListDays(language: null)` must merge conversation-bound fields once per language row without double-counting words of either language. Test in Task 2.
5. The Clean tip must never fire twice on one calendar day, and never when a weak word (not only a filler) appeared in the window. Test in Task 7.

---

## Interfaces already on the branch

- `CoachRecord(Kind, SourceId, UserId, ChatId, OccurredAt) { Entry: CoachEntryRecord?, Run: CoachRunRecord?, Version }`, `CoachEntryRecord(EntryLid, Language, DurationSeconds, SpeechSeconds, Words, Sentences, Questions, Repetitions, DistinctWords, Pauses, PauseSeconds, IsTagged, FilledPauses, Fillers, WeakWords, Profanities, Spans)`, `CoachRunRecord(StartEntryLid, OwnSpeechSeconds, TotalSpeechSeconds, OwnTurns, TotalTurns, Participants, LongestMonologueSeconds, Responses, ResponseGapSeconds, Interruptions)` in `src/dotnet/Api/Users/Coach/CoachRecord.cs`.
- `CoachDay(Moment Day)` with sums and `CoachDayBuilder.Build(day, records, minVocabularyWords)` / `Merge(day, days)` in `src/dotnet/Api/Users/Coach/CoachDay.cs`.
- `CoachSummary(Window, Score, ScoreDelta, Words, Entries, TaggedEntries, Metrics)`, `CoachMetric(Kind, Value, Rate, Band, Chips)`, enums `CoachWindow`, `CoachMetricKind`, `CoachBand` in `src/dotnet/Api/Users/Coach/CoachSummary.cs`.
- `CoachScoring.Summarize/Score/SubScore/PaceBand/PaceRange` in `src/dotnet/Users.Service/Coach/CoachScoring.cs`; `CoachTipPolicy.Evaluate(record, window, spans, previous, settings, s, now, language)` in `CoachTipPolicy.cs`; `CoachScoringSettings` in `src/dotnet/Users.Service/Module/UsersSettings.cs`.
- `ICoach` (`src/dotnet/Api.Contracts/Users/ICoach.cs`) and `Coach` (`src/dotnet/Users.Service/Coach/Coach.cs`); `ICoachBackend` (`src/dotnet/Users.Contracts/ICoachBackend.cs`) and `CoachBackend` (`src/dotnet/Users.Service/Coach/CoachBackend.cs`) with `ListDays`, `ListOccurrences`, `OnRecord`, `OnRebuildDays`, `ListAllDays` (protected compute), `RebuildDay`, `ListRecentEntries`.
- `DbCoachDay { UserId, Day, Version, Data(jsonb) }` keyed `(UserId, Day)` in `UsersDbContext`; `DbCoachEvent { UserId, SourceId, Kind, ChatId, Version, IsRemoved, Day, OccurredAt, Payload }`.
- `UserCoachSettings { Origin, IsCoachingEnabled, AreLiveTipsEnabled, TipInterval }` (keys 0..3), `UserCoachTip` (keys 0..14, `CoachTipKind { None, SlowDown, SpeedUp, Filler, WeakWord }`), KVAS accessors `kvas.UserCoachSettings()`, `kvas.UserCoachTip()`, `kvas.ChatUserSettings(chatId)`, `kvas.UserLanguageSettings()` in `src/dotnet/Users.Contracts/UserScopedKvasBackendExt.cs`; client side `UserSettingsUI.UserCoachSettings()`.
- `ChatUserSettings` (keys 0..8) in `src/dotnet/Api/Chat/StoredSettings/ChatUserSettings.cs`.
- `CoachAnalysisBackend.OnAnalyzeEntry` reads `UserCoachSettings.IsCoachingEnabled` to decide immediate tagging; `GetLanguage(id, userId, ct)` gives the entry language; `OnAnalyzeConversation` tags pending rows per author.
- UI: `CoachPanel`, `CoachMetricRow`, `CoachSettingsTile`, `CoachTrends`, `CoachDayChart(Kind, Metric, Window)`, `CoachOccurrences(Word, Window, Back)`, `CoachTipBar(ChatId, AutoDismissDelay)`, `CoachLabels(IStringLocalizer)`, `CoachDayRange`, `CoachUI` (hub service: `IsEnabled`, `IsMarkingEnabled`, `GetOwnMarks`, `JumpTo`), `RightPanelContent`, `RightPanelModeSwitch`, `CoachMenuEntry` in `ChatPropertiesMenu`; shared `Card`, `CardItem(Left, Title, Caption, Right, Click, TitleClass)`, `Tile`, `TileItem(Icon, Content, Caption, Right, Click, IsHoverable, Role, Class)`, `TileTopic(Topic)`, `Toggle(IsChecked, IsCheckedChanged, IsDisabled)`, `TabPanel(Tabs, TabsClass, BottomHill, DefaultTabId, SwapKind, SelectedTabIdChanged)`, `TabDef(id, title)`, `MenuEntry(Icon, Text, Click, TextContent)`.
- Tests: `tests/Users.UnitTests/Coach/*`, `tests/Users.IntegrationTests/CoachTest.cs` (helpers `Entry(userId, chatId, lid, words, seconds, at, fillers, word)`, `Run(userId, chatId, start, endsAt)`, `WhenDay`), `tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs` (`NewCoachHost`, `PostVoice`, `InitializeHub`, `OptIn`, `FakeTagger`), `tests/Chat.UI.Blazor.UnitTests/CoachLabelsTest.cs`.

## File structure

New:
- `src/dotnet/Api/Users/Coach/CoachLanguageLevel.cs` — `CoachLanguageLevel` enum.
- `src/dotnet/Api/Users/Coach/CoachConversation.cs` — `CoachConversation` record + `CoachConversationBuilder`.
- `src/dotnet/Api/Users/Coach/CoachProgress.cs` — `CoachScorePart`, `CoachWeekDelta`, `CoachMilestone`, `CoachLanguageInfo` records.
- `src/dotnet/Api/Users/StoredSettings/UserCoachWeeklyNote.cs` — the weekly note.
- `src/dotnet/Users.Service/Coach/CoachSkillSets.cs` — headline order per level, language-bound vs conversation-bound.
- `src/dotnet/Users.Service/Coach/CoachFocus.cs` — automatic focus.
- `src/dotnet/Users.Service/Coach/CoachProgressBuilder.cs` — weekly deltas, milestones, score history.
- `src/dotnet/Users.Service/Flows/CoachWeeklyNoteFlow.cs` — Monday note.
- `src/dotnet/Chat.Service/Coach/CoachScope.cs` — scope resolution.
- `src/dotnet/Users.Service.Migration/Migrations/<ts>_Coach_DayLanguage.cs` — day key.
- UI: `CoachHeader.razor`, `CoachScoreCard.razor`, `CoachLanguageChips.razor`, `CoachRecentTab.razor`, `CoachConversationCard.razor`, `CoachProgressTab.razor`, `CoachWeekDeltas.razor`, `CoachMilestones.razor`, `CoachSkillsTab.razor`, `CoachSkillRow.razor`, `CoachScoreSheet.razor`, `CoachEmptyState.razor`, `CoachSettingsPage.razor`, `CoachChatToggleEntry.razor` (replaces `CoachMenuEntry`), `CoachFindings.cs`.
- Removed: `CoachSettingsTile.razor`, `CoachTrends.razor`, `CoachMetricRow.razor` (after Task 12), `CoachMenuEntry.razor` (after Task 13).

Modified: `CoachDay.cs`, `CoachSummary.cs`, `UserCoachSettings.cs`, `UserCoachTip.cs`, `ChatUserSettings.cs`, `UserScopedKvasBackendExt.cs`, `ICoach.cs`, `ICoachBackend.cs`, `Coach.cs`, `CoachBackend.cs`, `CoachScoring.cs`, `CoachTipPolicy.cs`, `UsersSettings.cs`, `DbCoachDay.cs`, `UsersDbContext.cs`, `CoachAnalysisBackend.cs`, `CoachPanel.razor`, `CoachLabels.cs`, `CoachDayRange.cs`, `CoachUI.cs`, `coach.css`, `ChatPropertiesMenu.razor`, `PlaceMenu.razor`, catalogs, `docs/api-index.md`, `docs/api-index-full.md`.

---

### Task 1: Contracts and stored settings

**Files:**
- Create: `src/dotnet/Api/Users/Coach/CoachLanguageLevel.cs`, `src/dotnet/Api/Users/Coach/CoachProgress.cs`, `src/dotnet/Api/Users/StoredSettings/UserCoachWeeklyNote.cs`
- Modify: `src/dotnet/Api/Users/StoredSettings/UserCoachSettings.cs`, `src/dotnet/Api/Users/StoredSettings/UserCoachTip.cs`, `src/dotnet/Api/Chat/StoredSettings/ChatUserSettings.cs`, `src/dotnet/Api/Users/Coach/CoachSummary.cs`, `src/dotnet/Users.Contracts/UserScopedKvasBackendExt.cs`
- Test: `tests/Users.UnitTests/StoredSettingsSerializationTest.cs`

**Interfaces:**
- Produces: `enum CoachLanguageLevel { Native = 0, Learning = 1, Off = 2 }`; `UserCoachSettings` keys 4..9: `bool SkipPeerChats`, `ApiMap<string, CoachLanguageLevel> Languages`, `ApiMap<string, CoachMetricKind> FocusByLanguage`, `string SelectedLanguage`, `bool AreMarksEnabled = true`, `bool IsWeeklySummaryEnabled = true`, plus `CoachLanguageLevel LevelOf(string iso)`; `ChatUserSettings` key 9 `bool? IsCoachingEnabled`; `CoachTipKind.Clean = 5`; `CoachWindow.Days7 = 4, Days30 = 5`; records `CoachScorePart(Kind, Weight, Band, Points, MaxPoints)`, `CoachWeekDelta(Kind, Previous, Current, Band, IsBetter)`, `CoachMilestone(Kind: CoachMilestoneKind, AchievedAt: Moment?)`, `CoachLanguageInfo(Iso, Level, Words30Days, IsWordSplittable)`, `enum CoachMilestoneKind { Words1K, Words10K, Words100K, FiveDayWeek, CleanFillerWeek, NoLongMonologueWeek, RisingMonth }`; `UserCoachWeeklyNote { Origin, WeekStart, ScoreDelta: int?, FocusKind: CoachMetricKind?, FocusDelta: double?, BestChatId: ChatId, BestStartLid: long, IsSeen }` with `IsPending => WeekStart != default && !IsSeen`; accessor `kvas.UserCoachWeeklyNote()`.

- [ ] **Step 1: Write the failing round-trip test**

Append to `tests/Users.UnitTests/StoredSettingsSerializationTest.cs` (it already round-trips `UserCoachTip`; copy its helper use):

```csharp
[Fact]
public void UserCoachSettingsShouldRoundTripTheNewKeys()
{
    // arrange
    var settings = new UserCoachSettings {
        IsCoachingEnabled = true,
        SkipPeerChats = true,
        Languages = new ApiMap<string, CoachLanguageLevel>(new Dictionary<string, CoachLanguageLevel> {
            ["en"] = CoachLanguageLevel.Learning, ["ru"] = CoachLanguageLevel.Native }),
        FocusByLanguage = new ApiMap<string, CoachMetricKind>(new Dictionary<string, CoachMetricKind> {
            ["en"] = CoachMetricKind.WeakWords }),
        SelectedLanguage = "en",
        AreMarksEnabled = false,
        IsWeeklySummaryEnabled = false,
    };

    // act
    var copy = RoundTrip(settings);

    // assert
    copy.Should().Be(settings);
    copy.LevelOf("en-US").Should().Be(CoachLanguageLevel.Learning, "the level is keyed by the ISO code");
    copy.LevelOf("de").Should().Be(CoachLanguageLevel.Native, "absent means native");
}

[Fact]
public void UserCoachWeeklyNoteShouldRoundTrip()
{
    // arrange
    var note = new UserCoachWeeklyNote {
        WeekStart = new Moment(new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc)),
        ScoreDelta = 4,
        FocusKind = CoachMetricKind.Fillers,
        FocusDelta = -0.03,
        BestChatId = GroupChatId.New(),
        BestStartLid = 12,
    };

    // act
    var copy = RoundTrip(note);

    // assert
    copy.Should().Be(note);
    copy.IsPending.Should().BeTrue();
    (copy with { IsSeen = true }).IsPending.Should().BeFalse();
}

[Fact]
public void ChatUserSettingsShouldRoundTripCoaching()
{
    // arrange
    var settings = new ChatUserSettings { IsCoachingEnabled = false };

    // act
    var copy = RoundTrip(settings);

    // assert
    copy.IsCoachingEnabled.Should().BeFalse();
    RoundTrip(new ChatUserSettings()).IsCoachingEnabled.Should().BeNull("null means inherit");
}
```

If the file has no `RoundTrip<T>` helper, add one that serializes with `MessagePackByteSerializer.Default` and back, next to the existing tip test.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Users.UnitTests --filter FullyQualifiedName~StoredSettingsSerializationTest`
Expected: build errors on the missing members.

- [ ] **Step 3: Add the enum, the progress records and the note**

`src/dotnet/Api/Users/Coach/CoachLanguageLevel.cs`:

```csharp
namespace ActualChat.Users;

public enum CoachLanguageLevel
{
    Native = 0,
    Learning = 1,
    Off = 2,
}
```

`src/dotnet/Api/Users/Coach/CoachProgress.cs`:

```csharp
namespace ActualChat.Users;

public enum CoachMilestoneKind
{
    Words1K = 0,
    Words10K = 1,
    Words100K = 2,
    FiveDayWeek = 3,
    CleanFillerWeek = 4,
    NoLongMonologueWeek = 5,
    RisingMonth = 6,
}

[DataContract, MessagePackObject]
public sealed partial record CoachScorePart(
    [property: DataMember, Key(0)] CoachMetricKind Kind,
    [property: DataMember, Key(1)] int Weight,
    [property: DataMember, Key(2)] CoachBand Band,
    [property: DataMember, Key(3)] double Points,
    [property: DataMember, Key(4)] double MaxPoints
);

// Previous and Current are the metric's Rate when it has one, else its Value, so callers format
// them with CoachLabels the same way as a metric
[DataContract, MessagePackObject]
public sealed partial record CoachWeekDelta(
    [property: DataMember, Key(0)] CoachMetricKind Kind,
    [property: DataMember, Key(1)] double? Previous,
    [property: DataMember, Key(2)] double? Current,
    [property: DataMember, Key(3)] CoachBand Band,
    [property: DataMember, Key(4)] bool? IsBetter
);

[DataContract, MessagePackObject]
public sealed partial record CoachMilestone(
    [property: DataMember, Key(0)] CoachMilestoneKind Kind,
    [property: DataMember, Key(1)] Moment? AchievedAt
);

[DataContract, MessagePackObject]
public sealed partial record CoachLanguageInfo(
    [property: DataMember, Key(0)] string Iso,
    [property: DataMember, Key(1)] CoachLanguageLevel Level,
    [property: DataMember, Key(2)] int Words30Days,
    [property: DataMember, Key(3)] bool IsWordSplittable
);
```

`src/dotnet/Api/Users/StoredSettings/UserCoachWeeklyNote.cs`:

```csharp
using ActualChat.Kvas;

namespace ActualChat.Users;

[DataContract, MessagePackObject]
public sealed partial record UserCoachWeeklyNote : StoredSettings, IHasOrigin, IHasKvasKey<UserCoachWeeklyNote>
{
    public static string KvasKey => nameof(UserCoachWeeklyNote);

    [DataMember, Key(0)] public string Origin { get; init; } = "";
    [DataMember, Key(1)] public Moment WeekStart { get; init; }
    [DataMember, Key(2)] public int? ScoreDelta { get; init; }
    [DataMember, Key(3)] public CoachMetricKind? FocusKind { get; init; }
    [DataMember, Key(4)] public double? FocusDelta { get; init; }
    [DataMember, Key(5)] public ChatId BestChatId { get; init; }
    [DataMember, Key(6)] public long BestStartLid { get; init; }
    [DataMember, Key(7)] public bool IsSeen { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsPending => WeekStart != default && !IsSeen;
}
```

- [ ] **Step 4: Extend the existing records**

`UserCoachSettings.cs`, after key 3:

```csharp
    [DataMember, Key(4)]
    public bool SkipPeerChats { get; init; }
    [DataMember, Key(5)]
    public ApiMap<string, CoachLanguageLevel> Languages { get; init; } = new ();
    [DataMember, Key(6)]
    public ApiMap<string, CoachMetricKind> FocusByLanguage { get; init; } = new ();
    // "" = the language with the most words in the last 30 days
    [DataMember, Key(7)]
    public string SelectedLanguage { get; init; } = "";
    [DataMember, Key(8)]
    public bool AreMarksEnabled { get; init; } = true;
    [DataMember, Key(9)]
    public bool IsWeeklySummaryEnabled { get; init; } = true;

    public CoachLanguageLevel LevelOf(string? language)
        => language is not null && Languages.TryGetValue(Language.GetIsoCode(language), out var level)
            ? level
            : CoachLanguageLevel.Native;
```

`UserCoachTip.cs`: add `Clean = 5,` to `CoachTipKind`.

`ChatUserSettings.cs`, after key 8:

```csharp
    // null = inherit from the place, then from the user's coach settings
    [DataMember, MemoryPackOrder(10), Key(9)] public bool? IsCoachingEnabled { get; init; }
```

`CoachSummary.cs`: add `Days7 = 4,` and `Days30 = 5,` to `CoachWindow`.

`UserScopedKvasBackendExt.cs`, next to `UserCoachTip()`:

```csharp
    public static KvasAccessor<UserCoachWeeklyNote> UserCoachWeeklyNote(this UserScopedKvasBackend kvas)
        => kvas.AccessorFor<UserCoachWeeklyNote>();
```

Also add the same accessor to the client-side `UserSettingsUI` accessor file (find it with `grep -rn "UserCoachTip()" src/dotnet/UI.Blazor* --include=*.cs`) so the UI can read the note.

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Users.UnitTests --filter FullyQualifiedName~StoredSettingsSerializationTest`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Api/Users/Coach/CoachLanguageLevel.cs src/dotnet/Api/Users/Coach/CoachProgress.cs src/dotnet/Api/Users/StoredSettings src/dotnet/Api/Chat/StoredSettings/ChatUserSettings.cs src/dotnet/Api/Users/Coach/CoachSummary.cs src/dotnet/Users.Contracts/UserScopedKvasBackendExt.cs src/dotnet/UI.Blazor* tests/Users.UnitTests/StoredSettingsSerializationTest.cs
git commit -m "feat(coach): settings, levels and progress contracts for the panel redesign"
```

---

### Task 2: Day rows per language

**Files:**
- Modify: `src/dotnet/Api/Users/Coach/CoachDay.cs`, `src/dotnet/Users.Service/Db/DbCoachDay.cs`, `src/dotnet/Users.Service/Db/UsersDbContext.cs`, `src/dotnet/Users.Contracts/ICoachBackend.cs`, `src/dotnet/Users.Service/Coach/CoachBackend.cs`, `src/dotnet/Api.Contracts/Users/ICoach.cs`, `src/dotnet/Users.Service/Coach/Coach.cs`
- Create: migration `Coach_DayLanguage` in `src/dotnet/Users.Service.Migration/Migrations/`
- Test: `tests/Users.UnitTests/Coach/CoachDayBuilderTest.cs`, `tests/Users.IntegrationTests/CoachTest.cs`

**Interfaces:**
- Produces: `CoachDay.Language` (key 30, ISO code, `""` for none); `CoachDayBuilder.BuildAll(Moment day, IEnumerable<CoachRecord> records, int minVocabularyWords) : ApiArray<CoachDay>` (one per language present; run records land in every language row of the day that has entries, else in the `""` row); `CoachDayBuilder.Build(day, records, min)` unchanged (all languages merged); `CoachDayBuilder.Merge(day, days)` unchanged but conversation-bound fields (`Runs`, `OwnTurns`, `TotalTurns`, `Responses`, `Interruptions`, `OwnSpeechSeconds`, `TotalSpeechSeconds`, `FairShareSeconds`, `LongestMonologueSeconds`, `ResponseGapSeconds`) are taken from at most one row per `Day` (see step 3); `ICoachBackend.ListDays(UserId, Range<Moment>, string? language, ct)`; `ICoach.ListOwnDays(Session, Range<Moment>, string? language, ct)` and `GetOwnSummary(Session, CoachWindow, string? language, ct)` (language `null` = all).

- [ ] **Step 1: Write the failing builder tests**

Append to `tests/Users.UnitTests/Coach/CoachDayBuilderTest.cs` (reuse its record helper; if it has none, copy `Entry`/`Run` from `CoachTipPolicyTest` and add a `language` parameter):

```csharp
[Fact]
public void BuildAllShouldSplitEntriesByLanguageAndCopyRunsIntoEachRow()
{
    // arrange
    var day = UsageDay.DayOf(Now);
    var records = new[] {
        Entry(words: 100, speechSeconds: 60, language: "en-US"),
        Entry(words: 50, speechSeconds: 30, language: "ru-RU"),
        Entry(words: 20, speechSeconds: 10, language: "ru-RU"),
        Run(ownSpeechSeconds: 30, totalSpeechSeconds: 90, participants: 3),
    };

    // act
    var days = CoachDayBuilder.BuildAll(day, records, 20);

    // assert
    days.Select(d => d.Language).Should().BeEquivalentTo(["en", "ru"]);
    days.Single(d => d.Language == "en").Words.Should().Be(100);
    days.Single(d => d.Language == "ru").Words.Should().Be(70);
    days.Should().OnlyContain(d => d.Runs == 1 && d.OwnSpeechSeconds == 30, "runs are not language-bound");
}

[Fact]
public void MergeShouldCountConversationFieldsOncePerDay()
{
    // arrange
    var day = UsageDay.DayOf(Now);
    var records = new[] {
        Entry(words: 100, speechSeconds: 60, language: "en-US"),
        Entry(words: 50, speechSeconds: 30, language: "ru-RU"),
        Run(ownSpeechSeconds: 30, totalSpeechSeconds: 90, participants: 3),
    };
    var perLanguage = CoachDayBuilder.BuildAll(day, records, 20);

    // act
    var merged = CoachDayBuilder.Merge(day, perLanguage);

    // assert
    merged.Words.Should().Be(150);
    merged.Runs.Should().Be(1, "the same run sits in both language rows");
    merged.OwnSpeechSeconds.Should().Be(30);
    merged.FairShareSeconds.Should().Be(30);
}

[Fact]
public void BuildAllShouldPutRunsWithoutEntriesInTheNeutralRow()
{
    // arrange
    var day = UsageDay.DayOf(Now);

    // act
    var days = CoachDayBuilder.BuildAll(day, [Run(ownSpeechSeconds: 30, totalSpeechSeconds: 90, participants: 3)], 20);

    // assert
    days.Should().ContainSingle().Which.Language.Should().Be("");
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Users.UnitTests --filter FullyQualifiedName~CoachDayBuilderTest`
Expected: build errors (`BuildAll`, `Language`).

- [ ] **Step 3: Implement `Language`, `BuildAll` and the merge rule**

In `CoachDay.cs` add `[DataMember, Key(30)] public string Language { get; init; } = "";` and change `CoachDayBuilder`:

```csharp
public static ApiArray<CoachDay> BuildAll(Moment day, IEnumerable<CoachRecord> records, int minVocabularyWords)
{
    var list = records.Where(r => r.Day == day).ToList();
    var byLanguage = list
        .Where(r => r.Entry is not null)
        .GroupBy(r => r.Entry!.Language is { } l ? Language.GetIsoCode(l) : "")
        .ToDictionary(g => g.Key, g => g.ToList());
    var runs = list.Where(r => r.Run is not null).ToList();
    if (byLanguage.Count == 0)
        return runs.Count == 0
            ? ApiArray<CoachDay>.Empty
            : ApiArray.New(Build(day, runs, minVocabularyWords));

    return byLanguage
        .OrderBy(x => x.Key, StringComparer.Ordinal)
        .Select(x => Build(day, x.Value.Concat(runs), minVocabularyWords) with { Language = x.Key })
        .ToApiArray();
}
```

In `Merge`, conversation-bound fields must be added once per day: keep a `HashSet<Moment> seenRunDays` and apply the `Runs..ResponseGapSeconds` additions only when `seenRunDays.Add(x.Day)` returns true (rows of one day carry identical run sums, so any one of them is enough). `LongestMonologueSeconds` stays a max.

- [ ] **Step 4: Storage and rebuild**

`DbCoachDay`: add `public string Language { get; set; } = "";` and set it in `UpdateFrom`. `UsersDbContext`: `coachDay.HasKey(e => new { e.UserId, e.Day, e.Language }); coachDay.Property(e => e.Language).UseCollation("C");`.

Migration:

```bash
cd src/dotnet/Users.Service.Migration && dotnet ef migrations add Coach_DayLanguage
```

Rename the class in both generated files to `_<timestamp>_Coach_DayLanguage` (keep the `[Migration("…")]` string). Edit `Up` so it truncates the table before changing the key (day rows are derived): add `migrationBuilder.Sql("DELETE FROM coach_days;");` as the first statement, then the generated key drop, column add (`""` default), key add. Needs local PostgreSQL (`ac_dev_users`).

`CoachBackend.RebuildDay`: replace the single-row write with

```csharp
var models = CoachDayBuilder.BuildAll(day, events.Select(e => e.ToModel()), Settings.Coach.MinVocabularyWords);
var rows = await dbContext.CoachDays.ForUpdate()
    .Where(d => d.UserId == userId.Value && d.Day == dbDay)
    .ToListAsync(cancellationToken)
    .ConfigureAwait(false);
foreach (var row in rows.Where(r => models.All(m => m.Language != r.Language)))
    dbContext.Remove(row);
foreach (var model in models) {
    var row = rows.FirstOrDefault(r => r.Language == model.Language);
    if (row is null) {
        row = new DbCoachDay { UserId = userId.Value, Day = dbDay, Language = model.Language };
        dbContext.Add(row);
    }
    row.UpdateFrom(model);
    row.Version = VersionGenerator.NextVersion(row.Version);
}
```

(`events.Count == 0` still removes every row of the day.) `OnRebuildDays` stale-row removal is unchanged (it filters by day).

`ICoachBackend.ListDays` gains `string? language` before `cancellationToken`; `CoachBackend.ListDays` filters `d.Language == Language.GetIsoCode(language)` when `language` is not null, else returns all rows (several per day). Callers in `Coach.cs` pass the language through. Also add the neutral-row rule to `ListDays`: when `language` is given, rows with `Language == ""` are included too, so runs recorded on a day without entries in that language still count.

`ICoach.GetOwnSummary(session, window, string? language, ct)` and `ListOwnDays(session, dayRange, string? language, ct)`: thread the parameter; `GetOwnSummary` passes `language ?? languageSettings.Primary.Value` to `CoachScoring.Summarize` as the band language, and uses `Ranges(window)` extended with `Days7 => today - 6 days`, `Days30 => today - 29 days`. Update `CoachDayRange.DayCount` (`Days7 => 7`, `Days30 => 30`) and `CoachLabels.Window` (`Days7 => Coach_WindowDays7`, `Days30 => Coach_WindowDays30`; keys added in Task 8, use the existing `Coach_WindowWeek`/`Coach_WindowMonth` until then). Update existing callers (`CoachPanel`, `CoachTrends`, `CoachDayChart`, tests) to pass `null`.

- [ ] **Step 5: Integration test for the split**

Append to `tests/Users.IntegrationTests/CoachTest.cs` (give `Entry` an optional `Language? language = null` parameter defaulting to `Languages.English`):

```csharp
[Fact]
public async Task EntriesInTwoLanguagesShouldBuildOneDayRowPerLanguage()
{
    // arrange
    await using var tester = AppHost.NewWebClientTester(Out);
    var account = await tester.SignInAsUniqueBob();
    var chatId = GroupChatId.New();
    var day = UsageDay.DayOf(T0);

    // act
    await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 100, 60, T0), false));
    await Queues.Enqueue(new CoachEntryAnalyzedEvent(
        Entry(account.Id, chatId, 2, 40, 30, T0 + TimeSpan.FromMinutes(1), language: Languages.Russian), false));
    await Queues.Enqueue(new CoachConversationAnalyzedEvent(Run(account.Id, chatId, 1, T0 + TimeSpan.FromMinutes(2))));

    // assert
    var range = new Range<Moment>(day, day + TimeSpan.FromDays(1));
    var rows = await TestWait.When(async ct => {
        var all = await Backend.ListDays(account.Id, range, null, ct);
        all.Should().HaveCount(2);
        all.Should().OnlyContain(d => d.Runs == 1);
        return all;
    });
    rows.Single(d => d.Language == "ru").Words.Should().Be(40);
    (await Backend.ListDays(account.Id, range, "en-US", default)).Should().ContainSingle().Which.Words.Should().Be(100);
    var summary = await Coach.GetOwnSummary(tester.Session, CoachWindow.AllTime, null, default);
    summary.Words.Should().Be(140);
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Users.UnitTests --filter FullyQualifiedName~Coach && dotnet test tests/Users.IntegrationTests --filter FullyQualifiedName~CoachTest`
Expected: PASS (existing tests keep passing with `null`).

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/Api/Users/Coach/CoachDay.cs src/dotnet/Users.Service src/dotnet/Users.Service.Migration/Migrations src/dotnet/Users.Contracts/ICoachBackend.cs src/dotnet/Api.Contracts/Users/ICoach.cs src/dotnet/UI.Blazor.App/Components/Coach tests/Users.UnitTests/Coach/CoachDayBuilderTest.cs tests/Users.IntegrationTests/CoachTest.cs
git commit -m "feat(coach): day rows per language"
```

---

### Task 3: Coaching scope on the chat shard

**Files:**
- Create: `src/dotnet/Chat.Service/Coach/CoachScope.cs`
- Modify: `src/dotnet/Chat.Service/Coach/CoachAnalysisBackend.cs`
- Test: `tests/Chat.UnitTests/Coach/CoachScopeTest.cs`, `tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs`

**Interfaces:**
- Consumes: `UserCoachSettings.SkipPeerChats`, `LevelOf`, `ChatUserSettings.IsCoachingEnabled`, `ChatId.Kind`, `PlaceChatId.PlaceId`, `PlaceId.RootChatId`.
- Produces: `static class CoachScope { static bool IsInScope(ChatId chatId, ChatUserSettings chat, ChatUserSettings? place, UserCoachSettings user); static async Task<bool> IsInScope(UserScopedKvasBackend kvas, ChatId chatId, CancellationToken ct) }`.

- [ ] **Step 1: Write the failing unit test**

`tests/Chat.UnitTests/Coach/CoachScopeTest.cs`:

```csharp
using ActualChat.Chat.Coach;
using ActualChat.Users;

namespace ActualChat.Chat.UnitTests.Coach;

public class CoachScopeTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly ChatUserSettings Inherit = new();
    private static readonly ChatUserSettings On = new() { IsCoachingEnabled = true };
    private static readonly ChatUserSettings Off = new() { IsCoachingEnabled = false };
    private static readonly UserCoachSettings User = new();

    [Theory]
    [InlineData(null, null, false, true)]
    [InlineData(false, null, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(null, false, false, false)]
    [InlineData(null, true, false, true)]
    public void GroupChatShouldFollowChatThenPlaceThenUser(bool? chat, bool? place, bool skipPeers, bool expected)
    {
        // arrange
        var chatId = new PlaceChatId(PlaceId.New(), Generate.Option);
        var chatSettings = new ChatUserSettings { IsCoachingEnabled = chat };
        var placeSettings = place is null ? Inherit : new ChatUserSettings { IsCoachingEnabled = place };

        // act
        var inScope = CoachScope.IsInScope(chatId, chatSettings, placeSettings, User with { SkipPeerChats = skipPeers });

        // assert
        inScope.Should().Be(expected);
    }

    [Fact]
    public void PeerChatShouldBeSkippedOnlyByTheSkipFlagOrItsOwnFlag()
    {
        // arrange
        var peer = new PeerChatId(UserId.New(), UserId.New());

        // act & assert
        CoachScope.IsInScope(peer, Inherit, null, User).Should().BeTrue();
        CoachScope.IsInScope(peer, Inherit, null, User with { SkipPeerChats = true }).Should().BeFalse();
        CoachScope.IsInScope(peer, On, null, User with { SkipPeerChats = true }).Should().BeTrue("the chat flag wins");
        CoachScope.IsInScope(peer, Off, null, User).Should().BeFalse();
    }
}
```

(`PlaceChatId`/`PeerChatId` constructors: check `src/dotnet/Api/Identifiers` for the exact factory, `PlaceChatId.New(placeId)` may be the right call; use what compiles.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Chat.UnitTests --filter FullyQualifiedName~CoachScopeTest`
Expected: build error, `CoachScope` missing.

- [ ] **Step 3: Implement the resolver**

`src/dotnet/Chat.Service/Coach/CoachScope.cs`:

```csharp
using ActualChat.Users;

namespace ActualChat.Chat.Coach;

// Chat flag, then place flag, then the user's defaults; an out-of-scope entry is never analysed
public static class CoachScope
{
    public static bool IsInScope(ChatId chatId, ChatUserSettings chat, ChatUserSettings? place, UserCoachSettings user)
    {
        if (chat.IsCoachingEnabled is { } chatFlag)
            return chatFlag;
        if (place?.IsCoachingEnabled is { } placeFlag)
            return placeFlag;
        return !(user.SkipPeerChats && chatId.Kind == ChatKind.Peer);
    }

    public static async Task<bool> IsInScope(UserScopedKvasBackend kvas, ChatId chatId, CancellationToken cancellationToken)
    {
        var user = await kvas.UserCoachSettings().Get(cancellationToken).ConfigureAwait(false);
        var chat = await kvas.ChatUserSettings(chatId).Get(cancellationToken).ConfigureAwait(false);
        ChatUserSettings? place = null;
        if (chatId is PlaceChatId placeChatId && placeChatId.PlaceId.RootChatId != chatId)
            place = await kvas.ChatUserSettings(placeChatId.PlaceId.RootChatId).Get(cancellationToken).ConfigureAwait(false);
        return IsInScope(chatId, chat, place, user);
    }
}
```

- [ ] **Step 4: Gate analysis**

In `CoachAnalysisBackend.OnAnalyzeEntry`, right after the author check (before `GetLanguage`):

```csharp
var kvas = ServerKvasBackend.ForUser(author.UserId);
if (!await CoachScope.IsInScope(kvas, id.ChatId, cancellationToken).ConfigureAwait(false)) {
    if (existing is not null)
        await RemoveEntry(id, context, cancellationToken).ConfigureAwait(false);
    return;
}
```

and change the tagging condition to `if (settings.IsCoachingEnabled && settings.LevelOf(language?.Value) != CoachLanguageLevel.Off)`. In `TagPendingEntries` (the conversation path) skip authors whose level for the entry language is `Off` and chats out of scope: compute `IsInScope` once per author per run before tagging, and skip those out of scope (their entries are also skipped when building the run's per-author rows in `OnAnalyzeConversation`). Reuse the `kvas` variable pattern already there.

- [ ] **Step 5: Integration test**

Append to `CoachUITest.cs`:

```csharp
[Fact(Timeout = 60_000)]
public async Task SwitchingTheChatOffShouldStopAnalysis()
{
    // arrange
    var appHost = await NewCoachHost("coach-ui-scope");
    await using var _1 = appHost;
    await using var tester = appHost.NewBlazorTester(Out);
    var account = await tester.SignInAsUniqueBob();
    var (chatId, _) = await tester.CreateChat(true);
    await OptIn(tester);
    var kvas = appHost.Services.GetRequiredService<IServerKvasBackend>().ForUser(account.Id, isOutermost: true);
    await kvas.ChatUserSettings(chatId).Update(x => x with { IsCoachingEnabled = false });
    var coach = tester.ScopedAppServices.AppUIHub().Coach;

    // act
    await PostVoice(tester, chatId, Text);
    await appHost.Services.Queues().WhenProcessing(TimeSpan.FromSeconds(2), default);

    // assert
    var summary = await coach.GetOwnSummary(tester.Session, CoachWindow.AllTime, null, default);
    summary.Entries.Should().Be(0, "the chat is switched off");
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Chat.UnitTests --filter FullyQualifiedName~CoachScopeTest && dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter FullyQualifiedName~SwitchingTheChatOff`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/Chat.Service/Coach tests/Chat.UnitTests/Coach/CoachScopeTest.cs tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs
git commit -m "feat(coach): per-chat and per-place coaching scope"
```

---

### Task 4: Conversations

**Files:**
- Create: `src/dotnet/Api/Users/Coach/CoachConversation.cs`
- Modify: `src/dotnet/Users.Service/Module/UsersSettings.cs`, `src/dotnet/Users.Contracts/ICoachBackend.cs`, `src/dotnet/Users.Service/Coach/CoachBackend.cs`, `src/dotnet/Api.Contracts/Users/ICoach.cs`, `src/dotnet/Users.Service/Coach/Coach.cs`
- Test: `tests/Users.UnitTests/Coach/CoachConversationBuilderTest.cs`, `tests/Users.IntegrationTests/CoachTest.cs`

**Interfaces:**
- Consumes: `CoachRecord`, `CoachEntryRecord`, `CoachRunRecord`.
- Produces: `CoachConversation(ChatId, StartEntryLid, StartedAt, EndedAt, Language, SecondaryLanguage, Words, SpeechSeconds, Fillers, WeakWords, Pace, TalkShare, LongestMonologueSeconds, FillerCounts, WeakWordCounts)`; `CoachConversationBuilder.Build(IEnumerable<CoachRecord> records, TimeSpan gap) : ApiArray<CoachConversation>` (newest first); `CoachScoringSettings.ConversationGap = 30 min`, `RecentConversations = 20`; `ICoachBackend.ListConversations(UserId, int count, ct)`; `ICoach.ListOwnConversations(Session, int count, ct)`.

- [ ] **Step 1: Write the failing builder test**

`tests/Users.UnitTests/Coach/CoachConversationBuilderTest.cs`:

```csharp
using ActualChat.Chat;

namespace ActualChat.Users.UnitTests.Coach;

public class CoachConversationBuilderTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Moment T0 = new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Gap = TimeSpan.FromMinutes(30);
    private static readonly ChatId ChatA = GroupChatId.New();

    private static CoachRecord Entry(ChatId chatId, long lid, Moment at, int words, string language = "en-US",
        int fillers = 0)
        => new (CoachRecordKind.Entry, $"{chatId}:{lid}", UserId.New(), chatId, at) {
            Entry = new CoachEntryRecord(lid, language, 60, 60, words, 3, 0, 0, words, 0, 0, true, 0, fillers, 0, 0,
                Enumerable.Range(0, fillers)
                    .Select(i => new SpeechSpan(SpeechSpanKind.Filler, "like", i * 5, 4, ApiArray<string>.Empty))
                    .ToApiArray()),
        };

    private static CoachRecord Run(ChatId chatId, long startLid, Moment at, double own, double total, double monologue)
        => new (CoachRecordKind.Run, $"{chatId}:run:{startLid}", UserId.New(), chatId, at) {
            Run = new CoachRunRecord(startLid, own, total, 2, 5, 3, monologue, 1, 0.9, 0),
        };

    [Fact]
    public void EntriesWithinTheGapShouldFormOneConversationNewestFirst()
    {
        // arrange
        var records = new[] {
            Entry(ChatA, 1, T0, 100),
            Entry(ChatA, 2, T0 + TimeSpan.FromMinutes(10), 50, fillers: 3),
            Entry(ChatA, 3, T0 + TimeSpan.FromMinutes(41), 30),
            Run(ChatA, 1, T0 + TimeSpan.FromMinutes(12), 30, 90, 20),
        };

        // act
        var conversations = CoachConversationBuilder.Build(records, Gap);

        // assert
        conversations.Should().HaveCount(2, "31 minutes of silence ends a conversation");
        conversations[0].StartEntryLid.Should().Be(3);
        var first = conversations[1];
        first.Words.Should().Be(150);
        first.Fillers.Should().Be(3);
        first.FillerCounts["like"].Should().Be(3);
        first.TalkShare.Should().BeApproximately(30d / 90, 1e-9);
        first.LongestMonologueSeconds.Should().Be(20);
        first.Pace.Should().BeApproximately(150 * 60 / 120d, 1e-9);
    }

    [Fact]
    public void MajorityLanguageShouldWinAndAMinorityAboveAQuarterShouldBeSecondary()
    {
        // arrange
        var records = new[] {
            Entry(ChatA, 1, T0, 100, "ru-RU"),
            Entry(ChatA, 2, T0 + TimeSpan.FromMinutes(1), 60, "en-US"),
            Entry(ChatA, 3, T0 + TimeSpan.FromMinutes(2), 10, "de-DE"),
        };

        // act
        var conversation = CoachConversationBuilder.Build(records, Gap).Single();

        // assert
        conversation.Language.Should().Be("ru");
        conversation.SecondaryLanguage.Should().Be("en", "60 of 170 words is above a quarter");
    }

    [Fact]
    public void OneLongEntryShouldStayOneConversation()
    {
        // arrange
        var record = Entry(ChatA, 1, T0, 900) with {
            Entry = Entry(ChatA, 1, T0, 900).Entry! with { DurationSeconds = 2700, SpeechSeconds = 2700 },
        };

        // act
        var conversations = CoachConversationBuilder.Build([record], Gap);

        // assert
        conversations.Should().ContainSingle().Which.EndedAt.Should().Be(T0 + TimeSpan.FromSeconds(2700));
    }

    [Fact]
    public void ChatsShouldNeverMix()
    {
        // arrange
        var chatB = GroupChatId.New();

        // act
        var conversations = CoachConversationBuilder.Build(
            [Entry(ChatA, 1, T0, 10), Entry(chatB, 1, T0 + TimeSpan.FromMinutes(1), 10)], Gap);

        // assert
        conversations.Should().HaveCount(2);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Users.UnitTests --filter FullyQualifiedName~CoachConversationBuilderTest`
Expected: build error.

- [ ] **Step 3: Implement the record and the builder**

`src/dotnet/Api/Users/Coach/CoachConversation.cs`:

```csharp
using ActualChat.Chat;

namespace ActualChat.Users;

[DataContract, MessagePackObject]
public sealed partial record CoachConversation(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] long StartEntryLid,
    [property: DataMember, Key(2)] Moment StartedAt,
    [property: DataMember, Key(3)] Moment EndedAt,
    [property: DataMember, Key(4)] string Language,
    [property: DataMember, Key(5)] string? SecondaryLanguage,
    [property: DataMember, Key(6)] int Words,
    [property: DataMember, Key(7)] double SpeechSeconds,
    [property: DataMember, Key(8)] int Fillers,
    [property: DataMember, Key(9)] int WeakWords,
    [property: DataMember, Key(10)] double? Pace,
    [property: DataMember, Key(11)] double? TalkShare,
    [property: DataMember, Key(12)] double? LongestMonologueSeconds,
    [property: DataMember, Key(13)] ApiMap<string, int> FillerCounts,
    [property: DataMember, Key(14)] ApiMap<string, int> WeakWordCounts
);

// A conversation is a run of one user's entries in one chat with no gap longer than `gap` between
// consecutive entries; runs overlapping it lend talk share and the monologue. Derived, never stored.
public static class CoachConversationBuilder
{
    private const double SecondaryLanguageShare = 0.25;

    public static ApiArray<CoachConversation> Build(IEnumerable<CoachRecord> records, TimeSpan gap)
    {
        var list = records.ToList();
        var result = new List<CoachConversation>();
        foreach (var chatGroup in list.Where(r => r.Entry is not null).GroupBy(r => r.ChatId)) {
            var runs = list.Where(r => r.Run is not null && r.ChatId == chatGroup.Key).ToList();
            var group = new List<CoachRecord>();
            foreach (var record in chatGroup.OrderBy(r => r.OccurredAt).ThenBy(r => r.Entry!.EntryLid)) {
                if (group.Count > 0 && record.OccurredAt - EndOf(group[^1]) > gap) {
                    result.Add(Close(group, runs));
                    group = [];
                }
                group.Add(record);
            }
            if (group.Count > 0)
                result.Add(Close(group, runs));
        }
        return result.OrderByDescending(c => c.StartedAt).ToApiArray();
    }

    // Private methods

    private static Moment EndOf(CoachRecord r)
        => r.OccurredAt + TimeSpan.FromSeconds(r.Entry!.DurationSeconds);

    private static CoachConversation Close(List<CoachRecord> group, List<CoachRecord> runs)
    {
        var entries = group.Select(r => r.Entry!).ToList();
        var startedAt = group[0].OccurredAt;
        var endedAt = group.Max(EndOf);
        var words = entries.Sum(e => e.Words ?? 0);
        var speech = entries.Sum(e => e.SpeechSeconds ?? e.DurationSeconds);
        var byLanguage = entries
            .GroupBy(e => e.Language is { } l ? Language.GetIsoCode(l) : "")
            .Select(g => (Iso: g.Key, Words: g.Sum(e => e.Words ?? 0)))
            .OrderByDescending(x => x.Words)
            .ThenBy(x => x.Iso, StringComparer.Ordinal)
            .ToList();
        var secondary = byLanguage.Count > 1 && words > 0 && byLanguage[1].Words >= words * SecondaryLanguageShare
            ? byLanguage[1].Iso
            : null;
        var overlapping = runs
            .Where(r => r.OccurredAt >= startedAt - TimeSpan.FromHours(1) && r.OccurredAt <= endedAt + TimeSpan.FromHours(1))
            .Select(r => r.Run!)
            .ToList();
        var own = overlapping.Sum(r => r.OwnSpeechSeconds);
        var total = overlapping.Sum(r => r.TotalSpeechSeconds);
        var fillers = new Dictionary<string, int>();
        var weak = new Dictionary<string, int>();
        foreach (var span in entries.SelectMany(e => e.Spans)) {
            var map = span.Kind switch {
                SpeechSpanKind.FilledPause or SpeechSpanKind.Filler => fillers,
                SpeechSpanKind.Weak => weak,
                _ => null,
            };
            if (map is not null)
                map[span.Word] = map.GetValueOrDefault(span.Word) + 1;
        }
        return new CoachConversation(
            group[0].ChatId,
            entries[0].EntryLid,
            startedAt,
            endedAt,
            byLanguage.Count > 0 ? byLanguage[0].Iso : "",
            secondary,
            words,
            speech,
            entries.Sum(e => e.FilledPauses + e.Fillers),
            entries.Sum(e => e.WeakWords),
            speech > 0 && words > 0 ? words * 60 / speech : null,
            total > 0 ? own / total : null,
            overlapping.Count > 0 ? overlapping.Max(r => r.LongestMonologueSeconds) : null,
            new ApiMap<string, int>(fillers),
            new ApiMap<string, int>(weak));
    }
}
```

Run records carry the run's end time as `OccurredAt`; the one-hour tolerance around the conversation is what lets a run that closed after the conversation's last entry still attach.

- [ ] **Step 4: Backend and frontend**

`CoachScoringSettings`: add `public TimeSpan ConversationGap { get; set; } = TimeSpan.FromMinutes(30);` and `public int RecentConversations { get; set; } = 20;`.

`ICoachBackend`: `[ComputeMethod] Task<ApiArray<CoachConversation>> ListConversations(UserId userId, int count, CancellationToken cancellationToken);`.

`CoachBackend.ListConversations`: depend on `ListAllDays(userId)` (as `ListOccurrences` does, so every record invalidates it), then read the latest `count * 8` entry rows plus the run rows of the last 30 days (`OrderByDescending(e => e.OccurredAt)`, `!e.IsRemoved`), map with `ToModel()`, call `CoachConversationBuilder.Build(rows, Settings.Coach.ConversationGap)` and `Take(count)`.

`ICoach`: `[ComputeMethod] Task<ApiArray<CoachConversation>> ListOwnConversations(Session session, int count, CancellationToken cancellationToken);`. `Coach.ListOwnConversations`: guest → empty; else `Backend.ListConversations(account.Id, Math.Clamp(count, 1, Settings.Coach.RecentConversations), ct)`.

- [ ] **Step 5: Integration test**

Append to `CoachTest.cs`:

```csharp
[Fact]
public async Task ListOwnConversationsShouldGroupEntriesByChatAndGap()
{
    // arrange
    await using var tester = AppHost.NewWebClientTester(Out);
    var account = await tester.SignInAsUniqueBob();
    var chatA = GroupChatId.New();
    var chatB = GroupChatId.New();
    await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatA, 1, 50, 30, T0), false));
    await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatA, 2, 50, 30, T0 + TimeSpan.FromMinutes(5)), false));
    await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatB, 1, 20, 10, T0 + TimeSpan.FromHours(2)), false));

    // act
    var conversations = await TestWait.When(async ct => {
        var c = await Coach.ListOwnConversations(tester.Session, 10, ct);
        c.Should().HaveCount(2);
        return c;
    });

    // assert
    conversations[0].ChatId.Should().Be(chatB);
    conversations[1].Words.Should().Be(100);
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Users.UnitTests --filter FullyQualifiedName~CoachConversationBuilderTest && dotnet test tests/Users.IntegrationTests --filter FullyQualifiedName~ListOwnConversations`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/Api/Users/Coach/CoachConversation.cs src/dotnet/Users.Service src/dotnet/Users.Contracts/ICoachBackend.cs src/dotnet/Api.Contracts/Users/ICoach.cs tests/Users.UnitTests/Coach/CoachConversationBuilderTest.cs tests/Users.IntegrationTests/CoachTest.cs
git commit -m "feat(coach): conversations derived from the log"
```

---

### Task 5: Per-language bands, skill sets and the score explanation

**Files:**
- Create: `src/dotnet/Users.Service/Coach/CoachSkillSets.cs`
- Modify: `src/dotnet/Users.Service/Module/UsersSettings.cs`, `src/dotnet/Users.Service/Coach/CoachScoring.cs`, `src/dotnet/App.Server/appsettings.json` (or the shared settings file the server reads; check `grep -rn "PaceSlowWpm\|UsersSettings" src/dotnet/App.Server/appsettings*.json`), `src/dotnet/Api.Contracts/Users/ICoach.cs`, `src/dotnet/Users.Service/Coach/Coach.cs`
- Test: `tests/Users.UnitTests/Coach/CoachScoringTest.cs`, `tests/Users.UnitTests/Coach/CoachSkillSetsTest.cs`

**Interfaces:**
- Produces: `RateBand` settings class `{ Good, High }`; `CoachScoringSettings.FillerByLanguage : Dictionary<string, RateBand>`; `CoachScoring.FillerRange(s, language) : RateBand`; `CoachScoring.Explain(CoachDay d, CoachScoringSettings s, string? language) : ApiArray<CoachScorePart>` whose `Points` sum to `Score(d, s, language) * weights / 100`... precisely: `Score == round(sum(Points) / sum(Weight) * 100 / 100)` see step 3; `CoachSkillSets.Headline(CoachLanguageLevel) : CoachMetricKind[]`, `CoachSkillSets.IsLanguageBound(CoachMetricKind) : bool`, `CoachSkillSets.Conversation : CoachMetricKind[]`; `ICoach.ExplainOwnScore(Session, string? language, ct) : ApiArray<CoachScorePart>`.

- [ ] **Step 1: Write the failing tests**

Append to `CoachScoringTest.cs`:

```csharp
[Fact]
public void ExplainShouldAddUpToTheScore()
{
    // arrange: a day with every scored input present
    var day = ScoredDay(words: 1000, fillers: 50, weak: 30, speechSeconds: 400, sentences: 100,
        ownSpeech: 100, fairShare: 100);

    // act
    var parts = CoachScoring.Explain(day, S, "en-US");
    var score = CoachScoring.Score(day, S, "en-US");

    // assert
    parts.Select(p => p.Kind).Should().BeEquivalentTo(
        [CoachMetricKind.Fillers, CoachMetricKind.Pace, CoachMetricKind.WeakWords, CoachMetricKind.TurnTaking,
            CoachMetricKind.SentenceLength]);
    parts.Sum(p => p.MaxPoints).Should().Be(100);
    ((int)Math.Round(parts.Sum(p => p.Points))).Should().Be(score!.Value);
    parts.Single(p => p.Kind == CoachMetricKind.Fillers).Band.Should().Be(CoachBand.Medium);
}

[Fact]
public void ExplainShouldSpreadWeightsWhenAnInputIsMissing()
{
    // arrange: no runs, so no turn-taking
    var day = ScoredDay(words: 1000, fillers: 10, weak: 10, speechSeconds: 400, sentences: 100, ownSpeech: 0, fairShare: 0);

    // act
    var parts = CoachScoring.Explain(day, S, "en-US");

    // assert
    parts.Should().NotContain(p => p.Kind == CoachMetricKind.TurnTaking);
    parts.Sum(p => p.MaxPoints).Should().Be(100);
}

[Fact]
public void FillerBandShouldFollowTheLanguageTable()
{
    // arrange
    var s = new CoachScoringSettings { FillerByLanguage = { ["ru"] = new RateBand { Good = 0.05, High = 0.10 } } };
    var day = ScoredDay(words: 1000, fillers: 40, weak: 0, speechSeconds: 400, sentences: 100, ownSpeech: 0, fairShare: 0);

    // act
    var ru = CoachScoring.Summarize(CoachWindow.AllTime, day, null, s, "ru-RU").Metrics
        .Single(m => m.Kind == CoachMetricKind.Fillers).Band;
    var en = CoachScoring.Summarize(CoachWindow.AllTime, day, null, s, "en-US").Metrics
        .Single(m => m.Kind == CoachMetricKind.Fillers).Band;

    // assert
    ru.Should().Be(CoachBand.Good);
    en.Should().Be(CoachBand.Medium);
}
```

Add a `ScoredDay(...)` helper to the test that builds a `CoachDay` with `Words`, `TaggedWords = words`, `FilledPauses = fillers`, `WeakWords = weak`, `SpeechSeconds`, `Sentences`, `OwnSpeechSeconds`, `FairShareSeconds`, `TotalSpeechSeconds = ownSpeech * 3`, `Runs = ownSpeech > 0 ? 1 : 0`.

`tests/Users.UnitTests/Coach/CoachSkillSetsTest.cs`:

```csharp
namespace ActualChat.Users.UnitTests.Coach;

public class CoachSkillSetsTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void NativeHeadlineShouldLeadWithFillersAndLearningWithWeakWords()
    {
        CoachSkillSets.Headline(CoachLanguageLevel.Native).Should().Equal(
            CoachMetricKind.Fillers, CoachMetricKind.Pace, CoachMetricKind.TurnTaking, CoachMetricKind.Monologue);
        CoachSkillSets.Headline(CoachLanguageLevel.Learning).Should().Equal(
            CoachMetricKind.WeakWords, CoachMetricKind.Vocabulary, CoachMetricKind.SentenceLength, CoachMetricKind.Pace);
    }

    [Fact]
    public void ConversationSkillsShouldNotBeLanguageBound()
    {
        foreach (var kind in CoachSkillSets.Conversation)
            CoachSkillSets.IsLanguageBound(kind).Should().BeFalse(kind.ToString());
        CoachSkillSets.IsLanguageBound(CoachMetricKind.Fillers).Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Users.UnitTests --filter "FullyQualifiedName~CoachScoringTest|FullyQualifiedName~CoachSkillSetsTest"`
Expected: build errors.

- [ ] **Step 3: Implement**

`UsersSettings.cs`: next to `PaceBand` add

```csharp
public sealed class RateBand
{
    public double Good { get; set; } = 0.03;
    public double High { get; set; } = 0.06;
}
```

and to `CoachScoringSettings`: `public Dictionary<string, RateBand> FillerByLanguage { get; set; } = new();`.

`CoachSkillSets.cs`:

```csharp
namespace ActualChat.Users;

public static class CoachSkillSets
{
    public static readonly CoachMetricKind[] Conversation = [
        CoachMetricKind.TurnTaking, CoachMetricKind.Monologue, CoachMetricKind.Interruptions,
        CoachMetricKind.Patience, CoachMetricKind.Pauses,
    ];

    public static CoachMetricKind[] Headline(CoachLanguageLevel level)
        => level == CoachLanguageLevel.Learning
            ? [CoachMetricKind.WeakWords, CoachMetricKind.Vocabulary, CoachMetricKind.SentenceLength, CoachMetricKind.Pace]
            : [CoachMetricKind.Fillers, CoachMetricKind.Pace, CoachMetricKind.TurnTaking, CoachMetricKind.Monologue];

    public static bool IsLanguageBound(CoachMetricKind kind)
        => !Conversation.Contains(kind);
}
```

`CoachScoring.cs`: add `public static RateBand FillerRange(CoachScoringSettings s, string? language)` (same lookup shape as `Band`, falling back to `new RateBand { Good = s.FillerGoodRate, High = s.FillerHighRate }`); use it in `Metrics` for the filler band and in `Score` for the filler sub-score edge. Add:

```csharp
public static ApiArray<CoachScorePart> Explain(CoachDay d, CoachScoringSettings s, string? language)
{
    if (d.Words < s.MinScoreWords)
        return ApiArray<CoachScorePart>.Empty;

    var pace = Band(s, language);
    var filler = FillerRange(s, language);
    var raw = new List<(CoachMetricKind Kind, double? Value, double Low, double High, int Weight, CoachBand Band)> {
        (CoachMetricKind.Fillers, FillerRate(d), 0, filler.Good, s.WeightFillers, RateBand(FillerRate(d), filler.Good, filler.High)),
        (CoachMetricKind.Pace, Pace(d), pace.Slow, pace.Fast, s.WeightPace,
            Pace(d) is { } p ? PaceBand(p, s, language) : CoachBand.None),
        (CoachMetricKind.WeakWords, WeakRate(d), 0, s.WeakGoodRate, s.WeightWeakWords,
            RateBand(WeakRate(d), s.WeakGoodRate, s.WeakHighRate)),
        (CoachMetricKind.TurnTaking, TurnRatio(d), s.TurnLowFactor, s.TurnHighFactor, s.WeightTurnTaking,
            RangeBand(TurnRatio(d), s.TurnLowFactor, s.TurnHighFactor)),
        (CoachMetricKind.SentenceLength, SentenceLength(d), s.SentenceShort, s.SentenceLong, s.WeightSentenceLength,
            RangeBand(SentenceLength(d), s.SentenceShort, s.SentenceLong)),
    };
    var present = raw.Where(x => x.Value is not null).ToList();
    var totalWeight = present.Sum(x => x.Weight);
    if (totalWeight == 0)
        return ApiArray<CoachScorePart>.Empty;

    return present
        .Select(x => {
            var max = 100d * x.Weight / totalWeight;
            return new CoachScorePart(x.Kind, x.Weight, x.Band, SubScore(x.Value!.Value, x.Low, x.High) / 100 * max, max);
        })
        .OrderBy(x => x.Points - x.MaxPoints)
        .ToApiArray();
}
```

`ICoach`: `[ComputeMethod] Task<ApiArray<CoachScorePart>> ExplainOwnScore(Session session, string? language, CancellationToken cancellationToken);` implemented in `Coach` over the `Days7` window merged day (same code path as `GetOwnSummary`, extract a private `MergedDay(account, window, language, ct)` helper used by both).

Seed the pace table in the server's settings JSON under `UsersSettings:Coach:PaceByLanguage` with the spec's table (`en` 130/170; `ru`, `uk`, `pl`, `cs` 100/140; `de` 110/150; `es`, `it` 150/200; `fr`, `pt` 140/180) and `FillerByLanguage` with `en` and `ru` at 0.03/0.06 (the global values, listed so they are visibly per language).

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Users.UnitTests --filter "FullyQualifiedName~CoachScoringTest|FullyQualifiedName~CoachSkillSetsTest"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Users.Service src/dotnet/App.Server src/dotnet/Api.Contracts/Users/ICoach.cs tests/Users.UnitTests/Coach
git commit -m "feat(coach): per-language bands, skill sets and the score explanation"
```

---

### Task 6: Focus, weekly deltas, milestones, languages and the commands

**Files:**
- Create: `src/dotnet/Users.Service/Coach/CoachFocus.cs`, `src/dotnet/Users.Service/Coach/CoachProgressBuilder.cs`
- Modify: `src/dotnet/Api.Contracts/Users/ICoach.cs`, `src/dotnet/Users.Service/Coach/Coach.cs`, `src/dotnet/Users.Contracts/ICoachBackend.cs`, `src/dotnet/Users.Service/Coach/CoachBackend.cs`, `src/dotnet/Api/Chat/Coach/SpeechTextStats.cs` (make `IsWordSplittable(string iso)` public overload)
- Test: `tests/Users.UnitTests/Coach/CoachFocusTest.cs`, `tests/Users.UnitTests/Coach/CoachProgressBuilderTest.cs`, `tests/Users.IntegrationTests/CoachTest.cs`

**Interfaces:**
- Produces: `CoachFocus.Pick(CoachSummary summary, CoachLanguageLevel level, CoachScoringSettings s) : CoachMetricKind?` (worst band among `Headline(level)`, order breaks ties, `null` below `MinScoreWords`); `CoachProgressBuilder.WeekDeltas(CoachDay thisWeek, CoachDay lastWeek, CoachLanguageLevel level, CoachScoringSettings s, string? language) : ApiArray<CoachWeekDelta>`; `CoachProgressBuilder.Milestones(IReadOnlyList<CoachDay> days, CoachScoringSettings s, string? language) : ApiArray<CoachMilestone>`; `CoachProgressBuilder.WeeklyScores(IReadOnlyList<CoachDay> days, int weeks, Moment now, s, language) : ApiArray<(Moment WeekStart, int? Score)>` (return a small record `CoachWeekScore(WeekStart, Score)` added to `CoachProgress.cs`, key 0/1); `ICoach.GetOwnFocus(Session, string? language, ct) : CoachMetricKind?`, `GetOwnWeekDeltas(Session, string? language, ct)`, `ListOwnMilestones(Session, ct)`, `ListOwnWeekScores(Session, int weeks, ct)`, `ListOwnLanguages(Session, ct) : ApiArray<CoachLanguageInfo>`; commands `Coach_SetFocus(Session, string Language, CoachMetricKind? Kind)`, `Coach_SetLanguageLevel(Session, string Language, CoachLanguageLevel Level)`, `Coach_SetChatCoaching(Session, ChatId ChatId, bool? IsEnabled)`, `Coach_DeleteOwnData(Session)`; `ICoachBackend.OnDeleteUserData(CoachBackend_DeleteUserData(UserId))`.

- [ ] **Step 1: Write the failing unit tests**

`tests/Users.UnitTests/Coach/CoachFocusTest.cs`:

```csharp
using ActualChat.Users.Module;

namespace ActualChat.Users.UnitTests.Coach;

public class CoachFocusTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly CoachScoringSettings S = new();

    private static CoachSummary Summary(params (CoachMetricKind Kind, CoachBand Band)[] metrics)
        => new (CoachWindow.Days7, 60, null, 500, 5, 5,
            metrics.Select(m => new CoachMetric(m.Kind, 1, 1, m.Band, ApiArray<CoachChip>.Empty)).ToApiArray());

    [Fact]
    public void PickShouldReturnTheWorstHeadlineSkillInOrder()
    {
        // arrange
        var summary = Summary((CoachMetricKind.Fillers, CoachBand.Medium), (CoachMetricKind.Pace, CoachBand.High),
            (CoachMetricKind.TurnTaking, CoachBand.High), (CoachMetricKind.Monologue, CoachBand.Good));

        // act & assert
        CoachFocus.Pick(summary, CoachLanguageLevel.Native, S).Should().Be(CoachMetricKind.Pace, "first High in order");
        CoachFocus.Pick(summary, CoachLanguageLevel.Learning, S).Should().Be(CoachMetricKind.Pace,
            "weak words, vocabulary and sentence length have no data");
    }

    [Fact]
    public void PickShouldReturnNullBelowTheWordFloor()
    {
        var summary = Summary((CoachMetricKind.Fillers, CoachBand.High)) with { Words = 10 };
        CoachFocus.Pick(summary, CoachLanguageLevel.Native, S).Should().BeNull();
    }

    [Fact]
    public void PickShouldPreferGoodOverNothingWhenAllAreGood()
    {
        var summary = Summary((CoachMetricKind.Fillers, CoachBand.Good), (CoachMetricKind.Pace, CoachBand.Good));
        CoachFocus.Pick(summary, CoachLanguageLevel.Native, S).Should().Be(CoachMetricKind.Fillers);
    }
}
```

`tests/Users.UnitTests/Coach/CoachProgressBuilderTest.cs`:

```csharp
using ActualChat.Users.Module;

namespace ActualChat.Users.UnitTests.Coach;

public class CoachProgressBuilderTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly CoachScoringSettings S = new();
    private static readonly Moment Now = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    private static CoachDay Day(Moment day, int words, int fillers, double speechSeconds, double monologue = 0)
        => new (day) {
            Words = words, TaggedWords = words, FilledPauses = fillers, SpeechSeconds = speechSeconds,
            Sentences = Math.Max(1, words / 10), Entries = 1, Runs = monologue > 0 ? 1 : 0,
            LongestMonologueSeconds = monologue,
        };

    [Fact]
    public void WeekDeltasShouldSayWhichDirectionIsBetter()
    {
        // arrange
        var last = Day(UsageDay.DayOf(Now) - TimeSpan.FromDays(7), 1000, 70, 600);
        var now = Day(UsageDay.DayOf(Now), 1000, 40, 500);

        // act
        var deltas = CoachProgressBuilder.WeekDeltas(now, last, CoachLanguageLevel.Native, S, "en-US");

        // assert
        var fillers = deltas.Single(d => d.Kind == CoachMetricKind.Fillers);
        fillers.Previous.Should().BeApproximately(0.07, 1e-9);
        fillers.Current.Should().BeApproximately(0.04, 1e-9);
        fillers.IsBetter.Should().BeTrue();
        deltas.Single(d => d.Kind == CoachMetricKind.Pace).IsBetter.Should().BeTrue("100 → 120 wpm moves toward the band");
    }

    [Fact]
    public void WeekDeltasShouldBeUnknownBelowTheWordFloor()
    {
        var last = Day(UsageDay.DayOf(Now) - TimeSpan.FromDays(7), 50, 5, 60);
        var now = Day(UsageDay.DayOf(Now), 1000, 40, 500);
        CoachProgressBuilder.WeekDeltas(now, last, CoachLanguageLevel.Native, S, "en-US")
            .Should().OnlyContain(d => d.Previous == null && d.IsBetter == null);
    }

    [Fact]
    public void MilestonesShouldDateTheFirstDayThatSatisfiesThem()
    {
        // arrange
        var d0 = UsageDay.DayOf(Now) - TimeSpan.FromDays(9);
        var days = Enumerable.Range(0, 10).Select(i => Day(d0 + TimeSpan.FromDays(i), 150, 2, 100)).ToList();

        // act
        var milestones = CoachProgressBuilder.Milestones(days, S, "en-US");

        // assert
        milestones.Single(m => m.Kind == CoachMilestoneKind.Words1K).AchievedAt.Should().Be(d0 + TimeSpan.FromDays(6));
        milestones.Single(m => m.Kind == CoachMilestoneKind.Words10K).AchievedAt.Should().BeNull();
        milestones.Single(m => m.Kind == CoachMilestoneKind.FiveDayWeek).AchievedAt.Should().NotBeNull();
    }

    [Fact]
    public void WeeklyScoresShouldReturnOneEntryPerWeekNewestLast()
    {
        var days = Enumerable.Range(0, 28)
            .Select(i => Day(UsageDay.DayOf(Now) - TimeSpan.FromDays(27 - i), 300, 9, 150))
            .ToList();
        var scores = CoachProgressBuilder.WeeklyScores(days, 4, Now, S, "en-US");
        scores.Should().HaveCount(4);
        scores[^1].Score.Should().NotBeNull();
        scores.Select(s => s.WeekStart).Should().BeInAscendingOrder();
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Users.UnitTests --filter "FullyQualifiedName~CoachFocusTest|FullyQualifiedName~CoachProgressBuilderTest"`
Expected: build errors.

- [ ] **Step 3: Implement the pure functions**

`CoachFocus.cs`:

```csharp
using ActualChat.Users.Module;

namespace ActualChat.Users;

public static class CoachFocus
{
    private static readonly CoachBand[] WorstFirst = [CoachBand.High, CoachBand.Low, CoachBand.Medium, CoachBand.Good];

    public static CoachMetricKind? Pick(CoachSummary summary, CoachLanguageLevel level, CoachScoringSettings s)
    {
        if (summary.Words < s.MinScoreWords)
            return null;

        var headline = CoachSkillSets.Headline(level);
        foreach (var band in WorstFirst)
            foreach (var kind in headline)
                if (summary.Metrics.Any(m => m.Kind == kind && m.Band == band))
                    return kind;
        return null;
    }
}
```

`CoachProgressBuilder.cs`:

```csharp
using ActualChat.Users.Module;

namespace ActualChat.Users;

public static class CoachProgressBuilder
{
    private const int FiveDayWeekDays = 5;

    public static ApiArray<CoachWeekDelta> WeekDeltas(
        CoachDay thisWeek, CoachDay lastWeek, CoachLanguageLevel level, CoachScoringSettings s, string? language)
    {
        var current = CoachScoring.Summarize(CoachWindow.Days7, thisWeek, null, s, language).Metrics;
        var previous = lastWeek.Words >= s.MinScoreWords
            ? CoachScoring.Summarize(CoachWindow.Days7, lastWeek, null, s, language).Metrics
            : null;
        var kinds = CoachSkillSets.Headline(level).Concat(CoachSkillSets.Conversation).Distinct();
        return kinds
            .Select(kind => {
                var now = current.FirstOrDefault(m => m.Kind == kind);
                var was = previous?.FirstOrDefault(m => m.Kind == kind);
                var nowValue = now is null ? null : now.Rate ?? now.Value;
                var wasValue = was is null ? null : was.Rate ?? was.Value;
                return new CoachWeekDelta(kind, wasValue, nowValue, now?.Band ?? CoachBand.None,
                    IsBetter(kind, wasValue, nowValue, s, language));
            })
            .ToApiArray();
    }

    public static ApiArray<CoachMilestone> Milestones(IReadOnlyList<CoachDay> days, CoachScoringSettings s, string? language)
    {
        var ordered = days.OrderBy(d => d.Day).ToList();
        var result = new List<CoachMilestone>();
        var total = 0;
        Moment? at1K = null, at10K = null, at100K = null;
        foreach (var d in ordered) {
            total += d.Words;
            at1K ??= total >= 1_000 ? d.Day : null;
            at10K ??= total >= 10_000 ? d.Day : null;
            at100K ??= total >= 100_000 ? d.Day : null;
        }
        result.Add(new (CoachMilestoneKind.Words1K, at1K));
        result.Add(new (CoachMilestoneKind.Words10K, at10K));
        result.Add(new (CoachMilestoneKind.Words100K, at100K));

        var weeks = ordered.GroupBy(d => WeekStart(d.Day)).OrderBy(g => g.Key).ToList();
        var filler = CoachScoring.FillerRange(s, language);
        result.Add(new (CoachMilestoneKind.FiveDayWeek,
            weeks.FirstOrDefault(w => w.Count(d => d.Words > 0) >= FiveDayWeekDays)?.Key));
        result.Add(new (CoachMilestoneKind.CleanFillerWeek, weeks
            .Select(w => (w.Key, Day: CoachDayBuilder.Merge(w.Key, w)))
            .FirstOrDefault(x => x.Day.Words >= s.MinScoreWords && x.Day.TaggedWords > 0
                && (double)(x.Day.FilledPauses + x.Day.Fillers) / x.Day.TaggedWords < filler.Good).Key));
        result.Add(new (CoachMilestoneKind.NoLongMonologueWeek, weeks
            .Select(w => (w.Key, Day: CoachDayBuilder.Merge(w.Key, w)))
            .FirstOrDefault(x => x.Day.Runs > 0 && x.Day.LongestMonologueSeconds < s.MonologueFlagSeconds).Key));
        int? previousScore = null;
        Moment? rising = null;
        foreach (var month in ordered.GroupBy(d => new Moment(new DateTime(d.Day.ToDateTime().Year, d.Day.ToDateTime().Month, 1, 0, 0, 0, DateTimeKind.Utc))).OrderBy(g => g.Key)) {
            var score = CoachScoring.Score(CoachDayBuilder.Merge(month.Key, month), s, language);
            if (score is { } sc && previousScore is { } prev && sc > prev && rising is null)
                rising = month.Key;
            previousScore = score ?? previousScore;
        }
        result.Add(new (CoachMilestoneKind.RisingMonth, rising));
        return result.ToApiArray();
    }

    public static ApiArray<CoachWeekScore> WeeklyScores(
        IReadOnlyList<CoachDay> days, int weeks, Moment now, CoachScoringSettings s, string? language)
    {
        var thisWeek = WeekStart(UsageDay.DayOf(now));
        return Enumerable.Range(0, weeks)
            .Select(i => thisWeek - TimeSpan.FromDays(7 * (weeks - 1 - i)))
            .Select(start => {
                var end = start + TimeSpan.FromDays(7);
                var merged = CoachDayBuilder.Merge(start, days.Where(d => d.Day >= start && d.Day < end));
                return new CoachWeekScore(start, CoachScoring.Score(merged, s, language));
            })
            .ToApiArray();
    }

    // ISO weeks start on Monday, in UTC like the day rows
    public static Moment WeekStart(Moment day)
    {
        var date = day.ToDateTime();
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return day - TimeSpan.FromDays(offset);
    }

    // Private methods

    private static bool? IsBetter(CoachMetricKind kind, double? was, double? now, CoachScoringSettings s, string? language)
    {
        if (was is null || now is null || Math.Abs(was.Value - now.Value) < 1e-9)
            return null;

        switch (kind) {
            case CoachMetricKind.Fillers or CoachMetricKind.WeakWords or CoachMetricKind.Repetition
                or CoachMetricKind.Profanity or CoachMetricKind.Monologue or CoachMetricKind.Interruptions:
                return now < was;
            case CoachMetricKind.Vocabulary or CoachMetricKind.Questions:
                return now > was;
            case CoachMetricKind.Pace: {
                var band = CoachScoring.PaceRange(s, language);
                return Distance(now.Value, band.Slow, band.Fast) < Distance(was.Value, band.Slow, band.Fast);
            }
            case CoachMetricKind.TurnTaking:
                return Distance(now.Value, s.TurnLowFactor, s.TurnHighFactor) < Distance(was.Value, s.TurnLowFactor, s.TurnHighFactor);
            case CoachMetricKind.SentenceLength:
                return Distance(now.Value, s.SentenceShort, s.SentenceLong) < Distance(was.Value, s.SentenceShort, s.SentenceLong);
            case CoachMetricKind.Patience:
                return Distance(now.Value, s.PatienceLowSeconds, s.PatienceHighSeconds) < Distance(was.Value, s.PatienceLowSeconds, s.PatienceHighSeconds);
            default:
                return null;
        }
    }

    private static double Distance(double value, double low, double high)
        => value < low ? low - value : value > high ? value - high : 0;
}
```

Add to `CoachProgress.cs`: `[DataContract, MessagePackObject] public sealed partial record CoachWeekScore([property: DataMember, Key(0)] Moment WeekStart, [property: DataMember, Key(1)] int? Score);`.

- [ ] **Step 4: ICoach methods and commands**

`ICoach.cs` additions:

```csharp
[ComputeMethod]
Task<CoachMetricKind?> GetOwnFocus(Session session, string? language, CancellationToken cancellationToken);
[ComputeMethod]
Task<ApiArray<CoachWeekDelta>> GetOwnWeekDeltas(Session session, string? language, CancellationToken cancellationToken);
[ComputeMethod]
Task<ApiArray<CoachMilestone>> ListOwnMilestones(Session session, CancellationToken cancellationToken);
[ComputeMethod]
Task<ApiArray<CoachWeekScore>> ListOwnWeekScores(Session session, int weeks, CancellationToken cancellationToken);
[ComputeMethod]
Task<ApiArray<CoachLanguageInfo>> ListOwnLanguages(Session session, CancellationToken cancellationToken);
[CommandHandler]
Task OnSetFocus(Coach_SetFocus command, CancellationToken cancellationToken);
[CommandHandler]
Task OnSetLanguageLevel(Coach_SetLanguageLevel command, CancellationToken cancellationToken);
[CommandHandler]
Task OnSetChatCoaching(Coach_SetChatCoaching command, CancellationToken cancellationToken);
[CommandHandler]
Task OnDeleteOwnData(Coach_DeleteOwnData command, CancellationToken cancellationToken);
```

with records (array form, `Session` at key 0 as `Coach_DismissTip` has it):

```csharp
[DataContract, MessagePackObject]
public sealed partial record Coach_SetFocus(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] string Language,
    [property: DataMember, Key(2)] CoachMetricKind? Kind
) : ApiCommand<Unit>(Session), ISessionCommand;
// Coach_SetLanguageLevel(Session, string Language, CoachLanguageLevel Level),
// Coach_SetChatCoaching(Session, ChatId ChatId, bool? IsEnabled),
// Coach_DeleteOwnData(Session) follow the same shape
```

(Copy the exact base-class and interface spelling from `Coach_DismissTip` in the same file.)

`Coach.cs`:
- `GetOwnFocus`: settings `FocusByLanguage[iso]` when present, else `CoachFocus.Pick(await GetOwnSummary(session, CoachWindow.Days7, language, ct), settings.LevelOf(language), Settings.Coach)`.
- `GetOwnWeekDeltas`: merge days of `[weekStart, now]` and `[weekStart-7d, weekStart)` for the language (`Backend.ListDays`), call `CoachProgressBuilder.WeekDeltas`, `InvalidateAtMidnight`.
- `ListOwnMilestones`: all days (`language: null`), `CoachProgressBuilder.Milestones`.
- `ListOwnWeekScores`: days of the last `weeks * 7` days for the selected language (`settings.SelectedLanguage` or `null`), `WeeklyScores`.
- `ListOwnLanguages`: `UserLanguageSettings().ListSpoken()` (primary, second, third) → for each ISO: level from settings, words in the last 30 days from `Backend.ListDays(id, last30, iso)`, `IsWordSplittable` via `SpeechTextStats.IsWordSplittable(Language.Parse(...))` (add a public `IsWordSplittable(string iso)` overload).
- `OnSetFocus`, `OnSetLanguageLevel`: `Accounts.GetOwn`, `Require(AccountFull.MustBeActive)`, update `UserCoachSettings` via `ServerKvasBackend.ForUser(id, isOutermost: true).UserCoachSettings().Update(...)` (`Kind == null` removes the key).
- `OnSetChatCoaching`: `kvas.ChatUserSettings(chatId).Update(x => x with { IsCoachingEnabled = command.IsEnabled })`.
- `OnDeleteOwnData`: `Commander.Call(new CoachBackend_DeleteUserData(account.Id), true, ct)` then reset `UserCoachTip`, `UserCoachWeeklyNote` and `UserCoachSettings` (`Languages`, `FocusByLanguage`, `SelectedLanguage` cleared, toggles kept).

`ICoachBackend` + `CoachBackend.OnDeleteUserData(CoachBackend_DeleteUserData(UserId))`: delete `CoachEvents` and `CoachDays` of the user inside the operation, invalidate `ListAllDays`. Check `tests/Users.IntegrationTests/CoachTest.cs::DeletingTheAccountShouldRemoveCoachRows` for the existing account-removal path and reuse its deletion code.

- [ ] **Step 5: Integration test**

Append to `CoachTest.cs`:

```csharp
[Fact]
public async Task FocusAndLevelCommandsShouldUpdateSettingsAndDeleteShouldClearEverything()
{
    // arrange
    await using var tester = AppHost.NewWebClientTester(Out);
    var account = await tester.SignInAsUniqueBob();
    var chatId = GroupChatId.New();
    await Queues.Enqueue(new CoachEntryAnalyzedEvent(Entry(account.Id, chatId, 1, 300, 200, T0, 20, "like"), false));
    await WhenDay(account.Id, UsageDay.DayOf(T0), d => d.Words == 300);

    // act
    await Commander.Call(new Coach_SetLanguageLevel(tester.Session, "en", CoachLanguageLevel.Learning));
    await Commander.Call(new Coach_SetFocus(tester.Session, "en", CoachMetricKind.Pace));
    var focus = await Coach.GetOwnFocus(tester.Session, "en-US", default);
    var languages = await Coach.ListOwnLanguages(tester.Session, default);
    await Commander.Call(new Coach_DeleteOwnData(tester.Session));

    // assert
    focus.Should().Be(CoachMetricKind.Pace);
    languages.Should().Contain(l => l.Iso == "en" && l.Level == CoachLanguageLevel.Learning);
    await TestWait.When(async ct =>
        (await Coach.GetOwnSummary(tester.Session, CoachWindow.AllTime, null, ct)).Words.Should().Be(0));
    (await Kvas.ForUser(account.Id).UserCoachSettings().Get(default)).FocusByLanguage.Should().BeEmpty();
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Users.UnitTests --filter "FullyQualifiedName~CoachFocusTest|FullyQualifiedName~CoachProgressBuilderTest" && dotnet test tests/Users.IntegrationTests --filter FullyQualifiedName~FocusAndLevelCommands`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/Users.Service src/dotnet/Users.Contracts src/dotnet/Api src/dotnet/Api.Contracts tests/Users.UnitTests/Coach tests/Users.IntegrationTests/CoachTest.cs
git commit -m "feat(coach): focus, weekly deltas, milestones, languages and the settings commands"
```

---

### Task 7: Tips per language and the Clean tip

**Files:**
- Modify: `src/dotnet/Users.Service/Coach/CoachTipPolicy.cs`, `src/dotnet/Users.Service/Coach/CoachBackend.cs`, `src/dotnet/Api/Users/StoredSettings/UserCoachTip.cs`, `src/dotnet/Users.Service/Module/UsersSettings.cs`
- Test: `tests/Users.UnitTests/Coach/CoachTipPolicyTest.cs`

**Interfaces:**
- Produces: `UserCoachTip.CleanTipDay : Moment` (key 15); `CoachScoringSettings.CleanTipMinWords = 150`; `CoachTipPolicy.Evaluate` unchanged signature but: the window is filtered to entries of the record's language (ISO match) for word and pace tips; a `Clean` tip fires when the window has at least `CleanTipMinWords` words, zero fillers, filled pauses and weak words, all entries tagged, and `previous.CleanTipDay != today`.

- [ ] **Step 1: Write the failing tests**

Append to `CoachTipPolicyTest.cs` (extend `Entry` with `string language = "en-US"` and add `bool clean` to build tagged spans-free entries):

```csharp
[Fact]
public void AWindowWithNoFillersShouldEarnOneCleanTipPerDay()
{
    // arrange
    var earlier = Entry(120, 60, Now - TimeSpan.FromMinutes(10));
    var current = Entry(60, 30, Now);

    // act
    var tip = Evaluate(current, [earlier, current], NoSpans, NoTip);
    var again = Evaluate(current, [earlier, current], NoSpans, tip! with { LastTipAt = Now - TimeSpan.FromHours(1) });

    // assert
    tip!.Kind.Should().Be(CoachTipKind.Clean);
    tip.CleanTipDay.Should().Be(UsageDay.DayOf(Now));
    again.Should().BeNull("one clean tip a day");
}

[Fact]
public void AWeakWordInTheWindowShouldBlockTheCleanTip()
{
    var earlier = Entry(120, 60, Now - TimeSpan.FromMinutes(10), (SpeechSpanKind.Weak, "very"));
    var current = Entry(60, 30, Now);
    Evaluate(current, [earlier, current], NoSpans, NoTip).Should().BeNull();
}

[Fact]
public void OtherLanguagesShouldNotCountTowardAWordTip()
{
    // arrange
    var russian = Entry(20, 10, Now - TimeSpan.FromMinutes(5), language: "ru-RU", Filler("like"), Filler("like"));
    var current = Entry(20, 10, Now, Filler("like"));

    // act
    var tip = Evaluate(current, [russian, current], NoSpans, NoTip);

    // assert
    tip.Should().BeNull("two of the three uses are in another language");
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Users.UnitTests --filter FullyQualifiedName~CoachTipPolicyTest`
Expected: build error on `CleanTipDay` / failing assertions.

- [ ] **Step 3: Implement**

`UserCoachTip.cs`: `[DataMember, Key(15)] public Moment CleanTipDay { get; init; }`. `CoachScoringSettings`: `public int CleanTipMinWords { get; set; } = 150;`.

`CoachTipPolicy.Evaluate`: filter `entries` to `Language.GetIsoCode(r.Entry.Language ?? "") == iso` where `iso = Language.GetIsoCode(record.Entry.Language ?? language ?? "")`; then

```csharp
var tip = WordTip(entries, spansWithSynonyms, previous, s, now)
    ?? PaceTip(entries, s, language)
    ?? CleanTip(entries, previous, s, now);
```

with

```csharp
private static UserCoachTip? CleanTip(List<CoachEntryRecord> entries, UserCoachTip previous, CoachScoringSettings s, Moment now)
{
    var today = UsageDay.DayOf(now);
    if (previous.CleanTipDay == today || entries.Count == 0 || entries.Any(e => !e.IsTagged))
        return null;
    var words = entries.Sum(e => e.Words ?? 0);
    if (words < s.CleanTipMinWords)
        return null;
    if (entries.Any(e => e.FilledPauses + e.Fillers + e.WeakWords > 0))
        return null;

    return new UserCoachTip { Kind = CoachTipKind.Clean, Count = words, CleanTipDay = today };
}
```

and in the final `tip with { ... }` keep `CleanTipDay = tip.Kind == CoachTipKind.Clean ? tip.CleanTipDay : previous.CleanTipDay`.

`CoachBackend.EvaluateTip`: also return early when `settings.LevelOf(analysis.Language?.Value) == CoachLanguageLevel.Off`.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Users.UnitTests --filter FullyQualifiedName~CoachTipPolicyTest`
Expected: PASS, including the existing ten.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Users.Service src/dotnet/Api/Users/StoredSettings/UserCoachTip.cs tests/Users.UnitTests/Coach/CoachTipPolicyTest.cs
git commit -m "feat(coach): tips judged per language and a clean-window tip"
```

---

### Task 8: The `Coach_*` catalog keys

**Files:**
- Modify: `src/dotnet/Localization/Resources/Strings.en.json` and the 18 other hand-written catalogs, `src/dotnet/Localization/Resources/LocalizedStringsLocalizerExt.cs`
- Test: `tests/Chat.UI.Blazor.UnitTests/AppLocalizationTest.cs` (existing)

**Interfaces:**
- Produces the typed members used by Tasks 9–14. English values (plural forms `a|b`, `_Format` keys take `{n}`):

| Key | English |
|---|---|
| `Coach_OnlyYou` | Only you |
| `Coach_TabRecent` / `Coach_TabProgress` / `Coach_TabSkills` | Recent / Progress / Skills |
| `Coach_WindowDays7` / `Coach_WindowDays30` | Last 7 days / 30 days |
| `Coach_ScoreOf_Format` | {0} score — {1}/100 |
| `Coach_ScoreThisWeek_Format` | {0} this week |
| `Coach_ScoreAfterWords` | Score after {0} more word\|Score after {0} more words |
| `Coach_WhatMovesIt` | What moves it? |
| `Coach_WorkingOn_Format` | Working on: {0} |
| `Coach_ChangeFocus` / `Coach_FocusAutomatic` / `Coach_MakeFocus` | Change focus / Automatic / Work on this |
| `Coach_FocusHintFillers_Format` | «{0}» {1} times this week. Try a short pause instead. |
| `Coach_FocusHintWeakWords_Format` | "{0}" {1} times this week. Try a sharper word. |
| `Coach_FocusHintPace` / `Coach_FocusHintTurnTaking` / `Coach_FocusHintMonologue` / `Coach_FocusHintVocabulary` / `Coach_FocusHintSentenceLength` | Aim for the comfortable range. / Leave a little more room for others. / Pause before two and a half minutes. / Reach for a word you have not used yet. / One thought per sentence. |
| `Coach_SpokeMinutes` | {0} min spoken\|{0} min spoken |
| `Coach_MarkedTranscript` / `Coach_AllNumbers` | Marked transcript / All numbers |
| `Coach_OlderInProgress` | Older conversations are summed up in Progress |
| `Coach_FindingFillers_Format` | {0} filler words, {1}% of speech, mostly «{2}» |
| `Coach_FindingFillersFew_Format` | {0} filler words in {1} minutes |
| `Coach_FindingPace_Format` | {0} wpm, {1} |
| `Coach_PaceComfortable_Format` | comfortable is {0} to {1} |
| `Coach_PaceALittleSlow` / `Coach_PaceALittleFast` / `Coach_PaceComfortableWord` | a little slow / a little fast / comfortable |
| `Coach_FindingTalkShare_Format` | {0}% of talk time among {1} people |
| `Coach_FindingTalkShareBalanced` / `Coach_FindingTalkShareHigh` / `Coach_FindingTalkShareLow` | , balanced / . Others got less room / . You could take more room |
| `Coach_FindingMonologue_Format` | Longest monologue {0} |
| `Coach_FindingMonologueShort` / `Coach_FindingMonologueLong` | , nice and short / . Over 2:30 loses listeners |
| `Coach_FindingWeakWords_Format` | {0} weak words, mostly "{1}" |
| `Coach_FindingVocabulary_Format` | {0} of every 100 words were different |
| `Coach_FindingSentence_Format` | {0} words per sentence |
| `Coach_BestThisWeek` | Best this week |
| `Coach_GettingBetter` | You are getting better |
| `Coach_BetterCaption_Format` | {0} down {1}% versus the week before. {2} days with speech out of 7. |
| `Coach_ScoreLastWeeks` / `Coach_ThisWeekVsLast` / `Coach_DaysWithSpeech` / `Coach_Milestones` | Score, last 4 weeks / This week vs last week / Days with speech / Milestones |
| `Coach_DaysOfSeven_Format` | {0} of 7 |
| `Coach_WeeksInARow` | {0} week in a row\|{0} weeks in a row |
| `Coach_DeltaSame` / `Coach_NotEnoughSpeech` | same / not enough speech |
| `Coach_MilestoneWords1K` / `Words10K` / `Words100K` | 1,000 words analysed / 10,000 words analysed / 100,000 words analysed |
| `Coach_MilestoneFiveDayWeek` / `CleanFillerWeek` / `NoLongMonologueWeek` / `RisingMonth` | A week with five speaking days / A week under the filler line / A week with no monologue over 2:30 / A month with a rising score |
| `Coach_SkillsSpeaking_Format` / `Coach_SkillsInConversations` / `Coach_AllLanguages` / `Coach_More` | Speaking {0} / In conversations / all languages / More |
| `Coach_ExplainFillers_Format` | Words that fill a gap while you think: {0}. Under {1}% sounds natural. |
| `Coach_ExplainPace_Format` | Words per minute while you speak. {0} to {1} is easy to follow in {2}. |
| `Coach_ExplainTalkShare` | Your part of the talk time in group conversations, against an equal share. Solo chats do not count. |
| `Coach_ExplainMonologue` | Your longest stretch without anyone else speaking. Past 2:30 attention drops. |
| `Coach_ExplainWeakWords_Format` | Vague words that carry little: {0}. A richer word makes the point for you. |
| `Coach_ExplainVocabulary` | How many of your words are not repeats. No target, just a trend. |
| `Coach_ExplainSentence` | Short sentences are easier for listeners. |
| `Coach_MetricDifferentWords` | Different words |
| `Coach_OfEvery100_Format` | {0} of every 100 |
| `Coach_NotMeasuredFor_Format` | Not measured for {0} yet |
| `Coach_BandABitHigh` / `Coach_BandABitMuch` / `Coach_BandFine` / `Coach_BandNatural` / `Coach_BandClear` | a bit high / a bit much / fine / natural / clear |
| `Coach_TapMarkedWord` | Tap a marked word in any of your messages to see why it was counted. |
| `Coach_NewUserTitle` | Your speech, read back to you |
| `Coach_NewUserBody` | The coach listens to the voice messages you send and tells you what a good friend would: which words you lean on, how fast you go, and whether you leave room for others. |
| `Coach_NewUserPrivacy` | It reads only your own messages. Nobody else sees any of this, and the other side of the conversation is never scored. |
| `Coach_RecordAMessage` / `Coach_FirstCardAfter` | Record a voice message / Your first card appears after about {0} words |
| `Coach_WhatYouWillSee` | What you will see |
| `Coach_SeeCardTitle` / `Coach_SeeCardBody` | A card after each conversation / Three things worth knowing, with the words marked right in your message. |
| `Coach_SeeProgressTitle` / `Coach_SeeProgressBody` | Progress week over week / One thing to work on at a time, and a note when it gets better. |
| `Coach_SeeTipTitle` / `Coach_SeeTipBody` | A quiet tip, sometimes / If a word comes up three times in twenty minutes, a small note appears. Turn it off in settings if you prefer the reports only. |
| `Coach_Settings` | Coach settings |
| `Coach_CoachingCaption2` | Tips, marks and instant analysis of your voice messages. Off deletes nothing. |
| `Coach_Where` / `Coach_Everywhere` / `Coach_EverywhereCaption` | Where / Everywhere / Except the chats and places you switch off |
| `Coach_SkipPeerChats` / `Coach_SkipPeerChatsCaption` | Skip one-to-one chats / Private conversations with one person are left alone |
| `Coach_SwitchedOff` / `Coach_SwitchOn` / `Coach_SwitchOffHint` | Switched off / Switch on / Switch any chat or place off from its menu: "Coach me here". |
| `Coach_PlaceChats` | Place · {0} chat\|Place · {0} chats |
| `Coach_Languages` / `Coach_Manage` / `Coach_LanguagesCaption` | Languages / Manage / These are the languages you record in. Learning puts vocabulary first; native puts fillers and monologues first. |
| `Coach_LanguagePrimary` / `Coach_LanguageSecond` / `Coach_LanguageThird` | Primary / Second / Third |
| `Coach_WordsIn30Days` | {0} word in 30 days\|{0} words in 30 days |
| `Coach_LevelNative` / `Coach_LevelLearning` / `Coach_LevelOff` | Native / Learning / Do not coach |
| `Coach_HowYouHear` | How you hear from the coach |
| `Coach_Marks` / `Coach_MarksCaption` | Marks in my messages / Counted words are marked in your own transcripts |
| `Coach_LiveTipsCaption` | A small note while the conversation is still going |
| `Coach_AtMost` / `Coach_TipIntervalCaption` / `Coach_OncePerConversation` | At most / How often a tip may appear / One per conversation |
| `Coach_WeeklySummary` / `Coach_WeeklySummaryCaption` | Weekly summary / One note on Monday with what changed |
| `Coach_YourData` / `Coach_YourDataCaption` / `Coach_DeleteData` / `Coach_DeleteDataConfirm` | Your data / Only your own messages are analysed and only you can see the result. The people you talk to are never scored. / Delete all coaching data / This removes every coaching number and mark. Your messages stay. |
| `Coach_CoachMeHere` | Coach me here |
| `Coach_TipCleanTitle` / `Coach_TipCleanBody` | Clean run / {0} minutes, no filler words. Keep going. |
| `Coach_WeekNoteTitle` / `Coach_WeekNoteScore_Format` / `Coach_WeekNoteFocus_Format` / `Coach_WeekNoteBest` | Your week with the coach / Score {0} / {1}: {2} / Best conversation |
| `Coach_ScoreSheetTitle` / `Coach_ScorePoints_Format` | What moves your score / {0} of {1} points |
| `Coach_Native` / `Coach_Learning` | native / learning |

- [ ] **Step 1: Add the keys to English and the typed members**

Add every row to `Strings.en.json` in the `Coach_` block (keep alphabetical order within the block as the existing keys are), and to `LocalizedStringsLocalizerExt.cs` following the existing three shapes: plain `public string Coach_X => l["Coach_X"].Value;`, format `public string Coach_X_Format(object arg0, …) => l["Coach_X_Format", arg0, …].Value;`, plural `public string Coach_X(long count, object arg0) => l.Plural("Coach_X", count, arg0);`.

- [ ] **Step 2: Translate into the 18 other catalogs**

Add the same keys with translations to `Strings.{bg,bs,cs,de,es,fr,hi,id,it,ja,ko,pl,pt,ru,tr,uk,vi,zh}.json`. Plural forms follow each language's existing pattern in the file (Russian has three forms `a|b|c`). Then run:

```bash
scripts/derive-bcms.cmd && scripts/derive-max.cmd
```

- [ ] **Step 3: Run the gate**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter FullyQualifiedName~AppLocalizationTest`
Expected: PASS (every shipped catalog defines exactly the English keys, every value translated).

- [ ] **Step 4: Commit**

```bash
git add src/dotnet/Localization/Resources
git commit -m "feat(coach): catalog keys for the panel redesign"
```

---

### Task 9: Panel shell — header, language chips, score and focus card, tabs, new-user state

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/Coach/CoachHeader.razor`, `CoachLanguageChips.razor`, `CoachScoreCard.razor`, `CoachEmptyState.razor`, `CoachRecentTab.razor` (placeholder until Task 10), `CoachProgressTab.razor` (placeholder until Task 11), `CoachSkillsTab.razor` (placeholder until Task 12)
- Modify: `src/dotnet/UI.Blazor.App/Components/Coach/CoachPanel.razor`, `src/dotnet/UI.Blazor.App/Services/CoachUI.cs`, `src/dotnet/UI.Blazor.App/Components/Coach/CoachLabels.cs`, `src/dotnet/UI.Blazor.App/Components/Coach/coach.css`
- Test: `tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs`

**Interfaces:**
- Consumes: `ICoach.ListOwnLanguages`, `GetOwnSummary(session, Days7, language)`, `GetOwnFocus`, `ListOwnConversations`; `UserCoachSettings.SelectedLanguage`.
- Produces: `CoachUI.SelectedLanguage : IState<string?>` (the ISO chosen by the chips, resolved from settings and `ListOwnLanguages`; `null` when one language), `CoachUI.SelectLanguage(string iso)` (writes `SelectedLanguage` to settings), `CoachUI.SelectedTab : MutableState<CoachTab>` with `enum CoachTab { Recent, Progress, Skills }` persisted in `LocalSettings` under key `Coach.Tab`; `CoachLabels.LanguageName(string iso)` (via `Languages` catalog), `CoachLabels.Tab(CoachTab)`, `CoachLabels.FocusTitle(CoachMetricKind)` (`MetricTitle` lowercased inside `Coach_WorkingOn_Format`), `CoachLabels.FocusHint(CoachMetricKind, CoachChip? topChip)`; components: `CoachHeader` (cover, avatar, gear → `SettingsClick`, title, `Coach_OnlyYou` badge), `CoachLanguageChips` (renders only when `ListOwnLanguages` has 2+ entries with words), `CoachScoreCard(Summary, Focus, Language, ScoreClick, FocusClick)`, `CoachEmptyState`.

- [ ] **Step 1: Write the failing test**

Append to `CoachUITest.cs`:

```csharp
[Fact(Timeout = 60_000)]
public async Task PanelShouldShowTheNewUserStateThenTabsAfterTheFirstConversation()
{
    // arrange
    var appHost = await NewCoachHost("coach-ui-shell");
    await using var _1 = appHost;
    await using var tester = appHost.NewBlazorTester(Out);
    await tester.SignInAsUniqueBob();
    tester.JSInterop.Mode = JSRuntimeMode.Loose;
    var (chatId, _) = await tester.CreateChat(true);
    var hub = tester.ScopedAppServices.AppUIHub();
    await OptIn(tester);

    // act
    var cut = tester.Render<CoachPanel>();
    InitializeHub(tester, hub, cut.Instance);
    cut.WaitForAssertion(() => cut.Find(".coach-empty").TextContent.Should().Contain("Your speech, read back to you"));
    await PostVoice(tester, chatId, Text);

    // assert
    cut.WaitForAssertion(() => {
        cut.FindAll(".coach-tabs .btn-tab").Select(t => t.TextContent.Trim()).Should().Equal("Recent", "Progress", "Skills");
        cut.Find(".coach-score-card").TextContent.Should().Contain("Score after");
        cut.Find(".coach-header .status-badge").TextContent.Should().Contain("Only you");
    }, TimeSpan.FromSeconds(30));
    cut.FindAll(".coach-language-chips").Should().BeEmpty("one language spoken");
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter FullyQualifiedName~PanelShouldShowTheNewUserState`
Expected: FAIL (no `.coach-empty`, no `.coach-tabs`).

- [ ] **Step 3: `CoachUI` state**

Add to `CoachUI`:

```csharp
public enum CoachTab { Recent = 0, Progress = 1, Skills = 2 }

private StoredState<CoachTab> _selectedTab = null!;
public IState<CoachTab> SelectedTab => _selectedTab ??= StateFactory.NewKvasStored<CoachTab>(
    new (LocalSettings, "Coach.Tab") { InitialValue = CoachTab.Recent, Category = StateCategories.Get(GetType(), nameof(SelectedTab)) });

public void SelectTab(CoachTab tab) => _selectedTab.Value = tab;

[ComputeMethod]
public virtual async Task<ApiArray<CoachLanguageInfo>> ListOwnLanguages(CancellationToken cancellationToken)
    => await Hub.Coach.ListOwnLanguages(Session, cancellationToken).ConfigureAwait(false);

// The chip selection: the remembered language when it still has words, else the most spoken; null
// when only one language has words, so callers ask for "all"
[ComputeMethod]
public virtual async Task<string?> GetSelectedLanguage(CancellationToken cancellationToken)
{
    var languages = (await ListOwnLanguages(cancellationToken).ConfigureAwait(false))
        .Where(l => l.Words30Days > 0).ToList();
    if (languages.Count < 2)
        return null;

    var settings = await UserSettingsUI.UserCoachSettings().Get(cancellationToken).ConfigureAwait(false);
    var remembered = languages.FirstOrDefault(l => l.Iso == settings.SelectedLanguage);
    return (remembered ?? languages.MaxBy(l => l.Words30Days))!.Iso;
}

public Task SelectLanguage(string iso)
    => UserSettingsUI.UserCoachSettings().Update(x => x with { SelectedLanguage = iso });
```

(Look at `RightPanelStoredState` / `StateFactory.NewKvasStored` usage in `RightPanel.cs` for the exact stored-state constructor spelling in this codebase.)

- [ ] **Step 4: Components**

`CoachHeader.razor`:

```razor
@namespace ActualChat.UI.Blazor.App.Components
@inherits ComputedStateComponent<AppUIHub, Avatar?>

<div class="coach-header">
    <div class="c-cover">
        <ButtonRound Click="@OnClose" Class="right-panel-close-btn btn-sm close-btn">
            <i class="icon-close text-2xl"></i>
        </ButtonRound>
    </div>
    <div class="c-head">
        <div class="c-avatar">
            @if (State.Value is { } avatar) {
                <AvatarCircle Avatar="@avatar" Size="@SquareSize.Size16"/>
            }
        </div>
        <div class="c-buttons">
            <ButtonRound Click="@SettingsClick" Class="btn-sm btn-tinted">
                <i class="icon-settings text-2xl"></i>
            </ButtonRound>
        </div>
        <div class="c-bottom">
            <span class="c-title">@L.Coach_Title</span>
            <span class="status-badge"><i class="icon-lock"></i>@L.Coach_OnlyYou</span>
        </div>
    </div>
</div>

@code {
    [Parameter] public EventCallback SettingsClick { get; set; }

    protected override ComputedState<Avatar?>.Options GetStateOptions()
        => new() { InitialValue = null, Category = GetStateCategory(GetType()) };

    protected override async Task<Avatar?> ComputeState(CancellationToken cancellationToken) {
        var account = await AccountUI.OwnAccount.Use(cancellationToken).ConfigureAwait(false);
        return account.IsGuest ? null : account.Avatar;
    }

    private void OnClose()
        => PanelsUI.Right.SetIsVisible(false);
}
```

`CoachLanguageChips.razor` (`ComputedStateComponent<AppUIHub, CoachLanguageChips.Model>` with `Model(ApiArray<CoachLanguageInfo> Languages, string? Selected)`): renders nothing when `Selected is null`; otherwise a `.coach-language-chips` row of `<Button Class="chip @(on ? "on" : "")">` with `Labels.LanguageName(l.Iso)` and ` · @(l.Level == Learning ? L.Coach_Learning : L.Coach_Native)`, plus a caption with `L.Coach_WordsIn30Days(selected.Words30Days, selected.Words30Days)`. Click → `Hub.CoachUI.SelectLanguage(l.Iso)`.

`CoachScoreCard.razor`:

```razor
@namespace ActualChat.UI.Blazor.App.Components
@using ActualChat.Users
@inherits FusionComponentBase
@{
    var s = Summary;
    var labels = Labels;
}

<Card Class="coach-score-card">
    <CardItem Click="@ScoreClick">
        <Left><i class="text-2xl icon-star text-primary"></i></Left>
        <Title>
            @if (s.Score is { } score) {
                @(Language is null ? L.Coach_Score_Format(score) : L.Coach_ScoreOf_Format(labels.LanguageName(Language), score))
            } else {
                @L.Coach_ScoreAfterWords(Math.Max(0, MinScoreWords - s.Words), Math.Max(0, MinScoreWords - s.Words))
            }
        </Title>
        <Caption>
            @if (s.ScoreDelta is { } delta) {
                var text = delta > 0 ? "+" + delta : delta.ToString();
                <span class="@(delta > 0 ? "text-success" : "text-danger")">@L.Coach_ScoreThisWeek_Format(text)</span>
                <span> · </span>
            }
            @L.Coach_WhatMovesIt
        </Caption>
        <Right><i class="icon-chevron-right text-xl text-03"></i></Right>
    </CardItem>
    @if (Focus is { } focus) {
        <CardItem Click="@FocusClick">
            <Left><i class="text-2xl icon-target text-primary"></i></Left>
            <Title>@L.Coach_WorkingOn_Format(labels.MetricTitle(focus).ToLowerInvariant())</Title>
            <Caption>@labels.FocusHint(focus, s.Metrics.FirstOrDefault(m => m.Kind == focus)?.Chips.FirstOrDefault())</Caption>
            <Right><i class="icon-chevron-right text-xl text-03"></i></Right>
        </CardItem>
    }
</Card>

@code {
    private const int MinScoreWords = 200;

    [Parameter, EditorRequired] public CoachSummary Summary { get; set; } = CoachSummary.None;
    [Parameter] public CoachMetricKind? Focus { get; set; }
    [Parameter] public string? Language { get; set; }
    [Parameter] public EventCallback ScoreClick { get; set; }
    [Parameter] public EventCallback FocusClick { get; set; }

    private CoachLabels Labels => field ??= new CoachLabels(L);
}
```

(Use an icon that exists in the icon font; `grep -o "icon-[a-z0-9-]*" src/dotnet/UI.Blazor/wwwroot/css/*.css | sort -u | grep -i "star\|target\|award"`; fall back to `icon-ai-stars` and `icon-checkmark-circle-2`.)

`CoachEmptyState.razor`: static markup per the spec §3.7 inside `<div class="coach-empty">`: a `Card` with the mascot `<img src="/dist/images/kitties/coach-cat.png">`, `Coach_NewUserTitle`, `Coach_NewUserBody`, `Coach_NewUserPrivacy`, a `<Button Class="btn-modal btn-primary" Click="@OnRecord">@L.Coach_RecordAMessage</Button>` (`OnRecord` → `PanelsUI.HidePanels()` then `Hub.ChatEditorUI`/recorder focus: use the same call the "Focus message editor" shortcut uses, `grep -rn "FocusEditor\|Focus message editor" src/dotnet/UI.Blazor.App`), `Coach_FirstCardAfter(30)`, a `TileTopic Topic="@L.Coach_WhatYouWillSee"` and a `Card` with three `CardItem`s (`Coach_SeeCard*`, `Coach_SeeProgress*`, `Coach_SeeTip*`), then a `Card` with the Coaching toggle (`CardItem` + `Toggle` bound to `UserCoachSettings.IsCoachingEnabled`, same update call as the old `CoachSettingsTile`).

`CoachPanel.razor` becomes the shell:

```razor
@namespace ActualChat.UI.Blazor.App.Components
@using ActualChat.Users
@using ActualChat.UI.Blazor.App.Services
@inherits ComputedStateComponent<AppUIHub, CoachPanel.Model>
@{
    var m = State.Value;
    var tabs = Tabs.Select(t => new TabDef(t.ToString(), Labels.Tab(t))).ToList();
}

<div class="coach-panel">
    <ErrorBarrier Name="CoachPanel" Kind="@ErrorBarrierKind.Full">
        <CoachHeader SettingsClick="@OnSettingsClick"/>
        @if (_showSettings) {
            <CoachSettingsPage Back="@OnSettingsClick"/>
        } else if (!m.HasConversations) {
            <CoachEmptyState/>
        } else {
            <div class="c-body">
                <CoachLanguageChips/>
                <CoachScoreCard Summary="@m.Summary" Focus="@m.Focus" Language="@m.Language"
                                ScoreClick="@OnScoreClick" FocusClick="@OnFocusClick"/>
            </div>
            <TabPanel
                Tabs="@tabs"
                TabsClass="left-panel-tabs wide-left-panel-tabs coach-tabs"
                BottomHill="true"
                DefaultTabId="@m.Tab.ToString()"
                SwapKind="@TabContentSwap.None"
                SelectedTabIdChanged="@OnTabChanged"/>
            @switch (m.Tab) {
            case CoachTab.Recent:
                <CoachRecentTab/>
                break;
            case CoachTab.Progress:
                <CoachProgressTab Language="@m.Language"/>
                break;
            default:
                <CoachSkillsTab Language="@m.Language" Summary="@m.Summary" Focus="@m.Focus"/>
                break;
            }
        }
        <div class="safe-area-bottom safe-area-bottom-overlay"></div>
    </ErrorBarrier>
</div>

@code {
    private static readonly CoachTab[] Tabs = [CoachTab.Recent, CoachTab.Progress, CoachTab.Skills];
    private bool _showSettings;

    private CoachLabels Labels => field ??= new CoachLabels(L);
    private ICoach Coach => Hub.Coach;
    private CoachUI CoachUI => Hub.CoachUI;

    protected override ComputedState<Model>.Options GetStateOptions()
        => new() { InitialValue = Model.None, UpdateDelayer = FixedDelayer.NextTick, Category = GetStateCategory(GetType()) };

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        var tab = await CoachUI.SelectedTab.Use(cancellationToken).ConfigureAwait(false);
        var language = await CoachUI.GetSelectedLanguage(cancellationToken).ConfigureAwait(false);
        var conversations = await Coach.ListOwnConversations(Session, 1, cancellationToken).ConfigureAwait(false);
        var summary = await Coach.GetOwnSummary(Session, CoachWindow.Days7, language, cancellationToken).ConfigureAwait(false);
        var focus = await Coach.GetOwnFocus(Session, language, cancellationToken).ConfigureAwait(false);
        return new Model(tab, language, summary, focus, conversations.Count > 0);
    }

    private void OnTabChanged(string? tabId) {
        if (Enum.TryParse<CoachTab>(tabId, out var tab))
            CoachUI.SelectTab(tab);
    }

    private void OnSettingsClick() => _showSettings = !_showSettings;
    private void OnScoreClick() => ModalUI.Show(new CoachScoreSheet.Model(State.Value.Language)); // Task 12 adds the sheet
    private void OnFocusClick() => CoachUI.SelectTab(CoachTab.Skills);

    public sealed record Model(CoachTab Tab, string? Language, CoachSummary Summary, CoachMetricKind? Focus, bool HasConversations) {
        public static readonly Model None = new(CoachTab.Recent, null, CoachSummary.None, null, true);
    }
}
```

Until Task 12 exists, make `OnScoreClick` a no-op and leave a `// Task 12` marker; `CoachSettingsPage` (Task 13) is a placeholder `<div class="coach-settings-page"></div>` component with a `Back` parameter for now; the three tab components are placeholder `<div class="coach-tab-…"></div>` with the parameters named above. `Model.None.HasConversations = true` keeps the first paint on the tabs rather than a flash of the empty state.

`CoachLabels` additions:

```csharp
public string Tab(CoachTab tab)
    => tab switch { CoachTab.Recent => l.Coach_TabRecent, CoachTab.Progress => l.Coach_TabProgress, _ => l.Coach_TabSkills };

public string LanguageName(string iso)
    => Languages.All.FirstOrDefault(x => x.IsoCode == iso)?.Title ?? iso;

public string FocusHint(CoachMetricKind kind, CoachChip? top)
    => kind switch {
        CoachMetricKind.Fillers when top is not null => l.Coach_FocusHintFillers_Format(top.Word, top.Count),
        CoachMetricKind.WeakWords when top is not null => l.Coach_FocusHintWeakWords_Format(top.Word, top.Count),
        CoachMetricKind.Pace => l.Coach_FocusHintPace,
        CoachMetricKind.TurnTaking => l.Coach_FocusHintTurnTaking,
        CoachMetricKind.Monologue => l.Coach_FocusHintMonologue,
        CoachMetricKind.Vocabulary => l.Coach_FocusHintVocabulary,
        CoachMetricKind.SentenceLength => l.Coach_FocusHintSentenceLength,
        _ => "",
    };
```

(`Languages.All` and `Language.Title`: confirm the member names in `src/dotnet/Api/Identifiers/Languages.cs`; the display title is the English name the transcription settings select shows.)

`coach.css`: replace the v1 header rules with `.coach-header .c-cover` (h-26, blurred gradient, close top-right), `.coach-header .c-head` (relative, px-4), `.c-avatar` (absolute, -top-9, left-4, direct child), `.c-buttons` (flex justify-end gap-2 h-12 pt-2), `.c-bottom` (flex items-center justify-between pt-3), `.coach-language-chips` (flex gap-2 px-4 pt-3, `.chip` = `status-badge` look, `.chip.on` primary bg), `.coach-panel .c-body` (px-4 pt-3), `.coach-tabs` (mt-3), `.coach-empty` (px-4 pt-4 flex-y gap-4, mascot 80×64 centred).

- [ ] **Step 5: Run the test**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter FullyQualifiedName~PanelShouldShowTheNewUserState`
Expected: PASS. Also run the older `CoachPanelShouldShowNoDataThenTheDaysNumbers` and adjust its selectors to the new shell (the metric rows come back in Task 12; until then assert on the score card only).

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/UI.Blazor.App tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs
git commit -m "feat(coach-ui): the panel shell with header, chips, score card, tabs and the new-user state"
```

---

### Task 10: Recent tab

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/Coach/CoachFindings.cs`, `CoachConversationCard.razor`
- Modify: `CoachRecentTab.razor`, `CoachLabels.cs`, `coach.css`
- Test: `tests/Chat.UI.Blazor.UnitTests/CoachFindingsTest.cs`, `tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs`

**Interfaces:**
- Consumes: `ICoach.ListOwnConversations`, `IChats.Get` (titles), `CoachUI.JumpTo`-style navigation (`History.NavigateTo(Links.Chat(chatId, lid))`), `CoachScoring`-free bands: the client re-derives bands from `CoachSummary.Metrics` of the conversation's day? No: the card judges pace with `PaceSlow/Fast` from the summary is unavailable client-side, so the server adds bands to the conversation.
- Produces: `CoachConversation` gains keys 15..17 `PaceBand: CoachBand`, `TalkShareBand: CoachBand`, `MonologueBand: CoachBand`, `Participants: int` (key 18), `WeakWordCounts` already there; `CoachConversationBuilder.Build(records, gap, Func<CoachConversation, CoachConversation> band)` overload used by `CoachBackend` with `CoachScoring.BandConversation(c, s)`; `CoachFindings.Pick(CoachConversation c, CoachMetricKind? focus, CoachLanguageLevel level, bool isWeeksBest) : IReadOnlyList<CoachFinding>` with `record CoachFinding(CoachMetricKind Kind, CoachBand Band)`; `CoachLabels.Finding(CoachConversation c, CoachFinding f, bool isWeeksBest) : string`; `CoachConversationCard(Conversation, Title, Findings)`.

- [ ] **Step 1: Write the failing findings test**

`tests/Chat.UI.Blazor.UnitTests/CoachFindingsTest.cs`:

```csharp
using ActualChat.UI.Blazor.App.Components;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class CoachFindingsTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static CoachConversation Conversation(CoachBand pace, CoachBand share, CoachBand monologue, int fillers)
        => new (GroupChatId.New(), 1, Moment.EpochStart, Moment.EpochStart, "en", null, 200, 100, fillers, 0, 120,
            0.6, 100, new ApiMap<string, int>(), new ApiMap<string, int>()) {
            PaceBand = pace, TalkShareBand = share, MonologueBand = monologue, Participants = 3,
        };

    [Fact]
    public void PickShouldLeadWithProblemsThenTheFocusThenGoodNews()
    {
        // arrange
        var c = Conversation(CoachBand.Good, CoachBand.High, CoachBand.Good, fillers: 2);

        // act
        var findings = CoachFindings.Pick(c, CoachMetricKind.Fillers, CoachLanguageLevel.Native, isWeeksBest: false);

        // assert
        findings.Select(f => f.Kind).Should().Equal(CoachMetricKind.TurnTaking, CoachMetricKind.Fillers, CoachMetricKind.Pace);
    }

    [Fact]
    public void PickShouldNeverExceedThreeAndNeverRepeat()
    {
        var c = Conversation(CoachBand.High, CoachBand.High, CoachBand.High, fillers: 20);
        var findings = CoachFindings.Pick(c, CoachMetricKind.Pace, CoachLanguageLevel.Native, false);
        findings.Should().HaveCount(3);
        findings.Select(f => f.Kind).Should().OnlyHaveUniqueItems();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter FullyQualifiedName~CoachFindingsTest`
Expected: build error.

- [ ] **Step 3: Bands on the conversation (server) and the picker (client)**

`CoachConversation.cs`: add init properties `[DataMember, Key(15)] public CoachBand PaceBand { get; init; }`, `Key(16) TalkShareBand`, `Key(17) MonologueBand`, `Key(18) int Participants` (the builder fills `Participants` from the run with the most participants). `CoachScoring.BandConversation(CoachConversation c, CoachScoringSettings s)` returns `c with { PaceBand = c.Pace is {} p ? PaceBand(p, s, c.Language) : None, TalkShareBand = c.TalkShare is {} t && c.Participants > 0 ? RangeBand(t * c.Participants, s.TurnLowFactor, s.TurnHighFactor) : None, MonologueBand = c.LongestMonologueSeconds is {} m ? (m >= s.MonologueFlagSeconds ? High : Good) : None }` (make `RangeBand` internal-visible or reimplement the three lines). `CoachBackend.ListConversations` maps every conversation through it. Filler band per conversation: client computes `fillers / words` against nothing; instead add `[DataMember, Key(19)] CoachBand FillerBand` set by `RateBand(fillers/words, FillerRange(s, c.Language))` in the same method.

`CoachFindings.cs` (client, pure):

```csharp
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Components;

public sealed record CoachFinding(CoachMetricKind Kind, CoachBand Band);

public static class CoachFindings
{
    private const int Count = 3;

    public static IReadOnlyList<CoachFinding> Pick(
        CoachConversation c, CoachMetricKind? focus, CoachLanguageLevel level, bool isWeeksBest)
    {
        var all = new List<CoachFinding> {
            new (CoachMetricKind.Fillers, c.FillerBand),
            new (CoachMetricKind.Pace, c.PaceBand),
            new (CoachMetricKind.TurnTaking, c.TalkShareBand),
            new (CoachMetricKind.Monologue, c.MonologueBand),
        };
        var order = CoachSkillSets.Headline(level).Concat(all.Select(f => f.Kind)).Distinct().ToList();
        var available = all.Where(f => f.Band != CoachBand.None).OrderBy(f => order.IndexOf(f.Kind)).ToList();
        var picked = new List<CoachFinding>();
        void Take(IEnumerable<CoachFinding> source) {
            foreach (var f in source)
                if (picked.Count < Count && picked.All(p => p.Kind != f.Kind))
                    picked.Add(f);
        }
        Take(available.Where(f => f.Band is CoachBand.High or CoachBand.Low));
        Take(available.Where(f => f.Kind == focus));
        Take(available.Where(f => f.Band == CoachBand.Good));
        Take(available);
        return picked;
    }
}
```

`CoachSkillSets` lives in `Users.Service`; move it to `src/dotnet/Api/Users/Coach/CoachSkillSets.cs` in this task so the client can use it (update the namespace-free usages; it has no dependencies).

`CoachLabels.Finding(CoachConversation c, CoachFinding f, bool isWeeksBest)`:

```csharp
public string Finding(CoachConversation c, CoachFinding f, bool isWeeksBest)
    => f.Kind switch {
        CoachMetricKind.Fillers => c.Fillers <= 2 && c.SpeechSeconds >= 60
            ? l.Coach_FindingFillersFew_Format(c.Fillers, Round(c.SpeechSeconds / 60)) + (isWeeksBest ? ". " + l.Coach_BestThisWeek : "")
            : l.Coach_FindingFillers_Format(c.Fillers, Round(100d * c.Fillers / Math.Max(1, c.Words)), TopWord(c.FillerCounts)),
        CoachMetricKind.Pace => l.Coach_FindingPace_Format(Round(c.Pace ?? 0), f.Band switch {
            CoachBand.Low => l.Coach_PaceALittleSlow, CoachBand.High => l.Coach_PaceALittleFast, _ => l.Coach_PaceComfortableWord }),
        CoachMetricKind.TurnTaking => l.Coach_FindingTalkShare_Format(Round((c.TalkShare ?? 0) * 100), c.Participants) + f.Band switch {
            CoachBand.High => l.Coach_FindingTalkShareHigh, CoachBand.Low => l.Coach_FindingTalkShareLow, _ => l.Coach_FindingTalkShareBalanced },
        _ => l.Coach_FindingMonologue_Format(Clock(c.LongestMonologueSeconds ?? 0))
            + (f.Band == CoachBand.High ? l.Coach_FindingMonologueLong : l.Coach_FindingMonologueShort),
    };

private static string TopWord(ApiMap<string, int> counts)
    => counts.OrderByDescending(x => x.Value).ThenBy(x => x.Key).FirstOrDefault().Key ?? "";

public static string Clock(double seconds)
    => $"{(int)seconds / 60}:{(int)seconds % 60:00}";
```

- [ ] **Step 4: The tab and the card**

`CoachRecentTab.razor` (`ComputedStateComponent<AppUIHub, CoachRecentTab.Model>` with `Model(ApiArray<CoachConversation> Conversations, Dictionary<ChatId, string> Titles, CoachMetricKind? Focus, CoachLanguageLevel Level, long BestFillerStartLid)`): `ComputeState` reads `Coach.ListOwnConversations(Session, 20)`, `Hub.Chats.Get(Session, chatId)` per distinct chat for titles (peer chats: `chat.Title` already is the peer's name), `Coach.GetOwnFocus(Session, language)`, settings level for the language; `BestFillerStartLid` = the conversation of the last 7 days with the lowest `Fillers / Words` and at least 60 s of speech. Renders `.coach-recent` with a `CoachConversationCard` per conversation and the `Coach_OlderInProgress` caption (a link that calls `CoachUI.SelectTab(CoachTab.Progress)`).

`CoachConversationCard.razor`:

```razor
@namespace ActualChat.UI.Blazor.App.Components
@using ActualChat.Users
@inherits FusionComponentBase
@{
    var c = Conversation;
    var minutes = Math.Max(1, (int)Math.Round(c.SpeechSeconds / 60));
}

<Card Class="coach-conversation">
    <div class="c-head">
        <span class="c-title text-01">@Title</span>
        <span class="c-when">@DateFormatter.ToShortDate(c.StartedAt) · @L.Coach_SpokeMinutes(minutes, minutes)</span>
    </div>
    @foreach (var f in Findings) {
        <div class="c-finding">
            <span class="c-dot band-@f.Band.ToString().ToLowerInvariant()"></span>
            <span>@Labels.Finding(c, f, IsWeeksBest)</span>
        </div>
    }
    <div class="c-links">
        <a href="@Links.Chat(c.ChatId, c.StartEntryLid)">@L.Coach_MarkedTranscript</a>
        <a @onclick="@OnAllNumbers">@L.Coach_AllNumbers</a>
    </div>
</Card>

@code {
    [Parameter, EditorRequired] public CoachConversation Conversation { get; set; } = null!;
    [Parameter] public string Title { get; set; } = "";
    [Parameter] public IReadOnlyList<CoachFinding> Findings { get; set; } = [];
    [Parameter] public bool IsWeeksBest { get; set; }

    private CoachLabels Labels => field ??= new CoachLabels(L);

    private void OnAllNumbers() {
        Hub.CoachUI.SelectTab(CoachTab.Skills);
    }
}
```

(`DateFormatter`: the panel already formats day labels with `DateFormatter` in `CoachDayChart`; use the same service and its "today / yesterday / date" helper if one exists, `grep -n "Yesterday\|ToRelative" src/dotnet/UI.Blazor/Services/DateFormatter.cs`.) `coach.css`: `.coach-conversation { p-3 gap-2 }`, `.c-head` flex between, `.c-when` caption, `.c-finding` flex gap-2 text-sm, `.c-dot` 8px round with `.band-high/.band-low` danger, `.band-medium` warning, `.band-good` success, `.c-links` flex gap-4 text-sm primary.

- [ ] **Step 5: Integration test**

Append to `CoachUITest.cs`:

```csharp
[Fact(Timeout = 60_000)]
public async Task RecentTabShouldShowOneCardPerConversationWithThreeFindings()
{
    // arrange
    var appHost = await NewCoachHost("coach-ui-recent");
    await using var _1 = appHost;
    await using var tester = appHost.NewBlazorTester(Out);
    await tester.SignInAsUniqueBob();
    tester.JSInterop.Mode = JSRuntimeMode.Loose;
    var (chatId, _) = await tester.CreateChat(true);
    var hub = tester.ScopedAppServices.AppUIHub();
    await OptIn(tester);
    await PostVoice(tester, chatId, Text);

    // act
    var cut = tester.Render<CoachPanel>();
    InitializeHub(tester, hub, cut.Instance);

    // assert
    cut.WaitForAssertion(() => {
        var cards = cut.FindAll(".coach-conversation");
        cards.Should().HaveCount(1);
        cards[0].QuerySelectorAll(".c-finding").Length.Should().BeInRange(1, 3);
        cards[0].TextContent.Should().Contain("Marked transcript");
    }, TimeSpan.FromSeconds(30));
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter FullyQualifiedName~CoachFindingsTest && dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter FullyQualifiedName~RecentTabShouldShow`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet tests/Chat.UI.Blazor.UnitTests/CoachFindingsTest.cs tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs
git commit -m "feat(coach-ui): the Recent tab with conversation cards"
```

---

### Task 11: Progress tab

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/Coach/CoachWeekDeltas.razor`, `CoachMilestones.razor`
- Modify: `CoachProgressTab.razor`, `CoachLabels.cs`, `CoachDayChart.razor`, `coach.css`; delete `CoachTrends.razor`
- Test: `tests/Chat.UI.Blazor.UnitTests/CoachLabelsTest.cs`, `tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs`

**Interfaces:**
- Consumes: `ICoach.GetOwnWeekDeltas`, `ListOwnWeekScores`, `ListOwnMilestones`, `ListOwnDays(session, range, language)`, `UserCoachWeeklyNote` accessor (read only; written in Task 14).
- Produces: `CoachLabels.Milestone(CoachMilestoneKind)`, `CoachLabels.DeltaValue(CoachWeekDelta)` ("7% → 4% of speech", "95 → 118 wpm", "3:10 → 2:20"), `CoachLabels.DeltaBadge(CoachWeekDelta)` ("▼ 40%", "▲ 23", "same"), `CoachProgressTab(Language)`.

- [ ] **Step 1: Write the failing label tests**

Append to `CoachLabelsTest.cs`:

```csharp
[Fact]
public void DeltaValueAndBadgeShouldFormatPerKind()
{
    // arrange
    var l = NewLabels();
    var fillers = new CoachWeekDelta(CoachMetricKind.Fillers, 0.07, 0.04, CoachBand.Medium, true);
    var pace = new CoachWeekDelta(CoachMetricKind.Pace, 95, 118, CoachBand.Good, true);
    var monologue = new CoachWeekDelta(CoachMetricKind.Monologue, 190, 140, CoachBand.High, true);
    var same = new CoachWeekDelta(CoachMetricKind.WeakWords, 0.03, 0.03, CoachBand.Good, null);
    var unknown = new CoachWeekDelta(CoachMetricKind.Fillers, null, 0.04, CoachBand.Medium, null);

    // act & assert
    l.DeltaValue(fillers).Should().Be("7% → 4% of speech");
    l.DeltaBadge(fillers).Should().Be("▼ 43%");
    l.DeltaValue(pace).Should().Be("95 → 118 wpm");
    l.DeltaBadge(pace).Should().Be("▲ 23");
    l.DeltaValue(monologue).Should().Be("3:10 → 2:20");
    l.DeltaBadge(monologue).Should().Be("▼ 0:50");
    l.DeltaBadge(same).Should().Be("same");
    l.DeltaValue(unknown).Should().Be("not enough speech");
}
```

(`NewLabels()` exists in the test or builds `new CoachLabels(localizer)` from the English catalog the way the existing tests do.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter FullyQualifiedName~CoachLabelsTest`
Expected: build error.

- [ ] **Step 3: Labels**

```csharp
public string Milestone(CoachMilestoneKind kind)
    => kind switch {
        CoachMilestoneKind.Words1K => l.Coach_MilestoneWords1K,
        CoachMilestoneKind.Words10K => l.Coach_MilestoneWords10K,
        CoachMilestoneKind.Words100K => l.Coach_MilestoneWords100K,
        CoachMilestoneKind.FiveDayWeek => l.Coach_MilestoneFiveDayWeek,
        CoachMilestoneKind.CleanFillerWeek => l.Coach_MilestoneCleanFillerWeek,
        CoachMilestoneKind.NoLongMonologueWeek => l.Coach_MilestoneNoLongMonologueWeek,
        _ => l.Coach_MilestoneRisingMonth,
    };

public string DeltaValue(CoachWeekDelta d)
{
    if (d.Previous is not { } was || d.Current is not { } now)
        return l.Coach_NotEnoughSpeech;

    return d.Kind switch {
        CoachMetricKind.Fillers or CoachMetricKind.WeakWords or CoachMetricKind.Repetition
            => $"{Round(was * 100)}% → " + l.Coach_PercentOfSpeech_Format(Round(now * 100)),
        CoachMetricKind.TurnTaking => $"{Round(was * 100)}% → " + l.Coach_PercentOfTalkTime_Format(Round(now * 100)),
        CoachMetricKind.Pace => $"{Round(was)} → " + l.Coach_Wpm_Format(Round(now)),
        CoachMetricKind.Monologue => $"{Clock(was)} → {Clock(now)}",
        CoachMetricKind.Vocabulary => $"{Round(was * 100)} → " + l.Coach_OfEvery100_Format(Round(now * 100)),
        CoachMetricKind.SentenceLength => $"{was:F1} → " + l.Coach_WordsPerSentence_Format(now.ToString("F1", null)),
        _ => $"{Round(was)} → {Round(now)}",
    };
}

public string DeltaBadge(CoachWeekDelta d)
{
    if (d.Previous is not { } was || d.Current is not { } now || d.IsBetter is null)
        return l.Coach_DeltaSame;

    var arrow = now < was ? "▼" : "▲";
    return d.Kind switch {
        CoachMetricKind.Fillers or CoachMetricKind.WeakWords or CoachMetricKind.Repetition
            => was > 0 ? $"{arrow} {Round(Math.Abs(now - was) / was * 100)}%" : arrow,
        CoachMetricKind.TurnTaking => $"{arrow} {Round(Math.Abs(now - was) * 100)}",
        CoachMetricKind.Monologue => $"{arrow} {Clock(Math.Abs(now - was))}",
        _ => $"{arrow} {Round(Math.Abs(now - was))}",
    };
}
```

Note: `DeltaBadge(fillers)` for 0.07 → 0.04 is "▼ 43%" (relative), which is what the test asserts; the spec mock said 40% loosely.

- [ ] **Step 4: Components**

`CoachProgressTab.razor` (`ComputedStateComponent<AppUIHub, CoachProgressTab.Model>`, `[Parameter] string? Language`; `Model(ApiArray<CoachWeekDelta> Deltas, ApiArray<CoachWeekScore> Scores, ApiArray<CoachMilestone> Milestones, ApiArray<CoachDay> ThisWeekDays, int WeeksInARow, UserCoachWeeklyNote Note)`):

```razor
<div class="coach-progress">
    @if (m.Note.IsPending) {
        <Card Class="coach-note"> ... Coach_WeekNoteTitle, score/focus/best lines, OnSeen marks IsSeen ... </Card>
    }
    @if (m.Deltas.Any(d => d.IsBetter == true)) {
        var best = m.Deltas.First(d => d.IsBetter == true);
        <Card Class="coach-better">
            <CardItem>
                <Left><img class="c-mascot" src="/dist/images/kitties/coach-cat.png" alt=""></Left>
                <Title>@L.Coach_GettingBetter</Title>
                <Caption>@Labels.BetterCaption(best, m.ThisWeekDays.Count(d => d.Words > 0))</Caption>
            </CardItem>
        </Card>
    }
    <TileTopic Topic="@L.Coach_ScoreLastWeeks"/>
    <Card Class="coach-week-scores"><BarChart Items="@ScoreItems(m.Scores)" .../></Card>
    <TileTopic Topic="@L.Coach_ThisWeekVsLast"/>
    <CoachWeekDeltas Deltas="@m.Deltas"/>
    <TileTopic Topic="@L.Coach_DaysWithSpeech"/>
    <Card Class="coach-days"> seven .c-day cells Mon..Sun, .on when Words > 0; caption Coach_DaysOfSeven_Format + Coach_WeeksInARow </Card>
    <TileTopic Topic="@L.Coach_Milestones"/>
    <CoachMilestones Milestones="@m.Milestones"/>
</div>
```

`ComputeState`: `Coach.GetOwnWeekDeltas(Session, Language)`, `ListOwnWeekScores(Session, 4)`, `ListOwnMilestones(Session)`, `ListOwnDays(Session, [WeekStart(today), today+1d), Language)` for the day cells, `UserSettingsUI.UserCoachWeeklyNote().Get`, and `WeeksInARow` computed client-side from `ListOwnDays` over the last 12 weeks (count consecutive weeks ending now with at least one speaking day). `CoachLabels.BetterCaption(CoachWeekDelta best, int days)` uses `Coach_BetterCaption_Format(MetricTitle(best.Kind), relative percent, days)`.

`CoachWeekDeltas.razor`: a `Card` with one `CardItem` per delta: `Title` = `Labels.MetricTitle(d.Kind)`, `Caption` = `Labels.DeltaValue(d)`, `Right` = `<span class="@(d.IsBetter == true ? "text-success" : d.IsBetter == false ? "text-danger" : "text-03")">@Labels.DeltaBadge(d)</span>`.

`CoachMilestones.razor`: a `Card` with one `CardItem` per milestone: `Left` = `<span class="c-check @(m.AchievedAt is null ? "off" : "")">✓</span>`, `Title` = `Labels.Milestone(m.Kind)` (`TitleClass="text-03"` when not achieved), `Right` = the date when achieved (`DateFormatter.ToShortDate`).

Reuse `BarChart` (`UI.Blazor/Components/Charts/BarChart.razor`, `ChartItem`) for the four weekly bars; the days row is plain markup. Delete `CoachTrends.razor` and its test (`CoachUITest` "Trends" test), keep `CoachDayChart` only if Skills (Task 12) still uses it for the pace detail; otherwise delete it too and `DonutChart` stays as a shared component.

- [ ] **Step 5: Integration test**

Append to `CoachUITest.cs` a `ProgressTabShouldShowDeltasDaysAndMilestones` test: same arrange as the Recent test, then `hub.CoachUI.SelectTab(CoachTab.Progress)`, assert `.coach-progress .coach-days .c-day.on` count ≥ 1, `.coach-milestones` has 7 rows, and the deltas card shows "not enough speech" for at least one row (a fresh user has no previous week).

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter FullyQualifiedName~CoachLabelsTest && dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter FullyQualifiedName~ProgressTab`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add -A src/dotnet/UI.Blazor.App/Components/Coach tests/Chat.UI.Blazor.UnitTests/CoachLabelsTest.cs tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs
git commit -m "feat(coach-ui): the Progress tab"
```

---

### Task 12: Skills tab and the score sheet

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/Coach/CoachSkillRow.razor`, `CoachScoreSheet.razor`
- Modify: `CoachSkillsTab.razor`, `CoachPanel.razor` (score click), `CoachLabels.cs`, `coach.css`; delete `CoachMetricRow.razor`
- Test: `tests/Chat.UI.Blazor.UnitTests/CoachLabelsTest.cs`, `tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs`

**Interfaces:**
- Consumes: `ICoach.GetOwnSummary(session, window, language)`, `ExplainOwnScore`, `ListOwnLanguages`, `Coach_SetFocus`, `CoachSkillSets`, `CoachOccurrences(Word, Window, Back)`.
- Produces: `CoachLabels.Explain(CoachMetricKind, string? language, CoachSummary)` (the one-line caption with the language name and its range: the pace range and the filler line come from two new summary fields, see below), `CoachLabels.BandWord(CoachMetricKind, CoachBand)` ("a bit high", "comfortable", "fine", "natural", …), `CoachSkillRow(Metric, Language, IsHeadline, IsFocus, WordClick, FocusClick)`, `CoachScoreSheet` modal (`ModalUI.Show(new CoachScoreSheet.Model(language))`), `CoachSummary` gains `[Key(7)] double PaceSlow`, `[Key(8)] double PaceFast`, `[Key(9)] double FillerGood` (the bands the captions quote, filled by `CoachScoring.Summarize`).

- [ ] **Step 1: Write the failing label test**

Append to `CoachLabelsTest.cs`:

```csharp
[Fact]
public void ExplainShouldNameTheLanguageAndItsRange()
{
    // arrange
    var l = NewLabels();
    var summary = CoachSummary.None with { PaceSlow = 100, PaceFast = 140, FillerGood = 0.03 };

    // act & assert
    l.Explain(CoachMetricKind.Pace, "ru", summary).Should().Be("Words per minute while you speak. 100 to 140 is easy to follow in Russian.");
    l.Explain(CoachMetricKind.Fillers, "ru", summary).Should().StartWith("Words that fill a gap");
    l.BandWord(CoachMetricKind.Fillers, CoachBand.Medium).Should().Be("a bit high");
    l.BandWord(CoachMetricKind.TurnTaking, CoachBand.High).Should().Be("a bit much");
    l.BandWord(CoachMetricKind.Pace, CoachBand.Good).Should().Be("comfortable");
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter FullyQualifiedName~CoachLabelsTest`
Expected: build error.

- [ ] **Step 3: Summary bands, labels, rows, sheet**

`CoachSummary`: append the three keys; `CoachScoring.Summarize` fills them from `Band(s, language)` and `FillerRange(s, language)`.

`CoachLabels`:

```csharp
public string Explain(CoachMetricKind kind, string? language, CoachSummary s)
    => kind switch {
        CoachMetricKind.Fillers => l.Coach_ExplainFillers_Format(Examples(s, CoachMetricKind.Fillers), Round(s.FillerGood * 100)),
        CoachMetricKind.Pace => l.Coach_ExplainPace_Format(Round(s.PaceSlow), Round(s.PaceFast), LanguageName(language ?? "")),
        CoachMetricKind.TurnTaking => l.Coach_ExplainTalkShare,
        CoachMetricKind.Monologue => l.Coach_ExplainMonologue,
        CoachMetricKind.WeakWords => l.Coach_ExplainWeakWords_Format(Examples(s, CoachMetricKind.WeakWords)),
        CoachMetricKind.Vocabulary => l.Coach_ExplainVocabulary,
        CoachMetricKind.SentenceLength => l.Coach_ExplainSentence,
        _ => "",
    };

public string BandWord(CoachMetricKind kind, CoachBand band)
    => (kind, band) switch {
        (_, CoachBand.None) => "",
        (CoachMetricKind.Fillers or CoachMetricKind.WeakWords, CoachBand.Good) => l.Coach_BandNatural,
        (CoachMetricKind.Fillers or CoachMetricKind.WeakWords, CoachBand.Medium) => l.Coach_BandABitHigh,
        (CoachMetricKind.Fillers or CoachMetricKind.WeakWords, CoachBand.High) => l.Coach_BandHigh,
        (CoachMetricKind.Pace, CoachBand.Good) => l.Coach_PaceComfortableWord,
        (CoachMetricKind.Pace, CoachBand.Low) => l.Coach_PaceALittleSlow,
        (CoachMetricKind.Pace, CoachBand.High) => l.Coach_PaceALittleFast,
        (CoachMetricKind.TurnTaking, CoachBand.High) => l.Coach_BandABitMuch,
        (CoachMetricKind.Monologue, CoachBand.Good) => l.Coach_BandFine,
        (CoachMetricKind.SentenceLength, CoachBand.Good) => l.Coach_BandClear,
        _ => Band(kind, band),
    };

// The top chips of the metric, quoted, so the caption names the user's own words
private string Examples(CoachSummary s, CoachMetricKind kind)
    => string.Join(", ", (s.Metrics.FirstOrDefault(m => m.Kind == kind)?.Chips ?? ApiArray<CoachChip>.Empty)
        .Take(3).Select(c => $"«{c.Word}»"));
```

`Value(metric)` for `Vocabulary` becomes `l.Coach_OfEvery100_Format(Round(value * 100))` and `MetricTitle(Vocabulary)` becomes `Coach_MetricDifferentWords`.

`CoachSkillRow.razor`: a `CardItem`-shaped block (`.coach-skill` inside the `Card`, column layout) with a top row `MetricTitle` + right `<span class="band-…">Value · BandWord</span>`, the `Explain` caption when `IsHeadline`, chips (`Button` per `Metric.Chips`, `WordClick`), and a `Coach_MakeFocus` link when `IsHeadline && !IsFocus` (`FocusClick`). For non-headline rows: one line, title left, `Value` (or `Coach_NoData`) right, caption only for `Vocabulary`.

`CoachSkillsTab.razor` (`[Parameter] string? Language`, `Summary`, `Focus`; own `MutableState<CoachWindow> _window = Days7`, `MutableState<string?> _word`): period chips (`Days7`, `Days30`, `AllTime`) + words caption; when `_word` set → `CoachOccurrences`; else: `TileTopic` `Coach_SkillsSpeaking_Format(LanguageName)` when `Language != null`; `Card` of headline rows in `CoachSkillSets.Headline(level)` order (level from settings); if `!IsWordSplittable` (from `ListOwnLanguages`) headline language-bound rows show `Coach_NotMeasuredFor_Format(language name)` instead of values; `TileTopic` `Coach_SkillsInConversations` with the `Coach_AllLanguages` caption and a `Card` of `CoachSkillSets.Conversation` rows; `TileTopic Coach_More` and a `Card` of the remaining kinds; footer `Coach_TapMarkedWord`. `ComputeState` re-reads the summary for `_window` (the panel's summary is `Days7` only), `Coach.GetOwnSummary(Session, window, Language)`. Focus click → `UICommander.Run(new Coach_SetFocus(Session, Language ?? primaryIso, kind))`.

`CoachScoreSheet.razor`: a modal (`ModalUI.Show` pattern: copy the shape of an existing small modal such as `TranslationTargetLanguageModal`) listing `ExplainOwnScore(Session, Model.Language)` parts: `MetricTitle`, `BandWord`, `Coach_ScorePoints_Format(Round(part.Points), Round(part.MaxPoints))`, plus a `Coach_FocusAutomatic` / `Coach_ChangeFocus` footer that opens Skills. Wire `CoachPanel.OnScoreClick` to it.

Delete `CoachMetricRow.razor` and the `CoachDayChart` pace detail (the day chart is no longer mounted); keep `CoachDayChart` only if Task 11 used it, else delete it and its test.

- [ ] **Step 4: Integration test**

Append to `CoachUITest.cs` a `SkillsTabShouldGroupHeadlineAndConversationSkills` test: arrange as the Recent test, `hub.CoachUI.SelectTab(CoachTab.Skills)`, assert the first `Card` has 4 `.coach-skill` rows and the "In conversations" topic exists, click the first chip (`.coach-skill .chip`) and assert `.coach-occurrences` renders, click the score card and assert the sheet shows "What moves your score".

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter FullyQualifiedName~CoachLabelsTest && dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter FullyQualifiedName~SkillsTab`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A src/dotnet/UI.Blazor.App/Components/Coach src/dotnet/Api/Users/Coach/CoachSummary.cs src/dotnet/Users.Service/Coach/CoachScoring.cs tests
git commit -m "feat(coach-ui): the Skills tab and the score sheet"
```

---

### Task 13: Settings page and "Coach me here"

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/Coach/CoachSettingsPage.razor`, `src/dotnet/UI.Blazor.App/Components/ChatPropertiesMenu/CoachChatToggleEntry.razor`
- Modify: `ChatPropertiesMenu.razor`, `src/dotnet/UI.Blazor.App/Components/PlaceMenu/PlaceMenu.razor`, `CoachUI.cs`, `coach.css`; delete `CoachSettingsTile.razor`, `CoachMenuEntry.razor`
- Test: `tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs`

**Interfaces:**
- Consumes: `UserCoachSettings` accessor, `ICoach.ListOwnLanguages`, commands `Coach_SetLanguageLevel`, `Coach_SetChatCoaching`, `Coach_DeleteOwnData`, `ChatUserSettings(chatId)` accessor, `SettingsModal` navigation to the Transcription tab (`grep -rn "SettingsOption\|OpenSettings" src/dotnet/UI.Blazor.App/Services/*.cs` for the call, likely `ModalUI.Show(new SettingsModal.Model(...))` or `History.NavigateTo("/settings/transcription")`), `IChats.Get` for switched-off titles.
- Produces: `CoachSettingsPage(Back)`, `CoachChatToggleEntry` (menu entry showing `Coach_CoachMeHere` with a toggle bound to the effective flag for the cascaded `ChatState.Chat.Id`; a place's `LeftPanelPlaceMenu`/`PlaceMenu` gets the same entry with the place's root chat id), `CoachUI.ListSwitchedOff(ct) : ApiArray<(ChatId Id, string Title, bool IsPlace, int Chats)>` (from the user's KVAS keys with `@UserChatSettings(` prefix where `IsCoachingEnabled == false`; if listing keys by prefix is not available client-side, keep a `ApiArray<ChatId> SwitchedOff` list on `UserCoachSettings` (key 10) maintained by `Coach_SetChatCoaching`, and read that).

- [ ] **Step 1: Write the failing test**

Append to `CoachUITest.cs`:

```csharp
[Fact(Timeout = 60_000)]
public async Task SettingsPageShouldSwitchLevelsAndChatsAndOpenLanguages()
{
    // arrange
    var appHost = await NewCoachHost("coach-ui-settings");
    await using var _1 = appHost;
    await using var tester = appHost.NewBlazorTester(Out);
    var account = await tester.SignInAsUniqueBob();
    tester.JSInterop.Mode = JSRuntimeMode.Loose;
    var (chatId, _) = await tester.CreateChat(true);
    var hub = tester.ScopedAppServices.AppUIHub();
    await OptIn(tester);
    await PostVoice(tester, chatId, Text);
    var kvas = appHost.Services.GetRequiredService<IServerKvasBackend>().ForUser(account.Id);

    // act
    var cut = tester.Render<CoachPanel>();
    InitializeHub(tester, hub, cut.Instance);
    cut.WaitForAssertion(() => cut.Find(".coach-header .btn-tinted"));
    cut.Find(".coach-header .btn-tinted").Click();
    cut.WaitForAssertion(() => cut.Find(".coach-settings-page"));
    cut.Find(".coach-settings-page .c-skip-peers .toggle").Click();
    await hub.UICommander.Run(new Coach_SetChatCoaching(hub.Session, chatId, false));

    // assert
    await TestWait.When(async ct => (await kvas.UserCoachSettings().Get(ct)).SkipPeerChats.Should().BeTrue());
    cut.WaitForAssertion(() => cut.Find(".coach-settings-page .c-switched-off").TextContent.Should().Contain("Switch on"));
    cut.Find(".coach-settings-page .c-languages a").TextContent.Should().Be("Manage");
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter FullyQualifiedName~SettingsPageShould`
Expected: FAIL (no `.coach-settings-page`).

- [ ] **Step 3: The page**

`CoachSettingsPage.razor` (`ComputedStateComponent<AppUIHub, CoachSettingsPage.Model>`, `[Parameter] EventCallback Back`; `Model(UserCoachSettings Settings, ApiArray<CoachLanguageInfo> Languages, ApiArray<SwitchedOffItem> SwitchedOff)`), body built with the app vocabulary, in this order:

```razor
<div class="coach-settings-page">
    <div class="c-title-row">
        <ButtonRound Click="@Back" Class="btn-sm btn-transparent"><i class="icon-arrow-left text-2xl"></i></ButtonRound>
        <span class="c-title text-03">@L.Coach_Settings</span>
    </div>
    <Tile>
        <TileItem Click="@ToggleCoaching">
            <Icon><i class="icon-ai-stars text-2xl"></i></Icon>
            <Content>@L.Coach_Coaching</Content>
            <Caption>@L.Coach_CoachingCaption2</Caption>
            <Right><Toggle IsChecked="@s.IsCoachingEnabled" IsCheckedChanged="@(_ => ToggleCoaching())"/></Right>
        </TileItem>
    </Tile>
    <TileTopic Topic="@L.Coach_Where"/>
    <Tile>
        <TileItem IsHoverable="false" Role="">
            <Icon><i class="icon-message-circle text-2xl"></i></Icon>
            <Content>@L.Coach_Everywhere</Content>
            <Caption>@L.Coach_EverywhereCaption</Caption>
        </TileItem>
        <TileItem Class="c-skip-peers" Click="@ToggleSkipPeers">
            <Icon><i class="icon-person text-2xl"></i></Icon>
            <Content>@L.Coach_SkipPeerChats</Content>
            <Caption>@L.Coach_SkipPeerChatsCaption</Caption>
            <Right><Toggle IsChecked="@s.SkipPeerChats" IsCheckedChanged="@(_ => ToggleSkipPeers())"/></Right>
        </TileItem>
        @if (m.SwitchedOff.Count > 0) {
            <div class="c-switched-off">
                <div class="caption">@L.Coach_SwitchedOff</div>
                @foreach (var item in m.SwitchedOff) {
                    <TileItem IsHoverable="false" Role="">
                        <Icon><ChatIcon ChatId="@item.Id"/></Icon>   @* or the avatar component the chat list uses *@
                        <Content>@item.Title</Content>
                        <Caption>@(item.IsPlace ? L.Coach_PlaceChats(item.Chats, item.Chats) : "")</Caption>
                        <Right><a @onclick="@(() => SwitchOn(item.Id))">@L.Coach_SwitchOn</a></Right>
                    </TileItem>
                }
            </div>
        }
    </Tile>
    <div class="caption">@L.Coach_SwitchOffHint</div>
    <TileTopic Topic="@L.Coach_Languages"><a class="c-languages-manage" @onclick="@OpenLanguages">@L.Coach_Manage</a></TileTopic>
    ... one TileItem per language: Content = LanguageName, Caption = "Primary · N words in 30 days",
        Right = a <select> with Native / Learning / Do not coach bound to Coach_SetLanguageLevel ...
    <div class="caption">@L.Coach_LanguagesCaption</div>
    <TileTopic Topic="@L.Coach_HowYouHear"/>
    <Tile> Marks toggle (AreMarksEnabled), Live tips toggle (AreLiveTipsEnabled), At most <select> (1/5/15/30 min + Coach_OncePerConversation = 0 → TipInterval.Zero handled as "per conversation"), Weekly summary toggle </Tile>
    <TileTopic Topic="@L.Coach_YourData"/>
    <Tile>
        <div class="caption">@L.Coach_YourDataCaption</div>
        <TileItem Class="item-danger" Click="@DeleteData"><Icon><i class="icon-trash02 text-2xl"></i></Icon><Content>@L.Coach_DeleteData</Content></TileItem>
    </Tile>
</div>
```

`TileTopic` takes only `Topic`; if it has no child content slot, render the Manage link in a sibling `.tile-topic-row` flex container with the topic text. Wire the `TileItem` clicks to `UserSettingsUI.UserCoachSettings().Update(...)`, the language `<select>` to `UICommander.Run(new Coach_SetLanguageLevel(Session, iso, level))`, `DeleteData` to a confirm modal (`ModalUI.Show(new ConfirmModal.Model(...))`, find the existing confirm modal by `grep -rn "ConfirmModal" src/dotnet/UI.Blazor/Components`) then `UICommander.Run(new Coach_DeleteOwnData(Session))`, `OpenLanguages` to the settings modal's Transcription tab. Marks: `CoachUI.IsMarkingEnabled` must also require `AreMarksEnabled`. Tip interval "per conversation": `CoachTipPolicy` treats `TipInterval == TimeSpan.Zero` as "once per conversation": no tip while `previous.ChatId == record.ChatId && now - previous.LastTipAt < s.ConversationGap`.

`CoachChatToggleEntry.razor` (replaces `CoachMenuEntry`): `ComputedStateComponent<AppUIHub, bool?>` reading `UserSettingsUI.ChatUserSettings(chatId).Get` → `IsCoachingEnabled`; renders `<MenuEntry Icon="icon-ai-stars"><TextContent><div class="coach-menu-entry"><span>@L.Coach_CoachMeHere</span><Toggle IsChecked="@effective" .../></div></TextContent></MenuEntry>` where `effective` = the flag, or (when null) `!(settings.SkipPeerChats && chatId.Kind == Peer)`; click → `UICommander.Run(new Coach_SetChatCoaching(Session, chatId, !effective))`. Hidden when `CoachUI.IsEnabled` is false. Mount in `ChatPropertiesMenu` where `CoachMenuEntry` was, and in `PlaceMenu` with `ChatId = place.Id.RootChatId`. Also keep a way to open the panel: `RightPanelModeSwitch` already does; the old "Speech coach" open entry is removed.

Switched-off list: add `[DataMember, Key(10)] ApiArray<ChatId> SwitchedOff { get; init; }` to `UserCoachSettings`, maintained by `Coach.OnSetChatCoaching` (add on `false`, remove on `true`/`null`); the page maps ids to titles with `Hub.Chats.Get` and marks places by `id is PlaceChatId p && p.PlaceId.RootChatId == id`, counting a place's chats with `Hub.Chats`/`PlaceUI` list of the place's chats (or leave the count out if no cheap call exists; then drop `Coach_PlaceChats` from the caption).

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter "FullyQualifiedName~SettingsPageShould|FullyQualifiedName~CoachUITest"`
Expected: PASS (fix the older tests that referenced `CoachSettingsTile` selectors: the "disabled tips row" test moves to the page's `.c-live-tips` toggle).

- [ ] **Step 5: Commit**

```bash
git add -A src/dotnet/UI.Blazor.App src/dotnet/Api/Users/StoredSettings/UserCoachSettings.cs src/dotnet/Users.Service/Coach tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs
git commit -m "feat(coach-ui): the settings page and the Coach me here toggle"
```

---

### Task 14: Weekly note, docs and whole-suite verification

**Files:**
- Create: `src/dotnet/Users.Service/Flows/CoachWeeklyNoteFlow.cs`
- Modify: `src/dotnet/Users.Service/Module/UsersServiceModule.cs` (flow registration, next to `DigestFlow`), `src/dotnet/UI.Blazor.App/Components/RightPanel/RightPanelModeSwitch.razor` (dot when a note is pending), `docs/api-index.md`, `docs/api-index-full.md`, `docs/api-index-ts.md` if TS changed
- Test: `tests/Users.UnitTests/Coach/CoachWeeklyNoteTest.cs`

**Interfaces:**
- Consumes: `DigestFlow` shape (`PeriodicFlow`, `Prepare`, `Run`, `IsDue`), `UserEmailsSettings.DigestTime`, `AccountFull.TimeZone`, `CoachProgressBuilder.WeekDeltas/WeekStart`, `ICoachBackend.ListDays/ListConversations`, `UserCoachWeeklyNote` accessor.
- Produces: `CoachWeeklyNoteFlow` (per user, resumed by the same trigger that starts `DigestFlow`; check how `DigestFlow` instances are started, `grep -rn "DigestFlow" src/dotnet/Users.Service --include=*.cs`), `static UserCoachWeeklyNote? CoachWeeklyNoteFlow.Compose(Moment weekStart, CoachDay thisWeek, CoachDay lastWeek, ApiArray<CoachConversation> conversations, UserCoachSettings settings, CoachScoringSettings s)` (pure, `null` below `MinScoreWords`).

- [ ] **Step 1: Write the failing test**

`tests/Users.UnitTests/Coach/CoachWeeklyNoteTest.cs`:

```csharp
using ActualChat.Users.Flows;
using ActualChat.Users.Module;

namespace ActualChat.Users.UnitTests.Coach;

public class CoachWeeklyNoteTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly CoachScoringSettings S = new();
    private static readonly Moment WeekStart = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);

    private static CoachDay Week(Moment start, int words, int fillers)
        => new (start) { Words = words, TaggedWords = words, FilledPauses = fillers, SpeechSeconds = words / 2.5, Sentences = words / 10, Entries = 3 };

    [Fact]
    public void ComposeShouldCarryTheScoreDeltaAndTheFocusDelta()
    {
        // arrange
        var settings = new UserCoachSettings { FocusByLanguage = new (new Dictionary<string, CoachMetricKind> { ["en"] = CoachMetricKind.Fillers }) };
        var best = new CoachConversation(GroupChatId.New(), 7, WeekStart, WeekStart, "en", null, 300, 120, 2, 0, 150, null, null, new (), new ());

        // act
        var note = CoachWeeklyNoteFlow.Compose(WeekStart, Week(WeekStart, 1000, 40), Week(WeekStart - TimeSpan.FromDays(7), 1000, 70), [best], settings, S);

        // assert
        note!.ScoreDelta.Should().BePositive();
        note.FocusKind.Should().Be(CoachMetricKind.Fillers);
        note.FocusDelta.Should().BeApproximately(-0.03, 1e-9);
        note.BestStartLid.Should().Be(7);
        note.IsPending.Should().BeTrue();
    }

    [Fact]
    public void ComposeShouldReturnNullForAQuietWeek()
        => CoachWeeklyNoteFlow.Compose(WeekStart, Week(WeekStart, 50, 1), Week(WeekStart - TimeSpan.FromDays(7), 1000, 70), [], new (), S)
            .Should().BeNull();
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Users.UnitTests --filter FullyQualifiedName~CoachWeeklyNoteTest`
Expected: build error.

- [ ] **Step 3: The flow**

`CoachWeeklyNoteFlow.cs`: copy `DigestFlow`'s skeleton (`[Flow(DelayQuanta = 3600)]`, `PeriodicFlow`, `Prepare` requiring an account with a time zone and `UserCoachSettings.IsWeeklySummaryEnabled`, `Run` due on Mondays at `UserEmailsSettings.DigestTime` local: `IsDue` = the last Monday-at-digest-time before `now` is after `LastRunAt`). `Run` reads the previous ISO week (`WeekStart(today) - 7d` .. `WeekStart(today)`) and the one before from `ICoachBackend.ListDays(userId, range, language: settings.SelectedLanguage or null)`, `ListConversations(userId, 20)` filtered to the week, calls `Compose`, and when not null writes it with `kvas.UserCoachWeeklyNote().Set(note with { Origin = "" })`. `Compose`:

```csharp
public static UserCoachWeeklyNote? Compose(Moment weekStart, CoachDay thisWeek, CoachDay lastWeek,
    ApiArray<CoachConversation> conversations, UserCoachSettings settings, CoachScoringSettings s)
{
    if (thisWeek.Words < s.MinScoreWords)
        return null;

    var language = thisWeek.Language.IsNullOrEmpty() ? null : thisWeek.Language;
    var score = CoachScoring.Score(thisWeek, s, language);
    var previous = CoachScoring.Score(lastWeek, s, language);
    var level = settings.LevelOf(language);
    var focus = settings.FocusByLanguage.TryGetValue(language ?? "", out var f)
        ? f
        : CoachFocus.Pick(CoachScoring.Summarize(CoachWindow.Days7, thisWeek, null, s, language), level, s);
    var deltas = CoachProgressBuilder.WeekDeltas(thisWeek, lastWeek, level, s, language);
    var focusDelta = focus is { } kind ? deltas.FirstOrDefault(d => d.Kind == kind) : null;
    var best = conversations
        .Where(c => c.StartedAt >= weekStart && c.StartedAt < weekStart + TimeSpan.FromDays(7) && c.SpeechSeconds >= 60)
        .OrderBy(c => (double)c.Fillers / Math.Max(1, c.Words))
        .FirstOrDefault();
    return new UserCoachWeeklyNote {
        WeekStart = weekStart,
        ScoreDelta = score is { } sc && previous is { } pr ? sc - pr : null,
        FocusKind = focus,
        FocusDelta = focusDelta is { Previous: { } was, Current: { } now } ? now - was : null,
        BestChatId = best?.ChatId ?? ChatId.None,
        BestStartLid = best?.StartEntryLid ?? 0,
    };
}
```

Register the flow where `DigestFlow` is registered and start it for a user from the same place `DigestFlow` is started (the sign-in flow or the account-created path); the flow suspends itself when the summary is off.

`RightPanelModeSwitch.razor`: the Coach button gets a `<span class="c-dot"></span>` when `UserSettingsUI.UserCoachWeeklyNote().Get(ct)` is pending (compute in its state). The Progress tab (Task 11) marks the note seen when rendered (`UserCoachWeeklyNote().Update(x => x with { IsSeen = true })` in `OnAfterRenderAsync` once).

- [ ] **Step 4: Docs and whole-suite verification**

Update `docs/api-index.md` and `docs/api-index-full.md` for the new types (`CoachConversation`, `CoachConversationBuilder`, `CoachProgressBuilder`, `CoachFocus`, `CoachSkillSets`, `CoachScope`, `CoachLanguageLevel`, `CoachWeeklyNoteFlow`, `UserCoachWeeklyNote`, the new `ICoach` members). Then:

```bash
dotnet test tests/Users.UnitTests && dotnet test tests/Chat.UnitTests && dotnet test tests/Chat.UI.Blazor.UnitTests
dotnet test tests/Users.IntegrationTests --filter FullyQualifiedName~Coach
dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter FullyQualifiedName~Coach
```

Expected: all PASS. Then a manual pass with the server loop (`curl -sk https://local.voxt.ai/health/stop`, wait for step 3 in `tmp/server-loop.log`), through the chrome MCP at https://local.voxt.ai: new-user state, first voice message → Recent card, Skills groups, Progress deltas, settings page, "Coach me here" in a chat menu switching analysis off, the language chips after a Russian and an English message.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Users.Service src/dotnet/UI.Blazor.App docs/api-index.md docs/api-index-full.md tests/Users.UnitTests/Coach/CoachWeeklyNoteTest.cs
git commit -m "feat(coach): the weekly note and docs for the panel redesign"
```

---

## Reuse

Existing abstractions used (searched in `docs/api-index.md` and the branch):

| Need | Piece |
|---|---|
| log + day rollup + rebuild | `CoachBackend.OnRecord/RebuildDay/OnRebuildDays`, `CoachDayBuilder` |
| bands, sub-scores | `CoachScoring`, `CoachScoringSettings`, `PaceBand` |
| stored settings | `StoredSettings` + `IHasKvasKey`, `UserScopedKvasBackendExt`, `UserSettingsUI` accessors |
| per-chat settings | `ChatUserSettings` (same KVAS record the notification mode uses) |
| place root chat | `ChatId.RootChatId`, `PlaceId.RootChatId` |
| periodic per-user job | `PeriodicFlow` (`DigestFlow`), `UserEmailsSettings.DigestTime`, `AccountFull.TimeZone`, `TZConvert` |
| languages spoken | `UserLanguageSettings.ListSpoken()`, `Languages.AllTranscription`, `Language.GetIsoCode` |
| word-split languages | `SpeechTextStats.IsWordSplittable` |
| UI building blocks | `Card`, `CardItem`, `Tile`, `TileItem`, `TileTopic`, `Toggle`, `TabPanel`/`TabDef`, `ButtonRound`, `MenuEntry`, `AvatarCircle`, `BarChart`/`ChartItem`, `ModalUI`, `ErrorBarrier` |
| persisted UI state | `StateFactory.NewKvasStored` on `LocalSettings` (as `RightPanelStoredState`) |
| tests | `TestWait`, `BlazorTester`, `NewAppHost` in-memory config, `FakeTagger`, `ChatEntryOperations.FinalizeStreamingEntry(…, LinearMap)` |

New components and placement:

| Component | Local | Shared | Recommendation |
|---|---|---|---|
| `CoachConversationBuilder`, `CoachProgressBuilder`, `CoachFocus`, `CoachSkillSets` | Users.Service | `Api/Users/Coach` | `Api/Users/Coach` for the conversation builder and skill sets (client needs them); the progress builder and focus stay in Users.Service (they need `CoachScoringSettings`) |
| `CoachScope` | Chat.Service/Coach | Core.Server | local: it reads chat and user settings only the chat shard combines |
| `CoachWeeklyNoteFlow` | Users.Service/Flows | — | local |
| `CoachFindings`, `CoachLabels` additions | UI.Blazor.App/Coach | — | local: coaching copy |
| `CoachSettingsPage`, tabs, cards | UI.Blazor.App/Coach | — | local; they compose shared `Tile*`/`Card*` |

Nothing new belongs in `ActualChat.Core`.

## Deferred to follow-ups (write into the ledger, not into the code)

- Bands calibrated from our own users (spec §6.3).
- A bell-panel notification kind for the weekly note, and email delivery through the digest.
- Character or syllable pace for languages without word spaces.
- "Not a filler" dispute on the tap hint (spec mentions it in the Skills footer; the hint keeps its v1 shape here).
- The place chat count in the switched-off list if no cheap client call exists (Task 13).
- Snooze on the tip cross; the dashed sentence underline.

## Self-review notes

- Spec coverage: §3.1 header + chips → Task 9; §3.2 score/focus card → Tasks 6, 9, 12; §3.3 tabs → Task 9; §3.4 Recent → Tasks 4, 10; §3.5 Progress → Tasks 6, 11, 14; §3.6 Skills → Tasks 5, 12; §3.7 new user → Task 9; §4 conversations → Task 4; §5.1–5.3 skills and bands → Task 5; §5.4 findings → Task 10; §6.1 days per language → Task 2; §6.2 settings → Tasks 1, 13; §7 explanation → Tasks 5, 12; §8.1 focus → Task 6; §8.2 clean tip → Task 7; §8.3 deltas → Task 6; §8.4 milestones → Task 6; §8.5 weekly note → Task 14; §9 settings page → Task 13; §9.1 scope → Task 3; §10 API → Tasks 2, 4, 5, 6; §11 components → Tasks 9–13; §12 localization → Task 8; §13 tests → every task.
- Type consistency: `ListDays(UserId, Range<Moment>, string? language, ct)` and `GetOwnSummary(Session, CoachWindow, string? language, ct)` are used with the language argument everywhere from Task 2 on; `CoachConversation` gains keys 15–19 in Task 10 (bands + participants), all constructed in Task 4's builder plus `with` in `BandConversation`; `CoachSkillSets` moves to `Api/Users/Coach` in Task 10 (Task 5 creates it in Users.Service; the move is a file move, no signature change); `CoachTab` lives in `CoachUI.cs` (Task 9) and is used by Tasks 10–13.
- Review Focus mapping: 1 → Task 6 test (`ListOwnLanguages` levels) and Task 9 (`.coach-language-chips` absent with one language); 2 → Task 4 tests `OneLongEntryShouldStayOneConversation` and the 31-minute split; 3 → Task 3 theory row `(true, false, false, true)`; 4 → Task 2 `MergeShouldCountConversationFieldsOncePerDay` and the integration split test; 5 → Task 7 `AWindowWithNoFillersShouldEarnOneCleanTipPerDay` and `AWeakWordInTheWindowShouldBlockTheCleanTip`.
- Placeholder scan: Tasks 11–13 describe some markup in prose with the exact component names, parameters and catalog keys rather than full razor listings; the implementer has the full pattern from Tasks 9 and 10 and the app's own `ChatSidePanelInfo`/`TranscriptionSettings` to copy.
