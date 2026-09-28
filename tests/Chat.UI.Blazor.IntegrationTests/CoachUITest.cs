using ActualChat.Chat.ML;
using ActualChat.Chat.Module;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Components.MarkupParts;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using ActualChat.Kvas;
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
            ConfigureServices = (_, services) => {
                services.Replace(ServiceDescriptor.Singleton<ISpeechTagger>(new FakeTagger()));
                // Components rendered through BlazorTester call JS from OnAfterRender; the server's
                // remote runtime refuses that, a loose bUnit one answers with defaults
                services.Replace(ServiceDescriptor.Scoped<IJSRuntime>(
                    _ => new BunitJSInterop { Mode = JSRuntimeMode.Loose }.JSRuntime));
            },
        });
    }

    private static async Task<ChatEntry> PostVoice(IWebTester tester, ChatId chatId, string text)
    {
        var streaming = await tester.CreateStreamingEntry(chatId, Language.Parse("en-US"));
        var timeMap = new LinearMap(0, 0, text.Length, 10);
        return (await tester.FinalizeStreamingEntry(streaming, text, timeMap)).ChatEntrySlim;
    }

    // What AppBase does for the real app: the hub needs a root component's dispatcher before
    // anything that schedules on it (panel state, navigation, modals) can run
    private static void InitializeHub(BlazorTester tester, AppUIHub hub, ComponentBase root)
    {
        tester.Renderer.SetRendererInfo(new RendererInfo("Server", true));
        hub.Initialize(root, RenderModeDef.GetOrDefault(""));
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
            m.Select(s => s.Kind).Should().Contain(SpeechSpanKind.FilledPause, "the code spans land before the tagger's");
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
        var cut = tester.Render<RightPanelModeSwitch>();
        InitializeHub(tester, hub, cut.Instance);

        // act
        await cut.InvokeAsync(() => cut.FindAll(".btn-mode")[1].Click());

        // assert
        await TestWait.When(_ => {
            hub.PanelsUI.Right.Mode.Value.Should().Be(RightPanelMode.Coach);
            return Task.CompletedTask;
        });
        await TestWait.When(_ => {
            stored.Mode.Should().Be(RightPanelMode.Coach, "the mode survives a chat switch and the next session");
            return Task.CompletedTask;
        });
        cut.WaitForAssertion(() => cut.Find(".btn-mode.on").TextContent.Trim().Should().Be("Coach"));

        // act - the mobile menu entry opens the panel in Coach mode
        await cut.InvokeAsync(() => hub.PanelsUI.Right.Open(RightPanelMode.Coach));

        // assert
        await TestWait.When(_ => {
            hub.PanelsUI.Right.IsVisible.Value.Should().BeTrue();
            return Task.CompletedTask;
        });
    }

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
        var hub = tester.ScopedAppServices.AppUIHub();

        // act
        var cut = tester.Render<CoachPanel>();
        InitializeHub(tester, hub, cut.Instance);

        // assert
        cut.WaitForAssertion(() => cut.FindAll(".coach-metric-row").Count.Should().Be(13));
        cut.Find(".coach-score-card").TextContent.Should().Contain("Speak at least",
            "no score below the word floor");

        // act - one voice message lands in Today
        await PostVoice(tester, chatId, Text);
        await TestWait.When(async ct => (await hub.Coach.GetOwnSummary(tester.Session, CoachWindow.Today, ct))
            .Entries.Should().Be(1), TimeSpan.FromSeconds(30));

        // assert
        cut.WaitForAssertion(() => cut.Find(".coach-metric-row[data-metric=Pace]").TextContent
            .Should().Contain("wpm"), TimeSpan.FromSeconds(10));
        cut.WaitForAssertion(() => cut.Find(".coach-metric-row[data-metric=Fillers] .coach-chip").TextContent
            .Should().Contain("um"), TimeSpan.FromSeconds(10));

        // act - the Coaching toggle writes the setting
        await cut.InvokeAsync(() => cut.Find(".coach-settings-coaching input").Change(true));

        // assert
        await TestWait.When(async ct => (await hub.UserSettingsUI.UserCoachSettings().Get(ct))
            .IsCoachingEnabled.Should().BeTrue());
    }

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
        var cut = tester.Render<CoachOccurrences>(p => p
            .Add(x => x.Word, "um")
            .Add(x => x.Window, CoachWindow.Today));
        InitializeHub(tester, hub, cut.Instance);
        hub.ChatUI.SelectChatOnNavigation(chatId);
        cut.WaitForAssertion(() => cut.FindAll(".coach-occurrence").Should().ContainSingle());

        // act
        await cut.InvokeAsync(() => cut.Find(".coach-occurrence").Click());

        // assert
        var replay = await TestWait.When(_ => {
            hub.ChatAudioUI.ReplayState.Value.Should().NotBeNull();
            return Task.FromResult(hub.ChatAudioUI.ReplayState.Value!);
        });
        replay.ChatId.Should().Be(chatId);
        var wordStart = entry.Audio!.TimeMap.TryMap(entry.Content.IndexOf("um"));
        wordStart.Should().NotBeNull();
        var expectedStartAt = entry.BeginsAt + TimeSpan.FromSeconds(wordStart!.Value - 0.25);
        (replay.StartAt - expectedStartAt).Duration().Should().BeLessThan(TimeSpan.FromMilliseconds(50));
    }

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
        InitializeHub(tester, hub, inTipsChat.Instance);
        var inOtherChat = tester.Render<CoachTipBar>(p => p.Add(x => x.ChatId, otherChatId));

        // assert
        inTipsChat.WaitForAssertion(() => inTipsChat.FindAll(".coach-tip-bar .banner").Should().ContainSingle());
        inTipsChat.Find(".coach-tip-bar .banner").TextContent.Should().Contain("um");
        await Task.Delay(300);
        inOtherChat.FindAll(".coach-tip-bar .banner").Should().BeEmpty("the tip belongs to another chat");

        // act - dismiss
        await inTipsChat.InvokeAsync(() => inTipsChat.Find(".coach-tip-bar .close-banner").Click());

        // assert
        await TestWait.When(async ct => (await hub.Coach.GetPendingTip(tester.Session, ct)).Should().BeNull());
        inTipsChat.WaitForAssertion(() => inTipsChat.FindAll(".coach-tip-bar .banner").Should().BeEmpty());
    }
}
