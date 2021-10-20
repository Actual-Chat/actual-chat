using System.Collections.Concurrent;
using StackExchange.Redis;

namespace ActualChat.Redis;

public sealed class RedisHub
{
    private readonly ConcurrentDictionary<string, IConnectionMultiplexer> _multiplexers = new(StringComparer.Ordinal);

    public IConnectionMultiplexer GetMultiplexer(string configuration)
        => _multiplexers.GetOrAdd(configuration,
            cfg => ConnectionMultiplexer.Connect(cfg));
}
