using System.Security.Cryptography;
using ActualLab.IO;

namespace ActualChat.ContentCaching;

public sealed partial class FileSystemContentHandler
{
    private sealed class Download(
        FileSystemContentHandler owner,
        FilePath path,
        ContentRequest request,
        HttpResponseMessage response,
        Stream source,
        EncryptedContentFile file,
        ResponseMetadata metadata) : WorkerBase
    {
        private readonly Lock _lock = new();
        private Progress _progress;
        private TaskCompletionSource<long>? _whenAvailableSource;
        private Exception? _readerError;
        private int _readerCount;
        private bool _isClosed;
        private bool _isFinished;

        public Progress GetProgress(long position, out Task<long>? whenAvailable)
        {
            lock (_lock) {
                whenAvailable = _progress.Length <= position && !_progress.IsCompleted
                    ? (_whenAvailableSource ??= TaskCompletionSourceExt.New<long>()).Task
                    : null;
                return _progress;
            }
        }

        public bool HasEncryptionKey(byte[] other)
            => CryptographicOperations.FixedTimeEquals(owner._encryptionKey, other);

        public HttpResponseMessage? TryCreateResponse(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock) {
                if (_isClosed)
                    return null;

                _readerCount++;
            }
            ReadStream? body = null;
            try {
                body = new ReadStream(
                    owner, path, file, 0, metadata.ExpectedLength, this,
                    request, metadata, cancellationToken);
                var result = metadata.CreateResponse(body, metadata.ExpectedLength);
                _ = Run();
                body.ActivateCancellation();
                cancellationToken.ThrowIfCancellationRequested();
                return result;
            }
            catch {
                if (body == null)
                    Release();
                else
                    body.Dispose();
                throw;
            }
        }

        public void Release()
        {
            bool mustStop;
            bool mustDispose;
            lock (_lock) {
                _readerCount--;
                mustStop = _readerCount == 0;
                mustDispose = mustStop && _isFinished;
                if (mustStop)
                    _isClosed = true;
            }
            if (mustStop)
                _ = Stop();
            if (mustDispose)
                file.Dispose();
        }

        public void Invalidate(Exception error)
        {
            lock (_lock) {
                if (_readerError == null || _readerError is StorageFailure)
                    _readerError = error;
                _isClosed = true;
                if (_isFinished) {
                    owner.Delete(path);
                    Publish(new Progress(_progress.Length, true, _readerError));
                }
            }
            _ = Stop();
        }

        protected override async Task OnRun(CancellationToken cancellationToken)
        {
            Exception? error = null;
            var buffer = new byte[owner.Settings.DownloadBufferSize];
            var length = 0L;
            try {
                while (true) {
                    var count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (count == 0)
                        break;
                    var nextLength = checked(length + count);
                    if (metadata.ExpectedLength is { } expected && nextLength > expected)
                        throw new InvalidDataException("Content exceeded its declared length.");

                    try {
                        await file.Append(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e) when (IsStorageError(e)) {
                        throw new StorageFailure(e);
                    }
                    length = nextLength;
                    lock (_lock)
                        Publish(new Progress(length, false, null));
                }
                if (metadata.ExpectedLength is { } declared && length != declared)
                    throw new EndOfStreamException("Content ended before its declared length.");

                try {
                    await file.Complete(cancellationToken).ConfigureAwait(false);
                    lock (_lock) {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (_readerError != null)
                            throw _readerError;
                        File.Move(path + ".p", path, true);
                    }
                }
                catch (Exception e) when (IsStorageError(e)) {
                    throw new StorageFailure(e);
                }
                owner.Touch(path);
            }
            catch (Exception e) {
                error = e;
            }
            finally {
                CryptographicOperations.ZeroMemory(buffer);
                response.Dispose();
                bool mustDispose;
                lock (_lock) {
                    error = _readerError ?? error;
                    if (error != null)
                        owner.Delete(path + ".p");
                    if (_readerError != null)
                        owner.Delete(path);
                    _isFinished = true;
                    _isClosed = true;
                    mustDispose = _readerCount == 0;
                    Publish(new Progress(length, true, error));
                }
                if (mustDispose)
                    file.Dispose();
                Downloads.TryRemove(KeyValuePair.Create(path, this));
            }
        }

        // Private methods

        private void Publish(Progress progress)
        {
            _progress = progress;
            var whenAvailableSource = _whenAvailableSource;
            _whenAvailableSource = null;
            whenAvailableSource?.TrySetResult(progress.Length);
        }
    }

    private readonly record struct Progress(long Length, bool IsCompleted, Exception? Error);
    private sealed class StorageFailure(Exception inner) : IOException("Content cache storage failed.", inner);
}
