using ActualChat.Testing.Host.Assertion;
using ActualChat.Testing.Internal;

namespace ActualChat.Testing.Host;

public abstract class SharedDbLocalAppHostTestBase<TAppHostFixture>(
    TAppHostFixture fixture,
    ITestOutputHelper @out,
    ILogger? log = null
) : TestBase(@out, log)
    where TAppHostFixture : AppHostFixture
{
    private ITestOutputHelper? _originalAppHostOutput;

    protected TAppHostFixture Fixture { get; } = fixture;
    protected TestAppHost AppHost { get; private set; } = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        ActualFluentFormatters.Use();
        AppHost = await NewAppHost(Out.GetTest().GetInstanceName());
        _originalAppHostOutput = AppHost.Output;
        AppHost.Output = Out;
    }

    protected override async Task DisposeAsync()
    {
        await AppHost.Services.Queues().Purge().ConfigureAwait(false);
        AppHost.Output = _originalAppHostOutput;
        AppHost.DisposeSilently();
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
