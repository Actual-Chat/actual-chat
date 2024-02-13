namespace ActualChat.Testing.Host;

public abstract class AppHostTestBase(
    string instanceName,
    TestAppHostOptions? appHostOptions,
    ITestOutputHelper @out,
    ILogger? log = null
    ) : TestBase(@out, log)
{
    protected TestAppHostOptions AppHostOptions { get; init; }
        = (appHostOptions ?? TestAppHostOptions.Default).With(instanceName, @out);

    protected AppHostTestBase(string instanceName, ITestOutputHelper @out, ILogger? log = null)
        : this(instanceName, null, @out, log)
    { }

    protected virtual Task<TestAppHost> NewAppHost(Func<TestAppHostOptions, TestAppHostOptions>? optionOverrider = null)
    {
        var options = AppHostOptions;
        options = optionOverrider?.Invoke(options) ?? options;
        return TestAppHostFactory.NewAppHost(options);
    }
}
