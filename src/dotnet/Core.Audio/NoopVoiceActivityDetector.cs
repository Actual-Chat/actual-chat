namespace ActualChat.Audio;

public sealed class NoopVoiceActivityDetector(IServiceProvider services) : VoiceActivityDetector(services)
{
    private bool _started;
    public override bool IsInitialized => true;

    public override void Dispose()
    { }

    public override Task EnsureInitialized(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public override void Reset()
    {
        base.Reset();
        _started = false;
        LastActivityEvent = VoiceActivityChange.NoVoiceActivity;
    }

    public override VadResult[] AppendChunk(ReadOnlySpan<float> monoPcm)
    {
        var frames = monoPcm.Length / 512;
        if (monoPcm.Length % 512 != 0)
            throw new ArgumentException("Length must be a multiple of 512", nameof(monoPcm));

        var results = new VadResult[frames];
        for (int i = 0; i < frames; i++)
        {
            var frameSpan = monoPcm.Slice(i * 512, 512);
            var gain = AudioExt.ApproximateGain(frameSpan);

            if (_started)
                results[i] = VadResult.GainOnly(gain);
            else {
                _started = true;
                LastActivityEvent = VoiceActivityChange.Start(0, null, 1.0);
                results[i] = VadResult.Event(LastActivityEvent);
            }
        }
        return results;
    }

    protected internal override float[]? AppendChunkInternal(ReadOnlySpan<float> monoPcm)
        => null;

    public override void ConversationSignal()
    { }
}
