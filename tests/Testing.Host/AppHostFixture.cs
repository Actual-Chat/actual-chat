namespace ActualChat.Testing.Host;

public abstract class AppHostFixture(
    string instanceName,
    IMessageSink messageSink,
    TestAppHostOptions? appHostOptions = null
    ) : IAsyncLifetime
{
    public TestAppHostOptions AppHostOptions { get; protected init; }
        = (appHostOptions ?? TestAppHostOptions.Default).With(instanceName, messageSink);
    public TestAppHost AppHost { get; protected set; } = null!;

    async Task IAsyncLifetime.InitializeAsync()
        => AppHost = await NewAppHost();

    Task IAsyncLifetime.DisposeAsync()
    {
        AppHost.DisposeSilently();
        return Task.CompletedTask;
    }

    public virtual Task<TestAppHost> NewAppHost(Func<TestAppHostOptions, TestAppHostOptions>? optionOverrider = null)
    {
        var options = AppHostOptions;
        options = optionOverrider?.Invoke(options) ?? options;
        return TestAppHostFactory.NewAppHost(options);
    }
}
