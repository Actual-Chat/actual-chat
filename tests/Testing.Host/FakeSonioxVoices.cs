using ActualChat.Transcription;
using ActualLab.Generators;

namespace ActualChat.Testing.Host;

/// <summary>
/// In-memory <see cref="ISonioxVoices"/> stand-in for VoicePool tests: a created voice turns ready
/// only once <see cref="ReadyAfter"/> has elapsed (per Clocks.CpuClock), so a test can observe the
/// Creating window; a <see cref="FailCreate"/> voice never fails until it's set back to false.
/// </summary>
public sealed class FakeSonioxVoices(IServiceProvider services) : ISonioxVoices
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _voices = new();

    private MomentClockSet Clocks { get; } = services.Clocks();

    public TimeSpan ReadyAfter { get; set; } = TimeSpan.Zero;
    public bool FailCreate { get; set; }

    public Task<SonioxVoice> Create(string name, Stream wav, CancellationToken cancellationToken)
    {
        if (FailCreate)
            throw StandardError.External("Soniox voice create failed (FailCreate is set).");

        var id = RandomStringGenerator.Default.Next();
        SonioxVoice voice;
        lock (_lock) {
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
