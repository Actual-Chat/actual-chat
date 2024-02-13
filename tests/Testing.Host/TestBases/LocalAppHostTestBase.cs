namespace ActualChat.Testing.Host;

public abstract class LocalAppHostTestBase(
    string instanceName,
    ITestOutputHelper @out,
    TestAppHostOptions? appHostOptions = null
    ) : AppHostTestBase(instanceName, @out, appHostOptions)
{
    protected TestAppHost AppHost { get; set; } = null!;

    protected override async Task InitializeAsync()
        => AppHost = await NewAppHost();

    protected override Task DisposeAsync()
    {
        AppHost.DisposeSilently();
        return Task.CompletedTask;
    }
}
