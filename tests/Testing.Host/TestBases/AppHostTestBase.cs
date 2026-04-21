namespace ActualChat.Testing.Host;

public abstract class AppHostTestBase(
    string instanceName,
    TestAppHostOptions? appHostOptions,
    ITestOutputHelper @out,
    ILogger? log = null
    ) : TestBase(@out, log)
{
    public static TimeSpan DefaultTestTimeout { get; set; } = TimeSpan.FromMinutes(2);

    protected TestAppHostOptions AppHostOptions { get; init; }
        = (appHostOptions ?? TestAppHostOptions.Default).With(instanceName, @out);

    protected AppHostTestBase(string instanceName, ITestOutputHelper @out, ILogger? log = null)
        : this(instanceName, null, @out, log)
    { }

    protected virtual async Task<TestAppHost> NewAppHost(Func<TestAppHostOptions, TestAppHostOptions>? optionOverrider = null)
    {
        var options = AppHostOptions;
        options = optionOverrider?.Invoke(options) ?? options;
        var appHost = await TestAppHostFactory.NewAppHost(options);
        return appHost;
    }

    // Returns a CTS that cancels after DefaultTestTimeout — wraps test bodies so that
    // a hung await fails with TaskCanceledException in seconds instead of silently
    // running until the outer --blame-hang-timeout.
    protected CancellationTokenSource NewTestCts(TimeSpan? timeout = null)
        => new(timeout ?? DefaultTestTimeout);
}
