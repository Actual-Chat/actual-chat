namespace ActualChat.App.Maui;

// Ties the response's lifetime to its body, which is all a native WebView response closes
internal sealed class ContentResponseStream(HttpResponseMessage response, Stream inner) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position {
        get => inner.Position;
        set => inner.Position = value;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            response.Dispose();
        base.Dispose(disposing);
    }

    public override int Read(byte[] buffer, int offset, int count)
        => inner.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer)
        => inner.Read(buffer);

    public override long Seek(long offset, SeekOrigin origin)
        => inner.Seek(offset, origin);

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
