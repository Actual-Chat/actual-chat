using ActualChat.Testing.Host;
using ActualChat.Users.AppStores;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Users.IntegrationTests;

[CollectionDefinition(nameof(AppUpdatesCollection))]
public class AppUpdatesCollection : ICollectionFixture<AppUpdatesAppHostFixture>;

public class AppUpdatesAppHostFixture(IMessageSink messageSink)
    : ActualChat.Testing.Host.AppHostFixture("app-updates", messageSink, TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => services.Replace(
            ServiceDescriptor.Singleton<StoreProbes>(c => new ScriptedStoreProbes(c))),
    });

/// <summary>
/// Replaces the real store probes so the detection state machine can be driven from a test.
/// </summary>
public sealed class ScriptedStoreProbes(IServiceProvider services) : StoreProbes(services)
{
    public ConcurrentDictionary<AppKind, ScriptedStoreProbe> Probes { get; } = new();
    public override IStoreProbe? Get(AppKind appKind)
        => Probes.GetValueOrDefault(appKind);

    public ScriptedStoreProbe Script(AppKind appKind, StoreProbeResult? result = null)
        => Probes[appKind] = new ScriptedStoreProbe { Result = result };
}

public sealed class ScriptedStoreProbe : IStoreProbe
{
    private int _callCount;
    public StoreProbeResult? Result { get; set; }
    public int CallCount => Volatile.Read(ref _callCount);

    public Task<StoreProbeResult?> Probe(string storeId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult(Result);
    }
}
