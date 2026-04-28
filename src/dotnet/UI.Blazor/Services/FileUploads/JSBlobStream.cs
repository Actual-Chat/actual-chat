namespace ActualChat.UI.Blazor.Services;

internal sealed class JSBlobStream(IJSObjectReference jsRef, long length) : Stream
{
    // JS interop has a per-call payload limit; 256KB stays well under any reasonable cap
    // and amortizes interop overhead across larger ChunkedFileUploader reads.
    private const int MaxInteropChunkBytes = 256 * 1024;

    private long _position;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position {
        get => _position;
        set {
            if (value < 0 || value > length)
                throw new ArgumentOutOfRangeException(nameof(value));
            _position = value;
        }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var remaining = length - _position;
        if (remaining <= 0)
            return 0;

        var maxToRead = (int)Math.Min(buffer.Length, remaining);
        var totalRead = 0;
        while (totalRead < maxToRead) {
            var toRead = Math.Min(MaxInteropChunkBytes, maxToRead - totalRead);
            var bytes = await jsRef
                .InvokeAsync<byte[]>("readBlobChunk", cancellationToken, _position, toRead)
                .ConfigureAwait(false);
            if (bytes.Length == 0)
                break;

            bytes.CopyTo(buffer.Span[totalRead..]);
            _position += bytes.Length;
            totalRead += bytes.Length;
        }
        return totalRead;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override long Seek(long offset, SeekOrigin origin)
    {
        var newPosition = origin switch {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        Position = newPosition;
        return _position;
    }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() { }
}
