using ActualChat.Transcription;
using ActualLab.Generators;

namespace ActualChat.Testing.Host;

/// <summary>
/// In-memory <see cref="ISonioxVoices"/> stand-in for VoicePool tests: a created voice turns ready
/// only once <see cref="ReadyAfter"/> has elapsed (per Clocks.CpuClock), so a test can observe the
/// Creating window; <see cref="FailCreate"/> and <see cref="FailList"/> make those calls throw until
/// set back to false, <see cref="OnCreate"/> runs before every create. Names are unique, as on Soniox.
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
    public bool FailList { get; set; }
    public Func<string, Task>? OnCreate { get; set; }
    public int CreateCount
        // Attempts, so a failed Create counts too
        => Volatile.Read(ref _createCount);
    public int DeleteCount => Volatile.Read(ref _deleteCount);

    public async Task<SonioxVoice> Create(string name, Stream wav, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _createCount);
        if (OnCreate is { } onCreate)
            await onCreate.Invoke(name).ConfigureAwait(false);
        if (FailCreate)
            throw StandardError.External("Soniox voice create failed (FailCreate is set).");

        var id = RandomStringGenerator.Default.Next();
        lock (_lock) {
            // Names are unique per Soniox project, and the real API rejects a duplicate
            if (_voices.Values.Any(x => x.Name == name))
                throw StandardError.External($"Soniox voice named '{name}' already exists.");

            _voices[id] = new Entry(name, Clocks.CpuClock.Now + ReadyAfter);
            return ToVoice(id);
        }
    }

    public Task<SonioxVoice?> Get(string id, CancellationToken cancellationToken)
    {
        lock (_lock)
            return Task.FromResult(_voices.ContainsKey(id) ? ToVoice(id) : null);
    }

    public Task<ApiArray<SonioxVoice>> List(CancellationToken cancellationToken)
    {
        if (FailList)
            throw StandardError.External("Soniox voice list failed (FailList is set).");

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
