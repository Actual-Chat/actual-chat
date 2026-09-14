using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using ActualLab.IO;

namespace ActualChat.ContentCaching;

public sealed partial class FileSystemContentHandler
{
    private sealed class ReadStream : Stream
    {
        private readonly Lock _lock = new();
        private readonly FileSystemContentHandler _owner;
        private readonly FilePath _path;
        private readonly EncryptedContentFile _file;
        private readonly Stream _reader;
        private readonly long _offset;
        private readonly long? _length;
        private readonly Download? _download;
        private readonly ContentRequest _request;
        private readonly ResponseMetadata _metadata;
        private readonly CancellationToken _requestToken;
        private readonly CancellationTokenSource _disposeSource = new();
        private CancellationTokenRegistration _requestRegistration;
        private HttpResponseMessage? _fallbackResponse;
        private Stream? _fallbackStream;
        private long _position;
        private int _isDisposed;
        private bool _isReading;
        private bool _isCancellationSignaled;
        private bool _areResourcesDisposed;

        public override bool CanRead => Volatile.Read(ref _isDisposed) == 0;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length ?? throw new NotSupportedException();
        public override long Position {
            get => _position;
            set => throw new NotSupportedException();
        }

        public ReadStream(
            FileSystemContentHandler owner, FilePath path, EncryptedContentFile file,
            long offset, long? length, Download? download,
            ContentRequest request, ResponseMetadata metadata, CancellationToken requestToken)
        {
            _owner = owner;
            _path = path;
            _file = file;
            _offset = offset;
            _length = length;
            _download = download;
            _request = request;
            _metadata = metadata;
            _requestToken = requestToken;
            _reader = file.CreateReader();
            if (offset != 0)
                _reader.Seek(offset, SeekOrigin.Begin);
        }

        public void ActivateCancellation()
        {
            var registration = _requestToken.UnsafeRegister(static state => ((ReadStream)state!).Dispose(), this);
            lock (_lock) {
                _requestRegistration = registration;
                if (!CanRead)
                    registration.Unregister();
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            lock (_lock) {
                _requestToken.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(!CanRead, this);
                if (_isReading)
                    throw new InvalidOperationException("Concurrent reads on one response stream are not supported.");
                _isReading = true;
            }
            try {
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    _requestToken, cancellationToken, _disposeSource.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                return buffer.Length == 0
                    ? 0
                    : await ReadCore(buffer, cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                if (_requestToken.IsCancellationRequested)
                    Dispose();
                throw;
            }
            catch {
                Dispose();
                throw;
            }
            finally {
                lock (_lock) {
                    _isReading = false;
                    DisposeResources();
                }
            }
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _isDisposed, 1) == 0) {
                _disposeSource.Cancel();
                lock (_lock) {
                    _isCancellationSignaled = true;
                    _requestRegistration.Unregister();
                    _fallbackResponse?.Dispose();
                    if (_download == null)
                        _file.Dispose();
                    else
                        _download.Release();
                    DisposeResources();
                }
            }
            base.Dispose(disposing);
        }

        // Private methods

        private void DisposeResources()
        {
            // A canceled read can still be decrypting a block; keep its buffers alive until it returns.
            if (!_isCancellationSignaled || _isReading || _areResourcesDisposed)
                return;

            _areResourcesDisposed = true;
            _reader.Dispose();
            _disposeSource.Dispose();
        }

        private async ValueTask<int> ReadCore(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (_fallbackStream != null)
                return await ReadFallback(buffer, cancellationToken).ConfigureAwait(false);

            var available = _file.Length;
            if (_download != null) {
                while (true) {
                    var progress = _download.Progress;
                    var state = progress.Value;
                    if (state.Error is StorageFailure) {
                        await OpenFallback(cancellationToken).ConfigureAwait(false);
                        return await ReadFallback(buffer, cancellationToken).ConfigureAwait(false);
                    }
                    if (state.Error != null)
                        ExceptionDispatchInfo.Capture(state.Error).Throw();

                    available = state.Length;
                    if (available > _position || state.IsCompleted)
                        break;

                    await progress.WhenNext(cancellationToken).ConfigureAwait(false);
                }
            }
            var remaining = Math.Min(available - _offset - _position, (_length ?? long.MaxValue) - _position);
            if (remaining <= 0)
                return 0;

            buffer = buffer[..(int)Math.Min(buffer.Length, remaining)];
            try {
                var count = await _reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (count == 0)
                    throw new EndOfStreamException("Cached content ended before its published length.");

                _position += count;
                return count;
            }
            catch (Exception e) when (e is CryptographicException or InvalidDataException or EndOfStreamException) {
                _download?.Invalidate(e);
                _owner.Delete(_path);
                throw;
            }
            catch (Exception e) when (IsStorageError(e)) {
                _download?.Invalidate(new StorageFailure(e));
                _owner.Delete(_path);
                await OpenFallback(cancellationToken).ConfigureAwait(false);
                return await ReadFallback(buffer, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task OpenFallback(CancellationToken cancellationToken)
        {
            var response = await _owner.Downstream.Handle(_request, cancellationToken).ConfigureAwait(false);
            if (response == null)
                throw new IOException("The content source did not return a fallback response.");

            try {
                if (!_metadata.Matches(response))
                    throw new InvalidDataException("The fallback content representation changed.");

                var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var remaining = _offset + _position;
                var buffer = new byte[_owner.Settings.DownloadBufferSize];
                try {
                    while (remaining > 0) {
                        var count = await stream.ReadAsync(
                            buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken)
                            .ConfigureAwait(false);
                        if (count == 0)
                            throw new EndOfStreamException("The fallback content ended before the current position.");

                        remaining -= count;
                    }
                }
                finally {
                    CryptographicOperations.ZeroMemory(buffer);
                }
                lock (_lock) {
                    ObjectDisposedException.ThrowIf(!CanRead, this);
                    _fallbackResponse = response;
                    _fallbackStream = stream;
                }
            }
            catch {
                response.Dispose();
                throw;
            }
        }

        private async ValueTask<int> ReadFallback(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (_length is { } length) {
                if (_position == length)
                    return 0;

                buffer = buffer[..(int)Math.Min(buffer.Length, length - _position)];
            }
            var count = await _fallbackStream!.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0 && _length is { } expected && _position != expected)
                throw new EndOfStreamException("The fallback content ended before its declared length.");

            _position += count;
            return count;
        }
    }
}