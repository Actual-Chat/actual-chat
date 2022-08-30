namespace ActualChat.Audio;

public class AudioStreamServer : StreamServerBase<byte[]>, IAudioStreamServer
{
    public AudioStreamServer(IServiceProvider services) : base(services) { }

    public async Task<IAsyncEnumerable<byte[]>> Read(
        Symbol streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var stream = await Read(streamId, cancellationToken).ConfigureAwait(false);
        return SkipTo(stream, skipTo);
    }

    public new Task Write(Symbol streamId, IAsyncEnumerable<byte[]> stream, CancellationToken cancellationToken)
        => base.Write(streamId, stream, cancellationToken);

    // Private methods

    private static IAsyncEnumerable<byte[]> SkipTo(
        IAsyncEnumerable<byte[]> audioStream,
        TimeSpan skipTo)
    {
        // This method assumes there are 20ms packets!
        if (skipTo <= TimeSpan.Zero)
            return audioStream;

        var skipToFrameN = (int)skipTo.TotalMilliseconds / 20;
        return audioStream.SkipWhile((_, i) => i < skipToFrameN);
    }
}
