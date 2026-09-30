using System.Collections.Concurrent;
using System.Threading.Channels;
using ActualChat.Chat.Coach;
using ActualChat.Chat.Db;
using ActualChat.Chat.ML;
using ActualChat.Chat.Module;
using ActualChat.Queues;
using ActualChat.Testing.Host;
using ActualChat.Users;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class CoachAnalysisTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatCollection.AppHostFixture>(fixture, @out)
{
    private const string Text = "So, um, I went, you know, to the store. It was awesome.";

    private sealed class FakeTagger : ISpeechTagger
    {
        private readonly ConcurrentQueue<SpeechTagRequest> _requests = new();
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);
        public IReadOnlyList<SpeechTagRequest> Requests => _requests.ToList();
        public Task Gate { get; set; } = Task.CompletedTask;
        public bool IsFailing { get; set; }

        public async Task<SpeechTagResult?> Tag(SpeechTagRequest request, CancellationToken cancellationToken)
        {
            _requests.Enqueue(request);
            Interlocked.Increment(ref _calls);
            await Gate.ConfigureAwait(false);
            if (IsFailing)
                return null;

            var spans = SpeechTagger.ParseResponse(request.Text, """
                {"items":[{"class":"filledPause","word":"um","occurrence":1,"synonyms":[]},
                          {"class":"filler","word":"you know","occurrence":1,"synonyms":[]},
                          {"class":"weak","word":"awesome","occurrence":1,"synonyms":["excellent"]}]}
                """);
            return new SpeechTagResult(spans, 1);
        }
    }

    private sealed class FakeTranscriptSource : ICoachTranscriptSource
    {
        private readonly ConcurrentDictionary<string, Channel<string>> _channels = new();

        public Task<IAsyncEnumerable<string>?> Open(string streamId, CancellationToken cancellationToken)
            => Task.FromResult<IAsyncEnumerable<string>?>(ChannelOf(streamId).Reader.ReadAllAsync(cancellationToken));

        public void Say(string streamId, string text) => ChannelOf(streamId).Writer.TryWrite(text);
        public void End(string streamId) => ChannelOf(streamId).Writer.TryComplete();

        private Channel<string> ChannelOf(string streamId)
            => _channels.GetOrAdd(streamId, _ => Channel.CreateUnbounded<string>());
    }

    private async Task<(TestAppHost AppHost, FakeTagger Tagger)> NewCoachHost(
        string name,
        FakeTagger? tagger = null,
        FakeTranscriptSource? source = null,
        params (string Key, string Value)[] coachSettings)
    {
        tagger ??= new FakeTagger();
        var coach = $"{nameof(ChatSettings)}:{nameof(ChatSettings.Coach)}";
        var appHost = await NewAppHost(name, options => options with {
            UseNatsQueues = false,
            ConfigureHost = (_, cfg) => cfg.AddInMemoryCollection(
                new (string Key, string? Value)[] {
                    ($"{coach}:{nameof(CoachSettings.IsEnabled)}", "true"),
                    ($"{coach}:{nameof(CoachSettings.ConversationMaturity)}", "00:00:01"),
                    ($"{coach}:{nameof(CoachSettings.MaxRunEntries)}", "3"),
                    ($"{nameof(ChatSettings)}:{nameof(ChatSettings.IsTranslationEnabled)}", "true"),
                    ($"{nameof(ChatSettings)}:{nameof(ChatSettings.UseFakeLanguageDetection)}", "true"),
                }.Concat(coachSettings.Select(x => ($"{coach}:{x.Key}", (string?)x.Value))).ToArray()),
            ConfigureServices = (_, services) => {
                services.Replace(ServiceDescriptor.Singleton<ISpeechTagger>(tagger));
                if (source is not null)
                    services.Replace(ServiceDescriptor.Singleton<ICoachTranscriptSource>(source));
            },
        });
        return (appHost, tagger);
    }

    private static async Task OptIn(TestAppHost appHost, AccountFull account)
        => await appHost.Services.GetRequiredService<IServerKvasBackend>()
            .ForUser(account.Id, isOutermost: true)
            .UserCoachSettings()
            .Set(new UserCoachSettings { IsCoachingEnabled = true });

    private static async Task<ChatEntry> PostVoice(
        IWebTester tester, ChatId chatId, string text, string language = "en-US")
    {
        var streaming = await tester.CreateStreamingEntry(chatId, Language.Parse(language));
        return (await tester.FinalizeStreamingEntry(streaming, text)).ChatEntrySlim;
    }

    private static Task<CoachEntryAnalysis> WhenTagged(ICoachAnalysisBackend backend, ChatEntryId id)
        => TestWait.When(async ct => {
            var analysis = await backend.Get(id, ct);
            analysis.Should().NotBeNull();
            analysis!.TagState.Should().Be(CoachTagState.Tagged);
            return analysis;
        }, TimeSpan.FromSeconds(30));

    [Fact]
    public async Task FinalizedVoiceEntryOfOptedInUserShouldBeAnalyzedAndTaggedImmediately()
    {
        // arrange
        var (appHost, tagger) = await NewCoachHost("coach-immediate");
        await using var _ = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        await OptIn(appHost, account);
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();

        // act
        var entry = await PostVoice(tester, chatId, Text);

        // assert
        var analysis = await WhenTagged(backend, entry.Id);
        analysis.Words.Should().Be(12);
        analysis.Sentences.Should().Be(2);
        analysis.FilledPauses.Should().Be(1);
        analysis.Fillers.Should().Be(1);
        analysis.WeakWords.Should().Be(1);
        analysis.Spans.Should().HaveCount(3);
        analysis.PromptVersion.Should().Be(1);
        analysis.Language.Should().Be(Languages.English);
        tagger.Calls.Should().Be(1);
    }

    [Fact]
    public async Task VoiceEntryOfAUserWithCoachingOffShouldNeverBeAnalyzed()
    {
        // arrange
        var (appHost, tagger) = await NewCoachHost("coach-off");
        await using var _ = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();

        // act
        var entry = await PostVoice(tester, chatId, Text);
        await appHost.Services.Queues().WhenProcessing(TimeSpan.FromSeconds(1), default);

        // assert
        (await backend.Get(entry.Id, default)).Should().BeNull("coaching is off until the user turns it on");
        tagger.Calls.Should().Be(0);
    }

    [Fact]
    public async Task EntryInALanguageSwitchedOffShouldNotBeAnalyzed()
    {
        // arrange
        var (appHost, tagger) = await NewCoachHost("coach-language-off");
        await using var _ = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        await appHost.Services.GetRequiredService<IServerKvasBackend>()
            .ForUser(account.Id, isOutermost: true)
            .UserCoachSettings()
            .Set(new UserCoachSettings {
                IsCoachingEnabled = true,
                Languages = new ApiMap<string, CoachLanguageLevel> { ["en"] = CoachLanguageLevel.Off },
            });
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();

        // act
        var entry = await PostVoice(tester, chatId, Text);
        await appHost.Services.Queues().WhenProcessing(TimeSpan.FromSeconds(1), default);

        // assert
        (await backend.Get(entry.Id, default)).Should().BeNull("a language switched off leaves no row at all");
        tagger.Calls.Should().Be(0);
    }

    [Fact]
    public async Task DeletingOwnDataShouldRemoveTheChatSideRowsAndMarks()
    {
        // arrange
        var (appHost, _) = await NewCoachHost("coach-delete-data");
        await using var _1 = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        await OptIn(appHost, account);
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();
        var entry = await PostVoice(tester, chatId, Text);
        await WhenTagged(backend, entry.Id);
        await WhenMarks(backend, entry, m => m.Count == 1 && m[0].Spans.Count == 3);

        // act
        await tester.Commander.Call(new Coach_DeleteOwnData { Session = tester.Session });

        // assert
        await TestWait.When(async ct => (await backend.Get(entry.Id, ct)).Should().BeNull());
        var marks = await WhenMarks(backend, entry, m => m.Count == 1 && m[0].Spans.Count == 1);
        marks[0].Spans[0].Word.Should().Be("um", "only the word list is left to mark the entry");
    }

    [Fact]
    public async Task TaggerCallsShouldStopAtTheDailyCeilingOfTheUser()
    {
        // arrange
        var (appHost, tagger) = await NewCoachHost(
            "coach-daily-ceiling", null, null, (nameof(CoachSettings.MaxTaggerCallsPerUserPerDay), "1"));
        await using var _ = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        await OptIn(appHost, account);
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();

        // act
        var first = await PostVoice(tester, chatId, Text);
        await WhenTagged(backend, first.Id);
        var second = await PostVoice(tester, chatId, Text + " So, um, again.");

        // assert
        var pending = await TestWait.When(async ct => {
            var analysis = await backend.Get(second.Id, ct);
            analysis.Should().NotBeNull();
            return analysis!;
        });
        await appHost.Services.Queues().WhenProcessing(TimeSpan.FromSeconds(2), default);
        pending.TagState.Should().Be(CoachTagState.Pending);
        tagger.Calls.Should().Be(1);
    }

    [Fact]
    public async Task AFailingTaggerShouldBeGivenUpOnAfterAFewEntriesOfARun()
    {
        // arrange
        var (appHost, tagger) = await NewCoachHost("coach-failing-tagger", new FakeTagger { IsFailing = true });
        await using var _ = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        await OptIn(appHost, account);
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();

        // act: each of the six entries is tried once as it lands, then the run tries again
        ChatEntry first = null!;
        for (var i = 0; i < 6; i++) {
            var entry = await PostVoice(tester, chatId, $"{Text} Number {i}.");
            if (i == 0)
                first = entry;
        }

        // assert
        await TestWait.When(async ct => {
            var row = await backend.GetConversation(ConversationId.New(chatId, first.LocalId), first.AuthorId, ct);
            row.Should().NotBeNull();
        }, TimeSpan.FromSeconds(30));
        tagger.Calls.Should().Be(6 + 3, "the run stops after three failures in a row");
    }

    [Fact]
    public async Task EmptyTextShouldBeSkipped()
    {
        // arrange
        var (appHost, _) = await NewCoachHost("coach-empty");
        await using var _1 = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        await OptIn(appHost, account);
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();

        // act
        var entry = await PostVoice(tester, chatId, "   ");
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
        await OptIn(appHost, account);
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();

        // act
        var entry = await PostVoice(tester, chatId, "えっと、私は店に行きました。", "ja-JP");

        // assert
        var analysis = await WhenTagged(backend, entry.Id);
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
        await OptIn(appHost, account);
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();
        var entry = await PostVoice(tester, chatId, Text);
        var first = await WhenTagged(backend, entry.Id);

        // act
        await tester.Commander.Call(new CoachAnalysisBackend_AnalyzeEntry(entry.Id, false));

        // assert
        var second = await backend.Get(entry.Id, default);
        second!.ContentHash.Should().Be(first.ContentHash);
        second.Version.Should().Be(first.Version, "an unchanged entry must not be rewritten");
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
        await OptIn(appHost, account);
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();
        var entry = await PostVoice(tester, chatId, Text);
        await WhenTagged(backend, entry.Id);

        // act
        var edited = await tester.Commander.Call(new ChatsBackend_ChangeEntry(entry.Id, null,
            Change.Update(new ChatEntryDiff { Content = "Short and clean." })));

        // assert
        var reanalyzed = await TestWait.When(async ct => {
            var analysis = await backend.Get(entry.Id, ct);
            analysis!.ContentHash.Should().Be(edited.ContentHash);
            return analysis;
        }, TimeSpan.FromSeconds(30));
        reanalyzed.Words.Should().Be(3);
        reanalyzed.TagState.Should().Be(CoachTagState.Tagged);
        tagger.Calls.Should().Be(2);
    }

    [Fact]
    public async Task QuietRunOfEntriesShouldGetTurnTakingRowPerAuthor()
    {
        // arrange
        var (appHost, _) = await NewCoachHost("coach-conversation");
        await using var _1 = appHost;
        await using var bob = appHost.NewBlazorTester(Out);
        var bobAccount = await bob.SignInAsUniqueBob();
        var (chatId, inviteId) = await bob.CreateChat(true);
        await OptIn(appHost, bobAccount);
        await using var alice = appHost.NewBlazorTester(Out);
        await alice.SignInAsAlice();
        await alice.JoinChat(chatId, inviteId);
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();

        // act
        var b1 = await PostVoice(bob, chatId, "Hi Alice, how are you?");
        await PostVoice(alice, chatId, "Fine, thanks.");
        await PostVoice(bob, chatId, "Great.");

        // assert
        var conversationId = ConversationId.New(chatId, b1.LocalId);
        var bobRow = await TestWait.When(async ct => {
            var row = await backend.GetConversation(conversationId, b1.AuthorId, ct);
            row.Should().NotBeNull();
            return row!;
        }, TimeSpan.FromSeconds(30));
        bobRow.OwnTurns.Should().Be(2);
        bobRow.TotalTurns.Should().Be(3);
        bobRow.Participants.Should().Be(2);
        bobRow.OwnSpeechSeconds.Should().BeGreaterThan(0);
        (bobRow.Responses + bobRow.Interruptions).Should().Be(1, "Bob's second turn follows Alice's");
        var bobEntry = await WhenTagged(backend, b1.Id);
        bobEntry.Questions.Should().Be(1);
    }

    [Fact]
    public async Task RemovedChatShouldTakeItsCoachRowsWithIt()
    {
        // arrange
        var (appHost, _) = await NewCoachHost("coach-chat-removal");
        await using var _1 = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        await OptIn(appHost, account);
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();
        var entry = await PostVoice(tester, chatId, Text);
        await WhenTagged(backend, entry.Id);
        var dbHub = appHost.Services.GetRequiredService<DbHub<ChatDbContext>>();

        // act
        await tester.Commander.Call(new ChatsBackend_Change(chatId, null, Change.Remove(new ChatDiff())));

        // assert
        await using var dbContext = await dbHub.CreateDbContext(false);
        var rows = await dbContext.CoachEntries.CountAsync(x => x.ChatId == chatId.Value);
        rows.Should().Be(0, "chat removal deletes the coach rows with the entries");
    }

    [Fact]
    public async Task GetOwnMarksShouldReturnOnlyTheCallersSpans()
    {
        // arrange
        var (appHost, _) = await NewCoachHost("coach-marks");
        await using var _1 = appHost;
        await using var bob = appHost.NewBlazorTester(Out);
        var bobAccount = await bob.SignInAsUniqueBob();
        var (chatId, inviteId) = await bob.CreateChat(true);
        await OptIn(appHost, bobAccount);
        await using var alice = appHost.NewBlazorTester(Out);
        await alice.SignInAsAlice();
        await alice.JoinChat(chatId, inviteId);
        var chatCoach = appHost.Services.GetRequiredService<IChatCoach>();
        var entry = await PostVoice(bob, chatId, Text);
        var lidRange = new Range<long>(0, entry.LocalId + 1);

        // act
        var bobMarks = await TestWait.When(async ct => {
            var marks = await chatCoach.GetOwnMarks(bob.Session, chatId, lidRange, ct);
            marks.Should().ContainSingle().Which.Spans
                .Should().HaveCount(3, "the tagger's answer follows the instant marks");
            return marks;
        }, TimeSpan.FromSeconds(30));
        var aliceMarks = await chatCoach.GetOwnMarks(alice.Session, chatId, lidRange, default);

        // assert
        bobMarks[0].EntryLid.Should().Be(entry.LocalId);
        bobMarks[0].Spans.Select(s => s.Kind).Should().Contain(SpeechSpanKind.Filler);
        aliceMarks.Should().BeEmpty("marks are private to their author");
        (await chatCoach.IsEnabled(bob.Session, default)).Should().BeTrue();
    }

    [Fact]
    public async Task GetOwnMarksShouldRejectAnUnboundedRange()
    {
        // arrange
        var (appHost, _) = await NewCoachHost("coach-marks-range");
        await using var _1 = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        var chatCoach = appHost.Services.GetRequiredService<IChatCoach>();

        // act
        var act = () => chatCoach.GetOwnMarks(tester.Session, chatId, new Range<long>(0, 1_000_000_000), default);

        // assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>("a client must page marks by bounded ranges");
    }

    [Fact]
    public async Task EntryEditedToEmptyShouldDropItsRow()
    {
        // arrange
        var (appHost, _) = await NewCoachHost("coach-edit-empty");
        await using var _1 = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        await OptIn(appHost, account);
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();
        var entry = await PostVoice(tester, chatId, Text);
        await WhenTagged(backend, entry.Id);

        // act
        await tester.Commander.Call(new ChatsBackend_ChangeEntry(entry.Id, null,
            Change.Update(new ChatEntryDiff { Content = "   " })));

        // assert
        await TestWait.When(async ct => (await backend.Get(entry.Id, ct))
            .Should().BeNull("stale spans would point into text that is gone"));
    }

    [Fact]
    public async Task CappedRunShouldAnalyseItsTailUnderItsTrueStart()
    {
        // arrange: MaxRunEntries is 3 in this host; five alternating turns, so a capped tail has three
        var (appHost, _) = await NewCoachHost("coach-capped-run");
        await using var _1 = appHost;
        await using var bob = appHost.NewBlazorTester(Out);
        var bobAccount = await bob.SignInAsUniqueBob();
        var (chatId, inviteId) = await bob.CreateChat(true);
        await OptIn(appHost, bobAccount);
        await using var alice = appHost.NewBlazorTester(Out);
        await alice.SignInAsAlice();
        await alice.JoinChat(chatId, inviteId);
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();

        // act
        var first = await PostVoice(bob, chatId, "One.");
        await PostVoice(alice, chatId, "Two.");
        await PostVoice(bob, chatId, "Three.");
        await PostVoice(alice, chatId, "Four.");
        await PostVoice(bob, chatId, "Five.");

        // assert
        var row = await TestWait.When(async ct => {
            var r = await backend.GetConversation(ConversationId.New(chatId, first.LocalId), first.AuthorId, ct);
            r.Should().NotBeNull("the run is identified by its true first entry even when only its tail is analysed");
            return r!;
        }, TimeSpan.FromSeconds(30));
        row.TotalTurns.Should().Be(3, "only the last MaxRunEntries entries are analysed");
        row.OwnTurns.Should().Be(2);
        var midId = ConversationId.New(chatId, first.LocalId + 2);
        var midRow = await backend.GetConversation(midId, first.AuthorId, default);
        midRow.Should().BeNull("a capped scan must not mint a second identity inside the same run");
    }

    private static string LongText()
        => string.Join(" ", Enumerable.Range(1, 12).Select(n =>
            $"So, um, this is sentence number {n} and it goes on for a while longer than usual."));

    private static Task<ApiArray<CoachEntryMarks>> WhenMarks(
        ICoachAnalysisBackend backend, ChatEntry entry, Func<ApiArray<CoachEntryMarks>, bool> isReady)
        => TestWait.When(async ct => {
            var tile = Constants.Chat.EntryIdTiles.GetTile(entry.LocalId).Range;
            var marks = await backend.ListMarks(entry.ChatId, entry.AuthorId, tile, ct);
            isReady(marks).Should().BeTrue();
            return marks;
        }, TimeSpan.FromSeconds(30));

    [Fact]
    public async Task LongEntryShouldBeTaggedInChunksWithThePreviousSentenceAsContext()
    {
        // arrange
        var (appHost, tagger) = await NewCoachHost("coach-chunks");
        await using var _ = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        await OptIn(appHost, account);
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();
        var text = LongText();

        // act
        var entry = await PostVoice(tester, chatId, text);

        // assert
        var analysis = await WhenTagged(backend, entry.Id);
        tagger.Calls.Should().Be(4, "204 words in chunks of about 50");
        var requests = tagger.Requests.OrderBy(r => text.IndexOf(r.Text, StringComparison.Ordinal)).ToList();
        string.Concat(requests.Select(r => r.Text)).Should().Be(text);
        requests[0].Context.Should().BeNull();
        requests[1].Context
            .Should().Be("So, um, this is sentence number 3 and it goes on for a while longer than usual.");
        requests.Skip(1).Should().OnlyContain(r => !r.Context.IsNullOrEmpty());
        analysis.Spans.Should().OnlyContain(s => text.Substring(s.Start, s.Length).ToLowerInvariant() == s.Word);
        analysis.FilledPauses.Should().Be(12, "the tagger's first um per chunk plus the instant list for the rest");
    }

    [Fact]
    public async Task ChunkedTaggingSwitchedOffShouldTagTheWholeEntryInOneCall()
    {
        // arrange
        var (appHost, tagger) = await NewCoachHost(
            "coach-chunks-off", null, null, (nameof(CoachSettings.IsChunkedTaggingEnabled), "false"));
        await using var _ = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        await OptIn(appHost, account);
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();
        var text = LongText();

        // act
        var entry = await PostVoice(tester, chatId, text);

        // assert
        await WhenTagged(backend, entry.Id);
        tagger.Calls.Should().Be(1);
        tagger.Requests.Single().Text.Should().Be(text);
        tagger.Requests.Single().Context.Should().BeNull();
    }

    [Fact]
    public async Task InstantMarksShouldShowBeforeTheTaggerAnswers()
    {
        // arrange
        var gate = new TaskCompletionSource();
        var (appHost, tagger) = await NewCoachHost("coach-instant", new FakeTagger { Gate = gate.Task });
        await using var _ = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        await OptIn(appHost, account);
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();

        // act
        var entry = await PostVoice(tester, chatId, Text);
        var early = await WhenMarks(backend, entry, m => m.Count == 1);

        // assert
        early[0].Spans.Should().ContainSingle().Which.Word.Should().Be("um");
        (await backend.Get(entry.Id, default))?.TagState.Should().NotBe(CoachTagState.Tagged);
        gate.SetResult();
        await WhenTagged(backend, entry.Id);
        var late = await WhenMarks(backend, entry, m => m.Count == 1 && m[0].Spans.Count == 3);
        late[0].Spans.Select(s => s.Word).Should().BeEquivalentTo(["um", "you know", "awesome"]);
        tagger.Calls.Should().Be(1);
    }

    [Fact]
    public async Task InstantMarksSwitchedOffShouldShowNothingUntilTagged()
    {
        // arrange
        var gate = new TaskCompletionSource();
        var (appHost, tagger) = await NewCoachHost(
            "coach-instant-off",
            new FakeTagger { Gate = gate.Task },
            null,
            (nameof(CoachSettings.IsInstantMarkingEnabled), "false"));
        await using var _ = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        await OptIn(appHost, account);
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();

        // act
        var entry = await PostVoice(tester, chatId, Text);
        await TestWait.WhenPolled(() => tagger.Calls.Should().BeGreaterThan(0), TimeSpan.FromSeconds(30));
        var tile = Constants.Chat.EntryIdTiles.GetTile(entry.LocalId).Range;

        // assert
        (await backend.ListMarks(chatId, entry.AuthorId, tile, default)).Should().BeEmpty();
        gate.SetResult();
        await WhenTagged(backend, entry.Id);
        var late = await WhenMarks(backend, entry, m => m.Count == 1);
        late[0].Spans.Should().HaveCount(3);
    }

    private static string LiveSentences(int from, int to)
        => string.Join(" ", Enumerable.Range(from, to - from + 1).Select(n =>
            $"So, um, this is sentence number {n} and it goes on for a while longer than usual."));

    private static Task<ApiArray<CoachLiveMark>> WhenLiveMarks(
        ICoachAnalysisBackend backend, ChatEntry entry, int count)
        => TestWait.When(async ct => {
            var marks = await backend.ListLiveMarks(entry.ChatId, entry.AuthorId, entry.LocalId, ct);
            marks.Count.Should().BeGreaterThanOrEqualTo(count);
            return marks;
        }, TimeSpan.FromSeconds(30));

    [Fact]
    public async Task LiveTaggingShouldMarkFinishedSentencesBeforeTheEntryIsFinalizedAndTheFinalAnalysisShouldReuseIt()
    {
        // arrange
        var source = new FakeTranscriptSource();
        var (appHost, tagger) = await NewCoachHost("coach-live", null, source);
        await using var _ = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        await OptIn(appHost, account);
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();
        var streaming = await tester.CreateStreamingEntry(chatId, Language.Parse("en-US"));
        var entry = streaming.ChatEntrySlim;
        var streamId = entry.ContentStreamId;

        // act: the first three sentences have settled, the speaker is still talking
        source.Say(streamId, LiveSentences(1, 3) + " So");
        var early = await WhenLiveMarks(backend, entry, 1);

        // assert: marks exist while the entry is still streaming, and nothing was analysed as an entry yet
        early.Should().ContainSingle().Which.Word.Should().Be("um");
        (await backend.Get(entry.Id, default)).Should().BeNull();
        tagger.Calls.Should().Be(1);
        tagger.Requests[0].Context.Should().BeNull();

        // act: three more sentences, then the end of the speech
        source.Say(streamId, LiveSentences(1, 6) + " So");
        await WhenLiveMarks(backend, entry, 2);
        var finalText = LiveSentences(1, 6) + " So it ends.";
        source.Say(streamId, finalText);
        source.End(streamId);
        await TestWait.WhenPolled(() => tagger.Calls.Should().Be(3), TimeSpan.FromSeconds(30));
        await tester.FinalizeStreamingEntry(streaming, finalText);

        // assert: the settled entry is tagged from the live result, without another pass
        var analysis = await WhenTagged(backend, entry.Id);
        tagger.Calls.Should().Be(3, "two settled chunks and the tail, and no whole-entry pass");
        tagger.Requests[1].Context.Should().Be(
            "So, um, this is sentence number 3 and it goes on for a while longer than usual.");
        analysis.FilledPauses.Should().Be(6, "the tagger's first um per chunk plus the word list for the rest");
    }

    [Fact]
    public async Task LiveTaggingShouldFallBackToAFullPassWhenTheSettledTextDiffers()
    {
        // arrange
        var source = new FakeTranscriptSource();
        var (appHost, tagger) = await NewCoachHost("coach-live-refined", null, source);
        await using var _ = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        await OptIn(appHost, account);
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();
        var streaming = await tester.CreateStreamingEntry(chatId, Language.Parse("en-US"));
        var streamId = streaming.ChatEntrySlim.ContentStreamId;
        var realtime = LiveSentences(1, 3) + " So it ends.";
        source.Say(streamId, realtime);
        source.End(streamId);
        await TestWait.WhenPolled(() => tagger.Calls.Should().Be(1), TimeSpan.FromSeconds(30));

        // act: re-transcription rewrote the words
        await tester.FinalizeStreamingEntry(streaming, realtime.Replace("usual", "usual indeed"));

        // assert
        await WhenTagged(backend, streaming.ChatEntrySlim.Id);
        tagger.Calls.Should().Be(2, "a text that differs from the live one is tagged again");
    }

    [Fact]
    public async Task MarksOfAFinalizedEntryShouldKeepTheLiveModelMarksWhileTheFinalAnalysisReusesTheLiveTagging()
    {
        // arrange: the first chunk is tagged live, then the tagger stalls on the tail
        var source = new FakeTranscriptSource();
        var (appHost, tagger) = await NewCoachHost("coach-live-handover", null, source);
        await using var _ = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        await OptIn(appHost, account);
        var backend = appHost.Services.GetRequiredService<ICoachAnalysisBackend>();
        var streaming = await tester.CreateStreamingEntry(chatId, Language.Parse("en-US"));
        var entry = streaming.ChatEntrySlim;
        var streamId = entry.ContentStreamId;
        var head = "So, um, it was awesome, you know, and it goes on for a while longer than usual. "
            + LiveSentences(2, 3);
        source.Say(streamId, head + " So");
        await WhenLiveMarks(backend, entry, 3);
        var gate = new TaskCompletionSource();
        tagger.Gate = gate.Task;
        var text = head + " So it ends.";
        source.Say(streamId, text);
        source.End(streamId);
        await TestWait.WhenPolled(() => tagger.Calls.Should().Be(2), TimeSpan.FromSeconds(30));

        // act: the entry is finalized with the text the live tagging saw, while its tail is still being tagged
        await tester.FinalizeStreamingEntry(streaming, text);

        // assert: the model's marks stay on the finalized entry, and the entry has no analysis of its own yet
        var marks = await WhenMarks(
            backend,
            entry,
            m => m.Count == 1 && m[0].Spans.Any(s => s is { Kind: SpeechSpanKind.Weak, Word: "awesome" }));
        (await backend.Get(entry.Id, default)).Should().BeNull();
        gate.SetResult();
        await WhenTagged(backend, entry.Id);
        tagger.Calls.Should().Be(2, "the final analysis reuses the live tagging");
    }

    [Fact]
    public async Task LiveTaggingSwitchedOffShouldWaitForTheFinalizedEntry()
    {
        // arrange
        var source = new FakeTranscriptSource();
        var (appHost, tagger) = await NewCoachHost(
            "coach-live-off", null, source, (nameof(CoachSettings.IsLiveTaggingEnabled), "false"));
        await using var _ = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        await OptIn(appHost, account);
        var streaming = await tester.CreateStreamingEntry(chatId, Language.Parse("en-US"));

        // act
        source.Say(streaming.ChatEntrySlim.ContentStreamId, LiveSentences(1, 4) + " So");
        await Task.Delay(700);

        // assert
        tagger.Calls.Should().Be(0);
    }
}

