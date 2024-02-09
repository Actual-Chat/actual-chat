namespace ActualChat.Testing.Host;

public abstract class AppHostFixture(IMessageSink messageSink) : IAsyncLifetime
{
    public IMessageSink MessageSink { get; } = messageSink;
    public TestAppHost Host { get; protected set; } = null!;

    protected abstract string DbInstanceName { get; }

    public virtual async Task InitializeAsync()
        => Host = await TestAppHostFactory.NewAppHost(MessageSink, DbInstanceName, TestAppHostOptions.WithDefaultChat);

    public virtual Task DisposeAsync()
    {
        Host.DisposeSilently();
        return Task.CompletedTask;
    }
}
