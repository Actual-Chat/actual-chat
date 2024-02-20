using System.Collections.Concurrent;
using ActualChat.Commands;

namespace ActualChat.Testing.Host;

public abstract class AppHostTestBase(
    string instanceName,
    TestAppHostOptions? appHostOptions,
    ITestOutputHelper @out,
    ILogger? log = null
    ) : TestBase(@out, log)
{
    private readonly ConcurrentBag<TestAppHost> _activeHosts = [];

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
        _activeHosts.Add(appHost);
        return appHost;
    }

    protected override async Task DisposeAsync()
    {
        foreach (var testAppHost in _activeHosts) {
            var queues = testAppHost.Services.GetRequiredService<ICommandQueues>();
            await queues.Purge(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
