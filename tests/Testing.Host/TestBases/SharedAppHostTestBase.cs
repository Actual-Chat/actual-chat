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

    protected override Task InitializeAsync()
    {
        _originalAppHostOutput = AppHost.Output;
        AppHost.Output = Out;
        return base.InitializeAsync();
    }

    protected override Task DisposeAsync()
    {
        AppHost.Output = _originalAppHostOutput;
        return base.DisposeAsync();
    }

    // Just a shortcut
    protected virtual async Task<TestAppHost> NewAppHost(
        Func<TestAppHostOptions, TestAppHostOptions>? optionOverrider = null)
    {
        var appHost = await Fixture.NewAppHost(options => {
            options = options with { Output = Out };
            options = optionOverrider?.Invoke(options) ?? options;
            return options;
        });
        return appHost;
    }
}
