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
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<SpeechTagResult?> Tag(SpeechTagRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            var spans = SpeechTagger.ParseResponse(request.Text, """
                {"items":[{"class":"filledPause","word":"um","occurrence":1,"synonyms":[]},
                          {"class":"filler","word":"you know","occurrence":1,"synonyms":[]},
                          {"class":"weak","word":"awesome","occurrence":1,"synonyms":["excellent"]}]}
                """);
            return Task.FromResult<SpeechTagResult?>(new SpeechTagResult(spans, 1));
        }
    }

    private async Task<(TestAppHost AppHost, FakeTagger Tagger)> NewCoachHost(string name)
    {
        var tagger = new FakeTagger();
        var coach = $"{nameof(ChatSettings)}:{nameof(ChatSettings.Coach)}";
        var appHost = await NewAppHost(name, options => options with {
            UseNatsQueues = false,
            ConfigureHost = (_, cfg) => cfg.AddInMemoryCollection(
                ($"{coach}:{nameof(CoachSettings.IsEnabled)}", "true"),
                ($"{coach}:{nameof(CoachSettings.ConversationMaturity)}", "00:00:01"),
                ($"{nameof(ChatSettings)}:{nameof(ChatSettings.IsTranslationEnabled)}", "true"),
                ($"{nameof(ChatSettings)}:{nameof(ChatSettings.UseFakeLanguageDetection)}", "true")),
            ConfigureServices = (_, services) => services.Replace(ServiceDescriptor.Singleton<ISpeechTagger>(tagger)),
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
        var entry = await PostVoice(tester, chatId, Text);

        // assert
        var pending = await TestWait.When(async ct => {
            var analysis = await backend.Get(entry.Id, ct);
            analysis.Should().NotBeNull();
            return analysis!;
        });
        pending.TagState.Should().Be(CoachTagState.Pending, "the immediate path is for opted-in users only");
        pending.Words.Should().Be(12);
        tagger.Calls.Should().Be(0);
        var tagged = await WhenTagged(backend, entry.Id);
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
        await bob.SignInAsUniqueBob();
        var (chatId, inviteId) = await bob.CreateChat(true);
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
            marks.Should().ContainSingle();
            return marks;
        }, TimeSpan.FromSeconds(30));
        var aliceMarks = await chatCoach.GetOwnMarks(alice.Session, chatId, lidRange, default);

        // assert
        bobMarks[0].EntryLid.Should().Be(entry.LocalId);
        bobMarks[0].Spans.Select(s => s.Kind).Should().Contain(SpeechSpanKind.Filler);
        aliceMarks.Should().BeEmpty("marks are private to their author");
        (await chatCoach.IsEnabled(bob.Session, default)).Should().BeTrue();
    }
}
