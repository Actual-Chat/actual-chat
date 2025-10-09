using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public class PlayerNode(AVAudioPlayerNode node, AVAudioFormat format, Action<AVAudioNode> disposer, ILogger<PlayerNode> log)
    : AudioNode(node, disposer, log), IDisposable
{
    public AVAudioFormat Format { get; } = format;
    private readonly Lock _lock = new();

    public bool IsPlaying {
        get {
            lock (_lock)
                return node.Playing;
        }
    }

    protected override void DisposeCore()
        => Stop();

    public void Play()
    {
        lock (_lock)
            if (!node.Playing)
                node.Play();
    }

    public void Pause()
    {
        lock (_lock)
            if (node.Playing)
                node.Pause();
    }

    public void ScheduleBuffer(AVAudioPcmBuffer pcm, Action<AVAudioPlayerNodeCompletionCallbackType> callback)
    {
        lock (_lock)
            node.ScheduleBuffer(pcm, AVAudioPlayerNodeCompletionCallbackType.PlayedBack, callback);
    }

    public Task ScheduleFileAndWait(AVAudioFile audioFile, CancellationToken cancellationToken = default)
    {
        var whenPlayed = AsyncTaskMethodBuilderExt.New();
        lock (_lock)
            node.ScheduleFile(audioFile,
                null,
                AVAudioPlayerNodeCompletionCallbackType.PlayedBack,
                _ => whenPlayed.TrySetResult());
        return whenPlayed.Task.WaitAsync(cancellationToken);
    }

    public void Stop()
    {
        lock (_lock)
            node.Stop();
    }
}
