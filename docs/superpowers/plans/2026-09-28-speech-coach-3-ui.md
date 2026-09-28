# Speech coach v1 — plan 3: UI

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The four client surfaces of the speech coach on top of the plan-1 and plan-2 services: the
Coach mode of the right panel (score card, settings, window selector, metric rows), the Trends
screen, the live tip bar above the composer, inline marking of fillers and weak words in the user's
own voice messages, and jump-to-audio from a chip.

**Architecture:** The right panel gets a persisted *mode* (Chat | Coach) next to its visibility;
Coach mode swaps the chat side panel for `CoachPanel`, which is chat-independent and therefore
survives chat switches. A new scoped Fusion service `CoachUI` owns the client-side verdicts (feature
flag + user toggle), the per-tile marks cache that `PlayableTextMarkupView` reads, and jump-to-audio.
`CoachTipBar` renders the pending tip in the tip's chat from the SubFooter stack. Charts are two
small shared components in `UI.Blazor`. Every string is a `Coach_*` catalog key.

**Tech Stack:** Blazor (ActualLab.Fusion `ComputedStateComponent`, `TabPanel`, `ContentSwap`,
`Banner`, `TileItem`/`Toggle`), Fusion compute services on the client (`UIServiceBase<AppUIHub>`,
`IComputeService`), the localized catalog (`Strings.<lang>.json` + `LocalizedStringsLocalizerExt`),
Tailwind CSS via `styles.css` imports, bUnit (`TestBunitContext`, `BlazorTester`) and xUnit +
FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-09-25-speech-coach-design.md` — section *Client API and
surfaces* (the five surfaces and the mock corrections), *Metrics* (bands and labels), *Testing*
(UI bullets), *Reuse* (component placement table). Plans 1 and 2 are executed on this branch; the
contracts they produced are listed under *Interfaces already on the branch* below.

## Global Constraints

- Read `docs/CODING_STYLE.md` before writing any C#/razor/CSS: 120-char lines, no `Async` suffix,
  no `///` on members, no comments unless they explain a non-obvious invariant, `field ??=` lazy DI,
  `sealed` by default except Fusion-proxied services, `.ConfigureAwait(false)` in services and after
  locals are captured in components, tests named `<Subject>Should<Behavior>` with
  `// arrange / act / assert` and FluentAssertions. The style hook runs on every edit; fix what it
  reports.
- **Never hardcode user-visible English text** in razor or C# (`docs/i18n.md`). Every string is a
  `Coach_*` key in `Strings.en.json`, every hand-written catalog (`bg, bs, cs, de, en, es, fr, hi,
  id, it, ja, ko, pl, pt, ru, tr, uk, vi, zh`), a typed member in `LocalizedStringsLocalizerExt.cs`,
  then `scripts/derive-bcms.cmd` and `scripts/derive-max.cmd`. Enum-to-text mapping is an instance
  method that reads `L`, never `.ToString()`.
- The UI reads the feature only through `Features.Get<Features_EnableSpeechCoach>` (already on the
  branch, wraps `ICoach.IsEnabled`); with it false, no coach surface renders and the right panel
  behaves exactly as today.
- Marks and tips render only when `UserCoachSettings.IsCoachingEnabled` is true (tips additionally
  `AreLiveTipsEnabled`, which the server already enforces when writing a tip).
- New CSS files must be `@import`ed from `src/dotnet/UI.Blazor.App/styles.css` (App components) or
  the UI.Blazor equivalent; nothing is picked up by glob. Run `npm run build:Verify` after CSS/TS
  edits (or trigger the `/server-loop` rebuild if it is running).
- No new projects, no new NuGet packages, no new JS interop (the existing
  `playable-text-markup-view.ts` relies on `.playable-word` span index == word index; keep that).
- Dates and times go through `DateTimeConverter.ToLocalTime(...)` + `DateFormatter`, never
  `CultureInfo`.
- Prefer `List<T>`/arrays for client-local data; `ApiArray` only for what crosses RPC.
- Commit after every task with a conventional-commit message (`feat(coach-ui): ...`).

## Review Focus

1. **A translated voice message** (`ChatEntryMessageInternalView` swaps in a `PlayableTextMarkup`
   built from the translation, whose offsets differ from `entry.Content`): the marks must not be
   applied to the wrong words. Test in Task 4: markup text ≠ entry content → no coach classes.
2. **A message whose spans were computed before an edit** (server rows lag the new text): a span
   start beyond the text or inside whitespace must be ignored, never throw. Test in Task 2.
3. **A tip for another chat** (the user has chat A open, the tip was fired in chat B): the bar must
   stay hidden in A and show in B. Test in Task 10.
4. **The flag turning off while the panel is in Coach mode** (rollout change, or the server switch
   flipped): the panel must fall back to the chat side panel without a stale Coach mode sticking in
   local settings forever. `RightPanelContent` (Task 5) keys the swap on `IsCoachEnabled && Mode`, so
   a false flag always renders the chat panel; the stored mode is harmless. Checked in the manual
   pass of Task 11, since the flag cannot flip inside one test host.
5. **Windows with no speech** (a new user opens the tab): every metric row shows the "no data"
   text, the score card shows the words-needed hint, Trends shows the empty-days text, nothing
   divides by zero. Test in Task 6 and Task 8.

## Interfaces already on the branch (plans 1 and 2)

```csharp
// Api.Contracts/Users/ICoach.cs (client proxy registered via fusion.AddClient<ICoach>())
Task<bool> IsEnabled(Session session, CancellationToken ct);
Task<CoachSummary> GetOwnSummary(Session session, CoachWindow window, CancellationToken ct);
Task<ApiArray<CoachDay>> ListOwnDays(Session session, Range<Moment> dayRange, CancellationToken ct);
Task<UserCoachTip?> GetPendingTip(Session session, CancellationToken ct);
Task<ApiArray<CoachOccurrence>> ListOwnOccurrences(Session session, string word, CoachWindow window, CancellationToken ct);
// commands: Coach_DismissTip : ApiCommand<Unit>, Coach_RebuildOwnDays : ApiCommand<Unit>

// Api.Contracts/Chat/IChatCoach.cs
Task<bool> IsEnabled(Session session, CancellationToken ct);
Task<ApiArray<CoachEntryMarks>> GetOwnMarks(Session session, ChatId chatId, Range<long> lidRange, CancellationToken ct);
// lidRange ≤ 1280 lids; CoachEntryMarks(long EntryLid, ApiArray<SpeechSpan> Spans)

// Api/Chat/Coach: SpeechSpan(SpeechSpanKind Kind, string Word, int Start, int Length, ApiArray<string> Synonyms)
// SpeechSpanKind { FilledPause = 1, Filler = 2, Weak = 3, Profanity = 4, Repetition = 5 }
// Api/Users/Coach/CoachSummary.cs: CoachWindow { Today, Week, Month, AllTime },
//   CoachMetricKind { Pace, Pauses, Fillers, WeakWords, Repetition, Profanity, Questions, SentenceLength,
//                     Vocabulary, TurnTaking, Patience, Interruptions, Monologue },
//   CoachBand { None, Good, Medium, High, Low }, CoachChip(Word, Count),
//   CoachMetric(Kind, double? Value, double? Rate, Band, ApiArray<CoachChip> Chips),
//   CoachSummary(Window, int? Score, int? ScoreDelta, int Words, int Entries, int TaggedEntries, Metrics) + None,
//   CoachOccurrence(ChatId, long EntryLid, int Start, int Length, Moment At)
// Metric semantics (CoachScoring.Metrics): Pace.Value = wpm; Pauses.Value = pauses/min;
//   Fillers.Value = count, .Rate = share of tagged words; WeakWords same; Repetition.Value = count, .Rate;
//   Profanity.Value = count, .Rate; Questions.Value = count; SentenceLength.Value = words/sentence;
//   Vocabulary.Value = distinct/total (0..1); TurnTaking.Value = own share of talk time (0..1),
//   .Rate = own / fair share; Patience.Value = mean seconds; Interruptions.Value = count, .Rate per own turn;
//   Monologue.Value = seconds. Band: High doubles as fast/long, Low as slow/short, Good = balanced.
// Api/Users/Coach/CoachDay.cs: Day, Entries, TaggedEntries, Words, TaggedWords, Sentences, Questions,
//   Repetitions, Pauses, FilledPauses, Fillers, WeakWords, Profanities, Runs, OwnTurns, TotalTurns, Responses,
//   Interruptions, DurationSeconds, SpeechSeconds, PauseSeconds, ..., FillerCounts, WeakWordCounts
// Api/Users/StoredSettings: UserCoachSettings { IsCoachingEnabled, AreLiveTipsEnabled = true, TipInterval = 5 min }
//   accessor UserSettingsUI.UserCoachSettings() (Api/Users/UserSettingsUIExt.cs);
//   UserCoachTip { Kind: CoachTipKind { None, SlowDown, SpeedUp, Filler, WeakWord }, ChatId, EntryLid, Word,
//   Count, Wpm, Synonyms, ShownAt, IsDismissed, LastTipAt; IsPending }
// UI.Blazor/Services/Features/Features_EnableSpeechCoach.cs : FeatureDef<bool>, IClientFeatureDef
// Server test knobs: "ChatSettings:Coach:IsEnabled" = "true", "UsersSettings:Coach:Rollout" = "Everyone",
//   "ChatSettings:IsTranslationEnabled" = "true", "ChatSettings:UseFakeLanguageDetection" = "true";
//   tests/Testing.Host ChatEntryOperations.CreateStreamingEntry / FinalizeStreamingEntry post a voice entry.
```

## File structure

| File | Responsibility |
|---|---|
| `src/dotnet/Localization/Resources/Strings.*.json`, `LocalizedStringsLocalizerExt.cs` | the `Coach_*` keys |
| `src/dotnet/Api/Chat/Coach/SpeechSpanExt.cs` | span → word-index mapping (shared, next to `SpeechTextStats`) |
| `src/dotnet/UI.Blazor.App/Services/CoachUI.cs` | client verdicts, per-tile marks cache, jump-to-audio |
| `src/dotnet/UI.Blazor.App/Services/AppUIHub.cs` | `Coach`, `ChatCoach`, `CoachUI` accessors |
| `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs` | `CoachUI` registration |
| `src/dotnet/UI.Blazor.App/Components/MarkupParts/PlayableTextMarkupView.razor` + `markup-parts.css` | inline marking |
| `src/dotnet/UI.Blazor/Services/PanelsUI/RightPanelMode.cs`, `RightPanel.cs`, `RightPanelStoredState.cs` | persisted panel mode |
| `src/dotnet/UI.Blazor.App/Components/RightPanel/RightPanelContent.razor`, `RightPanelModeSwitch.razor`, `right-panel-mode-switch.css` | mode switch + swap |
| `src/dotnet/UI.Blazor.App/Components/ChatPropertiesMenu/ChatPropertiesMenu.razor`, `CoachMenuEntry.razor` | mobile entry point |
| `src/dotnet/UI.Blazor/Components/Charts/DonutChart.razor`, `BarChart.razor`, `ChartItem.cs`, `charts.css` | shared charts |
| `src/dotnet/UI.Blazor.App/Components/Coach/CoachPanel.razor`, `CoachScoreCard.razor`, `CoachSettingsTile.razor`, `CoachMetricRow.razor`, `CoachLabels.cs`, `CoachTrends.razor`, `CoachOccurrences.razor`, `CoachTipBar.razor`, `coach.css` | the surfaces |
| `src/dotnet/UI.Blazor.App/Components/ChatView/ChatView.razor` | mounts the tip bar |
| `tests/Chat.UnitTests/Coach/SpeechSpanExtTest.cs` | mapping tests |
| `tests/Chat.UI.Blazor.UnitTests/Charts/DonutChartTest.cs`, `BarChartTest.cs` | pure bUnit chart tests |
| `tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs` | host-backed tests for marks, panel mode, panel, tip bar, jump |
| `docs/api-index.md`, `docs/api-index-full.md` | new types |

---

### Task 1: The `Coach_*` catalog keys

**Files:**
- Modify: `src/dotnet/Localization/Resources/LocalizedStringsLocalizerExt.cs` (append members inside the `extension(IStringLocalizer l)` block)
- Modify: `src/dotnet/Localization/Resources/Strings.en.json` and `Strings.{bg,bs,cs,de,es,fr,hi,id,it,ja,ko,pl,pt,ru,tr,uk,vi,zh}.json`
- Generated: `Strings.{cnr,hr,sr,max}.json` via the scripts
- Test: `tests/Chat.UI.Blazor.UnitTests/AppLocalizationTest.cs` (existing; it fails until keys and members agree)

**Interfaces:**
- Produces: the members below, used by Tasks 5–10 exactly under these names.

- [ ] **Step 1: Add the typed members (this is the failing side of the test)**

Append inside the `extension(IStringLocalizer l)` block of `LocalizedStringsLocalizerExt.cs`, after the
last existing group:

```csharp
        // Speech coach
        public string Coach_Title => l["Coach_Title"].Value;
        public string Coach_ModeChat => l["Coach_ModeChat"].Value;
        public string Coach_ModeCoach => l["Coach_ModeCoach"].Value;
        public string Coach_TabMetrics => l["Coach_TabMetrics"].Value;
        public string Coach_TabTrends => l["Coach_TabTrends"].Value;
        public string Coach_WindowToday => l["Coach_WindowToday"].Value;
        public string Coach_WindowWeek => l["Coach_WindowWeek"].Value;
        public string Coach_WindowMonth => l["Coach_WindowMonth"].Value;
        public string Coach_WindowAllTime => l["Coach_WindowAllTime"].Value;
        public string Coach_Score_Format(object arg0) => l["Coach_Score_Format", arg0].Value;
        public string Coach_ScoreNeedsWords(long count, object arg0) => l.Plural("Coach_ScoreNeedsWords", count, arg0);
        public string Coach_ScoreDelta_Format(object arg0) => l["Coach_ScoreDelta_Format", arg0].Value;
        public string Coach_Analysed(long count, object arg0, object arg1) => l.Plural("Coach_Analysed", count, arg0, arg1);
        public string Coach_Words(long count, object arg0) => l.Plural("Coach_Words", count, arg0);
        public string Coach_Coaching => l["Coach_Coaching"].Value;
        public string Coach_CoachingCaption => l["Coach_CoachingCaption"].Value;
        public string Coach_LiveTips => l["Coach_LiveTips"].Value;
        public string Coach_TipInterval => l["Coach_TipInterval"].Value;
        public string Coach_TipIntervalMinutes(long count, object arg0)
            => l.Plural("Coach_TipIntervalMinutes", count, arg0);
        public string Coach_MetricPace => l["Coach_MetricPace"].Value;
        public string Coach_MetricPauses => l["Coach_MetricPauses"].Value;
        public string Coach_MetricFillers => l["Coach_MetricFillers"].Value;
        public string Coach_MetricWeakWords => l["Coach_MetricWeakWords"].Value;
        public string Coach_MetricRepetition => l["Coach_MetricRepetition"].Value;
        public string Coach_MetricProfanity => l["Coach_MetricProfanity"].Value;
        public string Coach_MetricQuestions => l["Coach_MetricQuestions"].Value;
        public string Coach_MetricSentenceLength => l["Coach_MetricSentenceLength"].Value;
        public string Coach_MetricVocabulary => l["Coach_MetricVocabulary"].Value;
        public string Coach_MetricTurnTaking => l["Coach_MetricTurnTaking"].Value;
        public string Coach_MetricPatience => l["Coach_MetricPatience"].Value;
        public string Coach_MetricInterruptions => l["Coach_MetricInterruptions"].Value;
        public string Coach_MetricMonologue => l["Coach_MetricMonologue"].Value;
        public string Coach_BandGood => l["Coach_BandGood"].Value;
        public string Coach_BandMedium => l["Coach_BandMedium"].Value;
        public string Coach_BandHigh => l["Coach_BandHigh"].Value;
        public string Coach_BandLow => l["Coach_BandLow"].Value;
        public string Coach_BandFast => l["Coach_BandFast"].Value;
        public string Coach_BandSlow => l["Coach_BandSlow"].Value;
        public string Coach_BandLong => l["Coach_BandLong"].Value;
        public string Coach_BandShort => l["Coach_BandShort"].Value;
        public string Coach_BandBalanced => l["Coach_BandBalanced"].Value;
        public string Coach_Wpm_Format(object arg0) => l["Coach_Wpm_Format", arg0].Value;
        public string Coach_PerMinute_Format(object arg0) => l["Coach_PerMinute_Format", arg0].Value;
        public string Coach_PercentOfSpeech_Format(object arg0) => l["Coach_PercentOfSpeech_Format", arg0].Value;
        public string Coach_WordsPerSentence_Format(object arg0) => l["Coach_WordsPerSentence_Format", arg0].Value;
        public string Coach_Seconds_Format(object arg0) => l["Coach_Seconds_Format", arg0].Value;
        public string Coach_PercentOfTalkTime_Format(object arg0) => l["Coach_PercentOfTalkTime_Format", arg0].Value;
        public string Coach_Percent_Format(object arg0) => l["Coach_Percent_Format", arg0].Value;
        public string Coach_NoData => l["Coach_NoData"].Value;
        public string Coach_TrendsComposition => l["Coach_TrendsComposition"].Value;
        public string Coach_TrendsOtherWords => l["Coach_TrendsOtherWords"].Value;
        public string Coach_TrendsPace => l["Coach_TrendsPace"].Value;
        public string Coach_TrendsNoDays => l["Coach_TrendsNoDays"].Value;
        public string Coach_Occurrences_Format(object arg0) => l["Coach_Occurrences_Format", arg0].Value;
        public string Coach_OccurrencesEmpty => l["Coach_OccurrencesEmpty"].Value;
        public string Coach_TipSlowDown_Format(object arg0) => l["Coach_TipSlowDown_Format", arg0].Value;
        public string Coach_TipSpeedUp_Format(object arg0) => l["Coach_TipSpeedUp_Format", arg0].Value;
        public string Coach_TipFiller_Format(object arg0, object arg1) => l["Coach_TipFiller_Format", arg0, arg1].Value;
        public string Coach_TipWeakWord_Format(object arg0) => l["Coach_TipWeakWord_Format", arg0].Value;
        public string Coach_TipSynonyms => l["Coach_TipSynonyms"].Value;
        public string Coach_OpenCoach => l["Coach_OpenCoach"].Value;
```

- [ ] **Step 2: Run the localization test to see it fail**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~AppLocalizationTest" -v q`
Expected: FAIL — `LocalizerExtMembersMatchEnglishKeysExactly` lists the `Coach_*` members with no key.

- [ ] **Step 3: Add the English keys**

Append to `Strings.en.json` as a new group before the closing brace (keep the file's JSONC style, add a
translator comment as the other groups do). Plural keys use `|`-separated forms; `{0}`/`{1}` are kept
in every translation:

```jsonc
  // Speech coach: the right panel's "Coach" mode (score, settings, metric rows, trends), the live
  // tip bar above the message composer, and the "where you said X" list. "Coaching" is the master
  // per-user toggle; "wpm" is words per minute; a "filler word" is "um", "you know"; a "weak word"
  // is a vague word such as "nice" or "stuff".
  "Coach_Title": "Speech coach",
  "Coach_ModeChat": "Chat",
  "Coach_ModeCoach": "Coach",
  "Coach_TabMetrics": "Metrics",
  "Coach_TabTrends": "Trends",
  "Coach_WindowToday": "Today",
  "Coach_WindowWeek": "Week",
  "Coach_WindowMonth": "Month",
  "Coach_WindowAllTime": "All time",
  "Coach_Score_Format": "Your score: {0}/100",
  "Coach_ScoreNeedsWords": "Speak at least {0} word to get a score|Speak at least {0} words to get a score",
  "Coach_ScoreDelta_Format": "{0} vs. your previous period",
  "Coach_Analysed": "{0} of {1} message analysed|{0} of {1} messages analysed",
  "Coach_Words": "{0} word|{0} words",
  "Coach_Coaching": "Coaching",
  "Coach_CoachingCaption": "Live tips and marks in your own messages",
  "Coach_LiveTips": "Show live tips",
  "Coach_TipInterval": "Tip frequency",
  "Coach_TipIntervalMinutes": "Once in {0} minute|Once in {0} minutes",
  "Coach_MetricPace": "Talking pace",
  "Coach_MetricPauses": "Pauses",
  "Coach_MetricFillers": "Filler words",
  "Coach_MetricWeakWords": "Weak words",
  "Coach_MetricRepetition": "Repetitions",
  "Coach_MetricProfanity": "Profanity",
  "Coach_MetricQuestions": "Questions",
  "Coach_MetricSentenceLength": "Sentence length",
  "Coach_MetricVocabulary": "Vocabulary variety",
  "Coach_MetricTurnTaking": "Turn-taking",
  "Coach_MetricPatience": "Patience",
  "Coach_MetricInterruptions": "Interruptions",
  "Coach_MetricMonologue": "Longest monologue",
  "Coach_BandGood": "Good",
  "Coach_BandMedium": "Medium",
  "Coach_BandHigh": "High",
  "Coach_BandLow": "Low",
  "Coach_BandFast": "Fast",
  "Coach_BandSlow": "Slow",
  "Coach_BandLong": "Long",
  "Coach_BandShort": "Short",
  "Coach_BandBalanced": "Balanced",
  "Coach_Wpm_Format": "{0} wpm",
  "Coach_PerMinute_Format": "{0} per minute",
  "Coach_PercentOfSpeech_Format": "{0}% of speech",
  "Coach_WordsPerSentence_Format": "{0} words per sentence",
  "Coach_Seconds_Format": "{0} s",
  "Coach_PercentOfTalkTime_Format": "{0}% of talk time",
  "Coach_Percent_Format": "{0}%",
  "Coach_NoData": "No data yet",
  "Coach_TrendsComposition": "What you say",
  "Coach_TrendsOtherWords": "Other words",
  "Coach_TrendsPace": "Pace by day",
  "Coach_TrendsNoDays": "No voice messages in this period",
  "Coach_Occurrences_Format": "Where you said “{0}”",
  "Coach_OccurrencesEmpty": "No occurrences in this period",
  "Coach_TipSlowDown_Format": "Slow down: {0} wpm",
  "Coach_TipSpeedUp_Format": "Speed up: {0} wpm",
  "Coach_TipFiller_Format": "Avoid filler words: “{0}” {1}+ times today",
  "Coach_TipWeakWord_Format": "Choose a different word for “{0}”",
  "Coach_TipSynonyms": "Try:",
  "Coach_OpenCoach": "Speech coach",
```

- [ ] **Step 4: Translate the same keys into the 18 other hand-written catalogs**

Add the identical key set to each of `Strings.{bg,bs,cs,de,es,fr,hi,id,it,ja,ko,pl,pt,ru,tr,uk,vi,zh}.json`
in the same position, translated. Rules from `docs/i18n.md`: keep every `{0}`/`{1}`; plural keys carry
the language's own number of forms (ru/uk/bs: one|few|many, e.g. `"Coach_Words": "{0} слово|{0} слова|{0} слов"`;
pl: one|few|many with 21 → many; cs: one|few|other; ja/ko/zh/id/vi/tr: a single form); `wpm`
stays Latin; "Coach" as the mode name is a product term (keep short, e.g. ru «Коуч», de «Coach»).
`bs` is the hand-written BCMS base; do **not** touch `cnr`, `hr`, `sr`, `max`.

- [ ] **Step 5: Regenerate the derived catalogs**

Run (from the repo root):
```
scripts/derive-bcms.cmd
scripts/derive-max.cmd
scripts/derive-bcms.cmd --check
scripts/derive-max.cmd --check
```
Expected: the four generated files change; both `--check` runs exit 0.

- [ ] **Step 6: Run the localization tests to see them pass**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~AppLocalizationTest|FullyQualifiedName~PluralLocalizerExtTest" -v q`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/Localization
git commit -m "feat(coach-ui): add the speech coach catalog keys"
```

---

### Task 2: Span → word-index mapping (`SpeechSpanExt`)

**Files:**
- Create: `src/dotnet/Api/Chat/Coach/SpeechSpanExt.cs`
- Test: `tests/Chat.UnitTests/Coach/SpeechSpanExtTest.cs`

**Interfaces:**
- Consumes: `PlayableTextMarkup.Words` (`Word(string Value, Range<int> TextRange, Range<float> TimeRange)`; `TextRange` includes trailing whitespace), `SpeechSpan`.
- Produces: `public static SpeechSpanKind?[]? SpeechSpanExt.MapToWords(this IReadOnlyList<SpeechSpan> spans, PlayableTextMarkup markup)` — one slot per word, `null` for unmarked words, whole result `null` when nothing maps. Used by Task 4.

- [ ] **Step 1: Write the failing tests**

`tests/Chat.UnitTests/Coach/SpeechSpanExtTest.cs`:

```csharp
using ActualChat.Chat;

namespace ActualChat.Chat.UnitTests.Coach;

public class SpeechSpanExtTest
{
    private static PlayableTextMarkup Markup(string text)
        => new (text, new LinearMap([0, text.Length], [0f, 10f]));

    private static SpeechSpan Span(SpeechSpanKind kind, string text, string word, int occurrence = 1)
    {
        var start = -1;
        for (var i = 0; i < occurrence; i++)
            start = text.IndexOf(word, start + 1);
        return new SpeechSpan(kind, word, start, word.Length, ApiArray<string>.Empty);
    }

    [Fact]
    public void MapToWordsShouldMarkTheWordContainingEachSpan()
    {
        // arrange
        const string text = "So, um, I went to the the store.";
        var markup = Markup(text);
        var spans = new[] {
            Span(SpeechSpanKind.FilledPause, text, "um"),
            Span(SpeechSpanKind.Repetition, text, "the", 2),
        };

        // act
        var kinds = spans.MapToWords(markup);

        // assert
        kinds.Should().NotBeNull();
        kinds!.Length.Should().Be(markup.Words.Length);
        kinds[1].Should().Be(SpeechSpanKind.FilledPause, "'um,' is the second word");
        kinds[6].Should().Be(SpeechSpanKind.Repetition, "the repeated 'the' is the seventh word");
        kinds.Count(k => k is not null).Should().Be(2);
    }

    [Fact]
    public void MapToWordsShouldIgnoreSpansOutsideTheText()
    {
        // arrange
        const string text = "short text";
        var markup = Markup(text);
        var spans = new[] {
            new SpeechSpan(SpeechSpanKind.Weak, "gone", 40, 4, ApiArray<string>.Empty),
            new SpeechSpan(SpeechSpanKind.Weak, "x", -1, 1, ApiArray<string>.Empty),
        };

        // act
        var kinds = spans.MapToWords(markup);

        // assert
        kinds.Should().BeNull("a span the text no longer holds maps to nothing");
    }

    [Fact]
    public void MapToWordsShouldKeepTheFirstKindWhenTwoSpansHitOneWord()
    {
        // arrange
        const string text = "like like";
        var markup = Markup(text);
        var spans = new[] {
            Span(SpeechSpanKind.Filler, text, "like", 2),
            Span(SpeechSpanKind.Repetition, text, "like", 2),
        };

        // act
        var kinds = spans.MapToWords(markup);

        // assert
        kinds![1].Should().Be(SpeechSpanKind.Filler);
    }

    [Fact]
    public void MapToWordsShouldReturnNullForNoSpans()
    {
        // act
        var kinds = Array.Empty<SpeechSpan>().MapToWords(Markup("a b"));

        // assert
        kinds.Should().BeNull();
    }
}
```

Check the `LinearMap` constructor used by the other coach unit tests (`tests/Chat.UnitTests/Coach/SpeechTimingStatsTest.cs`) and copy its form if it differs from `new LinearMap(float[] xs, float[] ys)`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Chat.UnitTests --filter "FullyQualifiedName~SpeechSpanExtTest" -v q`
Expected: FAIL to compile — `MapToWords` does not exist.

- [ ] **Step 3: Write the implementation**

`src/dotnet/Api/Chat/Coach/SpeechSpanExt.cs`:

```csharp
namespace ActualChat.Chat;

public static class SpeechSpanExt
{
    // One slot per markup word, null where no span starts; null overall when nothing maps. The
    // words carry trailing whitespace, so a span start inside a word's TextRange is that word.
    public static SpeechSpanKind?[]? MapToWords(this IReadOnlyList<SpeechSpan> spans, PlayableTextMarkup markup)
    {
        if (spans.Count == 0)
            return null;

        var words = markup.Words;
        if (words.Length == 0)
            return null;

        SpeechSpanKind?[]? kinds = null;
        foreach (var span in spans) {
            var index = FindWord(words, span.Start);
            if (index < 0)
                continue;

            kinds ??= new SpeechSpanKind?[words.Length];
            kinds[index] ??= span.Kind;
        }
        return kinds;
    }

    private static int FindWord(PlayableTextMarkup.Word[] words, int position)
    {
        if (position < 0)
            return -1;

        var low = 0;
        var high = words.Length - 1;
        while (low <= high) {
            var mid = (low + high) >> 1;
            var range = words[mid].TextRange;
            if (position < range.Start)
                high = mid - 1;
            else if (position >= range.End)
                low = mid + 1;
            else
                return mid;
        }
        return -1;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Chat.UnitTests --filter "FullyQualifiedName~SpeechSpanExtTest" -v q`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Api/Chat/Coach/SpeechSpanExt.cs tests/Chat.UnitTests/Coach/SpeechSpanExtTest.cs
git commit -m "feat(coach): map speech spans to markup words"
```

---

### Task 3: `CoachUI` — client verdicts, per-tile marks cache, jump-to-audio

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Services/CoachUI.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/AppUIHub.cs` (add three accessors next to `IUsage Usage`, line ~34, and `HighlightUI`, line ~83)
- Modify: `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs` (register next to `fusion.AddService<HighlightUI>(ServiceLifetime.Scoped);`, line ~79)
- Test: `tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs` (new; later tasks add to it)

**Interfaces:**
- Consumes: `ICoach`, `IChatCoach`, `Features_EnableSpeechCoach`, `UserSettingsUI.UserCoachSettings()`, `Constants.Chat.EntryIdTiles` (`TileLayer<long>`, `GetTile(long point).Range`), `ChatUI.GetEntry(ChatEntryId)`, `ChatUI.SelectedChatId`, `ChatUI.HighlightEntry(ChatEntryId?, bool navigate, bool updateUI = true)`, `History.NavigateTo(string url, ...)`, `Links.Chat(ChatId, long)`, `ChatAudioUI.StartReplay(ChatId, Moment)`, `PanelsUI.HidePanels()`.
- Produces (used by Tasks 4, 5, 6, 9, 10):
  ```csharp
  public class CoachUI : UIServiceBase<AppUIHub>, IComputeService
  [ComputeMethod] Task<bool> IsEnabled(CancellationToken ct)          // the feature flag
  [ComputeMethod] Task<bool> IsMarkingEnabled(CancellationToken ct)   // flag && IsCoachingEnabled
  [ComputeMethod] Task<ApiArray<SpeechSpan>> GetOwnMarks(ChatEntryId entryId, AuthorId authorId, CancellationToken ct)
  Task JumpTo(CoachOccurrence occurrence, CancellationToken ct)
  ```
  `AppUIHub.Coach` (`ICoach`), `AppUIHub.ChatCoach` (`IChatCoach`), `AppUIHub.CoachUI`.

- [ ] **Step 1: Write the failing test**

`tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs`:

```csharp
using ActualChat.Chat.Module;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Users;
using ActualChat.Users.Module;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class CoachUITest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    private const string Text = "So, um, I went to the the store. It was awesome.";

    private sealed class FakeTagger : ISpeechTagger
    {
        public Task<SpeechTagResult?> Tag(SpeechTagRequest request, CancellationToken cancellationToken)
        {
            var spans = SpeechTagger.ParseResponse(request.Text, """
                {"items":[{"class":"filledPause","word":"um","occurrence":1,"synonyms":[]},
                          {"class":"weak","word":"awesome","occurrence":1,"synonyms":["excellent"]}]}
                """);
            return Task.FromResult<SpeechTagResult?>(new SpeechTagResult(spans, 1));
        }
    }

    private Task<TestAppHost> NewCoachHost(string name)
    {
        var coach = $"{nameof(ChatSettings)}:{nameof(ChatSettings.Coach)}";
        var users = $"{nameof(UsersSettings)}:{nameof(UsersSettings.Coach)}";
        return NewAppHost(name, options => options with {
            UseNatsQueues = false,
            ConfigureHost = (_, cfg) => cfg.AddInMemoryCollection(
                ($"{coach}:{nameof(CoachSettings.IsEnabled)}", "true"),
                ($"{coach}:{nameof(CoachSettings.ConversationMaturity)}", "00:00:01"),
                ($"{users}:{nameof(CoachScoringSettings.Rollout)}", nameof(CoachRollout.Everyone)),
                ($"{nameof(ChatSettings)}:{nameof(ChatSettings.IsTranslationEnabled)}", "true"),
                ($"{nameof(ChatSettings)}:{nameof(ChatSettings.UseFakeLanguageDetection)}", "true")),
            ConfigureServices = (_, services)
                => services.Replace(ServiceDescriptor.Singleton<ISpeechTagger>(new FakeTagger())),
        });
    }

    private static async Task<ChatEntry> PostVoice(IWebTester tester, ChatId chatId, string text)
    {
        var streaming = await tester.CreateStreamingEntry(chatId, Language.Parse("en-US"));
        return (await tester.FinalizeStreamingEntry(streaming, text)).ChatEntrySlim;
    }

    private static Task OptIn(BlazorTester tester)
        => tester.ScopedAppServices.AppUIHub().UserSettingsUI.UserCoachSettings()
            .Set(new UserCoachSettings { IsCoachingEnabled = true });

    [Fact(Timeout = 60_000)]
    public async Task GetOwnMarksShouldReturnSpansOnlyForOwnEntriesWhenCoachingIsOn()
    {
        // arrange
        var appHost = await NewCoachHost("coach-ui-marks");
        await using var _1 = appHost;
        await using var bob = appHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        var (chatId, inviteId) = await bob.CreateChat(true);
        await using var alice = appHost.NewBlazorTester(Out);
        await alice.SignInAsAlice();
        await alice.JoinChat(chatId, inviteId);
        var bobUI = bob.ScopedAppServices.AppUIHub().CoachUI;
        var aliceUI = alice.ScopedAppServices.AppUIHub().CoachUI;
        var entry = await PostVoice(bob, chatId, Text);

        // act
        var beforeOptIn = await bobUI.GetOwnMarks(entry.Id, entry.AuthorId, default);
        await OptIn(bob);
        var marks = await TestWait.When(async ct => {
            var m = await bobUI.GetOwnMarks(entry.Id, entry.AuthorId, ct);
            m.Should().NotBeEmpty();
            return m;
        }, TimeSpan.FromSeconds(30));
        await OptIn(alice);
        var aliceMarks = await aliceUI.GetOwnMarks(entry.Id, entry.AuthorId, default);

        // assert
        beforeOptIn.Should().BeEmpty("marking is opt-in");
        marks.Select(s => s.Kind).Should().Contain(SpeechSpanKind.FilledPause);
        aliceMarks.Should().BeEmpty("the entry is not hers");
        (await bobUI.IsEnabled(default)).Should().BeTrue();
    }
}
```

Check `ChatUICollection`/`ChatAppHostFixture` are the names used by `AppReviewPromptUITest.cs` in the
same project, and that `SharedAppHostTestBase` exposes `NewAppHost` there as it does in
`tests/Chat.IntegrationTests/CoachAnalysisTest.cs`. Add project references or `using`s the compiler asks for
(`ActualChat.Chat.ML` for `ISpeechTagger`/`SpeechTagger`, `ActualChat.Users.Module` for `UsersSettings`).

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter "FullyQualifiedName~CoachUITest" -v q`
Expected: FAIL to compile — `AppUIHub.CoachUI` does not exist.

- [ ] **Step 3: Write the service, hub accessors and registration**

`src/dotnet/UI.Blazor.App/Services/CoachUI.cs`:

```csharp
using ActualChat.Chat;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// The client side of the speech coach: the per-user verdicts, the marks behind inline marking
/// (fetched once per entry tile), and jump-to-audio from an occurrence.
/// </summary>
public class CoachUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private const double ReplayLeadSeconds = 0.25;

    private IChats Chats => Hub.Chats;
    private IChatCoach ChatCoach => Hub.ChatCoach;
    private ChatUI ChatUI => Hub.ChatUI;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;

    [ComputeMethod]
    public virtual async Task<bool> IsEnabled(CancellationToken cancellationToken)
        => await Features.Get<Features_EnableSpeechCoach>(cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<bool> IsMarkingEnabled(CancellationToken cancellationToken)
    {
        if (!await IsEnabled(cancellationToken).ConfigureAwait(false))
            return false;

        var settings = await UserSettingsUI.UserCoachSettings().Get(cancellationToken).ConfigureAwait(false);
        return settings.IsCoachingEnabled;
    }

    [ComputeMethod]
    public virtual async Task<ApiArray<SpeechSpan>> GetOwnMarks(
        ChatEntryId entryId, AuthorId authorId, CancellationToken cancellationToken)
    {
        if (!await IsMarkingEnabled(cancellationToken).ConfigureAwait(false))
            return ApiArray<SpeechSpan>.Empty;

        var chat = await Chats.Get(Session, entryId.ChatId, cancellationToken).ConfigureAwait(false);
        if (chat?.Rules.Author?.Id != authorId)
            return ApiArray<SpeechSpan>.Empty;

        var tile = Constants.Chat.EntryIdTiles.GetTile(entryId.LocalId);
        var marks = await GetOwnMarksTile(entryId.ChatId, tile.Range, cancellationToken).ConfigureAwait(false);
        foreach (var mark in marks)
            if (mark.EntryLid == entryId.LocalId)
                return mark.Spans;
        return ApiArray<SpeechSpan>.Empty;
    }

    public async Task JumpTo(CoachOccurrence occurrence, CancellationToken cancellationToken)
    {
        var entryId = ChatEntryId.New(occurrence.ChatId, occurrence.EntryLid);
        var entry = await ChatUI.GetEntry(entryId, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return;

        if (ChatUI.SelectedChatId.Value == occurrence.ChatId)
            ChatUI.HighlightEntry(entryId, navigate: true);
        else
            await History.NavigateTo(Links.Chat(occurrence.ChatId, occurrence.EntryLid)).ConfigureAwait(false);
        PanelsUI.HidePanels();

        if (entry.Audio?.TimeMap.TryMap(occurrence.Start) is not { } startTime)
            return;

        var startAt = entry.BeginsAt + TimeSpan.FromSeconds(startTime - ReplayLeadSeconds);
        await ChatAudioUI.StartReplay(occurrence.ChatId, startAt).ConfigureAwait(false);
    }

    // Protected methods

    // One RPC per 5-lid tile; the entries of a tile share it through the compute cache
    [ComputeMethod]
    protected virtual Task<ApiArray<CoachEntryMarks>> GetOwnMarksTile(
        ChatId chatId, Range<long> lidTileRange, CancellationToken cancellationToken)
        => ChatCoach.GetOwnMarks(Session, chatId, lidTileRange, cancellationToken);
}
```

`AppUIHub.cs` — add next to `IUsage Usage`:
```csharp
    public ICoach Coach => field ??= Services.GetRequiredService<ICoach>();
    public IChatCoach ChatCoach => field ??= Services.GetRequiredService<IChatCoach>();
```
and next to `HighlightUI HighlightUI`:
```csharp
    public CoachUI CoachUI => field ??= Services.GetRequiredService<CoachUI>();
```

`BlazorUIAppModule.cs` — after `fusion.AddService<HighlightUI>(ServiceLifetime.Scoped);`:
```csharp
        fusion.AddService<CoachUI>(ServiceLifetime.Scoped);
```

If `History.NavigateTo` takes a `LocalUrl`/string mismatch, pass `Links.Chat(...).Value`. If
`PanelsUI.HidePanels()` needs the dispatcher, wrap it in `Dispatcher.InvokeSafeAsync` as `RightPanel.SetIsVisible` does.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter "FullyQualifiedName~CoachUITest" -v q`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/CoachUI.cs src/dotnet/UI.Blazor.App/Services/AppUIHub.cs \
  src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs
git commit -m "feat(coach-ui): add CoachUI with the per-tile marks cache and jump-to-audio"
```

---

### Task 4: Inline marking in `PlayableTextMarkupView`

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Components/MarkupParts/PlayableTextMarkupView.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/MarkupParts/markup-parts.css` (after the `.playable-text-markup .highlight` rule, ~line 170)
- Test: `tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs` (add two tests)

**Interfaces:**
- Consumes: `CoachUI.GetOwnMarks(entryId, authorId, ct)` (Task 3), `SpeechSpanExt.MapToWords` (Task 2).
- Produces: per-word CSS classes `coach-filler` (FilledPause, Filler) and `coach-weak` (Weak, Repetition); nothing for Profanity. The per-word `<span class="playable-word ...">` elements now render whenever marks exist, not only during playback; their count stays equal to `Markup.Words.Length` (the JS click handler depends on it).

- [ ] **Step 1: Write the failing tests**

Add to `CoachUITest.cs`:

```csharp
    [Fact(Timeout = 60_000)]
    public async Task PlayableTextMarkupViewShouldMarkFillersAndWeakWordsOfOwnEntries()
    {
        // arrange
        var appHost = await NewCoachHost("coach-ui-marking");
        await using var _1 = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        tester.JSInterop.Mode = JSRuntimeMode.Loose;
        var (chatId, _) = await tester.CreateChat(true);
        await OptIn(tester);
        var coachUI = tester.ScopedAppServices.AppUIHub().CoachUI;
        var entry = await PostVoice(tester, chatId, Text);
        await TestWait.When(async ct
            => (await coachUI.GetOwnMarks(entry.Id, entry.AuthorId, ct)).Should().NotBeEmpty(),
            TimeSpan.FromSeconds(30));
        var markup = new PlayableTextMarkup(entry.Content, entry.Audio!.TimeMap);

        // act
        var cut = tester.Render<CascadingValue<ChatEntry>>(p => p
            .Add(x => x.Value, entry)
            .Add(x => x.IsFixed, true)
            .AddChildContent<PlayableTextMarkupView>(c => c.Add(x => x.Markup, markup)));

        // assert
        cut.WaitForAssertion(() => {
            cut.FindAll(".playable-word").Count.Should().Be(markup.Words.Length,
                "the JS click handler maps span index to word index");
            cut.FindAll(".coach-filler").Should().ContainSingle().Which.TextContent.Trim().Should().Be("um,");
            cut.FindAll(".coach-weak").Select(e => e.TextContent.Trim()).Should().BeEquivalentTo(["the", "awesome."],
                "the repeated word and the weak word get the dotted underline");
        }, TimeSpan.FromSeconds(10));
    }

    [Fact(Timeout = 60_000)]
    public async Task PlayableTextMarkupViewShouldNotMarkATranslatedMarkup()
    {
        // arrange
        var appHost = await NewCoachHost("coach-ui-marking-translated");
        await using var _1 = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        tester.JSInterop.Mode = JSRuntimeMode.Loose;
        var (chatId, _) = await tester.CreateChat(true);
        await OptIn(tester);
        var coachUI = tester.ScopedAppServices.AppUIHub().CoachUI;
        var entry = await PostVoice(tester, chatId, Text);
        await TestWait.When(async ct
            => (await coachUI.GetOwnMarks(entry.Id, entry.AuthorId, ct)).Should().NotBeEmpty(),
            TimeSpan.FromSeconds(30));
        var translated = new PlayableTextMarkup("Итак, эм, я пошёл в магазин.", entry.Audio!.TimeMap);

        // act
        var cut = tester.Render<CascadingValue<ChatEntry>>(p => p
            .Add(x => x.Value, entry)
            .Add(x => x.IsFixed, true)
            .AddChildContent<PlayableTextMarkupView>(c => c.Add(x => x.Markup, translated)));
        await Task.Delay(500);

        // assert
        cut.FindAll(".coach-filler").Should().BeEmpty("the spans index the original text, not the translation");
        cut.FindAll(".coach-weak").Should().BeEmpty();
    }
```

`PlayableTextMarkupView` also cascades on `MarkupRenderContext`? Check `ComputedMarkupViewBase.cs`: if a
second cascading parameter is required, nest a second `CascadingValue` in the render call the same way.
If `Render<CascadingValue<ChatEntry>>` with `AddChildContent<T>` is not available in bUnit 2, use
`tester.Render(builder => { builder.OpenComponent<CascadingValue<ChatEntry>>(0); ... })` with a
`RenderFragment` child, as `ErrorBarrierBoundaryTest.cs` does with `RenderTreeBuilder`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter "FullyQualifiedName~PlayableTextMarkupViewShould" -v q`
Expected: the first FAILS (`.playable-word` count is 0 — words render as plain text without playback; no `.coach-*`); the second passes trivially (fine — it is the guard for the change).

- [ ] **Step 3: Change the view**

Replace the markup block and the relevant `@code` parts of `PlayableTextMarkupView.razor` so the file reads:

```razor
@using ActualChat.MediaPlayback
@using ActualChat.UI.Blazor.App.Module
@inherits ComputedMarkupViewBase<PlayableTextMarkup, PlayableTextMarkupView.Model>
@{
    var m = _rendered = State.Value;
    var colorCls = "playable-text-color-" + (AuthorColors.GetIndex(Entry.AuthorId) + 1);
    var playOnClickIsEnabled = !m.HasSelection;
    var containerCls = "playable-text-markup" + (playOnClickIsEnabled ? " cursor-pointer" : " play-disabled");
}

<span @ref="Ref" class="@containerCls @colorCls">
    @if (m.Map == null && m.MarkKinds == null) {
        @Markup.Text
    } else {
        var wordCount = Markup.Words.Length;
        var map = m.Map ?? new WordPlayback[wordCount];
        RenderFragment<int> renderWord = i =>
            @<span class="playable-word @(IsHighlighted(i) ? "highlight" : "") @MarkClass(i)">@if (i == wordCount - 1) {
                <BreakableWord Text="@Markup.Words[i].Value"/>
            }
            else {
                @Markup.Words[i].Value
            }</span>;
        foreach (var run in BuildRuns(map, wordCount)) {
            if (run.Fill == WordPlayback.None) {
                for (var i = run.Start; i <= run.End; i++) {
                    @renderWord(i)
                }
            }
            else {
                var runCls = run.Fill == WordPlayback.Playing ? "playing-run" : "played-run";
                <span class="@runCls">@for (var i = run.Start; i <= run.End; i++) {
                    @renderWord(i)
                }</span>
            }
        }
    }
</span>
```

In `ComputeState`, after `var highlightedWords = ...`, add the marks and carry them (and the
highlighted words, which the old code dropped) into both return branches:

```csharp
        var markKinds = await GetMarkKinds(cancellationToken).ConfigureAwait(false);
        if (rendered is not { Map: not null })
            return model with { // Never rendered
                HasSelection = hasSelection,
                HighlightedWords = highlightedWords,
                MarkKinds = markKinds,
            };

        var map = EnrichMapWithPlayedStatus(model.Map, rendered.Map);
        return new() {
            Map = map,
            HasSelection = hasSelection,
            HighlightedWords = highlightedWords,
            MarkKinds = markKinds,
        };
```

with, inside `ComputeState`'s locals (captured before any await): `var entry = Entry;` and a new
local function next to `InnerComputeState`:

```csharp
        async Task<SpeechSpanKind?[]?> GetMarkKinds(CancellationToken cancellationToken1)
        {
            // Spans index entry.Content; a translated markup has different offsets
            if (!hasAudio || markup.Text != entry.Content)
                return null;

            var spans = await Hub.CoachUI.GetOwnMarks(entryId, entry.AuthorId, cancellationToken1).ConfigureAwait(false);
            return spans.MapToWords(markup);
        }
```

Add the class helper next to `IsHighlighted`:

```csharp
    private string MarkClass(int index)
        => _rendered?.MarkKinds?[index] switch {
            SpeechSpanKind.FilledPause or SpeechSpanKind.Filler => "coach-filler",
            SpeechSpanKind.Weak or SpeechSpanKind.Repetition => "coach-weak",
            _ => "",
        };
```

and extend the model:

```csharp
    public sealed record Model {
        public static readonly Model None = new();

        public WordPlayback[]? Map { get; init; }
        public bool HasSelection { get; init; }
        public IReadOnlySet<string> HighlightedWords { get; init; } = new ApiSet<string>();
        public SpeechSpanKind?[]? MarkKinds { get; init; }
    }
```

Add `@using ActualChat.Chat` if `SpeechSpanKind` is not already in scope (the file already inherits a
`ComputedMarkupViewBase` from the App project, so it probably is).

CSS, `markup-parts.css`, after the `.playable-text-markup .highlight` rule:

```css
.playable-text-markup .coach-filler {
    @apply line-through decoration-2;
    text-decoration-color: var(--danger);
}
.playable-text-markup .coach-weak {
    @apply underline decoration-dotted decoration-2 underline-offset-4;
    text-decoration-color: var(--warning);
}
```

Use the color variables the project defines (grep `--danger`/`--warning` in
`src/dotnet/UI.Blazor.App/styles.css` or the theme CSS; fall back to `text-decoration-color: currentColor` if absent).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter "FullyQualifiedName~CoachUITest" -v q`
Expected: PASS (3 tests).

- [ ] **Step 5: Rebuild the web bundle and check the existing search-highlight and playback still work**

Run: `npm run build:Verify` (or trigger the `/server-loop` rebuild). Expected: no errors.
Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter "FullyQualifiedName~ChatReplayPlayerTest|FullyQualifiedName~SearchUITest" -v q`. Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Components/MarkupParts tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs
git commit -m "feat(coach-ui): mark fillers and weak words inline in own voice messages"
```

---

### Task 5: Right panel mode (Chat | Coach) and the mobile entry point

**Files:**
- Create: `src/dotnet/UI.Blazor/Services/PanelsUI/RightPanelMode.cs`
- Modify: `src/dotnet/UI.Blazor/Services/PanelsUI/RightPanelStoredState.cs`, `RightPanel.cs`
- Create: `src/dotnet/UI.Blazor.App/Components/RightPanel/RightPanelModeSwitch.razor`, `right-panel-mode-switch.css`
- Modify: `src/dotnet/UI.Blazor.App/Components/RightPanel/RightPanelContent.razor`
- Create: `src/dotnet/UI.Blazor.App/Components/ChatPropertiesMenu/CoachMenuEntry.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatPropertiesMenu/ChatPropertiesMenu.razor`
- Modify: `src/dotnet/UI.Blazor.App/styles.css` (import the new css)
- Test: `tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs` (add one test)

**Interfaces:**
- Consumes: `CoachUI.IsEnabled` (Task 3), `L.Coach_ModeChat`, `L.Coach_ModeCoach`, `L.Coach_OpenCoach` (Task 1). `CoachPanel` (Task 6) — until Task 6 lands, `RightPanelContent` renders a placeholder `<div class="coach-panel"/>` for the Coach layer; Task 6 replaces it.
- Produces:
  ```csharp
  public enum RightPanelMode { Chat = 0, Coach = 1 }
  RightPanel.Mode : IState<RightPanelMode>; RightPanel.SetMode(RightPanelMode); RightPanel.Open(RightPanelMode)
  RightPanelStoredState.Mode { get; set; }
  ```

- [ ] **Step 1: Write the failing test**

Add to `CoachUITest.cs`:

```csharp
    [Fact(Timeout = 60_000)]
    public async Task RightPanelModeShouldPersistAcrossScopes()
    {
        // arrange
        var appHost = await NewCoachHost("coach-ui-panel-mode");
        await using var _1 = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var hub = tester.ScopedAppServices.AppUIHub();
        var stored = tester.ScopedAppServices.GetRequiredService<RightPanelStoredState>();
        await stored.WhenRead;

        // act
        hub.PanelsUI.Right.Open(RightPanelMode.Coach);

        // assert
        await TestWait.When(ct => Task.FromResult(hub.PanelsUI.Right.Mode.Value.Should().Be(RightPanelMode.Coach)));
        await TestWait.When(ct => Task.FromResult(stored.Mode.Should().Be(RightPanelMode.Coach)),
            because: "the mode survives a chat switch and the next session");
        hub.PanelsUI.Right.IsVisible.Value.Should().BeTrue();
    }
```

(`TestWait.When` overloads: see `docs/testing/waiting.md`; use the `Func<CancellationToken, Task>` form
and drop `because:` if there is no such parameter.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter "FullyQualifiedName~RightPanelModeShould" -v q`
Expected: FAIL to compile — `RightPanelMode`, `Open`, `Mode` do not exist.

- [ ] **Step 3: Add the mode to the stored state and the panel service**

`RightPanelMode.cs`:
```csharp
namespace ActualChat.UI.Blazor.Services;

public enum RightPanelMode
{
    Chat = 0,
    Coach = 1,
}
```

`RightPanelStoredState.cs` — add a second stored state beside `_isVisibleStored`:
```csharp
    private const string PanelModeKey = "RightPanel.Mode";
    private readonly StoredState<Box<RightPanelMode>> _modeStored;

    public Task WhenRead => Task.WhenAll(_isVisibleStored.WhenRead, _modeStored.WhenRead);

    public RightPanelMode Mode {
        get => _modeStored.Value.Value;
        set => _modeStored.Value = Box.New(value);
    }
```
and in the constructor, after `_isVisibleStored = ...`:
```csharp
        _modeStored = stateFactory.NewKvasStored<Box<RightPanelMode>>(
            new (localSettings, PanelModeKey) {
                InitialValue = Box.New(RightPanelMode.Chat),
                Category = StateCategories.Get(GetType(), "ModeStored"),
            });
```
(`WhenRead` was `_isVisibleStored.WhenRead`; the continuation in the constructor that mirrors
`IsVisible` to LocalStorage keeps using `_isVisibleStored.WhenRead`.)

`RightPanel.cs` — add beside `_isSearchMode`:
```csharp
    private readonly MutableState<RightPanelMode> _mode;
    public IState<RightPanelMode> Mode => _mode;
```
in the constructor, after `_isSearchMode = ...`:
```csharp
        var modeInitState = _storedState.WhenRead.IsCompletedSuccessfully ? _storedState.Mode : RightPanelMode.Chat;
        _mode = stateFactory.NewMutable(modeInitState, StateCategories.Get(GetType(), nameof(Mode)));
        _ = _storedState.WhenRead
            .ContinueWith(_1 => SetMode(_storedState.Mode), TaskScheduler.Default);
```
and the two methods after `Toggle()`:
```csharp
    public void Open(RightPanelMode mode)
    {
        SetMode(mode);
        SetIsVisible(true);
    }

    public void SetMode(RightPanelMode value)
        => _ = Dispatcher.InvokeSafeAsync(() => {
            if (_mode.Value == value)
                return;

            _mode.Value = value;
            _storedState.Mode = value;
        }, Log);
```

- [ ] **Step 4: The switch, the swap and the menu entry**

`RightPanelModeSwitch.razor`:
```razor
@namespace ActualChat.UI.Blazor.App.Components
@inherits ComputedStateComponent<AppUIHub, RightPanelMode>
@{
    var mode = State.Value;
}

<div class="right-panel-mode-switch" role="tablist">
    <Button Class="@ButtonClass(mode, RightPanelMode.Chat)" Click="@(_ => Select(RightPanelMode.Chat))">
        @L.Coach_ModeChat
    </Button>
    <Button Class="@ButtonClass(mode, RightPanelMode.Coach)" Click="@(_ => Select(RightPanelMode.Coach))">
        @L.Coach_ModeCoach
    </Button>
</div>

@code {
    protected override ComputedState<RightPanelMode>.Options GetStateOptions()
        => new() {
            InitialValue = PanelsUI.Right.Mode.Value,
            UpdateDelayer = FixedDelayer.NextTick,
            Category = GetStateCategory(GetType()),
        };

    protected override Task<RightPanelMode> ComputeState(CancellationToken cancellationToken)
        => PanelsUI.Right.Mode.Use(cancellationToken);

    private void Select(RightPanelMode mode)
        => PanelsUI.Right.SetMode(mode);

    private static string ButtonClass(RightPanelMode current, RightPanelMode mode)
        => current == mode ? "btn-mode on" : "btn-mode";
}
```

`right-panel-mode-switch.css`:
```css
.right-panel-mode-switch {
    @apply flex-x items-center justify-center gap-x-1;
    @apply mx-4 my-2 p-1;
    @apply rounded-full bg-02;
}
.right-panel-mode-switch .btn.btn-mode {
    @apply flex-1 min-h-8;
    @apply rounded-full border-0 bg-transparent no-sheen;
    @apply text-03 text-sm;
}
.right-panel-mode-switch .btn.btn-mode.on {
    @apply bg-01 text-01 shadow-sm;
}
```
Add `@import './Components/RightPanel/right-panel-mode-switch.css';` to `styles.css` next to the other
`RightPanel` import (grep `chat-side-panel.css` there).

`RightPanelContent.razor` — replace the file:
```razor
@namespace ActualChat.UI.Blazor.App.Components
@inherits ComputedStateComponent<AppUIHub, RightPanelContent.Model>
@* The side panel's swap area: whatever occupies the panel goes through it, so a change of what is
   shown hands over instead of cutting. Chat mode is keyed by chat, Coach mode by nothing but itself,
   because that is what changes underneath each. *@
@{
    var m = State.Value;
    var isCoach = m is { IsCoachEnabled: true, Mode: RightPanelMode.Coach };
    var layerKey = isCoach ? "coach" : ChatContext?.Chat.Id.Value;
}

@if (m.IsCoachEnabled) {
    <RightPanelModeSwitch/>
}
<ContentSwap
    Name="SidePanel"
    LayerKey="@layerKey"
    Class="side-panel-swap"
    Effect="@ContentSwapEffect.WipeRight">
    <ChildContent>
        @if (isCoach) {
            <CoachPanel/>
        } else {
            <CascadingValuePin TValue="ChatContext" RetentionKeySelector="@(c => c?.Chat.Id)">
                <ChatSidePanel/>
            </CascadingValuePin>
        }
    </ChildContent>
</ContentSwap>

@code {
    [CascadingParameter] public ChatContext? ChatContext { get; set; }

    protected override ComputedState<Model>.Options GetStateOptions()
        => new() {
            InitialValue = Model.None,
            UpdateDelayer = FixedDelayer.NextTick,
            Category = GetStateCategory(GetType()),
        };

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        var isCoachEnabled = await Hub.CoachUI.IsEnabled(cancellationToken).ConfigureAwait(false);
        var mode = await PanelsUI.Right.Mode.Use(cancellationToken).ConfigureAwait(false);
        return new Model(isCoachEnabled, mode);
    }

    public sealed record Model(bool IsCoachEnabled, RightPanelMode Mode) {
        public static readonly Model None = new(false, RightPanelMode.Chat);
    }
}
```
Until Task 6 exists, put `<div class="coach-panel"></div>` where `<CoachPanel/>` is written and
replace it in Task 6. `ContentSwap.LayerKey` is typed `object?`/`string?` — check `ContentSwap.razor`
and match (`ChatContext?.Chat.Id` was passed before; `.Value` gives the string form).

`CoachMenuEntry.razor` (in `Components/ChatPropertiesMenu/`):
```razor
@namespace ActualChat.UI.Blazor.App.Components
@inherits ComputedStateComponent<AppUIHub, bool>

@if (State.Value) {
    <MenuEntry
        Icon="icon-microphone"
        Text="@L.Coach_OpenCoach"
        Click="@OnClick">
    </MenuEntry>
}

@code {
    protected override ComputedState<bool>.Options GetStateOptions()
        => new() { InitialValue = false, Category = GetStateCategory(GetType()) };

    protected override Task<bool> ComputeState(CancellationToken cancellationToken)
        => Hub.CoachUI.IsEnabled(cancellationToken);

    private void OnClick()
        => PanelsUI.Right.Open(RightPanelMode.Coach);
}
```
Pick an icon that exists in the icon font (grep `icon-microphone` / `icon-mic` in the razor files; use
what `RecordingSubHeader` or `ChatAudioControls` uses).

`ChatPropertiesMenu.razor` — after the "Open right panel" `MenuEntry`, add `<CoachMenuEntry/>`.

- [ ] **Step 5: Run the test and the existing right-panel tests**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter "FullyQualifiedName~CoachUITest" -v q`
Expected: PASS (4 tests).
Run: `npm run build:Verify`. Expected: clean.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/UI.Blazor/Services/PanelsUI src/dotnet/UI.Blazor.App/Components/RightPanel \
  src/dotnet/UI.Blazor.App/Components/ChatPropertiesMenu src/dotnet/UI.Blazor.App/styles.css \
  tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs
git commit -m "feat(coach-ui): add the Chat | Coach mode to the right panel"
```

---

### Task 6: `CoachPanel` — score card, settings, window selector, metric rows

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/Coach/CoachLabels.cs`, `CoachPanel.razor`, `CoachScoreCard.razor`, `CoachSettingsTile.razor`, `CoachMetricRow.razor`, `coach.css`
- Modify: `src/dotnet/UI.Blazor.App/Components/RightPanel/RightPanelContent.razor` (replace the placeholder with `<CoachPanel/>`), `src/dotnet/UI.Blazor.App/styles.css` (import `coach.css`)
- Test: `tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs` (add one test)

**Interfaces:**
- Consumes: `ICoach.GetOwnSummary`, `UserSettingsUI.UserCoachSettings()`, `PanelsUI.Right.SetIsVisible(false)`, `TabPanel`/`TabDef`, `TileItem`/`Toggle`/`Tile`, `InputSelect` + `Form` (as `PttReplyWindowSettings.razor`), the `Coach_*` strings.
- Produces:
  ```csharp
  // CoachLabels: instance helper constructed with IStringLocalizer
  public sealed class CoachLabels(IStringLocalizer l)
  string MetricTitle(CoachMetricKind kind); string Band(CoachMetricKind kind, CoachBand band);
  string Value(CoachMetric metric); string Window(CoachWindow window)
  // CoachPanel: [Parameter] none; internal state: window, selected chip word (Task 9), tab
  // CoachMetricRow: [Parameter] CoachMetric Metric; [Parameter] EventCallback<string> WordClick
  ```

- [ ] **Step 1: Write the failing test**

Add to `CoachUITest.cs`:

```csharp
    [Fact(Timeout = 60_000)]
    public async Task CoachPanelShouldShowNoDataThenTheDaysNumbers()
    {
        // arrange
        var appHost = await NewCoachHost("coach-ui-panel");
        await using var _1 = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        tester.JSInterop.Mode = JSRuntimeMode.Loose;
        var (chatId, _) = await tester.CreateChat(true);
        var coach = tester.ScopedAppServices.AppUIHub().Coach;

        // act
        var cut = tester.Render<CoachPanel>();

        // assert
        cut.WaitForAssertion(() => cut.FindAll(".coach-metric-row").Count.Should().Be(13));
        cut.Find(".coach-score-card").TextContent.Should().Contain("Coach_ScoreNeedsWords",
            "the test localizer echoes keys; no score below the word floor");

        // act - one voice message lands in Today
        var entry = await PostVoice(tester, chatId, Text);
        await TestWait.When(async ct => (await coach.GetOwnSummary(tester.Session, CoachWindow.Today, ct))
            .Entries.Should().Be(1), TimeSpan.FromSeconds(30));

        // assert
        cut.WaitForAssertion(() => cut.Find(".coach-metric-row[data-metric=Pace]").TextContent
            .Should().Contain("Coach_Wpm_Format"), TimeSpan.FromSeconds(10));
        cut.Find(".coach-metric-row[data-metric=Fillers] .coach-chip").TextContent.Should().Contain("um");

        // act - the Coaching toggle writes the setting
        await cut.InvokeAsync(() => cut.Find(".coach-settings-coaching input").Change(true));

        // assert
        await TestWait.When(async ct => (await tester.ScopedAppServices.AppUIHub().UserSettingsUI
            .UserCoachSettings().Get(ct)).IsCoachingEnabled.Should().BeTrue());
    }
```

The `BlazorTester` string localizer: check `tests/Testing.Host/BlazorTester.cs` for which
`IStringLocalizer` it registers. If it is the real `AppStringLocalizer`, assert on the English texts
(`"Speak at least"`, `"wpm"`) instead of key names. The `Toggle` component renders `<input type="checkbox">`
inside `label.toggle`; if `Change(true)` does not fire `IsCheckedChanged`, click the label instead.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter "FullyQualifiedName~CoachPanelShould" -v q`
Expected: FAIL to compile — `CoachPanel` does not exist.

- [ ] **Step 3: The labels helper**

`CoachLabels.cs`:

```csharp
using ActualChat.Users;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.App.Components;

/// <summary>
/// Every piece of coach text that depends on a metric kind, a band or a window; an instance so it
/// can reach the localizer, as the style guide asks of enum-to-text mappings.
/// </summary>
public sealed class CoachLabels(IStringLocalizer l)
{
    public string Window(CoachWindow window)
        => window switch {
            CoachWindow.Today => l.Coach_WindowToday,
            CoachWindow.Week => l.Coach_WindowWeek,
            CoachWindow.Month => l.Coach_WindowMonth,
            _ => l.Coach_WindowAllTime,
        };

    public string MetricTitle(CoachMetricKind kind)
        => kind switch {
            CoachMetricKind.Pace => l.Coach_MetricPace,
            CoachMetricKind.Pauses => l.Coach_MetricPauses,
            CoachMetricKind.Fillers => l.Coach_MetricFillers,
            CoachMetricKind.WeakWords => l.Coach_MetricWeakWords,
            CoachMetricKind.Repetition => l.Coach_MetricRepetition,
            CoachMetricKind.Profanity => l.Coach_MetricProfanity,
            CoachMetricKind.Questions => l.Coach_MetricQuestions,
            CoachMetricKind.SentenceLength => l.Coach_MetricSentenceLength,
            CoachMetricKind.Vocabulary => l.Coach_MetricVocabulary,
            CoachMetricKind.TurnTaking => l.Coach_MetricTurnTaking,
            CoachMetricKind.Patience => l.Coach_MetricPatience,
            CoachMetricKind.Interruptions => l.Coach_MetricInterruptions,
            _ => l.Coach_MetricMonologue,
        };

    // High doubles as fast/long and Low as slow/short, so the label depends on the kind too
    public string Band(CoachMetricKind kind, CoachBand band)
        => (kind, band) switch {
            (_, CoachBand.None) => "",
            (CoachMetricKind.Pace, CoachBand.High) => l.Coach_BandFast,
            (CoachMetricKind.Pace, CoachBand.Low) => l.Coach_BandSlow,
            (CoachMetricKind.SentenceLength, CoachBand.High) => l.Coach_BandLong,
            (CoachMetricKind.SentenceLength, CoachBand.Low) => l.Coach_BandShort,
            (CoachMetricKind.Monologue, CoachBand.High) => l.Coach_BandLong,
            (CoachMetricKind.TurnTaking or CoachMetricKind.Patience, CoachBand.Good) => l.Coach_BandBalanced,
            (_, CoachBand.Good) => l.Coach_BandGood,
            (_, CoachBand.Medium) => l.Coach_BandMedium,
            (_, CoachBand.High) => l.Coach_BandHigh,
            _ => l.Coach_BandLow,
        };

    public string Value(CoachMetric metric)
    {
        if (metric.Value is not { } value)
            return l.Coach_NoData;

        return metric.Kind switch {
            CoachMetricKind.Pace => l.Coach_Wpm_Format(Round(value)),
            CoachMetricKind.Pauses => l.Coach_PerMinute_Format(value.ToString("F1", null)),
            CoachMetricKind.Fillers or CoachMetricKind.WeakWords or CoachMetricKind.Repetition
                or CoachMetricKind.Profanity => Counted(value, metric.Rate),
            CoachMetricKind.Questions or CoachMetricKind.Interruptions => Round(value).ToString(),
            CoachMetricKind.SentenceLength => l.Coach_WordsPerSentence_Format(value.ToString("F1", null)),
            CoachMetricKind.Vocabulary => l.Coach_Percent_Format(Round(value * 100)),
            CoachMetricKind.TurnTaking => l.Coach_PercentOfTalkTime_Format(Round(value * 100)),
            CoachMetricKind.Patience or CoachMetricKind.Monologue => l.Coach_Seconds_Format(value.ToString("F1", null)),
            _ => Round(value).ToString(),
        };
    }

    public string Rate(CoachMetric metric)
        => metric.Rate is { } rate ? l.Coach_PercentOfSpeech_Format(Round(rate * 100)) : "";

    private string Counted(double count, double? rate)
        => rate is { } r
            ? $"{Round(count)} · {l.Coach_PercentOfSpeech_Format(Round(r * 100))}"
            : Round(count).ToString();

    private static int Round(double value)
        => (int)Math.Round(value);
}
```

- [ ] **Step 4: The components**

`CoachScoreCard.razor`:
```razor
@namespace ActualChat.UI.Blazor.App.Components
@inherits ComponentBase<AppUIHub>
@{
    var s = Summary;
}

<div class="coach-score-card">
    @if (s.Score is { } score) {
        <div class="c-score">@L.Coach_Score_Format(score)</div>
        @if (s.ScoreDelta is { } delta) {
            var deltaText = delta > 0 ? "+" + delta : delta.ToString();
            <div class="c-delta @(delta > 0 ? "up" : "down")">@L.Coach_ScoreDelta_Format(deltaText)</div>
        }
    } else {
        <div class="c-score c-no-score">@L.Coach_ScoreNeedsWords(MinScoreWords, MinScoreWords)</div>
    }
    <div class="c-coverage">
        @L.Coach_Words(s.Words, s.Words)
        @if (s.Entries > 0 && s.TaggedEntries < s.Entries) {
            <span> · @L.Coach_Analysed(s.Entries, s.TaggedEntries, s.Entries)</span>
        }
    </div>
</div>

@code {
    // The server's CoachScoringSettings.MinScoreWords; the client has no access to it, so the
    // copy shows the default and the server decides whether a score exists
    private const int MinScoreWords = 200;

    [Parameter, EditorRequired] public CoachSummary Summary { get; set; } = null!;
}
```

`CoachSettingsTile.razor`:
```razor
@namespace ActualChat.UI.Blazor.App.Components
@using ActualChat.Users
@inherits ComputedStateComponent<AppUIHub, UserCoachSettings>
@{
    var s = State.Value;
    _formModel.Minutes = (int)s.TipInterval.TotalMinutes;
}

<Tile Class="coach-settings">
    <TileItem Class="coach-settings-coaching" Click="@OnToggleCoaching">
        <Icon><i class="icon-microphone text-2xl"></i></Icon>
        <Content>@L.Coach_Coaching</Content>
        <Caption>@L.Coach_CoachingCaption</Caption>
        <Right>
            <Toggle IsChecked="@s.IsCoachingEnabled" IsCheckedChanged="@(_ => OnToggleCoaching())"/>
        </Right>
    </TileItem>
    <TileItem Class="coach-settings-tips" Click="@OnToggleTips">
        <Content>@L.Coach_LiveTips</Content>
        <Right>
            <Toggle IsChecked="@s.AreLiveTipsEnabled" IsDisabled="@(!s.IsCoachingEnabled)"
                    IsCheckedChanged="@(_ => OnToggleTips())"/>
        </Right>
    </TileItem>
    <Form Model="@_formModel">
        <FormBlock>
            <FormSection
                For="() => _formModel.Minutes"
                InputId="@_formModel.MinutesFormId"
                IsLabelInsideInput="true"
                Label="@L.Coach_TipInterval"
                HideValidationMessage="true">
                <InputSelect
                    Value="_formModel.Minutes"
                    ValueExpression="@(() => _formModel.Minutes)"
                    ValueChanged="@((int minutes) => OnMinutesChanged(minutes))">
                    @foreach (var item in Intervals) {
                        <option value="@item">@L.Coach_TipIntervalMinutes(item, item)</option>
                    }
                </InputSelect>
            </FormSection>
        </FormBlock>
    </Form>
</Tile>

@code {
    private static readonly int[] Intervals = [1, 5, 15, 30];
    private FormModel _formModel = null!;

    protected override void OnInitialized()
        => _formModel = new FormModel(ComponentIdGenerator);

    protected override ComputedState<UserCoachSettings>.Options GetStateOptions()
        => new() { InitialValue = new UserCoachSettings(), Category = GetStateCategory(GetType()) };

    protected override Task<UserCoachSettings> ComputeState(CancellationToken cancellationToken)
        => UserSettingsUI.UserCoachSettings().Get(cancellationToken);

    private Task OnToggleCoaching()
        => UserSettingsUI.UserCoachSettings().Update(x => x with { IsCoachingEnabled = !x.IsCoachingEnabled });

    private Task OnToggleTips()
        => UserSettingsUI.UserCoachSettings().Update(x => x with { AreLiveTipsEnabled = !x.AreLiveTipsEnabled });

    private Task OnMinutesChanged(int minutes) {
        _formModel.Minutes = minutes;
        return UserSettingsUI.UserCoachSettings()
            .Update(x => x with { TipInterval = TimeSpan.FromMinutes(minutes) }, CancellationToken.None);
    }

    public sealed class FormModel(ComponentIdGenerator componentIdGenerator) {
        public int Minutes { get; set; }
        public string MinutesFormId { get; } = componentIdGenerator.Next("coach-tip-interval");
    }
}
```
(`TileItem` renders `Caption` under `Content`; if `Toggle` has no `IsDisabled` parameter, drop it and
early-return in `OnToggleTips` when coaching is off.)

`CoachMetricRow.razor`:
```razor
@namespace ActualChat.UI.Blazor.App.Components
@using ActualChat.Users
@inherits ComponentBase<AppUIHub>
@{
    var m = Metric;
    var labels = Labels;
    var band = labels.Band(m.Kind, m.Band);
    var hasChips = m.Kind is CoachMetricKind.Fillers or CoachMetricKind.WeakWords && m.Chips.Count > 0;
}

<div class="coach-metric-row" data-metric="@m.Kind">
    <div class="c-head">
        <span class="c-title">@labels.MetricTitle(m.Kind)</span>
        @if (!band.IsNullOrEmpty()) {
            <span class="c-band band-@m.Band.ToString().ToLower()">@band</span>
        }
    </div>
    <div class="c-value @(m.Value is null ? "c-no-data" : "")">@labels.Value(m)</div>
    @if (hasChips) {
        <div class="c-chips">
            @foreach (var chip in m.Chips) {
                <button type="button" class="coach-chip" @onclick="@(() => WordClick.InvokeAsync(chip.Word))">
                    <span class="c-word">@chip.Word</span><span class="c-count">@chip.Count</span>
                </button>
            }
        </div>
    }
</div>

@code {
    private CoachLabels Labels => field ??= new CoachLabels(L);

    [Parameter, EditorRequired] public CoachMetric Metric { get; set; } = null!;
    [Parameter] public EventCallback<string> WordClick { get; set; }
}
```
`band-@m.Band.ToString().ToLower()` is a CSS class, not prose — allowed. (Use `.ToLower()`, not the
invariant variant, per the style guide.)

`CoachPanel.razor`:
```razor
@namespace ActualChat.UI.Blazor.App.Components
@using ActualChat.Users
@inherits ComputedStateComponent<AppUIHub, CoachPanel.Model>
@{
    var m = State.Value;
    var labels = Labels;
    var tabs = new List<TabDef> {
        new("metrics", L.Coach_TabMetrics) { Content = MetricsContent },
        new("trends", L.Coach_TabTrends) { Content = TrendsContent },
    };
}

<div class="coach-panel">
    <ErrorBarrier Name="CoachPanel" Kind="@ErrorBarrierKind.Full">
        <div class="c-header">
            <span class="c-title">@L.Coach_Title</span>
            <ButtonRound Click="@OnClose" Class="btn-sm close-btn"><i class="icon-close text-2xl"></i></ButtonRound>
        </div>
        <div class="c-body">
            <CoachScoreCard Summary="@m.Summary"/>
            <CoachSettingsTile/>
            <div class="coach-window-selector" role="tablist">
                @foreach (var window in Windows) {
                    <Button Class="@(window == m.Window ? "btn-window on" : "btn-window")"
                            Click="@(_ => OnWindowClick(window))">@labels.Window(window)</Button>
                }
            </div>
            @if (m.Word is { } word) {
                <CoachOccurrences Word="@word" Window="@m.Window" Back="@OnOccurrencesBack"/>
            } else {
                <TabPanel Tabs="@tabs" TabsClass="left-panel-tabs wide-left-panel-tabs" SwapKind="@TabContentSwap.Swipe"/>
            }
        </div>
        <div class="safe-area-bottom safe-area-bottom-overlay"></div>
    </ErrorBarrier>
</div>

@code {
    private static readonly CoachWindow[] Windows = [CoachWindow.Today, CoachWindow.Week, CoachWindow.Month, CoachWindow.AllTime];

    private readonly MutableState<CoachWindow> _window;
    private readonly MutableState<string?> _word;

    private CoachLabels Labels => field ??= new CoachLabels(L);
    private ICoach Coach => Hub.Coach;

    private RenderFragment MetricsContent => @<div class="coach-metrics">
        @foreach (var metric in State.Value.Summary.Metrics) {
            <CoachMetricRow Metric="@metric" WordClick="@OnWordClick"/>
        }
    </div>;

    private RenderFragment TrendsContent => @<CoachTrends Summary="@State.Value.Summary" Window="@State.Value.Window"/>;

    public CoachPanel() {
        _window = StateFactory.NewMutable(CoachWindow.Today);
        _word = StateFactory.NewMutable((string?)null);
    }

    protected override ComputedState<Model>.Options GetStateOptions()
        => new() {
            InitialValue = Model.None,
            UpdateDelayer = FixedDelayer.NextTick,
            Category = GetStateCategory(GetType()),
        };

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        var window = await _window.Use(cancellationToken).ConfigureAwait(false);
        var word = await _word.Use(cancellationToken).ConfigureAwait(false);
        var summary = await Coach.GetOwnSummary(Session, window, cancellationToken).ConfigureAwait(false);
        return new Model(window, summary, word);
    }

    private void OnWindowClick(CoachWindow window)
        => _window.Value = window;

    private void OnWordClick(string word)
        => _word.Value = word;

    private void OnOccurrencesBack()
        => _word.Value = null;

    private void OnClose()
        => PanelsUI.Right.SetIsVisible(false);

    public sealed record Model(CoachWindow Window, CoachSummary Summary, string? Word) {
        public static readonly Model None = new(CoachWindow.Today, CoachSummary.None, null);
    }
}
```
`StateFactory` may not be usable in the constructor of a component (the hub is injected later); if
so, create the two states in `OnInitialized()` and mark the fields `null!`. Until Tasks 8 and 9
exist, render `<div class="coach-trends"/>` and `<div class="coach-occurrences"/>` in place of
`<CoachTrends/>` / `<CoachOccurrences/>` and replace them in those tasks.

`coach.css` (imported from `styles.css`):
```css
.coach-panel {
    @apply flex-y h-full;
    @apply bg-01;
}
.coach-panel > .c-header {
    @apply flex-x items-center justify-between;
    @apply h-14 px-4;
    @apply text-headline-1;
}
.coach-panel > .c-body {
    @apply flex-1 flex-y gap-y-3;
    @apply px-4 pb-4;
    @apply overflow-y-auto;
}
.coach-score-card {
    @apply flex-y gap-y-1;
    @apply p-4 rounded-lg bg-02;
}
.coach-score-card .c-score { @apply text-title-1; }
.coach-score-card .c-no-score { @apply text-03 text-sm; }
.coach-score-card .c-delta { @apply text-sm; }
.coach-score-card .c-delta.up { @apply text-success; }
.coach-score-card .c-delta.down { @apply text-danger; }
.coach-score-card .c-coverage { @apply text-03 text-xs; }
.coach-window-selector {
    @apply flex-x gap-x-1 p-1 rounded-full bg-02;
}
.coach-window-selector .btn.btn-window {
    @apply flex-1 min-h-8 rounded-full border-0 bg-transparent no-sheen text-03 text-sm;
}
.coach-window-selector .btn.btn-window.on { @apply bg-01 text-01 shadow-sm; }
.coach-metrics { @apply flex-y gap-y-2; }
.coach-metric-row {
    @apply flex-y gap-y-1;
    @apply p-3 rounded-lg bg-02;
}
.coach-metric-row .c-head { @apply flex-x items-center justify-between; }
.coach-metric-row .c-title { @apply text-headline-1; }
.coach-metric-row .c-band { @apply text-xs px-2 py-0.5 rounded-full bg-01; }
.coach-metric-row .c-band.band-good { @apply text-success; }
.coach-metric-row .c-band.band-medium { @apply text-warning; }
.coach-metric-row .c-band.band-high,
.coach-metric-row .c-band.band-low { @apply text-danger; }
.coach-metric-row .c-value { @apply text-title-1; }
.coach-metric-row .c-no-data { @apply text-03 text-sm; }
.coach-metric-row .c-chips { @apply flex-x flex-wrap gap-1; }
.coach-chip {
    @apply flex-x items-center gap-x-1;
    @apply px-2 py-0.5 rounded-full bg-01 text-sm;
}
.coach-chip .c-count { @apply text-03; }
```
Use the text/color utility classes the project defines (`text-title-1`, `text-headline-1`, `text-03`,
`bg-02`, `text-success` and so on — grep `chat-side-panel.css` and `tailwind.config.*` for the real
names and swap in what exists).

Replace the placeholder in `RightPanelContent.razor` with `<CoachPanel/>`.

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter "FullyQualifiedName~CoachUITest" -v q`
Expected: PASS (5 tests). Run `npm run build:Verify`. Expected: clean.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Components/Coach src/dotnet/UI.Blazor.App/Components/RightPanel/RightPanelContent.razor \
  src/dotnet/UI.Blazor.App/styles.css tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs
git commit -m "feat(coach-ui): add the Coach panel with score, settings and metric rows"
```

---

### Task 7: Shared charts — `DonutChart` and `BarChart`

**Files:**
- Create: `src/dotnet/UI.Blazor/Components/Charts/ChartItem.cs`, `DonutChart.razor`, `BarChart.razor`, `charts.css`
- Modify: the UI.Blazor CSS entry that imports component styles (grep `round-progress.css` to find it) — add `charts.css`
- Test: `tests/Chat.UI.Blazor.UnitTests/Charts/DonutChartTest.cs`, `BarChartTest.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record ChartItem(string Label, double Value, string Class = "");
  // DonutChart: [Parameter, EditorRequired] IReadOnlyList<ChartItem> Items; [Parameter] string Class = "";
  //   [Parameter] RenderFragment? Center — renders one <circle class="c-slice {Class}"> per item with a
  //   positive value, dasharray "{pct} {100-pct}", plus a legend <li> per item.
  // BarChart: [Parameter, EditorRequired] IReadOnlyList<ChartItem> Items; [Parameter] string Class = "";
  //   [Parameter] string ValueFormat = "F0" — one .c-bar per item, height = value / max * 100%.
  ```
  No coaching knowledge; the coach passes labels already localized.

- [ ] **Step 1: Write the failing tests**

`tests/Chat.UI.Blazor.UnitTests/Charts/DonutChartTest.cs`:
```csharp
using ActualChat.UI.Blazor.Components;
using Bunit;

namespace ActualChat.Chat.UI.Blazor.UnitTests.Charts;

public class DonutChartTest
{
    [Fact]
    public void DonutChartShouldRenderOneSlicePerPositiveItemWithSharesSummingToOneHundred()
    {
        // arrange
        using var context = TestBunitContext.New();
        var items = new[] { new ChartItem("a", 30, "x"), new ChartItem("b", 10, "y"), new ChartItem("c", 0, "z") };

        // act
        var cut = context.Render<DonutChart>(p => p.Add(x => x.Items, items));

        // assert
        var slices = cut.FindAll("circle.c-slice");
        slices.Count.Should().Be(2, "a zero item has no slice");
        slices[0].GetAttribute("stroke-dasharray").Should().Be("75.00 25.00");
        slices[1].GetAttribute("stroke-dasharray").Should().Be("25.00 75.00");
        slices[1].GetAttribute("stroke-dashoffset").Should().Be("-50.00", "the second slice starts where the first ends");
        cut.FindAll("li").Count.Should().Be(3, "the legend lists every item");
    }

    [Fact]
    public void DonutChartShouldRenderNoSlicesForAllZeroItems()
    {
        // arrange
        using var context = TestBunitContext.New();

        // act
        var cut = context.Render<DonutChart>(p => p.Add(x => x.Items, [new ChartItem("a", 0)]));

        // assert
        cut.FindAll("circle.c-slice").Should().BeEmpty();
    }
}
```

`tests/Chat.UI.Blazor.UnitTests/Charts/BarChartTest.cs`:
```csharp
using ActualChat.UI.Blazor.Components;
using Bunit;

namespace ActualChat.Chat.UI.Blazor.UnitTests.Charts;

public class BarChartTest
{
    [Fact]
    public void BarChartShouldScaleBarsToTheTallestOne()
    {
        // arrange
        using var context = TestBunitContext.New();
        var items = new[] { new ChartItem("Mon", 120), new ChartItem("Tue", 60), new ChartItem("Wed", 0) };

        // act
        var cut = context.Render<BarChart>(p => p.Add(x => x.Items, items));

        // assert
        var bars = cut.FindAll(".c-bar");
        bars.Count.Should().Be(3);
        bars[0].GetAttribute("style").Should().Contain("height: 100%");
        bars[1].GetAttribute("style").Should().Contain("height: 50%");
        bars[2].GetAttribute("style").Should().Contain("height: 0%");
        bars[0].GetAttribute("title").Should().Be("120");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~ChartTest" -v q`
Expected: FAIL to compile.

- [ ] **Step 3: Write the components**

`ChartItem.cs`:
```csharp
namespace ActualChat.UI.Blazor.Components;

public sealed record ChartItem(string Label, double Value, string Class = "");
```

`DonutChart.razor`:
```razor
@namespace ActualChat.UI.Blazor.Components
@{
    var total = Items.Sum(i => Math.Max(i.Value, 0));
    var offset = 0d;
}

<div class="donut-chart @Class">
    <div class="c-ring">
        <svg viewBox="0 0 42 42">
            <circle class="c-track" cx="21" cy="21" r="@Radius"/>
            @if (total > 0) {
                foreach (var item in Items) {
                    if (item.Value <= 0)
                        continue;

                    var share = item.Value / total * 100;
                    <circle class="c-slice @item.Class" cx="21" cy="21" r="@Radius"
                            stroke-dasharray="@Format(share) @Format(100 - share)"
                            stroke-dashoffset="@Format(-offset)"/>
                    offset += share;
                }
            }
        </svg>
        <div class="c-center">@Center</div>
    </div>
    <ul class="c-legend">
        @foreach (var item in Items) {
            <li class="@item.Class"><span class="c-swatch"></span>@item.Label</li>
        }
    </ul>
</div>

@code {
    // 2πr = 100, so dash lengths are percentages
    private const string Radius = "15.9155";

    [Parameter, EditorRequired] public IReadOnlyList<ChartItem> Items { get; set; } = [];
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public RenderFragment? Center { get; set; }

    private static string Format(double value)
        => value.ToString("F2", null);
}
```
(SVG `stroke-dashoffset` runs clockwise-negative, so the running offset is negated; the `.c-slice`
CSS rotates the ring by -90° to start at 12 o'clock.)

`BarChart.razor`:
```razor
@namespace ActualChat.UI.Blazor.Components
@{
    var max = Items.Count == 0 ? 0 : Items.Max(i => i.Value);
}

<div class="bar-chart @Class">
    @foreach (var item in Items) {
        var height = max > 0 ? item.Value / max * 100 : 0;
        <div class="c-column">
            <div class="c-bar-track">
                <div class="c-bar @item.Class" style="height: @(height.ToString("F0", null))%"
                     title="@item.Value.ToString(ValueFormat, null)"></div>
            </div>
            <div class="c-label">@item.Label</div>
        </div>
    }
</div>

@code {
    [Parameter, EditorRequired] public IReadOnlyList<ChartItem> Items { get; set; } = [];
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public string ValueFormat { get; set; } = "F0";
}
```

`charts.css`:
```css
.donut-chart { @apply flex-x items-center gap-x-4; }
.donut-chart .c-ring { @apply relative w-28 h-28; }
.donut-chart .c-ring > svg { @apply absolute inset-0 w-full h-full; transform: rotate(-90deg); }
.donut-chart .c-track { fill: none; stroke: var(--background-02); stroke-width: 6; }
.donut-chart .c-slice { fill: none; stroke: currentColor; stroke-width: 6; }
.donut-chart .c-center { @apply absolute inset-0 flex items-center justify-center text-center text-sm; }
.donut-chart .c-legend { @apply flex-y gap-y-1 text-sm; }
.donut-chart .c-legend li { @apply flex-x items-center gap-x-2; }
.donut-chart .c-swatch { @apply w-3 h-3 rounded-sm; background: currentColor; }
.bar-chart { @apply flex-x items-end gap-x-1 h-32; }
.bar-chart .c-column { @apply flex-1 flex-y items-center gap-y-1 h-full; }
.bar-chart .c-bar-track { @apply flex-1 w-full flex-y justify-end; }
.bar-chart .c-bar { @apply w-full rounded-t-sm; background: currentColor; }
.bar-chart .c-label { @apply text-xs text-03; }
```
Import it where `round-progress.css` is imported. Colors come from the `Class` the caller passes
(`text-danger`, `text-warning`, ...), since the ring and bars use `currentColor`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~ChartTest" -v q`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor/Components/Charts src/dotnet/UI.Blazor tests/Chat.UI.Blazor.UnitTests/Charts
git commit -m "feat(ui): add inline donut and bar chart components"
```

---

### Task 8: Trends

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/Coach/CoachTrends.razor`
- Modify: `CoachPanel.razor` (replace the placeholder with `<CoachTrends .../>`), `coach.css`
- Test: `tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs` (add one test)

**Interfaces:**
- Consumes: `ICoach.ListOwnDays(Session, Range<Moment>)`, `DonutChart`, `BarChart`, `ChartItem` (Task 7), `CoachMetricRow` (Task 6), `CoachLabels`, `L.Coach_Trends*`, `DateTimeConverter`/`DateFormatter`.
- Produces: `CoachTrends` with `[Parameter] CoachSummary Summary`, `[Parameter] CoachWindow Window`.

- [ ] **Step 1: Write the failing test**

Add to `CoachUITest.cs`:
```csharp
    [Fact(Timeout = 60_000)]
    public async Task CoachTrendsShouldShowOneBarPerDayWithSpeech()
    {
        // arrange
        var appHost = await NewCoachHost("coach-ui-trends");
        await using var _1 = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        tester.JSInterop.Mode = JSRuntimeMode.Loose;
        var (chatId, _) = await tester.CreateChat(true);
        var coach = tester.ScopedAppServices.AppUIHub().Coach;
        await PostVoice(tester, chatId, Text);
        var summary = await TestWait.When(async ct => {
            var s = await coach.GetOwnSummary(tester.Session, CoachWindow.Week, ct);
            s.Entries.Should().Be(1);
            return s;
        }, TimeSpan.FromSeconds(30));

        // act
        var cut = tester.Render<CoachTrends>(p => p
            .Add(x => x.Summary, summary)
            .Add(x => x.Window, CoachWindow.Week));

        // assert
        cut.WaitForAssertion(() => {
            cut.FindAll(".bar-chart .c-bar").Count.Should().Be(7, "a week has seven columns");
            cut.FindAll(".bar-chart .c-bar").Count(b => b.GetAttribute("style")!.Contains("height: 100%"))
                .Should().Be(1, "only today has speech");
            cut.FindAll(".donut-chart circle.c-slice").Should().NotBeEmpty();
        }, TimeSpan.FromSeconds(10));
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter "FullyQualifiedName~CoachTrendsShould" -v q`
Expected: FAIL to compile.

- [ ] **Step 3: Write the component**

`CoachTrends.razor`:
```razor
@namespace ActualChat.UI.Blazor.App.Components
@using ActualChat.Users
@inherits ComputedStateComponent<AppUIHub, CoachTrends.Model>
@{
    var m = State.Value;
    var s = Summary;
    var composition = Composition(s);
}

<div class="coach-trends">
    <div class="c-section">
        <div class="c-section-title">@L.Coach_TrendsComposition</div>
        @if (composition.Count > 0) {
            <DonutChart Items="@composition">
                <Center>@L.Coach_Words(s.Words, s.Words)</Center>
            </DonutChart>
        } else {
            <div class="c-no-data">@L.Coach_NoData</div>
        }
    </div>
    <div class="c-section">
        <div class="c-section-title">@L.Coach_TrendsPace</div>
        @if (m.Bars.Count > 0) {
            <BarChart Items="@m.Bars" Class="text-primary"/>
        } else {
            <div class="c-no-data">@L.Coach_TrendsNoDays</div>
        }
    </div>
    @foreach (var metric in s.Metrics) {
        if (metric.Kind is CoachMetricKind.TurnTaking or CoachMetricKind.Patience
            or CoachMetricKind.Interruptions or CoachMetricKind.Monologue) {
            <CoachMetricRow Metric="@metric"/>
        }
    }
</div>

@code {
    private const int MaxDays = 90;

    private ICoach Coach => Hub.Coach;

    [Parameter, EditorRequired] public CoachSummary Summary { get; set; } = null!;
    [Parameter] public CoachWindow Window { get; set; }

    protected override ComputedState<Model>.Options GetStateOptions()
        => new() { InitialValue = Model.None, Category = GetStateCategory(GetType()) };

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        var window = Window;
        var dayCount = window switch {
            CoachWindow.Today or CoachWindow.Week => 7,
            CoachWindow.Month => 30,
            _ => MaxDays,
        };
        var today = UsageDay.DayOf(Clocks.SystemClock.Now);
        var range = new Range<Moment>(today - TimeSpan.FromDays(dayCount - 1), today + TimeSpan.FromDays(1));
        var days = await Coach.ListOwnDays(Session, range, cancellationToken).ConfigureAwait(false);
        if (days.Count == 0)
            return Model.None;

        var byDay = days.ToDictionary(d => d.Day);
        var bars = new List<ChartItem>(dayCount);
        for (var i = 0; i < dayCount; i++) {
            var day = range.Start + TimeSpan.FromDays(i);
            var wpm = byDay.TryGetValue(day, out var d) && d.SpeechSeconds > 0 ? d.Words * 60 / d.SpeechSeconds : 0;
            bars.Add(new ChartItem(DayLabel(day, dayCount), wpm));
        }
        return new Model(bars);
    }

    private string DayLabel(Moment day, int dayCount) {
        var local = DateTimeConverter.ToLocalTime(day);
        return dayCount <= 7 ? local.ToString("ddd", DateFormatter) : local.ToString("d", DateFormatter);
    }

    private List<ChartItem> Composition(CoachSummary s) {
        var fillers = Count(s, CoachMetricKind.Fillers);
        var weak = Count(s, CoachMetricKind.WeakWords);
        var repetitions = Count(s, CoachMetricKind.Repetition);
        var other = Math.Max(s.Words - fillers - weak - repetitions, 0);
        if (s.Words == 0)
            return [];

        var labels = new CoachLabels(L);
        return [
            new ChartItem(labels.MetricTitle(CoachMetricKind.Fillers), fillers, "text-danger"),
            new ChartItem(labels.MetricTitle(CoachMetricKind.WeakWords), weak, "text-warning"),
            new ChartItem(labels.MetricTitle(CoachMetricKind.Repetition), repetitions, "text-03"),
            new ChartItem(L.Coach_TrendsOtherWords, other, "text-primary"),
        ];
    }

    private static int Count(CoachSummary s, CoachMetricKind kind)
        => (int)(s.Metrics.FirstOrDefault(m => m.Kind == kind)?.Value ?? 0);

    public sealed record Model(List<ChartItem> Bars) {
        public static readonly Model None = new([]);
    }
}
```
`UsageDay.DayOf(Moment)` is in `Api/Users/Usage` (plan 2 used it for `CoachRecord.Day`); if the
`DateFormatter` overload of `ToString` differs from `ChatEntryMessageView`'s `.ToString("t", DateFormatter)`,
copy that file's form. Weekday names must come from the localizer, not ICU: check
`DateFormatsLocalizerExt.NewFormatInfo(L)` and use the format info the hub's `DateFormatter` wraps.

Add to `coach.css`:
```css
.coach-trends { @apply flex-y gap-y-3; }
.coach-trends .c-section { @apply flex-y gap-y-2 p-3 rounded-lg bg-02; }
.coach-trends .c-section-title { @apply text-headline-1; }
.coach-trends .c-no-data { @apply text-03 text-sm; }
```
Replace the placeholder in `CoachPanel.razor` with the real `CoachTrends`.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter "FullyQualifiedName~CoachUITest" -v q`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Components/Coach tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs
git commit -m "feat(coach-ui): add the Trends view"
```

---

### Task 9: Jump-to-audio — the occurrences list

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/Coach/CoachOccurrences.razor`
- Modify: `CoachPanel.razor` (replace the placeholder), `coach.css`
- Test: `tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs` (add one test)

**Interfaces:**
- Consumes: `ICoach.ListOwnOccurrences(Session, word, window)`, `IChats.Get(Session, chatId)` for titles, `CoachUI.JumpTo(occurrence, ct)` (Task 3), `L.Coach_Occurrences_Format`, `L.Coach_OccurrencesEmpty`, `L.Common_Back`.
- Produces: `CoachOccurrences` with `[Parameter] string Word`, `[Parameter] CoachWindow Window`, `[Parameter] EventCallback Back`.

- [ ] **Step 1: Write the failing test**

Add to `CoachUITest.cs`:
```csharp
    [Fact(Timeout = 60_000)]
    public async Task ClickingAnOccurrenceShouldStartReplayAtTheWord()
    {
        // arrange
        var appHost = await NewCoachHost("coach-ui-jump");
        await using var _1 = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        tester.JSInterop.Mode = JSRuntimeMode.Loose;
        var (chatId, _) = await tester.CreateChat(true);
        var hub = tester.ScopedAppServices.AppUIHub();
        await OptIn(tester);
        var entry = await PostVoice(tester, chatId, Text);
        await TestWait.When(async ct => (await hub.Coach.ListOwnOccurrences(tester.Session, "um", CoachWindow.Today, ct))
            .Should().ContainSingle(), TimeSpan.FromSeconds(30));

        // act
        var cut = tester.Render<CoachOccurrences>(p => p
            .Add(x => x.Word, "um")
            .Add(x => x.Window, CoachWindow.Today));
        cut.WaitForAssertion(() => cut.FindAll(".coach-occurrence").Should().ContainSingle());
        await cut.InvokeAsync(() => cut.Find(".coach-occurrence").Click());

        // assert
        var replay = await TestWait.When(ct => Task.FromResult(hub.ChatAudioUI.ReplayState.Value.Should().NotBeNull().And.Subject));
        replay!.ChatId.Should().Be(chatId);
        var wordStart = entry.Audio!.TimeMap.TryMap(entry.Content.IndexOf("um"))!.Value;
        replay.StartAt.Should().BeCloseTo(entry.BeginsAt + TimeSpan.FromSeconds(wordStart - 0.25), TimeSpan.FromMilliseconds(50));
    }
```
(`ReplayState` is `record ReplayState(ChatId ChatId, Moment StartAt, TimeSpan RewindOffset, double Speed)` in
`UI.Blazor.App/Services/ReplayState.cs`; check the member names. `StartReplay` opens a `ConfirmModal`
only when a listening session is active, which this test does not start. If the fake entry's time
map yields no time for the offset, assert `replay.StartAt >= entry.BeginsAt - TimeSpan.FromSeconds(0.25)`.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter "FullyQualifiedName~ClickingAnOccurrenceShould" -v q`
Expected: FAIL to compile.

- [ ] **Step 3: Write the component**

`CoachOccurrences.razor`:
```razor
@namespace ActualChat.UI.Blazor.App.Components
@using ActualChat.Users
@inherits ComputedStateComponent<AppUIHub, CoachOccurrences.Model>
@{
    var m = State.Value;
}

<div class="coach-occurrences">
    <div class="c-head">
        <ButtonRound Click="@(() => Back.InvokeAsync())" Class="btn-sm"><i class="icon-arrow-left text-2xl"></i></ButtonRound>
        <span class="c-title">@L.Coach_Occurrences_Format(Word)</span>
    </div>
    @if (m.Items.Count == 0) {
        <div class="c-no-data">@L.Coach_OccurrencesEmpty</div>
    } else {
        @foreach (var item in m.Items) {
            <button type="button" class="coach-occurrence" @onclick="@(() => OnClick(item.Occurrence))">
                <span class="c-chat">@item.ChatTitle</span>
                <span class="c-time">@DateTimeConverter.ToLocalTime(item.Occurrence.At).ToString("g", DateFormatter)</span>
            </button>
        }
    }
</div>

@code {
    private ICoach Coach => Hub.Coach;
    private IChats Chats => Hub.Chats;

    [Parameter, EditorRequired] public string Word { get; set; } = "";
    [Parameter] public CoachWindow Window { get; set; }
    [Parameter] public EventCallback Back { get; set; }

    protected override ComputedState<Model>.Options GetStateOptions()
        => new() { InitialValue = Model.None, Category = GetStateCategory(GetType()) };

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        var word = Word;
        var window = Window;
        var occurrences = await Coach.ListOwnOccurrences(Session, word, window, cancellationToken).ConfigureAwait(false);
        var items = new List<Item>(occurrences.Count);
        foreach (var occurrence in occurrences) {
            var chat = await Chats.Get(Session, occurrence.ChatId, cancellationToken).ConfigureAwait(false);
            items.Add(new Item(occurrence, chat?.Title ?? ""));
        }
        return new Model(items);
    }

    private Task OnClick(CoachOccurrence occurrence)
        => Hub.CoachUI.JumpTo(occurrence, CancellationToken.None);

    public sealed record Item(CoachOccurrence Occurrence, string ChatTitle);

    public sealed record Model(List<Item> Items) {
        public static readonly Model None = new([]);
    }
}
```
A peer chat's `Title` may be empty; if `ChatUI.Get(chatId)` (returns `ChatInfo` with a display title
through its `Contact`) gives a better name, use it — check what `ChatListItem`/`ChatHeader` render as
the title and copy that expression.

Add to `coach.css`:
```css
.coach-occurrences { @apply flex-y gap-y-2; }
.coach-occurrences .c-head { @apply flex-x items-center gap-x-2; }
.coach-occurrences .c-title { @apply text-headline-1; }
.coach-occurrences .c-no-data { @apply text-03 text-sm; }
.coach-occurrence {
    @apply flex-x items-center justify-between;
    @apply w-full p-3 rounded-lg bg-02 text-left;
}
.coach-occurrence .c-time { @apply text-03 text-xs; }
```
Replace the placeholder in `CoachPanel.razor` with the real `CoachOccurrences`.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter "FullyQualifiedName~CoachUITest" -v q`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Components/Coach tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs
git commit -m "feat(coach-ui): jump to the audio of a chip's occurrences"
```

---

### Task 10: The tip bar

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/Coach/CoachTipBar.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/ChatView.razor` (mount into the SubFooter stack next to `NavigationSubFooter`), `coach.css`
- Test: `tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs` (add one test)

**Interfaces:**
- Consumes: `ICoach.GetPendingTip`, `Coach_DismissTip`, `CoachUI.IsEnabled`, `UserSettingsUI.UserCoachSettings()`, `Banner` (`IsVisible`, `Severity`, `ShowDismissButton`, `Dismiss`, `Icon`, `Body`, `Buttons`), `PanelsUI.Right.Open(RightPanelMode.Coach)`, `L.Coach_Tip*`.
- Produces: `CoachTipBar` with `[Parameter] ChatId ChatId`.

- [ ] **Step 1: Write the failing test**

Add to `CoachUITest.cs`:
```csharp
    [Fact(Timeout = 60_000)]
    public async Task TipBarShouldShowOnlyInTheTipsChatAndClearOnDismiss()
    {
        // arrange
        var appHost = await NewCoachHost("coach-ui-tip");
        await using var _1 = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        tester.JSInterop.Mode = JSRuntimeMode.Loose;
        var (chatId, _) = await tester.CreateChat(true);
        var (otherChatId, _) = await tester.CreateChat(true);
        await OptIn(tester);
        var hub = tester.ScopedAppServices.AppUIHub();
        var kvas = appHost.Services.GetRequiredService<IServerKvasBackend>().ForUser(account.Id, isOutermost: true);
        await kvas.UserCoachTip().Set(new UserCoachTip {
            Kind = CoachTipKind.Filler, ChatId = chatId, EntryLid = 1, Word = "um", Count = 10,
            ShownAt = appHost.Services.Clocks().SystemClock.Now,
        });
        await TestWait.When(async ct => (await hub.Coach.GetPendingTip(tester.Session, ct)).Should().NotBeNull());

        // act
        var inTipsChat = tester.Render<CoachTipBar>(p => p.Add(x => x.ChatId, chatId));
        var inOtherChat = tester.Render<CoachTipBar>(p => p.Add(x => x.ChatId, otherChatId));

        // assert
        inTipsChat.WaitForAssertion(() => inTipsChat.FindAll(".coach-tip-bar .banner").Should().ContainSingle());
        inTipsChat.Find(".coach-tip-bar .banner").TextContent.Should().Contain("um");
        await Task.Delay(300);
        inOtherChat.FindAll(".coach-tip-bar .banner").Should().BeEmpty("the tip belongs to another chat");

        // act - dismiss
        await inTipsChat.InvokeAsync(() => inTipsChat.Find(".coach-tip-bar .close-banner-abs").Click());

        // assert
        await TestWait.When(async ct => (await hub.Coach.GetPendingTip(tester.Session, ct)).Should().BeNull());
        inTipsChat.WaitForAssertion(() => inTipsChat.FindAll(".coach-tip-bar .banner").Should().BeEmpty());
    }
```
`UserScopedKvasBackendExt.UserCoachTip()` is the backend accessor from plan 2 (`Users.Contracts`).
`Banner` hides through `IsVisible` with a 300 ms animator: if the element stays in the DOM with a
hidden class, assert on that class instead of absence — read `Banner.razor` and `ShowHideAnimator`.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter "FullyQualifiedName~TipBarShould" -v q`
Expected: FAIL to compile.

- [ ] **Step 3: Write the component and mount it**

`CoachTipBar.razor`:
```razor
@namespace ActualChat.UI.Blazor.App.Components
@using ActualChat.Users
@inherits ComputedStateComponent<AppUIHub, UserCoachTip?>
@{
    var tip = State.Value;
}

<div class="coach-tip-bar">
    <Banner
        IsVisible="@(tip is not null)"
        Severity="BannerSeverity.Info"
        ShowDismissButton="true"
        Dismiss="@OnDismiss">
        <Icon>
            <i class="icon-microphone text-2xl"></i>
        </Icon>
        <Body>
            @if (tip is not null) {
                <span class="c-text">@TipText(tip)</span>
                @if (tip.Kind == CoachTipKind.WeakWord && tip.Synonyms.Count > 0) {
                    <span class="c-synonyms">
                        @L.Coach_TipSynonyms
                        @foreach (var synonym in tip.Synonyms) {
                            <span class="coach-chip">@synonym</span>
                        }
                    </span>
                }
            }
        </Body>
        <Buttons>
            <BannerButton Click="@OnOpenCoach">@L.Coach_OpenCoach</BannerButton>
        </Buttons>
    </Banner>
</div>

@code {
    private ICoach Coach => Hub.Coach;

    [Parameter, EditorRequired] public ChatId ChatId { get; set; }

    protected override ComputedState<UserCoachTip?>.Options GetStateOptions()
        => new() { UpdateDelayer = FixedDelayer.NextTick, Category = GetStateCategory(GetType()) };

    protected override async Task<UserCoachTip?> ComputeState(CancellationToken cancellationToken) {
        var chatId = ChatId;
        if (!await Hub.CoachUI.IsMarkingEnabled(cancellationToken).ConfigureAwait(false))
            return null;

        var tip = await Coach.GetPendingTip(Session, cancellationToken).ConfigureAwait(false);
        return tip is { IsPending: true } && tip.ChatId == chatId ? tip : null;
    }

    private string TipText(UserCoachTip tip)
        => tip.Kind switch {
            CoachTipKind.SlowDown => L.Coach_TipSlowDown_Format(tip.Wpm),
            CoachTipKind.SpeedUp => L.Coach_TipSpeedUp_Format(tip.Wpm),
            CoachTipKind.Filler => L.Coach_TipFiller_Format(tip.Word, tip.Count),
            _ => L.Coach_TipWeakWord_Format(tip.Word),
        };

    private Task OnDismiss()
        => UICommander.Run(new Coach_DismissTip { Session = Session });

    private void OnOpenCoach()
        => PanelsUI.Right.Open(RightPanelMode.Coach);
}
```
`Banner.Dismiss` is an `EventCallback`; if it needs a `void` handler, discard the task with `_ =`.
`UICommander.Run` vs `Commander.Call`: use whichever `PttMutedBanner`'s siblings use for API commands
(`UICommander.Run(command)` reports errors as toasts).

`ChatView.razor` — next to the `NavigationSubFooter` stack item:
```razor
<RenderIntoStack Name="@LayoutSlots.SubFooter" Order="-900_000" Key="CoachTipBar">
    <CoachTipBar ChatId="@Chat.Id"/>
</RenderIntoStack>
```
`Chat` is the view's chat property (see how `ChatPinnedBar Chat="@Chat"` is passed a few lines below).
Verify in the running app that the sub-footer stack sits right above the composer and the bar does
not collide with the absolutely-positioned navigation panel; if the bar needs the pill pushed up, add
`body[data-has-coach-tip] ...` styling the way `chat-view-navigation-panel.css` handles the audio panel.

Add to `coach.css`:
```css
.coach-tip-bar { @apply px-2 pb-1; }
.coach-tip-bar .banner { @apply banner-wrap; }
.coach-tip-bar .c-synonyms { @apply flex-x flex-wrap items-center gap-1 ml-2; }
```
(`banner-wrap` is a class `Banner` applies for wrapping bodies; if `@apply` of it fails, pass
`Class="banner-wrap"` — check `Banner.razor`'s `NoWrap` parameter instead.)

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests --filter "FullyQualifiedName~CoachUITest" -v q`
Expected: PASS (8 tests). Run `npm run build:Verify`. Expected: clean.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Components/Coach src/dotnet/UI.Blazor.App/Components/ChatView/ChatView.razor \
  tests/Chat.UI.Blazor.IntegrationTests/CoachUITest.cs
git commit -m "feat(coach-ui): show the live tip above the composer"
```

---

### Task 11: Docs, whole-suite verification, manual pass

**Files:**
- Modify: `docs/api-index.md`, `docs/api-index-full.md` (add `CoachUI`, `CoachLabels`, `CoachPanel`, `CoachTipBar`, `DonutChart`, `BarChart`, `ChartItem`, `RightPanelMode`, `SpeechSpanExt` under their projects, in the style of the plan-2 entries)
- Modify: `.superpowers/sdd/.../progress.md` ledger (rulings and deferred minors)

- [ ] **Step 1: Update the indexes**

Add one line per new public type in both files next to the plan-2 coach entries (grep `CoachSummary`
in each to find the spot).

- [ ] **Step 2: Run the affected suites**

```
dotnet test tests/Chat.UnitTests -v q
dotnet test tests/Chat.UI.Blazor.UnitTests -v q
dotnet test tests/Chat.UI.Blazor.IntegrationTests -v q
dotnet build src/dotnet/App.Server -v q
npm run build:Verify
```
Expected: all green (record the counts in the ledger). If the Blazor integration suite is heavy for
the machine, run it in the foreground rather than as a background job (background runs were killed
for memory earlier on this branch).

- [ ] **Step 3: Manual pass in the running dev server**

With `ChatSettings:Coach:IsEnabled` on (it is, in `appsettings.Development.json`) and your account an
admin: open a chat, switch the right panel to Coach, check the score card, toggles, window buttons,
metric rows and Trends; post a voice message with "um" and a repeated word; confirm the inline marks
appear after the tagger runs (the configs repo prompt must be in `~/.actual/prompts`); confirm a tip
bar appears above the composer for an opted-in account and clears on dismiss; click a chip, then an
occurrence, and confirm the chat scrolls to the entry and replay starts at the word. On a narrow
window, open the coach from the header menu and confirm Back closes it. Note anything off in the
ledger as a deferred minor rather than fixing it silently.

- [ ] **Step 4: Commit**

```bash
git add docs/api-index.md docs/api-index-full.md
git commit -m "docs(coach-ui): index the coach UI types"
```

---

## Reuse

**Existing abstractions used** (all verified in the codebase for this plan):

| Need | Piece |
|---|---|
| right-panel host, swap, tabs | `RightPanel`, `RightPanelStoredState` (`StoredState<Box<T>>` on `LocalSettings`), `RightPanelContent` + `ContentSwap`, `TabPanel`/`TabDef` |
| mobile full-screen | the same `SideNav` goes full width on narrow screens; entry through `ChatPropertiesMenu` + `MenuEntry` |
| feature flag | `Features.Get<Features_EnableSpeechCoach>` (already on the branch) |
| settings UI | `UserSettingsUI.UserCoachSettings()` accessor (`UserSettingsAccessor<T>.Get/Update`), `Tile`/`TileItem`/`Toggle`, `Form`/`FormSection`/`InputSelect` as in `PttReplyWindowSettings` |
| in-chat bar | `Banner`/`BannerButton` from `UI.Blazor/Components/Banner`, `RenderIntoStack` into `LayoutSlots.SubFooter` |
| per-word rendering + play-from-word | `PlayableTextMarkupView` (per-word spans, `OnMarkupClick`), `ChatAudioUI.StartReplay(chatId, startAt)` |
| navigate to an entry | `ChatUI.HighlightEntry(id, navigate: true)` in the open chat, `History.NavigateTo(Links.Chat(chatId, lid))` otherwise |
| per-tile batching | Fusion compute cache keyed by `Constants.Chat.EntryIdTiles.GetTile(lid).Range` |
| client compute services | `UIServiceBase<AppUIHub>` + `IComputeService`, registered with `fusion.AddService<T>(ServiceLifetime.Scoped)` (`HighlightUI` is the model) |
| localized text | `LocalizedStringsLocalizerExt` typed members, `l.Plural(...)` for counted text, `DateTimeConverter`/`DateFormatter` for dates |
| progress ring precedent | `RoundProgress.razor` (dasharray technique the donut reuses) |
| tests | `TestBunitContext` (pure components), `AppHost.NewBlazorTester` + `SharedAppHostTestBase.NewAppHost` with in-memory config (host-backed), `ChatEntryOperations.Create/FinalizeStreamingEntry`, `TestWait.When` |

**Not found (so new):** no chart components exist anywhere in `UI.Blazor`/`UI.Blazor.App` (only the
progress ring and a segmented step bar); no persisted right-panel mode; no span→word mapping.

**Reusability of new components:**

| Component | Local | Shared | Recommendation |
|---|---|---|---|
| `SpeechSpanExt.MapToWords` | UI.Blazor.App | `Api/Chat/Coach` next to `SpeechTextStats` | **shared (Api)**: pure, depends only on `PlayableTextMarkup`; the mobile/native clients or a future "search in transcript" overlay need the same mapping |
| `DonutChart`, `BarChart`, `ChartItem` | UI.Blazor.App/Coach | `UI.Blazor/Components/Charts` | **shared (UI.Blazor)**: no coaching knowledge; usage stats and the admin pages are obvious next users |
| `RightPanelMode` + `RightPanel.Mode` | UI.Blazor.App | `UI.Blazor/Services/PanelsUI` | **shared (UI.Blazor)** because `RightPanel` lives there; the enum is generic (a future "Search" or "Members" mode fits) |
| `CoachUI` | UI.Blazor.App/Services | — | **local**: a new `*UI` service is justified by distinct state (the flag+toggle verdicts and the per-tile marks cache) and the style guide's "extend an existing service" alternative would bloat `ChatUI` with coach-only compute methods |
| `CoachLabels`, `CoachPanel`, `CoachScoreCard`, `CoachSettingsTile`, `CoachMetricRow`, `CoachTrends`, `CoachOccurrences`, `CoachTipBar` | UI.Blazor.App/Components/Coach | — | **local**: feature-specific |
| `RightPanelModeSwitch`, `CoachMenuEntry` | UI.Blazor.App | — | **local**: the switch is generic in shape but its two labels are coach product copy; promote if a third mode appears |

## Deferred to follow-ups (write into the ledger, not into the code)

- Per-language "no word-level metrics" note in the tab (the summary does not carry the language).
- Admin "Rebuild days" button (`Coach_RebuildOwnDays` exists; run it from the commander or the test page).
- A snooze on the tip cross (design's open question); v1 dismisses.
- Keyboard navigation and ARIA for the chips and occurrences beyond `role=tablist`/`button`.
- The dashed sentence underline from the desktop mock (no defined meaning; spec open question).

## Self-review notes

- Spec coverage: surface 1 → Tasks 5, 6; surface 2 → Tasks 7, 8; surface 3 → Task 10; surface 4 →
  Tasks 2, 3, 4; surface 5 → Task 9; two-layer flag → `CoachUI.IsEnabled` gating in Tasks 5, 10 and the
  marks path in Task 3; mock corrections (same band for pace label and tip) hold because both read the
  server's `CoachBand`/tip kind; "sharpness of your writing" never appears; the tone chart slot is absent.
- Type consistency: `CoachUI.GetOwnMarks(ChatEntryId, AuthorId, ct)` (Tasks 3, 4), `RightPanel.Open(RightPanelMode)`
  (Tasks 5, 10), `ChartItem(Label, Value, Class)` (Tasks 7, 8), `CoachLabels` members (Tasks 6, 8),
  `CoachOccurrences.Word/Window/Back` (Tasks 6, 9), `CoachTipBar.ChatId` (Task 10).
- Review Focus mapping: 1 → Task 4 test 2; 2 → Task 2 test 2; 3 → Task 10; 4 → Task 5 (flag false ⇒
  `RightPanelContent` renders the chat panel whatever the stored mode; add an explicit assertion in
  the manual pass, since the flag cannot be flipped inside one BlazorTester host); 5 → Tasks 6 and 8
  (initial "no data" assertions).
