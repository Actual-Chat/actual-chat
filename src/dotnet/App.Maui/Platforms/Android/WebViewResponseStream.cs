namespace ActualChat.App.Maui;

// Chromium reads an intercepted response body over JNI and survives only java.io.IOException:
// any other exception escaping Read - e.g. a dropped download - kills the app. This fails the
// one load instead.
internal sealed class WebViewResponseStream(Stream inner, string? url) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            try {
                inner.Dispose();
            }
            catch (Exception e) {
                ContentCacheLog.Log.LogWarning(e, "Intercepted response body failed to close: {Url}", url);
            }
        }
        base.Dispose(disposing);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        try {
            return inner.Read(buffer, offset, count);
        }
        catch (Exception e) when (e is not Java.IO.IOException) {
            ContentCacheLog.Log.LogWarning(e, "Intercepted response body failed: {Url}", url);
            throw new Java.IO.IOException(e.Message);
        }
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
