using ActualLab.Versioning;

namespace ActualChat.Concurrency;

/// <summary>
/// Makes the versions of the wrapped generator unique within the process: a clock-based one
/// returns the same value to concurrent callers that land in the same clock tick.
/// </summary>
public sealed class MonotonicVersionGenerator(VersionGenerator<long> generator) : VersionGenerator<long>
{
    private long _lastVersion;

    public override long NextVersion(long currentVersion = 0)
    {
        // CAS loop: a caller that lost the race retries above the version the winner took
        while (true) {
            var lastVersion = Interlocked.Read(ref _lastVersion);
            var version = generator.NextVersion(Math.Max(currentVersion, lastVersion));
            if (Interlocked.CompareExchange(ref _lastVersion, version, lastVersion) == lastVersion)
                return version;
        }
    }
}
