using Stl.Time;

namespace ActualChat
{
    public sealed class LongVersionProvider : IVersionProvider<long>
    {
        public static IVersionProvider<long> Default { get; } = new LongVersionProvider(SystemClock.Instance);

        private readonly IMomentClock _clock;

        public LongVersionProvider(IMomentClock clock)
            => _clock = clock;

        public long NextVersion(long currentVersion = default)
        {
            var nextVersion = _clock.Now.EpochOffset.Ticks;
            return nextVersion > currentVersion ? nextVersion : ++currentVersion;
        }
    }
}
