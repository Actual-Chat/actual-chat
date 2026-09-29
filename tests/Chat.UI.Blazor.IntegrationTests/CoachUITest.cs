using ActualChat.Chat.ML;
using ActualChat.Chat.Module;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Components.MarkupParts;
using ActualLab.Fusion.Blazor;
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
            cut.FindAll(".coach-filler").Should().ContainSingle().Which.TextContent.Trim().Should().Be("um,");
            cut.FindAll(".coach-weak").Select(e => e.TextContent.Trim()).Should().BeEquivalentTo(["the", "awesome."],
                "the repeated word and the weak word get the dotted underline");
        }, TimeSpan.FromSeconds(10));
        var weak = cut.FindAll(".coach-weak").Single(e => e.TextContent.Trim() == "awesome.");
        weak.GetAttribute("data-menu").Should().NotBeNullOrEmpty("a tap on a marked word opens its hint");
        weak.GetAttribute("data-menu-trigger").Should().Be("Primary");
        cut.FindAll("[data-menu]").Count.Should().Be(3,
            "only marked words get a hint; the others keep tap-to-play");
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
        var view = (IStatefulComponent<PlayableTextMarkupView.Model>)cut.FindComponent<PlayableTextMarkupView>().Instance;
        cut.WaitForAssertion(() => view.State.Snapshot.UpdateCount.Should().BePositive("the view must have computed once"));

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
        inTipsChat.WaitForAssertion(() => inTipsChat.FindAll(".banner.coach-tip-bar").Should().ContainSingle());
        inTipsChat.Find(".banner.coach-tip-bar .c-tip-title").TextContent.Should().Be("Avoid filler words");
        inTipsChat.Find(".banner.coach-tip-bar .c-tip-body").TextContent.Should().Contain("um").And.Contain("10");
        inTipsChat.Find(".banner.coach-tip-bar .c-tip-body .c-word").TextContent.Should().Be("um",
            "the word is highlighted inside the sentence, whatever the language puts around it");
        var otherBar = (IStatefulComponent<UserCoachTip?>)inOtherChat.Instance;
        inOtherChat.WaitForAssertion(() => otherBar.State.Snapshot.UpdateCount.Should().BePositive());
        inOtherChat.FindAll(".coach-tip-bar").Should().BeEmpty("the tip belongs to another chat, and nothing reserves space");

        // act - live tips off hides it, on brings it back
        await hub.UserSettingsUI.UserCoachSettings().Update(x => x with { AreLiveTipsEnabled = false });
        inTipsChat.WaitForAssertion(() => inTipsChat.FindAll(".banner.coach-tip-bar").Should().BeEmpty(),
            TimeSpan.FromSeconds(10));
        await hub.UserSettingsUI.UserCoachSettings().Update(x => x with { AreLiveTipsEnabled = true });
        inTipsChat.WaitForAssertion(() => inTipsChat.FindAll(".banner.coach-tip-bar").Should().ContainSingle(),
            TimeSpan.FromSeconds(10));

        // act - dismiss
        await inTipsChat.InvokeAsync(() => inTipsChat.Find(".banner.coach-tip-bar .c-tip-close").Click());

        // assert
        await TestWait.When(async ct => (await hub.Coach.GetPendingTip(tester.Session, ct)).Should().BeNull());
        inTipsChat.WaitForAssertion(() => inTipsChat.FindAll(".banner.coach-tip-bar").Should().BeEmpty());
    }

    [Fact(Timeout = 60_000)]
    public async Task TipBarShouldCountDownAndDismissItself()
    {
        // arrange
        var appHost = await NewCoachHost("coach-ui-tip-autodismiss");
        await using var _1 = appHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        tester.JSInterop.Mode = JSRuntimeMode.Loose;
        var (chatId, _) = await tester.CreateChat(true);
        await OptIn(tester);
        var hub = tester.ScopedAppServices.AppUIHub();
        var kvas = appHost.Services.GetRequiredService<IServerKvasBackend>().ForUser(account.Id, isOutermost: true);
        await kvas.UserCoachTip().Set(new UserCoachTip {
            Kind = CoachTipKind.SpeedUp, ChatId = chatId, EntryLid = 1, Wpm = 80, PaceSlowWpm = 110, PaceFastWpm = 160,
            ShownAt = appHost.Services.Clocks().SystemClock.Now,
        });
        await TestWait.When(async ct => (await hub.Coach.GetPendingTip(tester.Session, ct)).Should().NotBeNull());

        // act
        var cut = tester.Render<CoachTipBar>(p => p.Add(x => x.ChatId, chatId).Add(x => x.AutoDismissDelay, 1));
        InitializeHub(tester, hub, cut.Instance);

        // assert
        cut.WaitForAssertion(() => cut.Find(".banner.coach-tip-bar .btn-timer").Should().NotBeNull(
            "the close button carries the countdown ring, like a toast"));
        await TestWait.When(async ct => (await hub.Coach.GetPendingTip(tester.Session, ct)).Should().BeNull(),
            TimeSpan.FromSeconds(10));
        cut.WaitForAssertion(() => cut.FindAll(".banner.coach-tip-bar").Should().BeEmpty());
    }

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
        await kvas.ChatUserSettings(chatId).Set(new ChatUserSettings { IsCoachingEnabled = false });
        var coach = tester.ScopedAppServices.AppUIHub().Coach;

        var (otherChatId, _) = await tester.CreateChat(true);

        // act
        await PostVoice(tester, chatId, Text);
        await PostVoice(tester, otherChatId, Text);

        // assert
        var summary = await TestWait.When(async ct => {
            var x = await coach.GetOwnSummary(tester.Session, CoachWindow.AllTime, null, ct);
            x.Entries.Should().BeGreaterThan(0, "the chat left switched on is analysed");
            return x;
        }, TimeSpan.FromSeconds(30));
        summary.Entries.Should().Be(1, "the switched-off chat is skipped");
    }

    [Fact(Timeout = 60_000)]
    public async Task RecentTabShouldShowOneCardPerConversationWithFindings()
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

    [Fact(Timeout = 60_000)]
    public async Task ProgressTabShouldShowDeltasDaysAndMilestones()
    {
        // arrange
        var appHost = await NewCoachHost("coach-ui-progress");
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
        cut.WaitForAssertion(() => cut.Find(".coach-conversation"), TimeSpan.FromSeconds(30));
        hub.CoachUI.SelectTab(CoachTab.Progress);

        // assert
        cut.WaitForAssertion(() => {
            cut.FindAll(".coach-progress .coach-days .c-day.on").Count.Should().BeGreaterThan(0);
            cut.FindAll(".coach-milestones .card-item").Count.Should().Be(7);
            cut.Find(".coach-week-deltas").TextContent.Should().Contain("not enough speech");
        }, TimeSpan.FromSeconds(30));
    }

    [Fact(Timeout = 60_000)]
    public async Task SkillsTabShouldGroupHeadlineAndConversationSkillsAndOpenOccurrences()
    {
        // arrange
        var appHost = await NewCoachHost("coach-ui-skills");
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
        cut.WaitForAssertion(() => cut.Find(".coach-conversation"), TimeSpan.FromSeconds(30));
        hub.CoachUI.SelectTab(CoachTab.Skills);

        // assert
        cut.WaitForAssertion(() => {
            cut.FindAll(".coach-skills .c-headline .coach-skill").Count.Should().Be(4);
            cut.Find(".coach-skills .c-conversation").Should().NotBeNull();
            cut.FindAll(".coach-skills .c-headline .coach-chip").Count.Should().BeGreaterThan(0, "the marked words show as chips");
        }, TimeSpan.FromSeconds(30));
        await cut.InvokeAsync(() => cut.Find(".coach-skills .c-headline .coach-chip").Click());
        cut.WaitForAssertion(() => cut.Find(".coach-occurrences"), TimeSpan.FromSeconds(10));
    }

    [Fact(Timeout = 60_000)]
    public async Task SettingsPageShouldSwitchSkipPeersAndChatsAndOfferLanguages()
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
        cut.WaitForAssertion(() => cut.Find(".coach-header .c-settings-btn"), TimeSpan.FromSeconds(30));
        await cut.InvokeAsync(() => cut.Find(".coach-header .c-settings-btn").Click());
        cut.WaitForAssertion(() => cut.Find(".coach-settings-page"));
        await cut.InvokeAsync(() => cut.Find(".coach-settings-page .c-skip-peers").Click());
        await hub.UICommander.Run(new Coach_SetChatCoaching { Session = hub.Session, ChatId = chatId, IsEnabled = false });

        // assert
        await TestWait.When(async ct => (await kvas.UserCoachSettings().Get(ct)).SkipPeerChats.Should().BeTrue());
        cut.WaitForAssertion(() => cut.Find(".coach-settings-page .c-switched-off").TextContent.Should().Contain("Switch on"));
        cut.Find(".coach-settings-page .c-languages-manage").TextContent.Should().Contain("Manage");
        cut.FindAll(".coach-settings-page .c-language").Count.Should().BeGreaterThan(0);
    }
}
