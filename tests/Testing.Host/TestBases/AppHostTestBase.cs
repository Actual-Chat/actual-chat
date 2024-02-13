namespace ActualChat.Testing.Host;

public abstract class AppHostTestBase(
    string instanceName,
    ITestOutputHelper @out,
    TestAppHostOptions? appHostOptions = null
) : TestBase(@out)
{
    protected TestAppHostOptions AppHostOptions { get; init; }
        = (appHostOptions ?? TestAppHostOptions.Default).With(instanceName, @out);

    protected virtual Task<TestAppHost> NewAppHost(Func<TestAppHostOptions, TestAppHostOptions>? optionOverrider = null)
    {
        var options = AppHostOptions;
        options = optionOverrider?.Invoke(options) ?? options;
        return TestAppHostFactory.NewAppHost(options);
    }
}
