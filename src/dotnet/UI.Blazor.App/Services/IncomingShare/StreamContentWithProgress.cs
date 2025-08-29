namespace ActualChat.UI.Blazor.App.Services;

public class StreamContentWithProgress : HttpContent
{
    private readonly Stream _stream;
    private readonly IProgress<double>? _progress;
    private readonly CancellationToken _cancellationToken;

    public StreamContentWithProgress(Stream stream, IProgress<double> progress, CancellationToken token)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _progress = progress;
        _cancellationToken = token;
    }

    protected override async Task SerializeToStreamAsync(Stream targetStream, System.Net.TransportContext? _)
    {
        // TODO(DF): use buffer pool and adaptive buffer size depending no stream size.
        const int bufferSize = 81920;
        var buffer = new byte[bufferSize];
        long totalBytes = _stream.CanSeek ? _stream.Length : -1;
        long uploaded = 0;

        int read;
        while ((read = await _stream.ReadAsync(buffer.AsMemory(0, buffer.Length), _cancellationToken).ConfigureAwait(false)) > 0) {
            await targetStream.WriteAsync(buffer.AsMemory(0, read), _cancellationToken).ConfigureAwait(false);
            uploaded += read;

            if (totalBytes > 0 && _progress != null)
                _progress.Report((double)uploaded / totalBytes * 100);
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        if (_stream.CanSeek) {
            length = _stream.Length;
            return true;
        }
        length = -1;
        return false;
    }
}
