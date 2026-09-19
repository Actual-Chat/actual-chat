using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class AppReviewUITest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData(HostKind.MauiApp, AppKind.Ios, true)]
    [InlineData(HostKind.MauiApp, AppKind.Android, true)]
    [InlineData(HostKind.MauiApp, AppKind.Windows, true)]
    [InlineData(HostKind.MauiApp, AppKind.MacOS, true)]
    [InlineData(HostKind.WasmApp, AppKind.Wasm, false)]
    [InlineData(HostKind.Server, AppKind.Unknown, false)]
    public void IsAvailableShouldRequireStoreDistributedApp(HostKind hostKind, AppKind appKind, bool isExpected)
    {
        // arrange
        var (appReviewUI, _) = NewAppReviewUI(hostKind, appKind, reviewer: null);

        // act & assert
        appReviewUI.IsAvailable.Should().Be(isExpected);
    }

    [Fact]
    public async Task LeaveReviewShouldOpenStoreLinkWithoutReviewer()
    {
        // arrange
        var (appReviewUI, urlOpener) = NewAppReviewUI(HostKind.MauiApp, AppKind.Ios, reviewer: null);

        // act
        var outcome = await appReviewUI.LeaveReview();

        // assert
        outcome.Should().Be(AppReviewOutcome.Requested);
        urlOpener.OpenedUrls.Should().Equal(Links.Apps.iOSReview);
    }

    [Fact]
    public async Task LeaveReviewShouldOpenStoreLinkWhenReviewerFails()
    {
        // arrange
        var reviewer = new TestAppReviewer(AppReviewOutcome.Failed);
        var (appReviewUI, urlOpener) = NewAppReviewUI(HostKind.MauiApp, AppKind.Android, reviewer);

        // act
        var outcome = await appReviewUI.LeaveReview();

        // assert
        outcome.Should().Be(AppReviewOutcome.Requested);
        urlOpener.OpenedUrls.Should().Equal(Links.Apps.AndroidReview);
    }

    [Fact]
    public async Task LeaveReviewShouldOpenStoreLinkWhenReviewerThrows()
    {
        // arrange
        var reviewer = new TestAppReviewer(new InvalidOperationException("Not installed from the store"));
        var (appReviewUI, urlOpener) = NewAppReviewUI(HostKind.MauiApp, AppKind.Windows, reviewer);

        // act
        var outcome = await appReviewUI.LeaveReview();

        // assert
        outcome.Should().Be(AppReviewOutcome.Requested);
        urlOpener.OpenedUrls.Should().Equal(Links.Apps.WindowsAppReview);
    }

    [Theory]
    [InlineData(AppReviewOutcome.Requested)]
    [InlineData(AppReviewOutcome.Completed)]
    [InlineData(AppReviewOutcome.Cancelled)]
    public async Task LeaveReviewShouldPassNativeOutcomeThroughWithoutLink(AppReviewOutcome nativeOutcome)
    {
        // arrange
        var reviewer = new TestAppReviewer(nativeOutcome);
        var (appReviewUI, urlOpener) = NewAppReviewUI(HostKind.MauiApp, AppKind.Windows, reviewer);

        // act
        var outcome = await appReviewUI.LeaveReview();

        // assert
        outcome.Should().Be(nativeOutcome);
        urlOpener.OpenedUrls.Should().BeEmpty("the native flow handled it");
    }

    [Fact]
    public async Task LeaveReviewShouldFailOnWebWithoutOpeningAnything()
    {
        // arrange
        var (appReviewUI, urlOpener) = NewAppReviewUI(HostKind.WasmApp, AppKind.Wasm, reviewer: null);

        // act
        var outcome = await appReviewUI.LeaveReview();

        // assert
        outcome.Should().Be(AppReviewOutcome.Failed);
        urlOpener.OpenedUrls.Should().BeEmpty("a web host has no store to send the user to");
    }

    // Private methods

    private (AppReviewUI, TestUrlOpener) NewAppReviewUI(HostKind hostKind, AppKind appKind, IAppReviewer? reviewer)
    {
        var hostInfo = new HostInfo {
            HostKind = hostKind,
            AppKind = appKind,
            Environment = Environments.Development,
            BaseUrl = $"https://{Constants.Hosts.LocalVoxt}",
            IsTested = true,
        };
        var services = new ServiceCollection()
            .AddTestLogging(Out)
            .AddSingleton(_ => hostInfo)
            .AddSingleton(c => new Features(c))
            .AddSingleton(_ => new UrlMapper(hostInfo))
            .AddFusion(fusion => fusion.AddBlazor())
            .AddScoped<UIHub>()
            .AddScoped<ExternalUrlOpener>(c => new TestUrlOpener(c.GetRequiredService<UIHub>()))
            .AddScoped(c => new AppReviewUI(c.GetRequiredService<UIHub>()));
        if (reviewer is not null)
            services.AddScoped(_ => reviewer);
        var scopedServices = services.BuildServiceProvider().CreateScope().ServiceProvider;
        return (
            scopedServices.GetRequiredService<AppReviewUI>(),
            (TestUrlOpener)scopedServices.GetRequiredService<ExternalUrlOpener>());
    }

    // Nested types

    private sealed class TestUrlOpener(UIHub hub) : ExternalUrlOpener(hub)
    {
        public List<string> OpenedUrls { get; } = new();

        public override Task Open(string url)
        {
            OpenedUrls.Add(url);
            return Task.CompletedTask;
        }
    }

    private sealed class TestAppReviewer : IAppReviewer
    {
        private readonly AppReviewOutcome _outcome;
        private readonly Exception? _error;

        public TestAppReviewer(AppReviewOutcome outcome)
            => _outcome = outcome;

        public TestAppReviewer(Exception error)
            => _error = error;

        public Task<AppReviewOutcome> RequestReview(CancellationToken cancellationToken)
            => _error is null ? Task.FromResult(_outcome) : Task.FromException<AppReviewOutcome>(_error);
    }
}
