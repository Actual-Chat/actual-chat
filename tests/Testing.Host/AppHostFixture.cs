namespace ActualChat.Testing.Host;

public abstract class AppHostFixture(
    string instanceName,
    IMessageSink messageSink,
    TestAppHostOptions? baseHostOptions = null
    ) : IAsyncLifetime
{
    public string InstanceName { get; } = instanceName;
    public IMessageSink MessageSink { get; } = messageSink;
    public TestAppHostOptions BaseHostOptions { get; } = baseHostOptions ?? TestAppHostOptions.Default;
    public TestAppHost AppHost { get; protected set; } = null!;

    async Task IAsyncLifetime.InitializeAsync()
        => AppHost = await NewAppHost();

    Task IAsyncLifetime.DisposeAsync()
    {
        AppHost.DisposeSilently();
        return Task.CompletedTask;
    }

    public virtual Task<TestAppHost> NewAppHost(Func<TestAppHostOptions, TestAppHostOptions>? optionsBuilder = null)
    {
        var o = CreateAppHostOptions();
        o = optionsBuilder?.Invoke(o) ?? o;
        return TestAppHostFactory.NewAppHost(o);
    }

    // Protected methods

    protected virtual TestAppHostOptions CreateAppHostOptions()
        => BaseHostOptions.With(InstanceName, MessageSink);
}
