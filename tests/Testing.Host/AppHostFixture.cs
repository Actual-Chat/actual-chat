namespace ActualChat.Testing.Host;

public abstract class AppHostFixture(
    string instanceName,
    IMessageSink messageSink,
    TestAppHostOptions? baseHostOptions = null
    ) : IAsyncLifetime
{
    public string InstanceName { get; } = instanceName;
    public IMessageSink MessageSink { get; } = messageSink;
    public TestAppHostOptions BaseHostOptions { get; } = baseHostOptions ?? TestAppHostOptions.WithDefaultChat;
    public TestAppHost Host { get; protected set; } = null!;

    async Task IAsyncLifetime.InitializeAsync()
        => Host = await NewHost();

    public Task DisposeAsync()
    {
        Host.DisposeSilently();
        return Task.CompletedTask;
    }

    public virtual Task<TestAppHost> NewHost(Func<TestAppHostOptions, TestAppHostOptions>? optionsBuilder = null)
    {
        var o = CreateHostOptions();
        o = optionsBuilder?.Invoke(o) ?? o;
        return TestAppHostFactory.NewAppHost(o);
    }

    // Protected methods

    protected virtual TestAppHostOptions CreateHostOptions()
        => BaseHostOptions.With(InstanceName, MessageSink);
}
