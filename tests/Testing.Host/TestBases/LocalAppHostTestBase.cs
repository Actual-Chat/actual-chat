namespace ActualChat.Testing.Host;

public abstract class LocalAppHostTestBase(
    string instanceName,
    TestAppHostOptions? appHostOptions,
    ITestOutputHelper @out,
    ILogger? log = null
    ) : AppHostTestBase(instanceName, appHostOptions, @out, log)
{
    protected TestAppHost AppHost { get; set; } = null!;

    protected LocalAppHostTestBase(string instanceName, ITestOutputHelper @out, ILogger? log = null)
        : this(instanceName, null, @out, log)
    { }

    protected override async Task InitializeAsync()
        => AppHost = await NewAppHost();

    protected override Task DisposeAsync()
    {
        AppHost.DisposeSilently();
        return Task.CompletedTask;
    }
}
