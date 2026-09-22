using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using Bunit;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
using ActualLab.Fusion.Blazor;
using Microsoft.Extensions.Localization;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

// The client half of the review prompt: the server's pending prompt gated by the device facts
// (foreground, no open modal), the real modal, and the outcome that clears the prompt.

[Collection(nameof(ChatUICollection))]
public sealed class AppReviewPromptUITest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    [Fact(Timeout = 60_000)]
    public async Task PendingPromptShouldBeShowableOnlyInTheForegroundWithNoModalOpen()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var hub = tester.ScopedAppServices.AppUIHub();
        var promptUI = hub.AppReviewPromptUI;
        var browserInfo = tester.ScopedAppServices.GetRequiredService<BrowserInfo>();
        var chatId = ChatId.Parse("testchatid1234567890");
        var host = RenderModalHost(tester, hub);

        // act
        var beforePending = await promptUI.GetShowablePrompt(default);
        await MarkPending(account.Id, chatId);

        // assert
        beforePending.Should().BeNull();
        var showable = await TestWait.When(async ct => {
            var p = await promptUI.GetShowablePrompt(ct);
            p.Should().NotBeNull();
            return p!;
        });
        showable.ChatId.Should().Be(chatId);

        // act - background hides it, foreground brings it back
        browserInfo.OnIsVisibleChanged(false);
        await TestWait.When(async ct => (await promptUI.GetShowablePrompt(ct)).Should().BeNull());
        browserInfo.OnIsVisibleChanged(true);
        await TestWait.When(async ct => (await promptUI.GetShowablePrompt(ct)).Should().NotBeNull());

        // act - an open modal hides it too, without consuming it
        var otherModal = await hub.ModalUI.Show(new AppReviewModal.Model());
        await TestWait.When(async ct => (await promptUI.GetShowablePrompt(ct)).Should().BeNull());
        await host.InvokeAsync(() => otherModal.Close(true));
        await TestWait.When(async ct => (await promptUI.GetShowablePrompt(ct)).Should().NotBeNull());
    }

    [Fact(Timeout = 60_000)]
    public async Task DecliningTheModalShouldRecordTheOutcomeAndClearThePrompt()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var account = await tester.SignInAsUniqueBob();
        var hub = tester.ScopedAppServices.AppUIHub();
        var usage = hub.Usage;
        var host = RenderModalHost(tester, hub);
        await MarkPending(account.Id, ChatId.Parse("testchatid1234567890"));
        var outcomes = new List<ReviewPromptOutcome>();

        // act - the worker would do exactly this once GetShowablePrompt turns non-null
        var modal = await hub.ModalUI.Show(new AppReviewModal.Model(outcomes.Add));
        Click(tester, host, "AppReview_MaybeLater");
        Click(tester, host, "AppReview_NotRightNow");
        Click(tester, host, "Common_Close");
        await modal.WhenClosed.WaitAsync(TimeSpan.FromSeconds(10));
        // The outcome is reported from the modal's Dispose, which runs on the host's next render
        host.WaitForAssertion(() => outcomes.Should().ContainSingle(), TimeSpan.FromSeconds(10));
        var outcome = outcomes.Single();
        await tester.Commander.Call(new Usage_RecordReviewPrompt { Session = tester.Session, Outcome = outcome });

        // assert
        outcome.Should().Be(ReviewPromptOutcome.Declined);
        var history = await usage.GetOwnReviewPromptHistory(tester.Session, default);
        history.DeclineCount.Should().Be(1);
        history.PendingSince.Should().BeNull();
        (await usage.GetPendingReviewPrompt(tester.Session, default)).Should().BeNull();
    }

    private static IRenderedComponent<ModalHost> RenderModalHost(BlazorTester tester, AppUIHub hub)
    {
        // What AppBase does for the real app: the hub needs a root component's dispatcher before
        // ModalUI.Show can schedule anything, and ModalHost talks to JS on every render
        tester.JSInterop.Mode = JSRuntimeMode.Loose;
        tester.Renderer.SetRendererInfo(new RendererInfo("Server", true));
        var host = tester.Render<ModalHost>();
        hub.Initialize(host.Instance, RenderModeDef.GetOrDefault(""));
        return host;
    }

    private async Task MarkPending(UserId userId, ChatId chatId)
    {
        var kvas = AppHost.Services.GetRequiredService<IServerKvasBackend>().ForUser(userId).AppReviewPromptState();
        var history = await kvas.Get();
        await kvas.Set(ReviewPromptPolicy.MarkPending(history, chatId, Clocks.SystemClock.Now));
    }

    private static void Click(BlazorTester tester, IRenderedComponent<ModalHost> host, string localizedKey)
    {
        var text = tester.ScopedAppServices.GetRequiredService<IStringLocalizer>()[localizedKey].Value;
        host.WaitForAssertion(() => host.FindAll("button").Should().Contain(b => b.TextContent.Trim() == text));
        host.FindAll("button").First(b => b.TextContent.Trim() == text).Click();
    }
}
