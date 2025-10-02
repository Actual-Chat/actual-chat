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

        if (!_started)
        {
            _started = true;
            LastActivityEvent = VoiceActivityChange.Start(0, null, 1.0);
            return VadResult.Event(LastActivityEvent);
        }

        return VadResult.GainOnly(gain);
    }

    public override void ConversationSignal()
    { }
}
