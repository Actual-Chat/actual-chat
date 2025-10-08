namespace ActualChat.App.Maui.Services.Recording;

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
        _started = false;
        LastActivityEvent = VoiceActivityChange.NoVoiceActivity;
    }

    public override VadResult AppendChunk(ReadOnlySpan<float> monoPcm)
    {
        var gain = AudioExt.ApproximateGain(monoPcm);

        if (_started)
            return VadResult.GainOnly(gain);

        _started = true;
        LastActivityEvent = VoiceActivityChange.Start(0, null, 1.0);
        return VadResult.Event(LastActivityEvent);

    }

    protected override float? AppendChunkInternal(ReadOnlySpan<float> monoPcm)
        => null;


    public override void ConversationSignal()
    { }
}
