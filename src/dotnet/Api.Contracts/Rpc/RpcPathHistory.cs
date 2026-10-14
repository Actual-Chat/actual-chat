using ActualChat.Serialization;

namespace ActualChat.Rpc;

/// <summary>
/// Recent connection checks of one network, per host name: at most one per <see cref="CheckWindow"/>,
/// the newest <see cref="MaxChecks"/> kept. What a host's checks say is <see cref="GetScore"/>.
/// </summary>
public sealed class RpcPathHistory
{
    public const int MaxChecks = 12;
    public const double NeutralScore = 0.5;
    // 2: the times are the ping and the payload ones, no longer one overall time
    private const int CurrentVersion = 2;
    public static readonly TimeSpan CheckWindow = TimeSpan.FromHours(2);
    public static readonly TimeSpan ScoreHalfLife = TimeSpan.FromHours(6);
    private static readonly VersionedByteSerializer Serializer = new([MessagePackByteSerializer.Default]);

    private readonly Lock _lock = new();
    private ImmutableDictionary<string, ImmutableArray<RpcPathCheck>> _checks
        = ImmutableDictionary<string, ImmutableArray<RpcPathCheck>>.Empty;

    private ImmutableDictionary<string, ImmutableArray<RpcPathCheck>> Checks
        => Volatile.Read(ref _checks); // Publication: writers replace the snapshot under the lock

    public static RpcPathHistory FromBytesOrEmpty(ReadOnlyMemory<byte> data)
    {
        // A history is only a hint: whatever can't be read is the same as having none
        try {
            if (data.IsEmpty)
                return new RpcPathHistory();

            var value = (RpcPathHistoryData?)Serializer.Read(data, typeof(RpcPathHistoryData), out _);
            if (value is not { Version: CurrentVersion })
                return new RpcPathHistory();

            var result = new RpcPathHistory();
            var builder = result._checks.ToBuilder();
            foreach (var (host, checks) in value.Hosts)
                if (!host.IsNullOrEmpty() && checks.Length > 0)
                    builder[host] = [..checks.OrderBy(x => x.AtMs).TakeLast(MaxChecks)];
            result._checks = builder.ToImmutable();
            return result;
        }
        catch (Exception) {
            return new RpcPathHistory();
        }
    }

    public IReadOnlyList<RpcPathCheck> GetChecks(string host)
        => Checks.TryGetValue(host, out var checks) ? checks : [];

    public void Record(string host, bool isOk, double pingTimeMs, DateTimeOffset now, double payloadTimeMs = 0)
    {
        var check = new RpcPathCheck(now.ToUnixTimeMilliseconds(), isOk, pingTimeMs, payloadTimeMs);
        lock (_lock) {
            var all = _checks;
            var checks = all.GetValueOrDefault(host, []);
            checks = checks.Length > 0 && check.AtMs - checks[^1].AtMs < CheckWindow.TotalMilliseconds
                ? checks.SetItem(checks.Length - 1, check)
                : checks.Add(check);
            if (checks.Length > MaxChecks)
                checks = checks.RemoveRange(0, checks.Length - MaxChecks);
            Volatile.Write(ref _checks, all.SetItem(host, checks));
        }
    }

    // Share of the checks that passed, each counted by how recent it is; no checks score neutral
    public double GetScore(string host, DateTimeOffset now)
    {
        var nowMs = now.ToUnixTimeMilliseconds();
        double passed = 0;
        double total = 0;
        foreach (var check in GetChecks(host)) {
            var ageMs = Math.Max(0, nowMs - check.AtMs);
            var weight = Math.Pow(0.5, ageMs / ScoreHalfLife.TotalMilliseconds);
            total += weight;
            if (check.IsOk)
                passed += weight;
        }
        return total > 0 ? passed / total : NeutralScore;
    }

    public byte[] ToBytes()
    {
        var data = new RpcPathHistoryData(CurrentVersion, Checks.ToDictionary(x => x.Key, x => x.Value.ToArray()));
        using var buffer = Serializer.Write(data);
        return buffer.ToArray();
    }
}
