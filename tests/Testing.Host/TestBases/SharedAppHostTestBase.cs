using ActualChat.Queues;

namespace ActualChat.Testing.Host;

public abstract class SharedAppHostTestBase<TAppHostFixture>(
    TAppHostFixture fixture,
    ITestOutputHelper @out,
    ILogger? log = null
) : TestBase(@out, log)
    where TAppHostFixture : AppHostFixture
{
    private ITestOutputHelper? _originalAppHostOutput;

    protected TAppHostFixture Fixture { get; } = fixture;
    protected TestAppHost AppHost { get; } = fixture.AppHost;
    protected ICommander Commander { get; } = fixture.AppHost.Services.Commander();
    protected MomentClockSet Clocks { get; } = fixture.AppHost.Services.Clocks();

    protected override Task InitializeAsync()
    {
        _originalAppHostOutput = AppHost.Output;
        AppHost.Output = Out;
        return base.InitializeAsync();
    }

    protected override async Task DisposeAsync()
    {
        await AppHost.Services.Queues().Purge().ConfigureAwait(false);
        AppHost.Output = _originalAppHostOutput;
        await base.DisposeAsync();
    }

    // Just a shortcut
    protected virtual async Task<TestAppHost> NewAppHost(
        string instanceName,
        Func<TestAppHostOptions, TestAppHostOptions>? optionOverrider = null)
    {
        var appHost = await Fixture.NewAppHost(options => {
            options = options with { Output = Out };
            options = options with { InstanceName = instanceName };
            options = optionOverrider?.Invoke(options) ?? options;
            return options;
        });
        return appHost;
    }
}
