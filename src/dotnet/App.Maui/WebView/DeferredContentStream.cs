namespace ActualChat.App.Maui;

// Lets a native callback answer before the upstream headers arrive: the fetch runs in the
// background and the first read waits for it. Android's shouldInterceptRequest gets ~6 threads,
// so waiting there instead would park them all and stall every cache hit behind them.
internal sealed class DeferredContentStream(Task<HttpResponseMessage?> responseTask) : Stream
{
    private HttpResponseMessage? _response;
    private Stream? _inner;
    private bool _isOpened;
    private bool _isDisposed;

    public override bool CanRead => !_isDisposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_isDisposed) {
            _isDisposed = true;
            // The fetch may still be in flight; its response is disposed when it lands
            if (_isOpened)
                _response?.Dispose();
            else
                _ = responseTask.ContinueWith(
                    static x => x.Result?.Dispose(),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
        }
        base.Dispose(disposing);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_isDisposed)
            return 0;

        Open();
        return _inner?.Read(buffer, offset, count) ?? 0;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    // Private methods

    private void Open()
    {
        if (_isOpened)
            return;

        _isOpened = true;
        _response = responseTask.GetAwaiter().GetResult();
        _inner = _response?.Content.ReadAsStream();
    }
}
