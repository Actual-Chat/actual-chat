using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.Hosting;

namespace ActualChat.UI.Blazor.UnitTests;

public class PermissionHandlerTest : TestBase
{
    private IServiceProvider Services { get; }

    public PermissionHandlerTest(ITestOutputHelper @out) : base(@out)
    {
        var hostInfo = new HostInfo {
            HostKind = HostKind.MauiApp,
            AppKind = AppKind.Android,
            Environment = Environments.Development,
            BaseUrl = $"https://{Constants.Hosts.LocalVoxt}",
            IsTested = true,
        };
        Services = new ServiceCollection()
            .AddTestLogging(Out)
            .AddSingleton(_ => hostInfo)
            .AddSingleton(c => new Features(c))
            .AddSingleton(_ => new UrlMapper(hostInfo))
            .AddScoped<UIHub>()
            .AddScoped<IDispatcherResolver>(c => c.GetRequiredService<UIHub>())
            .AddScoped<TestPermissionHandler>()
            .AddFusion(fusion => fusion.AddBlazor())
            .BuildServiceProvider();
    }

    [Fact]
    public void CanPromptIsFalseWhenNothingIsRendered()
    {
        // arrange
        var handler = NewHandler();

        // act & assert
        handler.CanPrompt.Should().BeFalse();
    }

    [Fact]
    public async Task CheckAnswersWithoutADispatcher()
    {
        // arrange
        var handler = NewHandler();
        handler.GetResult = true;

        // act
        var isGranted = await handler.Check(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        // assert
        isGranted.Should().BeTrue();
        handler.GetCount.Should().Be(1);
        handler.RequestCount.Should().Be(0);
        handler.TroubleshootCount.Should().Be(0);
        handler.Cached.Value.Should().BeTrue();
    }

    [Fact]
    public async Task CheckFailsClosedWithoutADispatcher()
    {
        // arrange
        var handler = NewHandler();
        handler.GetResult = false;

        // act
        var isGranted = await handler.Check(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        // assert
        isGranted.Should().BeFalse();
        handler.RequestCount.Should().Be(0);
        handler.TroubleshootCount.Should().Be(0);
    }

    [Fact]
    public async Task CheckUsesTheCachedGrant()
    {
        // arrange
        var handler = NewHandler();
        handler.GetResult = true;

        // act
        await handler.Check(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        await handler.Check(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        // assert
        handler.GetCount.Should().Be(1);
    }

    [Fact]
    public async Task CheckOrRequestStillWaitsForTheDispatcher()
    {
        // arrange
        var handler = NewHandler();
        handler.GetResult = true;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(0.5));

        // act
        var check = async () => await handler.CheckOrRequest(cts.Token);

        // assert
        await check.Should().ThrowAsync<OperationCanceledException>();
        handler.GetCount.Should().Be(0);
    }

    // Private methods

    private TestPermissionHandler NewHandler()
        => Services.CreateScope().ServiceProvider.GetRequiredService<TestPermissionHandler>();
}

public sealed class TestPermissionHandler(UIHub hub) : PermissionHandler(hub, false)
{
    private int _getCount;
    private int _requestCount;
    private int _troubleshootCount;

    public bool? GetResult { get; set; }
    public bool RequestResult { get; set; }
    public int GetCount => Volatile.Read(ref _getCount);
    public int RequestCount => Volatile.Read(ref _requestCount);
    public int TroubleshootCount => Volatile.Read(ref _troubleshootCount);

    protected override Task<bool?> Get(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _getCount);
        return Task.FromResult(GetResult);
    }

    protected override Task<bool> Request(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requestCount);
        return Task.FromResult(RequestResult);
    }

    protected override Task Troubleshoot(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _troubleshootCount);
        return Task.CompletedTask;
    }
}
