# Speech Coach — Plan 1: chat-side analysis

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every finalized voice message of a tracked user gets a `CoachEntries` row with code metrics and, once tagged, LLM spans; every quiet conversation gets one `CoachConversations` row per author with turn-taking stats; the chat shard emits one event per row to the user shard; the caller can read their own marks per chat.

**Architecture:** A `Coach/` folder in `Chat.Service` with one backend (`CoachAnalysisBackend`) driven by `ChatEntryChangedEvent` and a delayed, deduplicated self-command for conversations. Pure calculators live in `Api` next to `PlayableTextMarkup` (whose word regex they reuse), the span locator in `Core`, the tagger in `Chat.ML`. Two new tables in the Chat DB. The user side (Plan 2) consumes `CoachEntryAnalyzedEvent` / `CoachConversationAnalyzedEvent`; until it exists those events log "Unhandled event" warnings, which is expected.

**Tech Stack:** .NET 11 / C#, ActualLab.Fusion (compute services, commander, queues, operations), EF Core + PostgreSQL (snake_case, `C` collation on ids), Semantic Kernel over OpenAI (`gpt-5.6-luna`) with JSON schema response format, xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-09-25-speech-coach-design.md` (sections *Pipeline*, *LLM work*, *Metrics*, *Storage*, *Client API*, *Failure handling*, *Reuse*). Plan 2 covers the Users side, Plan 3 the UI.

## Global Constraints

- Read `docs/CODING_STYLE.md` before writing any C#. Non-negotiable rules used throughout: no `Async` suffix; Allman braces for types/methods, K&R for everything else; control-flow statements on their own line followed by a blank line; `.ConfigureAwait(false)` in all service code; no `StringComparison.Ordinal` / `CultureInfo.InvariantCulture`; `x.IsNullOrEmpty()`; `field ??=` lazy DI properties; proxied services stay unsealed with `virtual` members; everything else `sealed`; member order per the guide; tests `<Subject>Should<Behavior>` with `// arrange` / `// act` / `// assert` and FluentAssertions.
- **No comments** unless the guide's test ("saves a skimming senior reader time on a non-obvious point") passes. No `///` on members.
- Serialization: every record crossing RPC or the operation log carries `[DataContract, MessagePackObject]`, `[DataMember, Key(N)]` on every member, and excludes computed members with all four ignore attributes. Never MemoryPack.
- Words are split by `new PlayableTextMarkup(text, timeMap).Words`; never a second tokenizer. Note: `Word.Value` **includes trailing whitespace** and `Word.TextRange` covers it; trim for counting, use `TextRange.Start` for offsets.
- Bands and thresholds come from `CoachSettings` (Task 6); no literal thresholds in calculators or the backend.
- Nothing from the LLM is stored unvalidated: every span must be re-located in the text by `SpanLocator`.
- The prompt file is not in this repo. It goes to `/home/undead/projects/configs/prompts/coach-tag-speech.md` (the `Actual-Chat/configs` repo), and is resolved via `CoreServerSettings.PromptsDir | Settings.Coach.PromptFile`.
- Branch `feat/4829-speech-coach`, worktree `/home/undead/projects/actual-chat-4829-speech-coach`. Commit after every task; never push unless asked. Run `/track-issue` before the first code commit (the branch is already linked to #4829; the skill is idempotent).
- Build a test project, not the CI solution filter: `dotnet build tests/Chat.UnitTests` etc. (`Chat.CI.slnf` is stale locally).

## Review Focus

1. A voice entry whose refined text is empty or whitespace after transcription: no row, no event, no exception (Task 9 test `EmptyTextShouldBeSkipped`).
2. Text in a script without word spaces (Japanese): pace, sentence, vocabulary and repetition are `null`, filler tagging still runs (Task 3 test `NoSpaceScriptShouldYieldNullWordMetrics`, Task 9 test `NoSpaceLanguageShouldStillTag`).
3. A time map with fewer than two points or with non-monotonic Y: no pause metrics, pace from duration (Task 4 test `DegenerateTimeMapShouldFallBackToDuration`).
4. The tagger returns a word that appears fewer times than the stated occurrence, or a class outside the enum: the item is dropped, the rest kept, tag state still `Tagged` (Task 7 test `UnlocatableItemsShouldBeDropped`).
5. The same entry-changed event delivered twice, and an entry edited after tagging: exactly one row, re-tagged on text change, never double-emitted with different content under one source id (Task 9 tests `RedeliveryShouldNotDuplicate`, `EditedEntryShouldBeReanalyzed`).

## File structure

| File | Responsibility |
|---|---|
| `tests/Transcription.IntegrationTests/FillerSurvivalTest.cs` | Task 1 spike: does Soniox keep "um"/"эээ"? Manual, `[LocalFact]`. |
| `src/dotnet/Core/Text/SpanLocator.cs` | Find the n-th occurrence of a word in text as a char range (shared). |
| `src/dotnet/Api/Chat/Coach/SpeechSpanKind.cs`, `SpeechSpan.cs` | Span model shared by tagger, storage, client. |
| `src/dotnet/Api/Chat/Coach/SpeechTextStats.cs` | Words, sentences, questions, repetition, vocabulary from text. |
| `src/dotnet/Api/Chat/Coach/SpeechTimingStats.cs` | Speech time and pauses from word time ranges. |
| `src/dotnet/Api/Chat/Coach/SpeechMetrics.cs` | Record holding both stats; `IsWordSplittable(language)`. |
| `src/dotnet/Api/Chat/Coach/CoachEntryMarks.cs` | Client model: spans per entry lid. |
| `src/dotnet/Api/Users/StoredSettings/UserCoachSettings.cs` | Coaching / live tips / tip interval (read here, written by Plan 3). |
| `src/dotnet/Chat.ML/SpeechTagger.cs` | `ISpeechTagger`, `SpeechTagger`, `SpeechTaggerStub`, response parsing. |
| `src/dotnet/Chat.Service/Module/ChatSettings.cs` | `CoachSettings` (enabled, model, prompt, bands, timings). |
| `src/dotnet/Chat.Service/Db/DbCoachEntry.cs`, `DbCoachConversation.cs` | Entities. |
| `src/dotnet/Chat.Service/Db/ChatDbContext.cs` | DbSets, collations, indexes. |
| `src/dotnet/Chat.Service.Migration/Migrations/…_Add_CoachAnalysis.cs` | Migration. |
| `src/dotnet/Backend/Events/CoachEntryAnalyzedEvent.cs`, `CoachConversationAnalyzedEvent.cs` | Seam to Users. |
| `src/dotnet/Chat.Contracts/ICoachAnalysisBackend.cs` | Backend contract + commands. |
| `src/dotnet/Chat.Service/Coach/ConversationStats.cs` | Turn-taking, patience, interruptions, monologue over timed entries. |
| `src/dotnet/Chat.Service/Coach/CoachAnalysisBackend.cs` | Handlers, commands, tagger call, event emission. |
| `src/dotnet/Api.Contracts/Chat/IChatCoach.cs`, `src/dotnet/Chat.Service/Coach/ChatCoach.cs` | Frontend `GetOwnMarks`. |
| `src/dotnet/Chat.Service/Module/ChatServiceModule.cs`, `src/dotnet/Api.Contracts/Module/ApiContractsModule.cs` | Registration. |
| `tests/Core.UnitTests/Text/SpanLocatorTest.cs`, `tests/Chat.UnitTests/Coach/*Test.cs`, `tests/Chat.IntegrationTests/CoachAnalysisTest.cs` | Tests. |

---

### Task 1: Filler-survival spike

**Files:**
- Create: `tests/Transcription.IntegrationTests/FillerSurvivalTest.cs`
- Create: `tests/Transcription.IntegrationTests/data/fillers-en.webm`, `fillers-ru.webm` (recorded by you: 20–30 s each, deliberately containing "um", "uh", "you know", "like" / "эээ", "ну", "как бы", "вот")

**Interfaces:**
- Consumes: `TranscriberTestBase.GetAudio(FilePath, withDelay)`, `SonioxTranscriber`, `SonioxOfflineTranscriber`, `SonioxCleaner`, `CreateServices()` pattern from `SonioxTranscriberTest.cs:96-110`.
- Produces: a written outcome in the spec's *Risks* §1 (which fillers survive realtime vs offline).

- [ ] **Step 1: Record the two clips.** Use the app on dev (any chat), download the `.webm` via the audio blob URL, or record with any tool and convert: `ffmpeg -i in.m4a -c:a libopus -b:a 32k fillers-en.webm`. Keep a text file next to each with the exact spoken script and a hand count of every filler.

- [ ] **Step 2: Write the diagnostic test**

```csharp
using ActualChat.Transcription;
using ActualChat.Transcription.Transcribers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace ActualChat.Transcription.IntegrationTests;

[Collection(nameof(TranscriptionCollection))]
public class FillerSurvivalTest(ITestOutputHelper @out, ILogger<FillerSurvivalTest> log)
    : TranscriberTestBase(@out, log)
{
    [LocalTheory("Requires CoreSettings__SonioxKey and real audio; diagnostic only")]
    [InlineData("fillers-en.webm", "en-US", "um,uh,you know,like")]
    [InlineData("fillers-ru.webm", "ru-RU", "эээ,ну,как бы,вот")]
    public async Task SonioxShouldReportWhichFillersSurvive(string file, string language, string fillers)
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().SonioxKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__SonioxKey is not set - skipping.");
            return;
        }
        var options = new TranscriptionOptions { Language = Language.Parse(language) };
        var expected = fillers.Split(',');

        // act
        var realtime = new SonioxTranscriber(services);
        var realtimeTranscripts = await realtime.Transcribe("spike", await GetAudio(file, withDelay: true), options).ToListAsync();
        var realtimeText = realtimeTranscripts[^1].Text;

        var offline = new SonioxOfflineTranscriber(services);
        var cleaner = services.GetRequiredService<SonioxCleaner>();
        await RequireSonioxCapacity(services.GetRequiredService<SonioxClient>(), file);
        var offlineText = (await offline.Transcribe(await GetAudio(file), options))?.Text ?? "";
        await cleaner.Flush();

        // assert (diagnostic: prints counts, fails only if the pipeline returned nothing)
        WriteLine($"REALTIME: {realtimeText}");
        WriteLine($"OFFLINE:  {offlineText}");
        foreach (var f in expected)
            WriteLine($"{f,-10} realtime={Count(realtimeText, f)} offline={Count(offlineText, f)}");
        realtimeText.Should().NotBeEmpty();
        offlineText.Should().NotBeEmpty();
    }

    private static int Count(string text, string word)
        => text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Count(t => t.Trim(',', '.', '!', '?').Equals(word, StringComparison.OrdinalIgnoreCase));

    private IServiceProvider CreateServices()
    {
        var configuration = new ConfigurationManager { Sources = { new EnvironmentVariablesConfigurationSource() } };
        return new ServiceCollection()
            .AddSingleton(MomentClockSet.Default)
            .AddSingleton(_ => configuration.Settings<CoreServerSettings>(nameof(CoreSettings)))
            .AddSingleton(new TranscriptionSettings())
            .AddSoniox()
            .AddTestLogging(Out)
            .BuildServiceProvider();
    }
}
```
Copy `CreateServices` from `SonioxTranscriberTest.cs:96-110` verbatim if it differs; `LocalTheory` is `tests/Testing/LocalTheoryAttribute.cs`. `Count` is single-word only; for the two-word fillers ("you know", "как бы") also print `text.Split(f, StringComparison.OrdinalIgnoreCase).Length - 1`.

- [ ] **Step 3: Run it**

Run: `CoreSettings__SonioxKey=... dotnet test tests/Transcription.IntegrationTests --filter FullyQualifiedName~FillerSurvivalTest --logger "console;verbosity=detailed"`
Expected: both texts printed, one line per filler with realtime/offline counts.

- [ ] **Step 4: Record the outcome in the spec.** Edit `docs/superpowers/specs/2026-09-25-speech-coach-design.md`, *Risks* §1: replace "Spike before implementation: …" with the measured table (filler | hand count | realtime | offline) and the decision: if offline strips filled pauses but realtime keeps them, Task 9's analysis reads the realtime text — **which is not stored**, so that decision means storing it (a follow-up task, out of this plan). If neither keeps them, the `FilledPause` class stays in the model but the spec's headline metric becomes lexical fillers.

- [ ] **Step 5: Commit**

```bash
git add tests/Transcription.IntegrationTests/FillerSurvivalTest.cs tests/Transcription.IntegrationTests/data/fillers-*.webm tests/Transcription.IntegrationTests/data/fillers-*.txt docs/superpowers/specs/2026-09-25-speech-coach-design.md
git commit -m "test(transcription): filler-word survival spike for the speech coach"
```

---

### Task 2: `SpanLocator` (Core)

**Files:**
- Create: `src/dotnet/Core/Text/SpanLocator.cs`
- Test: `tests/Core.UnitTests/Text/SpanLocatorTest.cs`

**Interfaces:**
- Produces: `public static class SpanLocator { public static Range<int>? Locate(string text, string word, int occurrence); }` — 1-based `occurrence`; whole-word, case-insensitive; multi-word `word` allowed (matched with single spaces against the text's whitespace runs collapsed). `null` when not found.

- [ ] **Step 1: Write the failing tests**

```csharp
using ActualChat.Text;

namespace ActualChat.Core.UnitTests.Text;

public class SpanLocatorTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData("So, you know, I went, you know, home.", "you know", 1, 4, 12)]
    [InlineData("So, you know, I went, you know, home.", "you know", 2, 22, 30)]
    [InlineData("Like I like it.", "like", 2, 7, 11)]
    [InlineData("Ну вот, вот так.", "вот", 2, 8, 11)]
    public void LocateShouldFindNthWholeWordOccurrence(string text, string word, int n, int start, int end)
    {
        // act
        var range = SpanLocator.Locate(text, word, n);

        // assert
        range.Should().Be(new Range<int>(start, end));
    }

    [Theory]
    [InlineData("I liked it.", "like", 1)]
    [InlineData("you know", "you know", 2)]
    [InlineData("", "um", 1)]
    public void LocateShouldReturnNullWhenAbsent(string text, string word, int n)
        => SpanLocator.Locate(text, word, n).Should().BeNull("a partial match or a missing occurrence must not produce a span");

    [Fact]
    public void LocateShouldIgnoreCaseAndSurroundingPunctuation()
        => SpanLocator.Locate("Um... UM! (um)", "um", 3).Should().Be(new Range<int>(11, 13));
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Core.UnitTests --filter FullyQualifiedName~SpanLocatorTest`
Expected: build error, `SpanLocator` does not exist.

- [ ] **Step 3: Implement**

```csharp
namespace ActualChat.Text;

/// <summary>
/// Locates the n-th whole-word occurrence of a word or phrase in a text; used to validate
/// LLM-returned spans instead of trusting their offsets.
/// </summary>
public static class SpanLocator
{
    public static Range<int>? Locate(string text, string word, int occurrence)
    {
        if (occurrence < 1 || text.IsNullOrEmpty() || word.IsNullOrWhiteSpace())
            return null;

        var parts = word.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var seen = 0;
        var i = 0;
        while (i < text.Length) {
            var start = SkipNonWord(text, i);
            if (start >= text.Length)
                return null;

            var end = MatchPhrase(text, start, parts);
            if (end > 0) {
                if (++seen == occurrence)
                    return new Range<int>(start, end);

                i = end;
                continue;
            }
            i = SkipWord(text, start);
        }
        return null;
    }

    // Private methods

    private static int MatchPhrase(string text, int start, string[] parts)
    {
        var pos = start;
        for (var p = 0; p < parts.Length; p++) {
            if (p > 0) {
                var next = SkipNonWord(text, pos);
                if (next == pos)
                    return 0;

                pos = next;
            }
            var wordEnd = SkipWord(text, pos);
            if (wordEnd - pos != parts[p].Length)
                return 0;
            if (!text.AsSpan(pos, parts[p].Length).Equals(parts[p], StringComparison.OrdinalIgnoreCase))
                return 0;

            pos = wordEnd;
        }
        return pos;
    }

    private static int SkipNonWord(string text, int i)
    {
        while (i < text.Length && !char.IsLetterOrDigit(text[i]) && text[i] != '\'')
            i++;
        return i;
    }

    private static int SkipWord(string text, int i)
    {
        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '\''))
            i++;
        return i;
    }
}
```
`Range<int>` is `ActualLab`'s (global using). The apostrophe keeps "don't" one word.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Core.UnitTests --filter FullyQualifiedName~SpanLocatorTest`
Expected: all green. Check the expected offsets in the theory against the implementation by hand once (the `"Um... UM! (um)"` third occurrence starts at index 11).

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Core/Text/SpanLocator.cs tests/Core.UnitTests/Text/SpanLocatorTest.cs
git commit -m "feat(core): SpanLocator finds the n-th whole-word occurrence"
```

---

### Task 3: Span model and `SpeechTextStats` (Api)

**Files:**
- Create: `src/dotnet/Api/Chat/Coach/SpeechSpanKind.cs`, `src/dotnet/Api/Chat/Coach/SpeechSpan.cs`, `src/dotnet/Api/Chat/Coach/SpeechTextStats.cs`
- Test: `tests/Chat.UnitTests/Coach/SpeechTextStatsTest.cs`

**Interfaces:**
- Produces:
  ```csharp
  public enum SpeechSpanKind { FilledPause = 1, Filler = 2, Weak = 3, Profanity = 4, Repetition = 5 }
  public sealed partial record SpeechSpan(SpeechSpanKind Kind, string Word, int Start, int Length, ApiArray<string> Synonyms);
  public sealed record SpeechTextStats(int Words, int Sentences, int Questions, int Repetitions, int DistinctWords, ApiArray<SpeechSpan> RepetitionSpans)
  { public static SpeechTextStats? Compute(PlayableTextMarkup markup); public static bool IsWordSplittable(Language? language); }
  ```
  `Compute` returns `null` when the text has no words. `Words`, `Sentences`, `DistinctWords` are counts; rates are computed by consumers.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace ActualChat.Chat.UnitTests.Coach;

public class SpeechTextStatsTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static SpeechTextStats? Compute(string text)
        => SpeechTextStats.Compute(new PlayableTextMarkup(text, LinearMap.Zero));

    [Fact]
    public void ComputeShouldCountWordsSentencesAndQuestions()
    {
        // act
        var stats = Compute("I went home. Did you? Yes!\nGood.");

        // assert
        stats!.Words.Should().Be(7);
        stats.Sentences.Should().Be(4);
        stats.Questions.Should().Be(1);
    }

    [Fact]
    public void ComputeShouldTreatLineBreakAsSentenceBoundary()
        => Compute("first line\nsecond line")!.Sentences.Should().Be(2);

    [Fact]
    public void ComputeShouldCountBackToBackRepetitionsAndMarkTheSecondWord()
    {
        // act
        var stats = Compute("So so I I think, think it's fine.");

        // assert
        stats!.Repetitions.Should().Be(3);
        stats.RepetitionSpans.Should().HaveCount(3);
        stats.RepetitionSpans[0].Should().Be(new SpeechSpan(SpeechSpanKind.Repetition, "so", 3, 2, ApiArray<string>.Empty));
        stats.RepetitionSpans[2].Word.Should().Be("think", "the comma between the two must not hide the repeat");
    }

    [Fact]
    public void ComputeShouldCountDistinctWordsCaseInsensitively()
        => Compute("The the THE cat cat.")!.DistinctWords.Should().Be(2);

    [Fact]
    public void ComputeShouldReturnNullForNoWords()
        => Compute("... !!!").Should().BeNull();

    [Fact]
    public void WordsShouldExcludePunctuationOnlyTokens()
        => Compute("a - b")!.Words.Should().Be(2);

    [Theory]
    [InlineData("en-US", true)]
    [InlineData("ru-RU", true)]
    [InlineData("ja-JP", false)]
    [InlineData("zh-CN", false)]
    [InlineData("th-TH", false)]
    [InlineData(null, true)]
    public void IsWordSplittableShouldRejectScriptsWithoutWordSpaces(string? tag, bool expected)
        => SpeechTextStats.IsWordSplittable(tag is null ? null : Language.Parse(tag)).Should().Be(expected);

    [Fact]
    public void NoSpaceScriptShouldYieldNullWordMetrics()
        => SpeechTextStats.IsWordSplittable(Language.Parse("ja-JP")).Should().BeFalse("callers skip Compute for such languages");
}
```
Check `Language.Parse` accepts these tags (`src/dotnet/Api/Identifiers/Language.cs:75`); if it needs `Language.Parse("ja")` use ISO codes.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Chat.UnitTests --filter FullyQualifiedName~SpeechTextStatsTest`
Expected: build errors.

- [ ] **Step 3: Implement the model and stats**

`SpeechSpanKind.cs`:
```csharp
namespace ActualChat.Chat;

public enum SpeechSpanKind
{
    FilledPause = 1,
    Filler = 2,
    Weak = 3,
    Profanity = 4,
    Repetition = 5,
}
```

`SpeechSpan.cs`:
```csharp
namespace ActualChat.Chat;

[DataContract, MessagePackObject]
public sealed partial record SpeechSpan(
    [property: DataMember, Key(0)] SpeechSpanKind Kind,
    [property: DataMember, Key(1)] string Word,
    [property: DataMember, Key(2)] int Start,
    [property: DataMember, Key(3)] int Length,
    [property: DataMember, Key(4)] ApiArray<string> Synonyms
);
```

`SpeechTextStats.cs`:
```csharp
namespace ActualChat.Chat;

/// <summary>
/// Word-level counts of one transcript, computed over <see cref="PlayableTextMarkup.Words"/>
/// so offsets agree with the client's word indices.
/// </summary>
public sealed record SpeechTextStats(
    int Words,
    int Sentences,
    int Questions,
    int Repetitions,
    int DistinctWords,
    ApiArray<SpeechSpan> RepetitionSpans)
{
    private static readonly HashSet<string> NoWordSpaceIsoCodes = ["ja", "zh", "th", "km", "lo", "my"];

    public static bool IsWordSplittable(Language? language)
        => language is null || !NoWordSpaceIsoCodes.Contains(language.IsoCode);

    public static SpeechTextStats? Compute(PlayableTextMarkup markup)
    {
        var words = new List<(string Core, int Start)>();
        foreach (var w in markup.Words) {
            var value = w.Value.TrimEnd();
            var trimmedStart = 0;
            while (trimmedStart < value.Length && !char.IsLetterOrDigit(value[trimmedStart]))
                trimmedStart++;
            var trimmedEnd = value.Length;
            while (trimmedEnd > trimmedStart && !char.IsLetterOrDigit(value[trimmedEnd - 1]))
                trimmedEnd--;
            if (trimmedEnd <= trimmedStart)
                continue;

            var core = value[trimmedStart..trimmedEnd].ToLower();
            words.Add((core, w.TextRange.Start + trimmedStart));
        }
        if (words.Count == 0)
            return null;

        var text = markup.Text;
        var sentences = 0;
        var questions = 0;
        var hasContent = false;
        for (var i = 0; i < text.Length; i++) {
            var c = text[i];
            if (c is '.' or '!' or '?' or '\n') {
                if (hasContent) {
                    sentences++;
                    if (c == '?')
                        questions++;
                }
                hasContent = false;
            }
            else if (char.IsLetterOrDigit(c))
                hasContent = true;
        }
        if (hasContent)
            sentences++;

        var repetitionSpans = new List<SpeechSpan>();
        for (var i = 1; i < words.Count; i++)
            if (words[i].Core == words[i - 1].Core)
                repetitionSpans.Add(new SpeechSpan(
                    SpeechSpanKind.Repetition, words[i].Core, words[i].Start, words[i].Core.Length, ApiArray<string>.Empty));

        return new SpeechTextStats(
            words.Count,
            sentences,
            questions,
            repetitionSpans.Count,
            words.Select(w => w.Core).Distinct().Count(),
            repetitionSpans.ToApiArray());
    }
}
```
If `ApiArray<string>.Empty` is not the static name, use `default` / check `ActualLab.Api.ApiArray<T>`.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Chat.UnitTests --filter FullyQualifiedName~SpeechTextStatsTest`
Expected: green. If `Words` in the first test is off by one, `PlayableTextMarkup.Words` may yield a final empty token; the `trimmedEnd <= trimmedStart` skip handles it, so investigate rather than adjust the expectation.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Api/Chat/Coach tests/Chat.UnitTests/Coach/SpeechTextStatsTest.cs
git commit -m "feat(coach): speech span model and text stats over playable-text words"
```

---

### Task 4: `SpeechTimingStats` and `SpeechMetrics` (Api)

**Files:**
- Create: `src/dotnet/Api/Chat/Coach/SpeechTimingStats.cs`, `src/dotnet/Api/Chat/Coach/SpeechMetrics.cs`
- Test: `tests/Chat.UnitTests/Coach/SpeechTimingStatsTest.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record SpeechTimingStats(double SpeechSeconds, int Pauses, double PauseSeconds)
  { public static SpeechTimingStats? Compute(PlayableTextMarkup markup, double durationSeconds, double minPauseSeconds); }
  public sealed record SpeechMetrics(double DurationSeconds, SpeechTextStats? Text, SpeechTimingStats? Timing)
  { public double? WordsPerMinute { get; } }
  ```
  `Compute` returns `null` when the time map is degenerate (fewer than two points, or any word time range unmappable / non-increasing). `WordsPerMinute` uses `Timing.SpeechSeconds` when available, else `DurationSeconds`; `null` when `Text` is `null` or the divisor is ≤ 0.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace ActualChat.Chat.UnitTests.Coach;

public class SpeechTimingStatsTest(ITestOutputHelper @out) : TestBase(@out)
{
    // "one two three four" — 18 chars; words at 0-3, 4-7, 8-13, 14-18
    private const string Text = "one two three four";

    private static LinearMap Map(params (float X, float Y)[] points)
        => new(points.Select(p => p.X).ToArray(), points.Select(p => p.Y).ToArray());

    [Fact]
    public void ComputeShouldSubtractPausesFromSpeechTime()
    {
        // arrange: "two" ends at 2s, "three" starts at 5s => a 3s pause; total 8s
        var map = Map((0, 0f), (7, 2f), (8, 5f), (18, 8f));
        var markup = new PlayableTextMarkup(Text, map);

        // act
        var stats = SpeechTimingStats.Compute(markup, durationSeconds: 8, minPauseSeconds: 1);

        // assert
        stats!.Pauses.Should().Be(1);
        stats.PauseSeconds.Should().BeApproximately(3, 0.01);
        stats.SpeechSeconds.Should().BeApproximately(5, 0.01);
    }

    [Fact]
    public void ComputeShouldIgnoreGapsBelowThreshold()
    {
        // arrange
        var map = Map((0, 0f), (7, 2f), (8, 2.5f), (18, 6f));

        // act
        var stats = SpeechTimingStats.Compute(new PlayableTextMarkup(Text, map), 6, 1);

        // assert
        stats!.Pauses.Should().Be(0);
        stats.SpeechSeconds.Should().BeApproximately(6, 0.01);
    }

    [Fact]
    public void DegenerateTimeMapShouldFallBackToDuration()
    {
        // act
        var stats = SpeechTimingStats.Compute(new PlayableTextMarkup(Text, LinearMap.Zero), 6, 1);
        var metrics = new SpeechMetrics(6, SpeechTextStats.Compute(new PlayableTextMarkup(Text, LinearMap.Zero)), stats);

        // assert
        stats.Should().BeNull();
        metrics.WordsPerMinute.Should().BeApproximately(40, 0.01, "4 words over 6 s of duration");
    }

    [Fact]
    public void WordsPerMinuteShouldUseSpeechTimeWhenAvailable()
    {
        // arrange
        var map = Map((0, 0f), (7, 2f), (8, 5f), (18, 8f));
        var markup = new PlayableTextMarkup(Text, map);

        // act
        var metrics = new SpeechMetrics(8, SpeechTextStats.Compute(markup), SpeechTimingStats.Compute(markup, 8, 1));

        // assert
        metrics.WordsPerMinute.Should().BeApproximately(48, 0.01, "4 words over 5 s of speech");
    }

    [Fact]
    public void WordsPerMinuteShouldBeNullWithoutWords()
        => new SpeechMetrics(6, null, null).WordsPerMinute.Should().BeNull();
}
```
Check `LinearMap`'s constructor signature in `src/dotnet/Core/Mathematics/LinearMap.cs` (it may take `float[] xPoints, float[] yPoints` or a `Vector2[]`); adjust `Map` accordingly. `LinearMap.Zero` is the degenerate map used by `ChatMarkupHubExt`.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Chat.UnitTests --filter FullyQualifiedName~SpeechTimingStatsTest`
Expected: build errors.

- [ ] **Step 3: Implement**

`SpeechTimingStats.cs`:
```csharp
namespace ActualChat.Chat;

public sealed record SpeechTimingStats(double SpeechSeconds, int Pauses, double PauseSeconds)
{
    public static SpeechTimingStats? Compute(PlayableTextMarkup markup, double durationSeconds, double minPauseSeconds)
    {
        if (markup.TimeMap.IsDegenerate || durationSeconds <= 0)
            return null;

        var words = markup.Words;
        if (words.Length == 0)
            return null;

        var pauses = 0;
        var pauseSeconds = 0d;
        var previousEnd = words[0].TimeRange.End;
        if (!IsSane(words[0].TimeRange))
            return null;

        for (var i = 1; i < words.Length; i++) {
            var range = words[i].TimeRange;
            if (!IsSane(range) || range.Start < previousEnd - 0.001)
                return null;

            var gap = range.Start - previousEnd;
            if (gap >= minPauseSeconds) {
                pauses++;
                pauseSeconds += gap;
            }
            previousEnd = range.End;
        }
        return new SpeechTimingStats(Math.Max(0, durationSeconds - pauseSeconds), pauses, pauseSeconds);
    }

    // Private methods

    private static bool IsSane(Range<float> range)
        => range.Start >= 0 && range.End >= range.Start && range.End < PlayableTextMarkup.InfTime;
}
```
`PlayableTextMarkup.InfTime` is the 1e6 sentinel used for unmappable times; if it is private, make it `internal const float InfTime = 1e6f;` in `PlayableTextMarkup.cs` (same assembly). Note the last word's `TimeRange.End` is mapped from the text length; unmapped words become `InfTime` and correctly yield `null`.

`SpeechMetrics.cs`:
```csharp
namespace ActualChat.Chat;

public sealed record SpeechMetrics(double DurationSeconds, SpeechTextStats? Text, SpeechTimingStats? Timing)
{
    public double? WordsPerMinute {
        get {
            if (Text is null)
                return null;

            var seconds = Timing?.SpeechSeconds ?? DurationSeconds;
            return seconds > 0 ? Text.Words * 60d / seconds : null;
        }
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Chat.UnitTests --filter FullyQualifiedName~SpeechTimingStatsTest`
Expected: green.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Api/Chat/Coach src/dotnet/Api/Chat/Markup/PlayableTextMarkup.cs tests/Chat.UnitTests/Coach/SpeechTimingStatsTest.cs
git commit -m "feat(coach): speech timing stats and combined metrics"
```

---

### Task 5: `ConversationStats` (Chat.Service, local)

**Files:**
- Create: `src/dotnet/Chat.Service/Coach/ConversationStats.cs`
- Test: `tests/Chat.UnitTests/Coach/ConversationStatsTest.cs`

**Interfaces:**
- Consumes: `ChatEntry` (`AuthorId`, `BeginsAt`, `EndsAt`, `HasAudio`, `IsRemoved`).
- Produces:
  ```csharp
  public sealed record ConversationStats(
      double OwnSpeechSeconds, double TotalSpeechSeconds, int OwnTurns, int TotalTurns, int Participants,
      double LongestMonologueSeconds, int Responses, double ResponseGapSeconds, int Interruptions)
  { public static ConversationStats? Compute(IReadOnlyList<ChatEntry> entries, AuthorId authorId, double maxResponseGapSeconds); }
  ```
  `entries` are the conversation's entries sorted by lid; only entries with audio and both `BeginsAt`/`EndsAt` count. `null` if the author has no such entries. A *turn* is a maximal run of consecutive entries by one author. *Response gap* = own turn start − previous other-author turn end, counted only when 0 ≤ gap ≤ `maxResponseGapSeconds`. *Interruption* = own turn starting before the previous other-author entry's `EndsAt`.

- [ ] **Step 1: Write the failing tests**

```csharp
namespace ActualChat.Chat.UnitTests.Coach;

public class ConversationStatsTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Moment T0 = Moment.EpochStart + TimeSpan.FromDays(20_000);
    private static readonly ChatId ChatId = new("testchatid1");
    private static readonly AuthorId Me = new(ChatId, 1, AssumeValid.Option);
    private static readonly AuthorId Other = new(ChatId, 2, AssumeValid.Option);

    private static ChatEntry Voice(long lid, AuthorId author, double from, double to)
        => new(new ChatEntryId(ChatId, ChatEntryKind.Text, lid, AssumeValid.Option)) {
            AuthorId = author,
            BeginsAt = T0 + TimeSpan.FromSeconds(from),
            EndsAt = T0 + TimeSpan.FromSeconds(to),
            Audio = new ChatEntryAudio { MediaId = MediaId.Parse("fake:m") },
        };

    [Fact]
    public void ComputeShouldGiveShareTurnsAndMonologue()
    {
        // arrange: me 0-10, me 10-15 (one turn of 15 s), other 16-20, me 21-24
        var entries = new[] { Voice(1, Me, 0, 10), Voice(2, Me, 10, 15), Voice(3, Other, 16, 20), Voice(4, Me, 21, 24) };

        // act
        var s = ConversationStats.Compute(entries, Me, maxResponseGapSeconds: 5)!;

        // assert
        s.OwnSpeechSeconds.Should().Be(18);
        s.TotalSpeechSeconds.Should().Be(22);
        s.OwnTurns.Should().Be(2);
        s.TotalTurns.Should().Be(3);
        s.Participants.Should().Be(2);
        s.LongestMonologueSeconds.Should().Be(15);
    }

    [Fact]
    public void ComputeShouldMeasurePatienceAsGapAfterTheOtherStops()
    {
        // arrange: other ends at 20, me starts at 21 → gap 1
        var entries = new[] { Voice(1, Other, 16, 20), Voice(2, Me, 21, 24), Voice(3, Other, 30, 31), Voice(4, Me, 40, 41) };

        // act
        var s = ConversationStats.Compute(entries, Me, 5)!;

        // assert
        s.Responses.Should().Be(1, "the 9 s gap is above the cap and is not a response");
        s.ResponseGapSeconds.Should().Be(1);
    }

    [Fact]
    public void ComputeShouldCountInterruptions()
    {
        // arrange: other 0-10, me starts at 8
        var entries = new[] { Voice(1, Other, 0, 10), Voice(2, Me, 8, 12) };

        // act
        var s = ConversationStats.Compute(entries, Me, 5)!;

        // assert
        s.Interruptions.Should().Be(1);
        s.Responses.Should().Be(0);
    }

    [Fact]
    public void ComputeShouldIgnoreTextOnlyAndRemovedEntries()
    {
        // arrange
        var text = new ChatEntry(new ChatEntryId(ChatId, ChatEntryKind.Text, 2, AssumeValid.Option)) { AuthorId = Other, BeginsAt = T0 };
        var removed = Voice(3, Other, 5, 9) with { IsRemoved = true };

        // act
        var s = ConversationStats.Compute([Voice(1, Me, 0, 4), text, removed], Me, 5)!;

        // assert
        s.TotalSpeechSeconds.Should().Be(4);
        s.Participants.Should().Be(1);
    }

    [Fact]
    public void ComputeShouldReturnNullWhenAuthorHasNoVoice()
        => ConversationStats.Compute([Voice(1, Other, 0, 4)], Me, 5).Should().BeNull();
}
```
Verify the `ChatEntry`/`ChatEntryId`/`AuthorId` constructors against `src/dotnet/Api/Chat/ChatEntry.cs` and `Api/Identifiers/*` — copy the shapes used in `tests/Chat.UnitTests/ContextStartScannerTest.cs` or `EntryGroupExtractorTest.cs` if they differ. `IsRemoved` may be a computed property; if so build the removed entry the way those tests do.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Chat.UnitTests --filter FullyQualifiedName~ConversationStatsTest`
Expected: build errors.

- [ ] **Step 3: Implement**

```csharp
namespace ActualChat.Chat.Coach;

/// <summary>
/// Turn-taking numbers for one author over one conversation, from entry timings only.
/// </summary>
public sealed record ConversationStats(
    double OwnSpeechSeconds,
    double TotalSpeechSeconds,
    int OwnTurns,
    int TotalTurns,
    int Participants,
    double LongestMonologueSeconds,
    int Responses,
    double ResponseGapSeconds,
    int Interruptions)
{
    public static ConversationStats? Compute(
        IReadOnlyList<ChatEntry> entries, AuthorId authorId, double maxResponseGapSeconds)
    {
        var voice = entries
            .Where(e => e is { HasAudio: true, IsRemoved: false, EndsAt: not null })
            .OrderBy(e => e.LocalId)
            .ToList();
        if (voice.All(e => e.AuthorId != authorId))
            return null;

        var ownSpeech = 0d;
        var totalSpeech = 0d;
        var ownTurns = 0;
        var totalTurns = 0;
        var longestMonologue = 0d;
        var responses = 0;
        var responseGap = 0d;
        var interruptions = 0;
        var authors = new HashSet<AuthorId>();

        AuthorId? turnAuthor = null;
        Moment turnStart = default;
        Moment turnEnd = default;
        ChatEntry? previousOther = null;
        foreach (var e in voice) {
            var duration = (e.EndsAt!.Value - e.BeginsAt).TotalSeconds;
            totalSpeech += duration;
            authors.Add(e.AuthorId);
            var isOwn = e.AuthorId == authorId;
            if (isOwn)
                ownSpeech += duration;

            if (e.AuthorId != turnAuthor) {
                CloseTurn();
                turnAuthor = e.AuthorId;
                turnStart = e.BeginsAt;
                totalTurns++;
                if (isOwn) {
                    ownTurns++;
                    if (previousOther is not null) {
                        var gap = (e.BeginsAt - previousOther.EndsAt!.Value).TotalSeconds;
                        if (gap < 0)
                            interruptions++;
                        else if (gap <= maxResponseGapSeconds) {
                            responses++;
                            responseGap += gap;
                        }
                    }
                }
            }
            turnEnd = e.EndsAt.Value;
            if (!isOwn)
                previousOther = e;
        }
        CloseTurn();

        return new ConversationStats(
            ownSpeech, totalSpeech, ownTurns, totalTurns, authors.Count,
            longestMonologue, responses, responses > 0 ? responseGap / responses : 0, interruptions);

        void CloseTurn() {
            if (turnAuthor == authorId)
                longestMonologue = Math.Max(longestMonologue, (turnEnd - turnStart).TotalSeconds);
        }
    }
}
```
`ResponseGapSeconds` is the **mean** gap. Note the local function and the explicit `return` before it, per the style guide.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Chat.UnitTests --filter FullyQualifiedName~ConversationStatsTest`
Expected: green.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Chat.Service/Coach/ConversationStats.cs tests/Chat.UnitTests/Coach/ConversationStatsTest.cs
git commit -m "feat(coach): conversation turn-taking, patience and interruption stats"
```

---

### Task 6: `CoachSettings` and `UserCoachSettings`

**Files:**
- Modify: `src/dotnet/Chat.Service/Module/ChatSettings.cs`
- Create: `src/dotnet/Api/Users/StoredSettings/UserCoachSettings.cs`
- Modify: `src/dotnet/Users.Contracts/UserScopedKvasBackendExt.cs` (add accessor)
- Modify: `src/dotnet/App.Server/appsettings.Development.json` (enable in dev)

**Interfaces:**
- Produces:
  ```csharp
  public class CoachSettings {
      public bool IsEnabled { get; set; }
      public string OpenAIModel { get; set; } = "gpt-5.6-luna";
      public FilePath PromptFile { get; set; } = "coach-tag-speech.md";
      public int PromptVersion { get; set; } = 1;
      public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(60);
      public double MinPauseSeconds { get; set; } = 1;
      public double MaxResponseGapSeconds { get; set; } = 10;
      public int BatchChunkWords { get; set; } = 2000;
      public TimeSpan ConversationMaturity { get; set; } = TimeSpan.FromMinutes(10);
      public TimeSpan MaxConversationWait { get; set; } = TimeSpan.FromHours(2);
      public int MaxTaggerCallsPerUserPerDay { get; set; } = 500;
  }
  ```
  on `ChatSettings.Coach`; and `UserCoachSettings { bool IsCoachingEnabled; bool AreLiveTipsEnabled; TimeSpan TipInterval = 5 min; string Origin }` with `KvasKey = nameof(UserCoachSettings)` plus `kvas.UserCoachSettings()` accessor.

- [ ] **Step 1: Add `CoachSettings`** to `ChatSettings.cs` after `SummarizationSettings`, and `public CoachSettings Coach { get; set; } = new ();` on `ChatSettings` (next to `Summarization`). Use the exact class above; bands (wpm etc.) are Plan 2's concern and are not added here.

- [ ] **Step 2: Add `UserCoachSettings`**

```csharp
using ActualChat.Kvas;

namespace ActualChat.Users;

[DataContract, MessagePackObject]
public sealed partial record UserCoachSettings
    : StoredSettings, IHasOrigin, IHasKvasKey<UserCoachSettings>
{
    public static string KvasKey => nameof(UserCoachSettings);

    [DataMember, Key(0)]
    public bool IsCoachingEnabled { get; init; }
    [DataMember, Key(1)]
    public bool AreLiveTipsEnabled { get; init; } = true;
    [DataMember, Key(2)]
    public TimeSpan TipInterval { get; init; } = TimeSpan.FromMinutes(5);
    [DataMember, Key(3)]
    public string Origin { get; init; } = "";
}
```
Compare with `UserReplaySettings.cs` for the exact attribute set of a post-May-2026 settings record (no MemoryPack). Add to `UserScopedKvasBackendExt.cs`:
```csharp
    public static KvasAccessor<UserCoachSettings> UserCoachSettings(this UserScopedKvasBackend kvas)
        => kvas.AccessorFor<UserCoachSettings>();
```
There is a client-side twin list in `Api/Users/UserSettingsUIExt.cs` or similar (grep `UserReplaySettings(` to find every accessor list) — add the same accessor there so Plan 3 can read it.

- [ ] **Step 3: Enable in dev.** In `appsettings.Development.json`, inside `"ChatSettings"`, add `"Coach": { "IsEnabled": true }`.

- [ ] **Step 4: Build**

Run: `dotnet build src/dotnet/Chat.Service && dotnet build src/dotnet/Users.Contracts`
Expected: success.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Chat.Service/Module/ChatSettings.cs src/dotnet/Api/Users/StoredSettings/UserCoachSettings.cs src/dotnet/Users.Contracts/UserScopedKvasBackendExt.cs src/dotnet/App.Server/appsettings.Development.json
git commit -m "feat(coach): server and per-user coach settings"
```

---

### Task 7: `SpeechTagger` (Chat.ML) and the prompt

**Files:**
- Create: `src/dotnet/Chat.ML/SpeechTagger.cs`
- Create: `/home/undead/projects/configs/prompts/coach-tag-speech.md` (configs repo — separate commit there)
- Test: `tests/Chat.UnitTests/Coach/SpeechTaggerTest.cs`

**Interfaces:**
- Consumes: `SpanLocator.Locate`, `SpeechSpan`, `SpeechSpanKind`, `OpenAIModels.GetLowestReasoningEffort`, `IPromptHelpers.BuildPrompt`.
- Produces:
  ```csharp
  public sealed record SpeechTagRequest(string Text, Language? Language);
  public sealed record SpeechTagResult(ApiArray<SpeechSpan> Spans, int PromptVersion);
  public interface ISpeechTagger { Task<SpeechTagResult?> Tag(SpeechTagRequest request, CancellationToken cancellationToken); }
  public class SpeechTagger(SpeechTagger.Options settings, IServiceProvider services) : ISpeechTagger
  { public const string ServiceKey = nameof(SpeechTagger); public class Options { FilePath PromptFile; int PromptVersion; }
    public static ApiArray<SpeechSpan> ParseResponse(string text, string json); }
  public sealed class SpeechTaggerStub : ISpeechTagger  // returns empty spans, PromptVersion 0
  ```
  `Tag` returns `null` on any failure (logged), never throws except on cancellation.

- [ ] **Step 1: Write the failing tests for parsing**

```csharp
using ActualChat.Chat.ML;

namespace ActualChat.Chat.UnitTests.Coach;

public class SpeechTaggerTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const string Text = "So, um, I went, you know, to the store and it was awesome, like really awesome.";

    [Fact]
    public void ParseResponseShouldLocateEveryItem()
    {
        // arrange
        const string json = """
            {"items":[
              {"class":"filledPause","word":"um","occurrence":1},
              {"class":"filler","word":"you know","occurrence":1},
              {"class":"weak","word":"awesome","occurrence":2,"synonyms":["excellent","remarkable"]},
              {"class":"filler","word":"like","occurrence":1}
            ]}
            """;

        // act
        var spans = SpeechTagger.ParseResponse(Text, json);

        // assert
        spans.Should().HaveCount(4);
        spans[0].Should().Be(new SpeechSpan(SpeechSpanKind.FilledPause, "um", 4, 2, ApiArray<string>.Empty));
        spans[2].Kind.Should().Be(SpeechSpanKind.Weak);
        spans[2].Start.Should().Be(Text.LastIndexOf("awesome"));
        spans[2].Synonyms.Should().Equal("excellent", "remarkable");
    }

    [Fact]
    public void UnlocatableItemsShouldBeDropped()
    {
        // arrange
        const string json = """
            {"items":[
              {"class":"filler","word":"basically","occurrence":1},
              {"class":"weak","word":"awesome","occurrence":3},
              {"class":"banana","word":"um","occurrence":1},
              {"class":"filledPause","word":"um","occurrence":1}
            ]}
            """;

        // act
        var spans = SpeechTagger.ParseResponse(Text, json);

        // assert
        spans.Should().ContainSingle().Which.Word.Should().Be("um");
    }

    [Fact]
    public void ParseResponseShouldTolerateCodeFences()
        => SpeechTagger.ParseResponse(Text, "```json\n{\"items\":[]}\n```").Should().BeEmpty();

    [Fact]
    public void ParseResponseShouldThrowOnInvalidJson()
    {
        // act
        var act = () => SpeechTagger.ParseResponse(Text, "not json");

        // assert
        act.Should().Throw<Exception>("a schema failure must count as a failed call, not as an empty result");
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Chat.UnitTests --filter FullyQualifiedName~SpeechTaggerTest`
Expected: build errors.

- [ ] **Step 3: Implement the tagger**

```csharp
using System.Text.Json;
using ActualChat.Text;
using ActualLab.IO;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Services;
using OpenAI.Chat;

namespace ActualChat.Chat.ML;

#pragma warning disable OPENAI001

public sealed record SpeechTagRequest(string Text, Language? Language);

public sealed record SpeechTagResult(ApiArray<SpeechSpan> Spans, int PromptVersion);

public interface ISpeechTagger
{
    Task<SpeechTagResult?> Tag(SpeechTagRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Asks the LLM for filler, weak and profane words in a transcript and keeps only the items
/// that can be re-located in the text.
/// </summary>
public class SpeechTagger(SpeechTagger.Options settings, IServiceProvider services) : ISpeechTagger
{
    public class Options
    {
        public FilePath PromptFile { get; set; } = "";
        public int PromptVersion { get; set; } = 1;
    }

    public const string ServiceKey = nameof(SpeechTagger);

    private static readonly JsonElement ResponseSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "items": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "class": { "type": "string", "enum": ["filledPause", "filler", "weak", "profanity"] },
                  "word": { "type": "string" },
                  "occurrence": { "type": "integer" },
                  "synonyms": { "type": "array", "items": { "type": "string" } }
                },
                "required": ["class", "word", "occurrence", "synonyms"],
                "additionalProperties": false
              }
            }
          },
          "required": ["items"],
          "additionalProperties": false
        }
        """).RootElement;

    private Options Settings { get; } = settings;
    private Kernel Kernel => field ??= services.GetRequiredService<Kernel>();
    private IChatCompletionService Completion => field ??= Kernel.GetRequiredService<IChatCompletionService>(ServiceKey);
    private IPromptHelpers PromptHelpers => field ??= services.GetRequiredService<IPromptHelpers>();
    private ILogger Log => field ??= services.LogFor(GetType());
    private string PromptTemplate => field ??= File.ReadAllText(Settings.PromptFile).Trim();

    public async Task<SpeechTagResult?> Tag(SpeechTagRequest request, CancellationToken cancellationToken)
    {
        if (request.Text.IsNullOrWhiteSpace())
            return new SpeechTagResult(ApiArray<SpeechSpan>.Empty, Settings.PromptVersion);

        try {
            var systemMessage = PromptHelpers.BuildPrompt(PromptTemplate, new Dictionary<string, string> {
                { "LANGUAGE", request.Language?.ToString() ?? "unknown" },
            });
            var history = new ChatHistory();
            history.AddSystemMessage(systemMessage);
            history.AddUserMessage(request.Text);
            var executionSettings = new OpenAIPromptExecutionSettings {
                Temperature = 0,
                ReasoningEffort = OpenAIModels.GetLowestReasoningEffort(Completion.GetModelId()),
                ResponseFormat = ChatResponseFormat.ForJsonSchema(
                    ResponseSchema, "speech_tags", "Filler, weak and profane words found in a transcript"),
            };
            var response = await Completion
                .GetChatMessageContentAsync(history, executionSettings, Kernel, cancellationToken)
                .ConfigureAwait(false);
            var spans = ParseResponse(request.Text, response.Content ?? "");
            return new SpeechTagResult(spans, Settings.PromptVersion);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogError(e, "Speech tagging failed");
            return null;
        }
    }

    public static ApiArray<SpeechSpan> ParseResponse(string text, string json)
    {
        json = json.Replace("```json", "", StringComparison.OrdinalIgnoreCase).Replace("```", "").Trim();
        using var doc = JsonDocument.Parse(json);
        var spans = new List<SpeechSpan>();
        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray()) {
            var kind = item.GetProperty("class").GetString() switch {
                "filledPause" => SpeechSpanKind.FilledPause,
                "filler" => SpeechSpanKind.Filler,
                "weak" => SpeechSpanKind.Weak,
                "profanity" => SpeechSpanKind.Profanity,
                _ => (SpeechSpanKind?)null,
            };
            if (kind is null)
                continue;

            var word = item.GetProperty("word").GetString()?.Trim();
            if (word.IsNullOrEmpty() || !item.TryGetProperty("occurrence", out var occurrenceProperty))
                continue;

            var range = SpanLocator.Locate(text, word, occurrenceProperty.GetInt32());
            if (range is null)
                continue;

            var synonyms = item.TryGetProperty("synonyms", out var s) && s.ValueKind == JsonValueKind.Array
                ? s.EnumerateArray().Select(x => x.GetString()).Where(x => !x.IsNullOrWhiteSpace()).Select(x => x!.Trim()).Take(3).ToApiArray()
                : ApiArray<string>.Empty;
            spans.Add(new SpeechSpan(kind.Value, word.ToLower(), range.Value.Start, range.Value.End - range.Value.Start, synonyms));
        }
        return spans.OrderBy(s => s.Start).ToApiArray();
    }
}

public sealed class SpeechTaggerStub : ISpeechTagger
{
    public Task<SpeechTagResult?> Tag(SpeechTagRequest request, CancellationToken cancellationToken)
        => Task.FromResult<SpeechTagResult?>(new SpeechTagResult(ApiArray<SpeechSpan>.Empty, 0));
}
```
`IsCancellationOf` is the extension `ChatImageDescriber` uses. Check `OpenAIPromptExecutionSettings.Temperature` exists on the installed SK version (it does in 1.x); if the model rejects `temperature` with reasoning, drop it and rely on the schema.

- [ ] **Step 4: Write the prompt** at `/home/undead/projects/configs/prompts/coach-tag-speech.md`:

```markdown
You tag spoken-language transcripts for a speech coach. The transcript language is {{LANGUAGE}} (it may be wrong; judge from the text).

Return every occurrence of:
- filledPause: non-lexical hesitation sounds ("um", "uh", "er", "эээ", "ммм", and their equivalents in the transcript's language).
- filler: lexical fillers that carry no meaning in context ("you know", "like" as a filler, "basically", "I mean", "sort of", "ну", "как бы", "вот", "типа", "это самое"). Do not tag these words when they are used with their real meaning ("I like it", "sort of thing" as a noun phrase).
- weak: vague, overused or hedging words that a sharper word would improve ("awesome", "very", "really", "stuff", "things", "nice", "кстати" when meaningless, "очень", "прикольно"). For each weak word give two or three stronger synonyms in the same language that fit the sentence.
- profanity: swear words and slurs in any language.

Rules:
- "word" is the exact word or phrase as it appears in the transcript, lowercase, without punctuation.
- "occurrence" is the 1-based index of that word or phrase among its occurrences in the whole transcript, counting case-insensitively and ignoring punctuation. If "like" appears three times and the second one is a filler, return occurrence 2.
- One item per occurrence. Never merge occurrences.
- "synonyms" is empty for every class except weak.
- If nothing qualifies, return {"items": []}.
- Output JSON only, matching the schema you were given.
```
Commit it in the configs repo: `cd /home/undead/projects/configs && git add prompts/coach-tag-speech.md && git commit -m "prompts: coach-tag-speech for the speech coach tagger"` (do not push). Run `./copy-prompts.sh` there if that's how your local `CoreSettings__PromptsDir` is populated.

- [ ] **Step 5: Run the unit tests**

Run: `dotnet test tests/Chat.UnitTests --filter FullyQualifiedName~SpeechTaggerTest`
Expected: green.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Chat.ML/SpeechTagger.cs tests/Chat.UnitTests/Coach/SpeechTaggerTest.cs
git commit -m "feat(coach): LLM speech tagger with span validation"
```

---

### Task 8: Entities, DbContext, migration, events, backend contract

**Files:**
- Create: `src/dotnet/Chat.Service/Db/DbCoachEntry.cs`, `src/dotnet/Chat.Service/Db/DbCoachConversation.cs`
- Modify: `src/dotnet/Chat.Service/Db/ChatDbContext.cs`
- Create: migration in `src/dotnet/Chat.Service.Migration/Migrations/`
- Create: `src/dotnet/Backend/Events/CoachEntryAnalyzedEvent.cs`, `src/dotnet/Backend/Events/CoachConversationAnalyzedEvent.cs`
- Create: `src/dotnet/Chat.Contracts/ICoachAnalysisBackend.cs`
- Create: `src/dotnet/Api/Chat/Coach/CoachEntryAnalysis.cs`, `CoachConversationAnalysis.cs`, `CoachEntryMarks.cs`

**Interfaces:**
- Produces (Api models, all `[DataContract, MessagePackObject]` with `[DataMember, Key(N)]`):
  ```csharp
  public enum CoachTagState { Pending = 0, Tagged = 1, Skipped = 2 }
  public sealed partial record CoachEntryAnalysis(ChatEntryId Id, long Version) {
      AuthorId AuthorId; UserId UserId; Moment BeginsAt; Language? Language;
      double DurationSeconds; double? SpeechSeconds; int? Words; int? Sentences; int? Questions; int? Repetitions; int? DistinctWords;
      int? Pauses; double? PauseSeconds; ApiArray<SpeechSpan> Spans;
      int FilledPauses; int Fillers; int WeakWords; int Profanities;
      CoachTagState TagState; int PromptVersion; Moment? TaggedAt; HashString ContentHash; }
  public sealed partial record CoachConversationAnalysis(ConversationId Id, AuthorId AuthorId, long Version) {
      UserId UserId; long ConversationVersion; Moment EndsAt;
      double OwnSpeechSeconds; double TotalSpeechSeconds; int OwnTurns; int TotalTurns; int Participants;
      double LongestMonologueSeconds; int Responses; double ResponseGapSeconds; int Interruptions; }
  public sealed partial record CoachEntryMarks(long EntryLid, ApiArray<SpeechSpan> Spans);
  ```
- Events (`Backend/Events`, `EventCommand, IHasShardKey` with `ShardKey => UserId.ShardKey`):
  `CoachEntryAnalyzedEvent(CoachEntryAnalysis Analysis, bool IsRemoved)`, `CoachConversationAnalyzedEvent(CoachConversationAnalysis Analysis)`.
- Backend contract:
  ```csharp
  public interface ICoachAnalysisBackend : IComputeService, IBackendService {
      // lidTileRange must be a Constants.Chat.EntryIdTiles tile: writes invalidate per tile
      [ComputeMethod] Task<ApiArray<CoachEntryMarks>> ListMarks(ChatId chatId, AuthorId authorId, Range<long> lidTileRange, CancellationToken ct);
      [ComputeMethod] Task<CoachEntryAnalysis?> Get(ChatEntryId id, CancellationToken ct);
      [ComputeMethod] Task<CoachConversationAnalysis?> GetConversation(ConversationId id, AuthorId authorId, CancellationToken ct);
      [CommandHandler] Task OnAnalyzeEntry(CoachAnalysisBackend_AnalyzeEntry command, CancellationToken ct);
      [CommandHandler] Task OnAnalyzeConversation(CoachAnalysisBackend_AnalyzeConversation command, CancellationToken ct);
      [EventHandler] Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken ct);
  }
  CoachAnalysisBackend_AnalyzeEntry(ChatEntryId Id, bool IsRemoved) : ICommand<Unit>, IBackendCommand, IHasShardKey   // ShardKey = Id.ChatId.ShardKey
  CoachAnalysisBackend_AnalyzeConversation(ChatId ChatId, long EntryLid) : ICommand<Unit>, IBackendCommand, IHasShardKey, IHasDelayUntil, IHasUuid, IHasTimeout
      // DelayUntil init; Uuid => $"coach:{ChatId}:{EntryLid / 64}"; Timeout 5 min
  ```

- [ ] **Step 1: Api models.** Create the three records exactly as above in `src/dotnet/Api/Chat/Coach/`. Model `CoachEntryAnalysis` on `ChatEntryLanguage.cs` (`IHasId<ChatEntryId>, IHasVersion<long>, IRequirementTarget`, `[property: DataMember(Order = 0), Key(0)] ChatEntryId Id, [property: DataMember(Order = 1), Key(1)] long Version = 0`, then `[DataMember, Key(N)]` init properties, numbered from 2 upward in the order listed). `Language?` needs the same serialization the `Language` type has elsewhere (it's a `StringIdentifier`; look at how `UserLanguageSettings` stores one). `CoachConversationAnalysis` keys: `(ConversationId Id, AuthorId AuthorId, long Version = 0)`.

- [ ] **Step 2: Entities.** `DbCoachEntry` mirrors `DbChatEntryLanguage.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Db;

[Table("CoachEntries")]
[Index(nameof(ChatId), nameof(AuthorId), nameof(LocalId))]
[Index(nameof(ChatId), nameof(TagState))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbCoachEntry : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [DbKey] public string Id { get; set; } = null!;   // ChatEntryId.Value
    [ConcurrencyCheck] public long Version { get; set; }
    public string ChatId { get; set; } = "";
    public long LocalId { get; set; }
    public string AuthorId { get; set; } = "";
    public string UserId { get; set; } = "";
    public DateTime BeginsAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }
    public string? Language { get; set; }
    public double DurationSeconds { get; set; }
    public double? SpeechSeconds { get; set; }
    public int? Words { get; set; }
    public int? Sentences { get; set; }
    public int? Questions { get; set; }
    public int? Repetitions { get; set; }
    public int? DistinctWords { get; set; }
    public int? Pauses { get; set; }
    public double? PauseSeconds { get; set; }
    public string Spans { get; set; } = "[]";
    public int FilledPauses { get; set; }
    public int Fillers { get; set; }
    public int WeakWords { get; set; }
    public int Profanities { get; set; }
    public CoachTagState TagState { get; set; }
    public int PromptVersion { get; set; }
    public DateTime? TaggedAt {
        get => field?.DefaultKind(DateTimeKind.Utc);
        set => field = value?.DefaultKind(DateTimeKind.Utc);
    }
    public string ContentHash { get; set; } = "";

    public DbCoachEntry() { }
    public DbCoachEntry(CoachEntryAnalysis model) => UpdateFrom(model);

    public CoachEntryAnalysis ToModel()
        => new (new ChatEntryId(Id), Version) {
            AuthorId = new AuthorId(AuthorId),
            UserId = new UserId(UserId),
            BeginsAt = BeginsAt.ToMoment(),
            Language = Language.IsNullOrEmpty() ? null : Chat.Language.ParseNullable(Language),
            DurationSeconds = DurationSeconds,
            SpeechSeconds = SpeechSeconds,
            Words = Words, Sentences = Sentences, Questions = Questions, Repetitions = Repetitions,
            DistinctWords = DistinctWords, Pauses = Pauses, PauseSeconds = PauseSeconds,
            Spans = JsonSerializer.Deserialize<SpeechSpan[]>(Spans)?.ToApiArray() ?? ApiArray<SpeechSpan>.Empty,
            FilledPauses = FilledPauses, Fillers = Fillers, WeakWords = WeakWords, Profanities = Profanities,
            TagState = TagState, PromptVersion = PromptVersion, TaggedAt = TaggedAt?.ToMoment(),
            ContentHash = new HashString(ContentHash),
        };

    public void UpdateFrom(CoachEntryAnalysis model)
    {
        this.RequireSameOrEmptyId(model.Id.Value);
        model.RequireVersion();
        Version = model.Version;
        ChatId = model.Id.ChatId.Value;
        LocalId = model.Id.LocalId;
        AuthorId = model.AuthorId.Value;
        UserId = model.UserId.Value;
        BeginsAt = model.BeginsAt.ToDateTime();
        Language = model.Language?.ToString();
        DurationSeconds = model.DurationSeconds;
        SpeechSeconds = model.SpeechSeconds;
        Words = model.Words; Sentences = model.Sentences; Questions = model.Questions;
        Repetitions = model.Repetitions; DistinctWords = model.DistinctWords;
        Pauses = model.Pauses; PauseSeconds = model.PauseSeconds;
        Spans = JsonSerializer.Serialize(model.Spans.ToArray());
        FilledPauses = model.FilledPauses; Fillers = model.Fillers; WeakWords = model.WeakWords; Profanities = model.Profanities;
        TagState = model.TagState; PromptVersion = model.PromptVersion; TaggedAt = model.TaggedAt?.ToDateTime();
        ContentHash = model.ContentHash.Value;
    }
}
```
Put each assignment on its own line in the real file (the compact lines above are for the plan). `HashString`'s constructor/`Value` and `Language.ParseNullable` per their definitions. `DbCoachConversation`: `[Table("CoachConversations")]`, `[Index(nameof(ChatId), nameof(AuthorId))]`, key `Id = $"{conversationId}:{authorId}"`, columns `ChatId, StartEntryLid, AuthorId, UserId, ConversationVersion, EndsAt (UTC), OwnSpeechSeconds, TotalSpeechSeconds, OwnTurns, TotalTurns, Participants, LongestMonologueSeconds, Responses, ResponseGapSeconds, Interruptions, Version`, with the same `ToModel`/`UpdateFrom` shape.

- [ ] **Step 3: DbContext.** In `ChatDbContext.cs` add `public DbSet<DbCoachEntry> CoachEntries { get; protected set; } = null!;` and `CoachConversations`; in `OnModelCreating` add, following the `webHook` block at lines 159-163:
```csharp
        var coachEntry = model.Entity<DbCoachEntry>();
        coachEntry.Property(e => e.Id).UseCollation("C");
        coachEntry.Property(e => e.ChatId).UseCollation("C");
        coachEntry.Property(e => e.AuthorId).UseCollation("C");
        coachEntry.Property(e => e.UserId).UseCollation("C");
        var coachConversation = model.Entity<DbCoachConversation>();
        coachConversation.Property(e => e.Id).UseCollation("C");
        coachConversation.Property(e => e.ChatId).UseCollation("C");
        coachConversation.Property(e => e.AuthorId).UseCollation("C");
        coachConversation.Property(e => e.UserId).UseCollation("C");
```
Also register entity resolvers in `ChatServiceModule.cs` next to line 268: `db.AddEntityResolver<string, DbCoachEntry>();`.

- [ ] **Step 4: Migration.** Needs local PostgreSQL at `127.0.0.1:5432` (`postgres`/`postgres`, db `ac_dev_chat`), as `Chat.Service.Migration/ChatDbContextContextFactory.cs` expects:
```bash
cd src/dotnet/Chat.Service.Migration && dotnet ef migrations add Add_CoachAnalysis
```
Then rename the generated class to the convention used by the latest migrations (`public partial class _20260917163140_Add_WebHooks`): prefix the class name in both `.cs` and `.Designer.cs` with `_` and the timestamp, keeping `[Migration("…_Add_CoachAnalysis")]`. Inspect the generated `Up`: two `CreateTable` calls with snake_case names `coach_entries` / `coach_conversations`, collations on the four id columns, and the two indexes. If `dotnet ef` cannot reach the DB from this environment, stop and ask the user to run the command on the host.

- [ ] **Step 5: Events and contract.** Create the two events modelled on `ChatEntryChangedEvent.cs` (`[DataContract, MessagePackObject(true)] public partial record …Event(…) : EventCommand, IHasShardKey { ShardKey => Analysis.UserId.ShardKey; }` with the four ignore attributes on `ShardKey`). Create `ICoachAnalysisBackend.cs` in `Chat.Contracts` with the interface and commands from the Interfaces block, modelled on `IChatEntryLanguagesBackend.cs:9-45` and `ConversationBackend_Summarize` (`IConversationsBackend.cs:63-78`) for the delayed command:

```csharp
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record CoachAnalysisBackend_AnalyzeConversation(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] long EntryLid
) : ICommand<Unit>, IBackendCommand, IHasShardKey, IHasDelayUntil, IHasUuid, IHasTimeout
{
    [DataMember, Key(2)]
    public Moment DelayUntil { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ChatId.ShardKey;
    string IHasUuid.Uuid => $"coach:{ChatId}:{EntryLid / 64}";
    TimeSpan? IHasTimeout.Timeout => TimeSpan.FromMinutes(5);
}
```
The `/ 64` bucket dedupes the burst of commands one conversation produces while it is still being spoken; a conversation spanning two buckets is analysed by the first command that finds it mature and skipped by the second (Task 9 checks `ConversationVersion`).

- [ ] **Step 6: Build**

Run: `dotnet build src/dotnet/Chat.Service.Migration`
Expected: success (this builds Api, Backend, Chat.Contracts, Chat.Service transitively).

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/Api/Chat/Coach src/dotnet/Chat.Service/Db src/dotnet/Chat.Service.Migration/Migrations src/dotnet/Backend/Events/Coach*.cs src/dotnet/Chat.Contracts/ICoachAnalysisBackend.cs src/dotnet/Chat.Service/Module/ChatServiceModule.cs
git commit -m "feat(coach): entry and conversation analysis tables, events and backend contract"
```

---

### Task 9: `CoachAnalysisBackend`

**Files:**
- Create: `src/dotnet/Chat.Service/Coach/CoachAnalysisBackend.cs`
- Modify: `src/dotnet/Chat.Service/Module/ChatServiceModule.cs` (register backend, tagger, keyed OpenAI)
- Modify: `tests/Testing.Host/ChatEntryOperations.cs` (optional `LinearMap? timeMap` on `FinalizeStreamingEntry`)
- Test: `tests/Chat.IntegrationTests/CoachAnalysisTest.cs`

**Interfaces:**
- Consumes: everything above; `IChatsBackend.GetTile` / `ChatsBackendExt.ListEntries` (filter by lid yourself: it returns whole tiles); `IConversationsBackend.GetConversationRangeTile(chatId, start, ct)` + `Get(conversationId, ct)`; `IChatEntryLanguagesBackend.GetTile(chatId, Constants.Chat.EntryIdTiles.GetTile(lid).Range, ct)`; `IServerKvasBackend.ForUser(userId).UserCoachSettings().Get(ct)`; `IQueues.Enqueue`; `UsageEventSource.FromEntryChange` (Users.Contracts) for the "finalized voice entry" test.
- Produces: the registered `ICoachAnalysisBackend`; `CoachEntryAnalyzedEvent` / `CoachConversationAnalyzedEvent` added to the operation on every row write.

- [ ] **Step 1: Extend the test helper.** In `tests/Testing.Host/ChatEntryOperations.cs`, `FinalizeStreamingEntry`: add `LinearMap? timeMap = null` before `cancellationToken` and set `Audio = new ChatEntryAudio { MediaId = MediaId.Parse("fake:mediaid"), TimeMap = timeMap ?? default }`. Existing callers are unaffected.

- [ ] **Step 2: Write the failing integration tests**

```csharp
using ActualChat.Chat.ML;
using ActualChat.Chat.Module;
using ActualChat.Testing.Host;
using ActualChat.Users;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class CoachAnalysisTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private const string Text = "So, um, I went, you know, to the store. It was awesome.";

    private sealed class FakeTagger : ISpeechTagger
    {
        public int Calls;
        public Task<SpeechTagResult?> Tag(SpeechTagRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            var spans = SpeechTagger.ParseResponse(request.Text, """
                {"items":[{"class":"filledPause","word":"um","occurrence":1},
                          {"class":"filler","word":"you know","occurrence":1},
                          {"class":"weak","word":"awesome","occurrence":1,"synonyms":["excellent"]}]}
                """);
            return Task.FromResult<SpeechTagResult?>(new SpeechTagResult(spans, 1));
        }
    }

    private async Task<(TestAppHost AppHost, FakeTagger Tagger)> NewCoachHost(string name)
    {
        var tagger = new FakeTagger();
        var appHost = await NewAppHost(name, options => options with {
            UseNatsQueues = false,
            ConfigureHost = (_, cfg) => cfg.AddInMemoryCollection(
                ($"{nameof(ChatSettings)}:{nameof(ChatSettings.Coach)}:{nameof(CoachSettings.IsEnabled)}", "true"),
                ($"{nameof(ChatSettings)}:{nameof(ChatSettings.Coach)}:{nameof(CoachSettings.ConversationMaturity)}", "00:00:01")),
            ConfigureServices = (_, services) => services.Replace(ServiceDescriptor.Singleton<ISpeechTagger>(tagger)),
        });
        return (appHost, tagger);
    }

    [Fact]
    public async Task FinalizedVoiceEntryOfOptedInUserShouldBeAnalyzedAndTaggedImmediately()
    {
        // arrange
        var (appHost, tagger) = await NewCoachHost("coach-immediate");
        await using var _ = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        var kvas = appHost.Services.GetRequiredService<IServerKvasBackend>().ForUser(account.Id, isOutermost: true);
        await kvas.UserCoachSettings().Set(new UserCoachSettings { IsCoachingEnabled = true });
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();

        // act
        var streaming = await tester.CreateStreamingEntry(chatId, Language.Parse("en-US"), null);
        var entry = (await tester.FinalizeStreamingEntry(streaming, Text)).ChatEntrySlim;

        // assert
        var analysis = await TestWait.When(async ct => {
            var a = await backend.Get(entry.Id, ct);
            a.Should().NotBeNull();
            a!.TagState.Should().Be(CoachTagState.Tagged);
            return a;
        });
        analysis.Words.Should().Be(12);
        analysis.Sentences.Should().Be(2);
        analysis.FilledPauses.Should().Be(1);
        analysis.Fillers.Should().Be(1);
        analysis.WeakWords.Should().Be(1);
        analysis.Spans.Should().HaveCount(3);
        analysis.PromptVersion.Should().Be(1);
        tagger.Calls.Should().Be(1);
    }

    [Fact]
    public async Task VoiceEntryOfNonOptedInUserShouldStayPendingUntilConversationMatures()
    {
        // arrange
        var (appHost, tagger) = await NewCoachHost("coach-batch");
        await using var _ = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();

        // act
        var streaming = await tester.CreateStreamingEntry(chatId, Language.Parse("en-US"), null);
        var entry = (await tester.FinalizeStreamingEntry(streaming, Text)).ChatEntrySlim;

        // assert
        await TestWait.When(async ct => (await backend.Get(entry.Id, ct))!.TagState.Should().Be(CoachTagState.Pending));
        tagger.Calls.Should().Be(0);
        var tagged = await TestWait.When(async ct => {
            var a = await backend.Get(entry.Id, ct);
            a!.TagState.Should().Be(CoachTagState.Tagged);
            return a;
        }, TimeSpan.FromSeconds(30));
        tagged.Fillers.Should().Be(1);
        tagger.Calls.Should().Be(1);
    }

    [Fact]
    public async Task EmptyTextShouldBeSkipped()
    {
        // arrange
        var (appHost, _) = await NewCoachHost("coach-empty");
        await using var _1 = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();

        // act
        var streaming = await tester.CreateStreamingEntry(chatId, Language.Parse("en-US"), null);
        var entry = (await tester.FinalizeStreamingEntry(streaming, "   ")).ChatEntrySlim;
        await appHost.Services.Queues().WhenProcessing(TimeSpan.FromSeconds(1), default);

        // assert
        (await backend.Get(entry.Id, default)).Should().BeNull();
    }

    [Fact]
    public async Task NoSpaceLanguageShouldStillTag()
    {
        // arrange
        var (appHost, tagger) = await NewCoachHost("coach-ja");
        await using var _ = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        await appHost.Services.GetRequiredService<IServerKvasBackend>().ForUser(account.Id, isOutermost: true)
            .UserCoachSettings().Set(new UserCoachSettings { IsCoachingEnabled = true });
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();

        // act
        var streaming = await tester.CreateStreamingEntry(chatId, Language.Parse("ja-JP"), null);
        var entry = (await tester.FinalizeStreamingEntry(streaming, "えっと、私は店に行きました。")).ChatEntrySlim;

        // assert
        var analysis = await TestWait.When(async ct => {
            var a = await backend.Get(entry.Id, ct);
            a!.TagState.Should().Be(CoachTagState.Tagged);
            return a;
        });
        analysis.Words.Should().BeNull("scripts without word spaces get no word metrics");
        analysis.DurationSeconds.Should().BeGreaterThan(0);
        tagger.Calls.Should().Be(1);
    }

    [Fact]
    public async Task RedeliveryShouldNotDuplicate()
    {
        // arrange
        var (appHost, tagger) = await NewCoachHost("coach-redelivery");
        await using var _ = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        await appHost.Services.GetRequiredService<IServerKvasBackend>().ForUser(account.Id, isOutermost: true)
            .UserCoachSettings().Set(new UserCoachSettings { IsCoachingEnabled = true });
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();
        var streaming = await tester.CreateStreamingEntry(chatId, Language.Parse("en-US"), null);
        var entry = (await tester.FinalizeStreamingEntry(streaming, Text)).ChatEntrySlim;
        var first = await TestWait.When(async ct => {
            var a = await backend.Get(entry.Id, ct);
            a!.TagState.Should().Be(CoachTagState.Tagged);
            return a;
        });

        // act
        await tester.Commander.Call(new CoachAnalysisBackend_AnalyzeEntry(entry.Id, false));

        // assert
        var second = await backend.Get(entry.Id, default);
        second!.ContentHash.Should().Be(first.ContentHash);
        tagger.Calls.Should().Be(1, "an unchanged entry must not be tagged twice");
    }

    [Fact]
    public async Task EditedEntryShouldBeReanalyzed()
    {
        // arrange
        var (appHost, tagger) = await NewCoachHost("coach-edit");
        await using var _ = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        await appHost.Services.GetRequiredService<IServerKvasBackend>().ForUser(account.Id, isOutermost: true)
            .UserCoachSettings().Set(new UserCoachSettings { IsCoachingEnabled = true });
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();
        var streaming = await tester.CreateStreamingEntry(chatId, Language.Parse("en-US"), null);
        var entry = (await tester.FinalizeStreamingEntry(streaming, Text)).ChatEntrySlim;
        await TestWait.When(async ct => (await backend.Get(entry.Id, ct))!.TagState.Should().Be(CoachTagState.Tagged));

        // act
        var edited = await tester.Commander.Call(new ChatsBackend_ChangeEntry(entry.Id, entry.Version,
            Change.Update(new ChatEntryDiff { Content = "Short and clean." })));

        // assert
        await TestWait.When(async ct => (await backend.Get(entry.Id, ct))!.ContentHash.Should().Be(edited.ContentHash));
        (await backend.Get(entry.Id, default))!.Words.Should().Be(3);
        tagger.Calls.Should().Be(2);
    }

    [Fact]
    public async Task MatureConversationShouldGetTurnTakingRowPerAuthor()
    {
        // arrange
        var (appHost, _) = await NewCoachHost("coach-conversation");
        await using var _1 = appHost;
        await using var bob = appHost.NewBlazorTester(Out);
        var bobAccount = await bob.SignInAsUniqueBob();
        var (chatId, inviteId) = await bob.CreateChat(true);
        await using var alice = appHost.NewBlazorTester(Out);
        await alice.SignInAsAlice();
        await alice.JoinChat(chatId, inviteId);
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();
        var conversations = appHost.Services.GetRequiredService<IConversationsBackend>();

        // act
        var b1 = await bob.FinalizeStreamingEntry(await bob.CreateStreamingEntry(chatId, Language.Parse("en-US"), null), "Hi Alice, how are you?");
        var a1 = await alice.FinalizeStreamingEntry(await alice.CreateStreamingEntry(chatId, Language.Parse("en-US"), null), "Fine, thanks.");
        var b2 = await bob.FinalizeStreamingEntry(await bob.CreateStreamingEntry(chatId, Language.Parse("en-US"), null), "Great.");

        // assert
        var range = await TestWait.When(async ct => {
            var tile = await conversations.GetConversationRangeTile(chatId, Constants.Chat.ConversationIdTiles.GetTile(b1.ChatEntrySlim.LocalId).Start, ct);
            tile.ConversationRanges.Should().NotBeEmpty();
            return tile.ConversationRanges[0];
        }, TimeSpan.FromSeconds(30));
        var conversationId = ConversationId.New(chatId, range.Start);
        var bobRow = await TestWait.When(async ct => {
            var row = await backend.GetConversation(conversationId, b1.ChatEntrySlim.AuthorId, ct);
            row.Should().NotBeNull();
            return row!;
        }, TimeSpan.FromSeconds(30));
        bobRow.OwnTurns.Should().Be(2);
        bobRow.TotalTurns.Should().Be(3);
        bobRow.Participants.Should().Be(2);
        bobRow.Responses.Should().Be(1);
    }
}
```
Fix-ups you will need: `SignInAsAlice` / `JoinChat` names come from `tests/Testing.Host/*Operations.cs` (grep `SignInAs`, `JoinChat`). Conversation splitting needs `ChatSettings.IsSummarizationEnabled`? Check `ConversationSplitFlow` gating; if a stub summarizer is required, replace `IConversationSummarizer` with `ConversationSummarizerStub` in `ConfigureServices`, as `ConversationSummarizationTest.cs:170-186` does. `FinalizeStreamingEntry` sets `EndsAt = now`, so timings are ordered as posted; durations are tiny but positive if `CreateStreamingEntry(beginsAt)` is a bit in the past — pass `beginsAt: clock.Now - 5s` for each entry to make `OwnSpeechSeconds > 0` meaningful.

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test tests/Chat.IntegrationTests --filter FullyQualifiedName~CoachAnalysisTest`
Expected: build errors (`ICoachAnalysisBackend` has no implementation registered / missing members).

- [ ] **Step 4: Implement the backend**

```csharp
using ActualChat.Chat.Db;
using ActualChat.Chat.ML;
using ActualChat.Chat.Module;
using ActualChat.Db;
using ActualChat.Queues;
using ActualChat.Users;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Versioning;

namespace ActualChat.Chat.Coach;

/// <summary>
/// Analyses a user's own voice entries (metrics + LLM spans) and, once a conversation is quiet,
/// their turn-taking in it; every row write emits the matching event to the user shard.
/// </summary>
public class CoachAnalysisBackend(IServiceProvider services)
    : DbServiceBase<ChatDbContext>(services), ICoachAnalysisBackend
{
    private ChatSettings Settings { get; } = services.GetRequiredService<ChatSettings>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IAuthorsBackend AuthorsBackend => field ??= Services.GetRequiredService<IAuthorsBackend>();
    private IConversationsBackend ConversationsBackend => field ??= Services.GetRequiredService<IConversationsBackend>();
    private IChatEntryLanguagesBackend LanguagesBackend => field ??= Services.GetRequiredService<IChatEntryLanguagesBackend>();
    private IServerKvasBackend ServerKvasBackend => field ??= Services.GetRequiredService<IServerKvasBackend>();
    private ISpeechTagger Tagger => field ??= Services.GetRequiredService<ISpeechTagger>();
    private IQueues Queues => field ??= Services.Queues();
    private IDbEntityResolver<string, DbCoachEntry> EntryResolver => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbCoachEntry>>();

    // [ComputeMethod]
    public virtual async Task<CoachEntryAnalysis?> Get(ChatEntryId id, CancellationToken cancellationToken)
    {
        var dbEntry = await EntryResolver.Get(id.Value, cancellationToken).ConfigureAwait(false);
        return dbEntry?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<CoachConversationAnalysis?> GetConversation(
        ConversationId id, AuthorId authorId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var dbRow = await dbContext.CoachConversations
            .FirstOrDefaultAsync(x => x.Id == DbCoachConversation.ComposeId(id, authorId), cancellationToken)
            .ConfigureAwait(false);
        return dbRow?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<CoachEntryMarks>> ListMarks(
        ChatId chatId, AuthorId authorId, Range<long> lidTileRange, CancellationToken cancellationToken)
    {
        var lidRange = lidTileRange;
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var rows = await dbContext.CoachEntries
            .Where(x => x.ChatId == chatId.Value && x.AuthorId == authorId.Value
                && x.LocalId >= lidRange.Start && x.LocalId < lidRange.End && x.Spans != "[]")
            .OrderBy(x => x.LocalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(r => r.ToModel()).Select(m => new CoachEntryMarks(m.Id.LocalId, m.Spans)).ToApiArray();
    }

    // [CommandHandler]
    public virtual async Task OnAnalyzeEntry(CoachAnalysisBackend_AnalyzeEntry command, CancellationToken cancellationToken)
    {
        var id = command.Id;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            var invalidate = context.Operation.Items.KeylessGet<CoachEntryAnalysis?>();
            if (invalidate is not null) {
                _ = Get(id, default);
                _ = ListMarks(id.ChatId, invalidate.AuthorId, Constants.Chat.EntryIdTiles.GetTile(id.LocalId).Range, default);
            }
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var dbEntry = await dbContext.CoachEntries.ForUpdate().FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken).ConfigureAwait(false);

        if (command.IsRemoved) {
            if (dbEntry is null)
                return;

            var removed = dbEntry.ToModel();
            dbContext.Remove(dbEntry);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            context.Operation.Items.KeylessSet(removed);
            context.Operation.AddEvent(new CoachEntryAnalyzedEvent(removed, true));
            return;
        }

        var entry = await ChatsBackend.GetEntry(id, cancellationToken).ConfigureAwait(false);
        if (entry is null || !IsAnalyzable(entry))
            return;
        if (dbEntry is not null && dbEntry.ContentHash == entry.ContentHash.Value && dbEntry.TagState != CoachTagState.Pending)
            return;

        var author = await AuthorsBackend.Get(id.ChatId, entry.AuthorId, RequestedAuthorKind.Default, cancellationToken).ConfigureAwait(false);
        if (author is null || author.UserId.IsNone)
            return;

        var language = await GetLanguage(id, cancellationToken).ConfigureAwait(false);
        var analysis = Analyze(entry, author.UserId, language, dbEntry?.ToModel());
        var settings = await ServerKvasBackend.ForUser(author.UserId).UserCoachSettings().Get(cancellationToken).ConfigureAwait(false);
        if (settings.IsCoachingEnabled)
            analysis = await ApplyTags(analysis, entry.Content, language, cancellationToken).ConfigureAwait(false);

        analysis = analysis with { Version = VersionGenerator.NextVersion(dbEntry?.Version ?? 0) };
        if (dbEntry is null)
            dbContext.Add(new DbCoachEntry(analysis));
        else
            dbEntry.UpdateFrom(analysis);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(analysis);
        context.Operation.AddEvent(new CoachEntryAnalyzedEvent(analysis, false));
    }

    // [CommandHandler]
    public virtual async Task OnAnalyzeConversation(CoachAnalysisBackend_AnalyzeConversation command, CancellationToken cancellationToken)
    {
        var (chatId, entryLid) = command;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            var touched = context.Operation.Items.KeylessGet<TouchedConversation?>();
            if (touched is not null) {
                foreach (var authorId in touched.AuthorIds)
                    _ = GetConversation(touched.Id, authorId, default);
                foreach (var entryId in touched.TaggedEntryIds) {
                    _ = Get(entryId, default);
                    _ = ListMarks(chatId, touched.AuthorIdOf(entryId), Constants.Chat.EntryIdTiles.GetTile(entryId.LocalId).Range, default);
                }
            }
            return;
        }

        var now = Clocks.SystemClock.Now;
        var cidTile = Constants.Chat.ConversationIdTiles.GetTile(entryLid);
        var rangeTile = await ConversationsBackend.GetConversationRangeTile(chatId, cidTile.Start, cancellationToken).ConfigureAwait(false);
        var range = rangeTile.ConversationRanges.FirstOrDefault(r => r.Contains(entryLid));
        if (range == default) {
            if (now - command.DelayUntil < Settings.Coach.MaxConversationWait)
                throw StandardError.Postpone(Settings.Coach.ConversationMaturity);

            Log.LogWarning("No conversation found for {ChatId}:{EntryLid} after {Wait}; skipping", chatId, entryLid, Settings.Coach.MaxConversationWait);
            return;
        }
        var conversation = await ConversationsBackend.Get(ConversationId.New(chatId, range.Start), cancellationToken).ConfigureAwait(false);
        if (conversation is null)
            throw StandardError.Postpone(Settings.Coach.ConversationMaturity);
        if (now - conversation.EndsAt < Settings.Coach.ConversationMaturity)
            throw StandardError.Postpone(Settings.Coach.ConversationMaturity);

        var entries = (await ChatsBackend.ListEntries(chatId, conversation.EntryLidRange, false, cancellationToken).ConfigureAwait(false))
            .Where(e => conversation.EntryLidRange.Contains(e.LocalId))
            .ToList();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        var touched = new TouchedConversation(conversation.Id);

        foreach (var authorId in entries.Where(IsAnalyzable).Select(e => e.AuthorId).Distinct()) {
            var author = await AuthorsBackend.Get(chatId, authorId, RequestedAuthorKind.Default, cancellationToken).ConfigureAwait(false);
            if (author is null || author.UserId.IsNone)
                continue;

            var rowId = DbCoachConversation.ComposeId(conversation.Id, authorId);
            var dbRow = await dbContext.CoachConversations.ForUpdate().FirstOrDefaultAsync(x => x.Id == rowId, cancellationToken).ConfigureAwait(false);
            if (dbRow is not null && dbRow.ConversationVersion == conversation.Version)
                continue;

            var stats = ConversationStats.Compute(entries, authorId, Settings.Coach.MaxResponseGapSeconds);
            if (stats is null)
                continue;

            var model = new CoachConversationAnalysis(conversation.Id, authorId, VersionGenerator.NextVersion(dbRow?.Version ?? 0)) {
                UserId = author.UserId,
                ConversationVersion = conversation.Version,
                EndsAt = conversation.EndsAt,
                OwnSpeechSeconds = stats.OwnSpeechSeconds,
                TotalSpeechSeconds = stats.TotalSpeechSeconds,
                OwnTurns = stats.OwnTurns,
                TotalTurns = stats.TotalTurns,
                Participants = stats.Participants,
                LongestMonologueSeconds = stats.LongestMonologueSeconds,
                Responses = stats.Responses,
                ResponseGapSeconds = stats.ResponseGapSeconds,
                Interruptions = stats.Interruptions,
            };
            if (dbRow is null)
                dbContext.Add(new DbCoachConversation(model));
            else
                dbRow.UpdateFrom(model);
            touched.AuthorIds.Add(authorId);
            context.Operation.AddEvent(new CoachConversationAnalyzedEvent(model));

            await TagPendingEntries(dbContext, entries, authorId, touched, context, cancellationToken).ConfigureAwait(false);
        }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(touched);
    }

    // [EventHandler]
    public virtual async Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive || !Settings.Coach.IsEnabled)
            return;

        var (entry, author, changeKind, oldEntry) = eventCommand;
        if (author.UserId.IsNone || author.IsAnonymous)
            return;

        if (changeKind == ChangeKind.Remove || entry.IsRemoved) {
            await Queues.Enqueue(new CoachAnalysisBackend_AnalyzeEntry(entry.Id, true), cancellationToken).ConfigureAwait(false);
            return;
        }

        var isFinalized = UsageEventSource.FromEntryChange(entry, oldEntry, changeKind) is { Kind: UsageEventKind.Speech };
        var isEdited = changeKind == ChangeKind.Update && oldEntry is not null && oldEntry.ContentHash != entry.ContentHash && !entry.IsContentStreaming;
        if (!isFinalized && !isEdited)
            return;

        await Queues.Enqueue(new CoachAnalysisBackend_AnalyzeEntry(entry.Id, false), cancellationToken).ConfigureAwait(false);
        if (isFinalized) {
            var delayUntil = (entry.EndsAt ?? Clocks.SystemClock.Now) + Settings.Coach.ConversationMaturity;
            await Queues.Enqueue(new CoachAnalysisBackend_AnalyzeConversation(entry.ChatId, entry.LocalId) { DelayUntil = delayUntil }, cancellationToken).ConfigureAwait(false);
        }
    }

    // Private methods

    private static bool IsAnalyzable(ChatEntry entry)
        => entry is { HasAudio: true, IsRemoved: false, IsSystemEntry: false, IsContentStreaming: false, EndsAt: not null }
            && !entry.Content.IsNullOrWhiteSpace();

    private CoachEntryAnalysis Analyze(ChatEntry entry, UserId userId, Language? language, CoachEntryAnalysis? existing)
    {
        var duration = (entry.EndsAt!.Value - entry.BeginsAt).TotalSeconds;
        var markup = new PlayableTextMarkup(entry.Content, entry.Audio?.TimeMap ?? default);
        var isSplittable = SpeechTextStats.IsWordSplittable(language);
        var text = isSplittable ? SpeechTextStats.Compute(markup) : null;
        var timing = isSplittable ? SpeechTimingStats.Compute(markup, duration, Settings.Coach.MinPauseSeconds) : null;
        var isUnchanged = existing is not null && existing.ContentHash == entry.ContentHash;
        return new CoachEntryAnalysis(entry.Id, existing?.Version ?? 0) {
            AuthorId = entry.AuthorId,
            UserId = userId,
            BeginsAt = entry.BeginsAt,
            Language = language,
            DurationSeconds = duration,
            SpeechSeconds = timing?.SpeechSeconds,
            Words = text?.Words,
            Sentences = text?.Sentences,
            Questions = text?.Questions,
            Repetitions = text?.Repetitions,
            DistinctWords = text?.DistinctWords,
            Pauses = timing?.Pauses,
            PauseSeconds = timing?.PauseSeconds,
            Spans = isUnchanged ? existing!.Spans : text?.RepetitionSpans ?? ApiArray<SpeechSpan>.Empty,
            FilledPauses = isUnchanged ? existing!.FilledPauses : 0,
            Fillers = isUnchanged ? existing!.Fillers : 0,
            WeakWords = isUnchanged ? existing!.WeakWords : 0,
            Profanities = isUnchanged ? existing!.Profanities : 0,
            TagState = isUnchanged ? existing!.TagState : CoachTagState.Pending,
            PromptVersion = isUnchanged ? existing!.PromptVersion : 0,
            TaggedAt = isUnchanged ? existing!.TaggedAt : null,
            ContentHash = entry.ContentHash,
        };
    }

    private async Task<CoachEntryAnalysis> ApplyTags(CoachEntryAnalysis analysis, string text, Language? language, CancellationToken cancellationToken)
    {
        if (analysis.TagState == CoachTagState.Tagged && analysis.PromptVersion >= Settings.Coach.PromptVersion)
            return analysis;

        var result = await Tagger.Tag(new SpeechTagRequest(text, language), cancellationToken).ConfigureAwait(false);
        if (result is null)
            return analysis;

        var repetitions = analysis.Spans.Where(s => s.Kind == SpeechSpanKind.Repetition);
        var spans = result.Spans.Concat(repetitions).OrderBy(s => s.Start).ToApiArray();
        return analysis with {
            Spans = spans,
            FilledPauses = spans.Count(s => s.Kind == SpeechSpanKind.FilledPause),
            Fillers = spans.Count(s => s.Kind == SpeechSpanKind.Filler),
            WeakWords = spans.Count(s => s.Kind == SpeechSpanKind.Weak),
            Profanities = spans.Count(s => s.Kind == SpeechSpanKind.Profanity),
            TagState = CoachTagState.Tagged,
            PromptVersion = result.PromptVersion,
            TaggedAt = Clocks.SystemClock.Now,
        };
    }

    private async Task TagPendingEntries(
        ChatDbContext dbContext, List<ChatEntry> entries, AuthorId authorId,
        TouchedConversation touched, CommandContext context, CancellationToken cancellationToken)
    {
        var ownIds = entries.Where(e => e.AuthorId == authorId && IsAnalyzable(e)).Select(e => e.Id.Value).ToList();
        var pending = await dbContext.CoachEntries.ForUpdate()
            .Where(x => ownIds.Contains(x.Id) && (x.TagState == CoachTagState.Pending || x.PromptVersion < Settings.Coach.PromptVersion))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var dbEntry in pending) {
            var entry = entries.First(e => e.Id.Value == dbEntry.Id);
            var analysis = await ApplyTags(dbEntry.ToModel(), entry.Content, dbEntry.ToModel().Language, cancellationToken).ConfigureAwait(false);
            if (analysis.TagState != CoachTagState.Tagged)
                continue;

            analysis = analysis with { Version = VersionGenerator.NextVersion(dbEntry.Version) };
            dbEntry.UpdateFrom(analysis);
            touched.TaggedEntryIds.Add(entry.Id, authorId);
            context.Operation.AddEvent(new CoachEntryAnalyzedEvent(analysis, false));
        }
    }

    private async Task<Language?> GetLanguage(ChatEntryId id, CancellationToken cancellationToken)
    {
        var tile = await LanguagesBackend
            .GetTile(id.ChatId, Constants.Chat.EntryIdTiles.GetTile(id.LocalId).Range, cancellationToken)
            .ConfigureAwait(false);
        return tile.Entries.FirstOrDefault(e => e.Id == id)?.Languages.FirstOrDefault();
    }

    // Nested types

    private sealed class TouchedConversation(ConversationId id)
    {
        public ConversationId Id { get; } = id;
        public List<AuthorId> AuthorIds { get; } = [];
        public Dictionary<ChatEntryId, AuthorId> TaggedEntryIds { get; } = [];
        public AuthorId AuthorIdOf(ChatEntryId entryId) => TaggedEntryIds[entryId];
    }
}
```
Notes for the implementer:
- The spec's batch call is "one call per conversation"; this first version calls the tagger **per pending entry** inside `TagPendingEntries`. Chunked multi-entry tagging (`BatchChunkWords`) is a follow-up once the per-entry path is proven. Do not leave a marker comment in the code for it; it is recorded in the *Deferred* list at the bottom of this plan.
- `TouchedConversation` is stored in `Operation.Items` for the invalidation pass; it must be Newtonsoft-serializable if operations are persisted with items — check `KeylessSet` usage in `ChatEntryLanguagesBackend` (it stores a `bool`). If a class does not serialize, store two arrays (`AuthorId[]`, `ChatEntryId[]`) instead.
- `DbCoachConversation.ComposeId(ConversationId, AuthorId) => $"{id}:{authorId}"` is a static on the entity.
- `ForUpdate()` is the row-lock extension used across `*Backend.cs`; `Range<long>.Contains` exists on `ActualLab`'s range.
- The daily cap (`MaxTaggerCallsPerUserPerDay`) is enforced in Plan 2 where the per-user day counts live; here it is only a setting.

- [ ] **Step 5: Cleanup on chat and own-entries removal.** In `src/dotnet/Chat.Service/ChatsBackend.cs`, the remove branch of `OnChange` (line ~1093, the block that deletes attachments, reaction summaries, reactions and mentions with `ExecuteDeleteAsync`) gets two more deletes right before `// Remove entries`:
```csharp
            await dbContext.CoachEntries
                .Where(x => x.ChatId == chatId.Value)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            await dbContext.CoachConversations
                .Where(x => x.ChatId == chatId.Value)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
```
and `OnRemoveOwnEntries` (line ~1689, the per-author variant used by account deletion) gets the same two deletes with `&& x.AuthorId == authorId` added to each `Where`. These bulk deletes do not emit `CoachEntryAnalyzedEvent`; the user-side rows of a deleted chat are removed by Plan 2's account-deletion path or stay as counts, which is acceptable (see *Deferred*). Add a test to `CoachAnalysisTest`:
```csharp
    [Fact]
    public async Task RemovedChatShouldTakeItsCoachRowsWithIt()
    {
        // arrange
        var (appHost, _) = await NewCoachHost("coach-chat-removal");
        await using var _1 = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        await appHost.Services.GetRequiredService<IServerKvasBackend>().ForUser(account.Id, isOutermost: true)
            .UserCoachSettings().Set(new UserCoachSettings { IsCoachingEnabled = true });
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();
        var entry = (await tester.FinalizeStreamingEntry(await tester.CreateStreamingEntry(chatId, Language.Parse("en-US"), null), Text)).ChatEntrySlim;
        await TestWait.When(async ct => (await backend.Get(entry.Id, ct)).Should().NotBeNull());

        // act
        await tester.Commander.Call(new ChatsBackend_Change(chatId, null, Change.Remove<ChatDiff>()));

        // assert
        await TestWait.WhenPolled(async ct => (await backend.Get(entry.Id, ct)).Should().BeNull("bulk deletes bypass invalidation, so poll"));
    }
```
Copy the exact `ChatsBackend_Change` remove shape from an existing chat-removal test (grep `Change.Remove` in `tests/Chat.IntegrationTests`).

- [ ] **Step 6: Register.** In `ChatServiceModule.cs`: `rpcHost.AddBackend<ICoachAnalysisBackend, CoachAnalysisBackend>();` next to line 89; after the `isBackendClient` return (line ~121), inside the LLM block:
```csharp
        if (Settings.Coach.IsEnabled && !CoreServerSettings.OpenAIKey.IsNullOrEmpty()) {
            AddKeyedOpenAI(services, SpeechTagger.ServiceKey, Settings.Coach.OpenAIModel, Settings.Coach.HttpTimeout);
            services.AddSingleton<ISpeechTagger>(c => new SpeechTagger(
                new SpeechTagger.Options {
                    PromptFile = c.GetRequiredService<CoreServerSettings>().PromptsDir | Settings.Coach.PromptFile,
                    PromptVersion = Settings.Coach.PromptVersion,
                },
                c));
        }
        else
            services.AddSingleton<ISpeechTagger, SpeechTaggerStub>();
```
Check how the summarization block tests for the key (line ~171) and mirror it.

- [ ] **Step 7: Run the integration tests**

Run: `dotnet test tests/Chat.IntegrationTests --filter FullyQualifiedName~CoachAnalysisTest`
Expected: all green. Typical failures and their meaning: `Words` off → whitespace token; `Pending` never becomes `Tagged` in the batch test → the conversation split did not run (stub summarizer / settings) or `ConversationMaturity` not applied (check the config key path); `Unhandled event` warnings in the log are expected.

- [ ] **Step 8: Run the whole Chat unit and integration suites once**

Run: `dotnet test tests/Chat.UnitTests && dotnet test tests/Chat.IntegrationTests`
Expected: no regressions.

- [ ] **Step 9: Commit**

```bash
git add src/dotnet/Chat.Service/Coach/CoachAnalysisBackend.cs src/dotnet/Chat.Service/ChatsBackend.cs src/dotnet/Chat.Service/Module/ChatServiceModule.cs src/dotnet/Chat.Contracts/ICoachAnalysisBackend.cs tests/Testing.Host/ChatEntryOperations.cs tests/Chat.IntegrationTests/CoachAnalysisTest.cs
git commit -m "feat(coach): analyse finalized voice entries and quiet conversations"
```

---

### Task 10: `IChatCoach.GetOwnMarks`

**Files:**
- Create: `src/dotnet/Api.Contracts/Chat/IChatCoach.cs`, `src/dotnet/Chat.Service/Coach/ChatCoach.cs`
- Modify: `src/dotnet/Chat.Service/Module/ChatServiceModule.cs` (`rpcHost.AddApi<IChatCoach, ChatCoach>()`), `src/dotnet/Api.Contracts/Module/ApiContractsModule.cs` (`fusion.AddClient<IChatCoach>()`)
- Test: add to `tests/Chat.IntegrationTests/CoachAnalysisTest.cs`

**Interfaces:**
- Produces:
  ```csharp
  public interface IChatCoach : IComputeService {
      [ComputeMethod] Task<bool> IsEnabled(Session session, CancellationToken ct);
      [ComputeMethod] Task<ApiArray<CoachEntryMarks>> GetOwnMarks(Session session, ChatId chatId, Range<long> lidRange, CancellationToken ct);
  }
  ```

- [ ] **Step 1: Write the failing test** (append to `CoachAnalysisTest`):

```csharp
    [Fact]
    public async Task GetOwnMarksShouldReturnOnlyTheCallersSpans()
    {
        // arrange
        var (appHost, _) = await NewCoachHost("coach-marks");
        await using var _1 = appHost;
        await using var bob = appHost.NewBlazorTester(Out);
        var bobAccount = await bob.SignInAsUniqueBob();
        var (chatId, inviteId) = await bob.CreateChat(true);
        await appHost.Services.GetRequiredService<IServerKvasBackend>().ForUser(bobAccount.Id, isOutermost: true)
            .UserCoachSettings().Set(new UserCoachSettings { IsCoachingEnabled = true });
        await using var alice = appHost.NewBlazorTester(Out);
        await alice.SignInAsAlice();
        await alice.JoinChat(chatId, inviteId);
        var chatCoach = appHost.Services.GetRequiredService<IChatCoach>();
        var entry = (await bob.FinalizeStreamingEntry(await bob.CreateStreamingEntry(chatId, Language.Parse("en-US"), null), Text)).ChatEntrySlim;

        // act
        var bobMarks = await TestWait.When(async ct => {
            var marks = await chatCoach.GetOwnMarks(bob.Session, chatId, new Range<long>(0, entry.LocalId + 1), ct);
            marks.Should().ContainSingle();
            return marks;
        });
        var aliceMarks = await chatCoach.GetOwnMarks(alice.Session, chatId, new Range<long>(0, entry.LocalId + 1), default);

        // assert
        bobMarks[0].EntryLid.Should().Be(entry.LocalId);
        bobMarks[0].Spans.Select(s => s.Kind).Should().Contain(SpeechSpanKind.Filler);
        aliceMarks.Should().BeEmpty("marks are private to their author");
        (await chatCoach.IsEnabled(bob.Session, default)).Should().BeTrue();
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Chat.IntegrationTests --filter FullyQualifiedName~GetOwnMarksShouldReturnOnlyTheCallersSpans`
Expected: build error.

- [ ] **Step 3: Implement**

`IChatCoach.cs` (Api.Contracts, namespace `ActualChat.Chat`): the interface above.

`ChatCoach.cs`:
```csharp
using ActualChat.Chat.Module;

namespace ActualChat.Chat.Coach;

public class ChatCoach(IServiceProvider services) : IChatCoach
{
    private ChatSettings Settings { get; } = services.GetRequiredService<ChatSettings>();
    private IAuthors Authors { get; } = services.GetRequiredService<IAuthors>();
    private ICoachAnalysisBackend Backend { get; } = services.GetRequiredService<ICoachAnalysisBackend>();

    // [ComputeMethod]
    public virtual Task<bool> IsEnabled(Session session, CancellationToken cancellationToken)
        => Task.FromResult(Settings.Coach.IsEnabled);

    // [ComputeMethod]
    public virtual async Task<ApiArray<CoachEntryMarks>> GetOwnMarks(
        Session session, ChatId chatId, Range<long> lidRange, CancellationToken cancellationToken)
    {
        if (!Settings.Coach.IsEnabled)
            return ApiArray<CoachEntryMarks>.Empty;

        var author = await Authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author is null)
            return ApiArray<CoachEntryMarks>.Empty;

        var marks = new List<CoachEntryMarks>();
        foreach (var tile in Constants.Chat.EntryIdTiles.GetCoveringTiles(lidRange)) {
            var tileMarks = await Backend.ListMarks(chatId, author.Id, tile.Range, cancellationToken).ConfigureAwait(false);
            marks.AddRange(tileMarks.Where(m => lidRange.Contains(m.EntryLid)));
        }
        return marks.ToApiArray();
    }
}
```
The backend's `ListMarks` already filters by author, so a caller can only ever get rows keyed by their own author id. The per-tile split is what makes the backend's per-tile invalidation (Task 9) reach every client range. Register in both modules (see Files).

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Chat.IntegrationTests --filter FullyQualifiedName~CoachAnalysisTest`
Expected: green.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/Api.Contracts/Chat/IChatCoach.cs src/dotnet/Chat.Service/Coach/ChatCoach.cs src/dotnet/Chat.Service/Module/ChatServiceModule.cs src/dotnet/Api.Contracts/Module/ApiContractsModule.cs tests/Chat.IntegrationTests/CoachAnalysisTest.cs
git commit -m "feat(coach): IChatCoach exposes the caller's own inline marks"
```

---

### Task 11: Golden-set tagger test (real model, local only)

**Files:**
- Create: `tests/Chat.IntegrationTests/SpeechTaggerGoldenTest.cs`, `tests/Chat.IntegrationTests/data/coach-golden.json`

**Interfaces:**
- Consumes: `SpeechTagger` with the real keyed completion (needs `CoreSettings__OpenAIKey` and `CoreSettings__PromptsDir`).

- [ ] **Step 1: Write the golden set** as JSON: six items, three English, three Russian, each `{ "language": "en-US", "text": "...", "expected": [ {"class":"filler","word":"you know","occurrence":1}, ... ] }`. Write real spoken-style sentences with 2–4 tagged items each, and at least one decoy per language ("I like it", "Мне нравится вот этот"), whose `expected` omits the decoy.

- [ ] **Step 2: Write the test**

```csharp
using System.Text.Json;
using ActualChat.Chat.ML;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class SpeechTaggerGoldenTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private sealed record Golden(string Language, string Text, GoldenItem[] Expected);
    private sealed record GoldenItem(string Class, string Word, int Occurrence);

    [LocalFact("Needs CoreSettings__OpenAIKey and CoreSettings__PromptsDir; gates prompt edits")]
    public async Task TaggerShouldMatchGoldenSetWithinTolerance()
    {
        // arrange
        var tagger = AppHost.Services.GetRequiredService<ISpeechTagger>();
        tagger.Should().BeOfType<SpeechTagger>("the real tagger must be configured for this test");
        var json = await File.ReadAllTextAsync(Path.Combine("data", "coach-golden.json"));
        var goldens = JsonSerializer.Deserialize<Golden[]>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var hits = 0;
        var total = 0;
        var extras = 0;

        // act
        foreach (var g in goldens) {
            var result = await tagger.Tag(new SpeechTagRequest(g.Text, Language.Parse(g.Language)), default);
            result.Should().NotBeNull();
            var got = result!.Spans.Select(s => (s.Kind.ToString(), s.Word, s.Start)).ToHashSet();
            foreach (var e in g.Expected) {
                total++;
                var range = SpanLocator.Locate(g.Text, e.Word, e.Occurrence)!.Value;
                var kind = e.Class switch { "filledPause" => "FilledPause", "filler" => "Filler", "weak" => "Weak", _ => "Profanity" };
                if (got.Contains((kind, e.Word, range.Start)))
                    hits++;
            }
            extras += result.Spans.Count - g.Expected.Length;
            Out.WriteLine($"{g.Language}: {result.Spans.Count} spans, expected {g.Expected.Length}");
        }

        // assert
        (hits / (double)total).Should().BeGreaterOrEqualTo(0.8, "recall on the golden set");
        extras.Should().BeLessOrEqualTo(total / 2, "over-tagging");
    }
}
```
Add the data file as `<None Update="data\coach-golden.json" CopyToOutputDirectory="PreserveNewest"/>` in the test csproj if the `data/` folder is not already globbed (check how `Chat.UnitTests` ships `data/`).

- [ ] **Step 3: Run once with a real key** and adjust the prompt if recall is below the bar.

Run: `CoreSettings__OpenAIKey=... CoreSettings__PromptsDir=/home/undead/projects/configs/prompts ChatSettings__Coach__IsEnabled=true dotnet test tests/Chat.IntegrationTests --filter FullyQualifiedName~SpeechTaggerGoldenTest`
Expected: pass; print the per-item results.

- [ ] **Step 4: Commit**

```bash
git add tests/Chat.IntegrationTests/SpeechTaggerGoldenTest.cs tests/Chat.IntegrationTests/data/coach-golden.json tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj
git commit -m "test(coach): golden-set gate for the speech tagger prompt"
```

---

### Task 12: Docs and hand-off

**Files:**
- Modify: `docs/api-index.md` (add the Coach types under Api / Chat.Service / Chat.ML / Core), `docs/api-index-full.md` if it is generated by a script (check `scripts/` for an index generator and run it instead of editing by hand).
- Modify: `docs/superpowers/specs/2026-09-25-speech-coach-design.md` — *Pipeline* Trigger B now reads: "a delayed `AnalyzeConversation` command per lid bucket, postponed until the conversation has been quiet for `ConversationMaturity`"; note that `ConversationChangedEvent` is not used.

- [ ] **Step 1: Update the two docs.**
- [ ] **Step 2: Build everything touched:** `dotnet build src/dotnet/App.Server` (pulls the whole server graph).
- [ ] **Step 3: Commit**

```bash
git add docs/api-index.md docs/api-index-full.md docs/superpowers/specs/2026-09-25-speech-coach-design.md
git commit -m "docs(coach): index the chat-side coach types; spec follows the conversation trigger"
```

---

## Deferred to a follow-up (recorded here, not as code comments)

- Multi-entry batched tagging by `BatchChunkWords` in `TagPendingEntries` (one call per author per conversation instead of per entry).
- Storing the realtime transcript text for analysis if Task 1 shows only realtime keeps filled pauses.
- The per-user daily tagger cap (`MaxTaggerCallsPerUserPerDay`): enforced in Plan 2 where per-user day counts exist.
- User-side log rows for a chat that was deleted as a whole: the chat-side bulk delete emits no events, so Plan 2's day counts keep them; Plan 3's jump-to-audio must tolerate a missing entry.
- Nightly re-tag sweep for rows below `PromptVersion` (a `PeriodicFlow`), once Plan 2's counts show how many such rows exist.
