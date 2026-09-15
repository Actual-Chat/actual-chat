using ActualChat.Transcription;
using ActualLab.Generators;

namespace ActualChat.Testing.Host;

/// <summary>
/// In-memory <see cref="ISonioxVoices"/> stand-in for VoicePool tests: a created voice turns ready
/// only once <see cref="ReadyAfter"/> has elapsed (per Clocks.CpuClock), so a test can observe the
/// Creating window; a <see cref="FailCreate"/> voice never fails until it's set back to false. Names
/// are unique, as on Soniox.
/// </summary>
public sealed class FakeSonioxVoices(IServiceProvider services) : ISonioxVoices
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _voices = new();
    private int _createCount;
    private int _deleteCount;

    private MomentClockSet Clocks { get; } = services.Clocks();

    public TimeSpan ReadyAfter { get; set; } = TimeSpan.Zero;
    public bool FailCreate { get; set; }
    // Attempts, so a failed Create counts too
    public int CreateCount => Volatile.Read(ref _createCount);
    public int DeleteCount => Volatile.Read(ref _deleteCount);

    public Task<SonioxVoice> Create(string name, Stream wav, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _createCount);
        if (FailCreate)
            throw StandardError.External("Soniox voice create failed (FailCreate is set).");

        var id = RandomStringGenerator.Default.Next();
        SonioxVoice voice;
        lock (_lock) {
            // Names are unique per Soniox project, and the real API rejects a duplicate
            if (_voices.Values.Any(x => x.Name == name))
                throw StandardError.External($"Soniox voice named '{name}' already exists.");

            _voices[id] = new Entry(name, Clocks.CpuClock.Now + ReadyAfter);
            voice = ToVoice(id);
        }
        return Task.FromResult(voice);
    }

    public Task<SonioxVoice?> Get(string id, CancellationToken cancellationToken)
    {
        lock (_lock)
            return Task.FromResult(_voices.ContainsKey(id) ? ToVoice(id) : null);
    }

    public Task<ApiArray<SonioxVoice>> List(CancellationToken cancellationToken)
    {
        lock (_lock)
            return Task.FromResult(_voices.Keys.Select(ToVoice).ToApiArray());
    }

    public Task Delete(string id, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _deleteCount);
        lock (_lock)
            _voices.Remove(id);
        return Task.CompletedTask;
    }

    // Private methods

    private SonioxVoice ToVoice(string id)
    {
        var entry = _voices[id];
        var isReady = Clocks.CpuClock.Now >= entry.ReadyAt;
        return new SonioxVoice(id, entry.Name, isReady, false);
    }

    // Nested types

    private sealed record Entry(string Name, Moment ReadyAt);
}
