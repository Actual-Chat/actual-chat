using AVFoundation;

namespace ActualChat.App.Maui.Playback;

public class PlayerNode(AVAudioPlayerNode node, AVAudioFormat format, Action<AVAudioNode> disposer) : AudioNode(node, disposer), IDisposable
{
    public AVAudioFormat Format { get; } = format;
    private readonly Lock _lock = new();

    public bool IsPlaying {
        get {
            lock (_lock)
                return node.Playing;
        }
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
    {
        Task whenPlayed;
        lock (_lock)
            whenPlayed = node.ScheduleFileAsync(audioFile, null, AVAudioPlayerNodeCompletionCallbackType.PlayedBack);
        return whenPlayed;
    }

    public void Stop()
    {
        lock (_lock)
            node.Stop();
    }
}
