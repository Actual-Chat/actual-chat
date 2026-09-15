using ActualChat.Testing.Host;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Users.IntegrationTests;

[CollectionDefinition(nameof(AppUpdatesCollection))]
public class AppUpdatesCollection : ICollectionFixture<AppUpdatesAppHostFixture>;

public class AppUpdatesAppHostFixture(IMessageSink messageSink)
    : ActualChat.Testing.Host.AppHostFixture("app-updates", messageSink, TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => services.Replace(
            ServiceDescriptor.Singleton<AppStoreProbes>(c => new ScriptedAppStoreProbes(c))),
    });

/// <summary>
/// Replaces the real store probes so the detection state machine can be driven from a test.
/// </summary>
public sealed class ScriptedAppStoreProbes(IServiceProvider services) : AppStoreProbes(services)
{
    public ConcurrentDictionary<AppKind, ScriptedStoreProbe> Probes { get; } = new();
    public override StoreProbe? Get(AppKind appKind)
        => Probes.TryGetValue(appKind, out var probe) ? probe.Probe : null;

    public ScriptedStoreProbe Script(AppKind appKind, AppStoreProbeResult? result = null)
        => Probes[appKind] = new ScriptedStoreProbe { Result = result };
}

public sealed class ScriptedStoreProbe
{
    private int _callCount;
    public AppStoreProbeResult? Result { get; set; }
    public int CallCount => Volatile.Read(ref _callCount);

    public Task<AppStoreProbeResult?> Probe(string storeId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult(Result);
    }
}
