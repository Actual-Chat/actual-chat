using AVFoundation;

namespace ActualChat.App.Maui.Playback;

public class ThreadSafePlayerNode(AVAudioPlayerNode node, Action<AVAudioPlayerNode> disposer) : IDisposable
{
    private readonly Lock _lock = new();

    public bool IsPlaying {
        get {
            lock (_lock)
                return node.Playing;
        }
    }


    public void Dispose()
    {
        lock (_lock)
            disposer(node);
    }

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

    public Task ScheduleFileAndWait(AVAudioFile audioFile)
        => node.ScheduleFileAsync(audioFile, null, AVAudioPlayerNodeCompletionCallbackType.PlayedBack);

    public void Stop()
    {
        lock (_lock)
            node.Stop();
    }
}
