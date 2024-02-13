namespace ActualChat.Testing.Host;

public abstract class SharedAppHostTestBase<TAppHostFixture> : TestBase
    where TAppHostFixture : AppHostFixture
{
    protected TAppHostFixture Fixture { get; }
    protected TestAppHost AppHost { get; }

    protected SharedAppHostTestBase(TAppHostFixture fixture, ITestOutputHelper @out) : base(@out)
    {
        Fixture = fixture;
        AppHost = fixture.AppHost;
        AppHost.Output = @out;
    }

    // Just a shortcut
    protected virtual Task<TestAppHost> NewAppHost(Func<TestAppHostOptions, TestAppHostOptions>? optionOverrider = null)
        => Fixture.NewAppHost(options => {
            options = options with { Output = Out };
            options = optionOverrider?.Invoke(options) ?? options;
            return options;
        });
}
