using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
using Bunit;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class AttentionUITest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    // Long enough for a wrongly started step to show - the negative checks below rely on it
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(1);

    [Fact(Timeout = 60_000)]
    public async Task HoldShouldDelayOnboardingUntilDisposed()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (hub, _) = await Start(tester);
        var attentionUI = hub.AttentionUI;

        // act
        var hold = attentionUI.Hold("test");
        await attentionUI.WhenSettled.WaitAsync(TestWait.DefaultTimeout);
        await Task.Delay(GracePeriod);
        var isShownWhileHeld = await IsOnboardingShown(hub, default);
        hold.Dispose();

        // assert
        isShownWhileHeld.Should().BeFalse();
        await TestWait.When(async ct => (await IsOnboardingShown(hub, ct)).Should().BeTrue());
        attentionUI.Stage.Value.Should().Be(AttentionStage.Onboarding);
    }

    [Fact(Timeout = 60_000)]
    public async Task BubblesShouldWaitForOnboardingToClose()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (hub, host) = await Start(tester);
        var attentionUI = hub.AttentionUI;
        await TestWait.When(async ct => (await IsOnboardingShown(hub, ct)).Should().BeTrue());

        // act
        var isAvailableDuringOnboarding = await attentionUI.IsAvailableFor(AttentionKind.Bubbles, default);
        var onboarding = hub.ModalUI.ActiveModals.Value.Single();
        await host.InvokeAsync(() => onboarding.Close(true));

        // assert
        isAvailableDuringOnboarding.Should().BeFalse();
        await TestWait.When(async ct
            => (await attentionUI.IsAvailableFor(AttentionKind.Bubbles, ct)).Should().BeTrue());
    }

    [Fact(Timeout = 60_000)]
    public async Task ReviewShouldWaitForHoldsAndModalsButNotForBubbles()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (hub, host) = await Start(tester, AttentionKind.Onboarding);
        var attentionUI = hub.AttentionUI;
        await WhenDone(attentionUI);

        // act
        var isAvailableWhenDone = await attentionUI.IsAvailableFor(AttentionKind.AppReview, default);
        var hold = attentionUI.Hold("test");
        await TestWait.When(async ct
            => (await attentionUI.IsAvailableFor(AttentionKind.AppReview, ct)).Should().BeFalse());
        hold.Dispose();
        var modal = await hub.ModalUI.Show(new AppReviewModal.Model());

        // assert
        isAvailableWhenDone.Should().BeTrue();
        await TestWait.When(async ct => {
            (await attentionUI.IsAvailableFor(AttentionKind.AppReview, ct)).Should().BeFalse();
            (await attentionUI.IsAvailableFor(AttentionKind.Bubbles, ct)).Should().BeTrue();
        });
        await host.InvokeAsync(() => modal.Close(true));
        await TestWait.When(async ct
            => (await attentionUI.IsAvailableFor(AttentionKind.AppReview, ct)).Should().BeTrue());
    }

    [Fact(Timeout = 60_000)]
    public async Task HoldShouldPauseBubblesAfterTheFlowIsDone()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (hub, _) = await Start(tester, AttentionKind.Onboarding);
        var attentionUI = hub.AttentionUI;
        await WhenDone(attentionUI);

        // act
        var hold = attentionUI.Hold("test");

        // assert
        await TestWait.When(async ct
            => (await attentionUI.IsAvailableFor(AttentionKind.Bubbles, ct)).Should().BeFalse());
        hold.Dispose();
        await TestWait.When(async ct
            => (await attentionUI.IsAvailableFor(AttentionKind.Bubbles, ct)).Should().BeTrue());
    }

    [Fact(Timeout = 60_000)]
    public async Task SignInShouldRestartTheFlowForTheAccount()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (hub, _) = await Start(tester, mustSignIn: false);
        var attentionUI = hub.AttentionUI;
        await WhenDone(attentionUI);
        await Task.Delay(GracePeriod);
        var isOnboardingShownToGuest = await IsOnboardingShown(hub, default);

        // act
        await tester.SignInAsUniqueBob();

        // assert
        isOnboardingShownToGuest.Should().BeFalse();
        await TestWait.When(async ct => (await IsOnboardingShown(hub, ct)).Should().BeTrue());
    }

    [Fact(Timeout = 60_000)]
    public async Task SuppressShouldCloseOnboardingAndLetTheFlowFinish()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (hub, _) = await Start(tester);
        var attentionUI = hub.AttentionUI;
        await TestWait.When(async ct => (await IsOnboardingShown(hub, ct)).Should().BeTrue());
        var onboarding = hub.ModalUI.ActiveModals.Value.Single();

        // act
        attentionUI.Suppress(AttentionKind.Onboarding);

        // assert
        await onboarding.WhenClosed.WaitAsync(TestWait.DefaultTimeout);
        await WhenDone(attentionUI);
    }

    [Fact(Timeout = 60_000)]
    public async Task NestedSuppressShouldLastUntilTheOuterOneIsDisposed()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var attentionUI = tester.ScopedAppServices.AppUIHub().AttentionUI;
        var outer = attentionUI.Suppress(AttentionKind.Onboarding | AttentionKind.Bubbles);

        // act
        attentionUI.Suppress(AttentionKind.Onboarding).Dispose();

        // assert
        attentionUI.Suppressed.Value.Should().Be(AttentionKind.Onboarding | AttentionKind.Bubbles);
        outer.Dispose();
        attentionUI.Suppressed.Value.Should().Be(AttentionKind.None);
    }

    // Private methods

    private static async Task<(AppUIHub Hub, IRenderedComponent<ModalHost> Host)> Start(
        BlazorTester tester, AttentionKind suppressed = AttentionKind.None, bool mustSignIn = true)
    {
        if (mustSignIn)
            await tester.SignInAsUniqueBob();
        var hub = tester.ScopedAppServices.AppUIHub();
        hub.AttentionUI.Suppress(suppressed);
        // Checking the real permissions needs JS, which bUnit's static render doesn't have
        var onboardingUI = (OnboardingUI)hub.OnboardingUI;
        onboardingUI.UpdateLocalSettings(onboardingUI.LocalSettings.Value with { IsPermissionsStepCompleted = true });
        var host = tester.RenderModalHost(hub);
        hub.AttentionUI.Start();
        return (hub, host);
    }

    private static Task WhenDone(AttentionUI attentionUI)
        => TestWait.When(async ct => (await attentionUI.Stage.Use(ct)).Should().Be(AttentionStage.Done));

    private static async Task<bool> IsOnboardingShown(AppUIHub hub, CancellationToken cancellationToken)
        => (await hub.ModalUI.ActiveModals.Use(cancellationToken)).Any(x => x.Model is OnboardingModal.Model);
}
