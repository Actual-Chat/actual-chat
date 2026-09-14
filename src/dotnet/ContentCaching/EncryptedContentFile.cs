using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ActualLab.IO;
using Microsoft.Win32.SafeHandles;

namespace ActualChat.ContentCaching;

internal sealed class EncryptedContentFile : IDisposable
{
    private const int MaxBlockLength = 65536;
    private const int HeaderLength = 44;
    private const int RecordHeaderLength = 24;
    private const int TagLength = 16;
    private const int FooterLength = 52;
    private readonly object _lock = new();
    private readonly SafeFileHandle _handle;
    private readonly byte[] _key;
    private readonly long _dataStart;
    private long _dataEnd;
    private long _length;
    private int _referenceCount = 1;
    private bool _isDisposed;
    private bool _isComplete;
    private bool _isWriting;
    private bool _hasWriteError;

    public byte[] Metadata { get; }
    public long Length => Volatile.Read(ref _length);

    public static async Task<EncryptedContentFile> Create(
        FilePath path,
        string identity,
        byte[] rootKey,
        byte[] metadata,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(metadata.Length, MaxBlockLength);
        cancellationToken.ThrowIfCancellationRequested();
        var header = new byte[HeaderLength];
        "ACECF001"u8.CopyTo(header);
        RandomNumberGenerator.Fill(header.AsSpan(8, 32));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(40), metadata.Length);
        var key = DeriveKey(identity, rootKey, header.AsSpan(8, 32));
        SafeFileHandle? handle = null;
        try {
            handle = File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete, FileOptions.Asynchronous | FileOptions.RandomAccess);
            var bytes = new byte[HeaderLength + 12 + metadata.Length + TagLength];
            header.CopyTo(bytes, 0);
            RandomNumberGenerator.Fill(bytes.AsSpan(HeaderLength, 12));
            using var cipher = new AesGcm(key, TagLength);
            cipher.Encrypt(bytes.AsSpan(HeaderLength, 12), metadata,
                bytes.AsSpan(HeaderLength + 12, metadata.Length), bytes.AsSpan(bytes.Length - TagLength), header);
            await RandomAccess.WriteAsync(handle, bytes, 0, cancellationToken).ConfigureAwait(false);
            return new EncryptedContentFile(handle, key, metadata.ToArray(), bytes.Length);
        }
        catch {
            handle?.Dispose();
            CryptographicOperations.ZeroMemory(key);
            throw;
        }
    }

    public static Task<EncryptedContentFile> Open(
        FilePath path, string identity, byte[] rootKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, FileOptions.Asynchronous | FileOptions.RandomAccess);
        byte[]? key = null;
        try {
            var fileLength = RandomAccess.GetLength(handle);
            if (fileLength < HeaderLength + 12 + TagLength + FooterLength)
                throw new InvalidDataException("Incomplete encrypted content header.");

            var header = new byte[HeaderLength];
            ReadExactly(handle, header, 0);
            if (!header.AsSpan(0, 8).SequenceEqual("ACECF001"u8))
                throw new InvalidDataException("Unknown encrypted content format.");

            var metadataLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(40));
            if (metadataLength is < 0 or > MaxBlockLength)
                throw new InvalidDataException("Invalid encrypted metadata length.");

            var dataStart = HeaderLength + 12 + metadataLength + TagLength;
            var dataEnd = fileLength - FooterLength;
            if (dataStart > dataEnd)
                throw new InvalidDataException("Truncated encrypted metadata.");

            key = DeriveKey(identity, rootKey, header.AsSpan(8, 32));
            using var cipher = new AesGcm(key, TagLength);
            var encryptedMetadata = new byte[12 + metadataLength + TagLength];
            ReadExactly(handle, encryptedMetadata, HeaderLength);
            var metadata = new byte[metadataLength];
            cipher.Decrypt(encryptedMetadata.AsSpan(0, 12), encryptedMetadata.AsSpan(12, metadataLength),
                encryptedMetadata.AsSpan(12 + metadataLength), metadata, header);
            var footer = new byte[FooterLength];
            ReadExactly(handle, footer, dataEnd);
            if (!footer.AsSpan(0, 8).SequenceEqual("ACECFEND"u8))
                throw new InvalidDataException("Missing encrypted content footer.");

            cipher.Decrypt(footer.AsSpan(24, 12), [], footer.AsSpan(36), [], footer.AsSpan(0, 36));
            var length = BinaryPrimitives.ReadInt64LittleEndian(footer.AsSpan(8));
            var storedDataEnd = BinaryPrimitives.ReadInt64LittleEndian(footer.AsSpan(16));
            var physicalLength = dataEnd - dataStart;
            if (storedDataEnd != dataEnd || length < 0 || length > physicalLength
                || (length == 0) != (physicalLength == 0)
                || (length > 0 && (physicalLength - length < RecordHeaderLength + TagLength
                    || (physicalLength - length) / (RecordHeaderLength + TagLength) > length)))
                throw new InvalidDataException("Invalid encrypted content bounds.");

            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new EncryptedContentFile(handle, key, metadata, dataStart) {
                _dataEnd = dataEnd,
                _length = length,
                _isComplete = true,
            });
        }
        catch {
            handle.Dispose();
            if (key != null)
                CryptographicOperations.ZeroMemory(key);
            throw;
        }
    }

    private EncryptedContentFile(SafeFileHandle handle, byte[] key, byte[] metadata, long dataStart)
    {
        _handle = handle;
        _key = key;
        Metadata = metadata;
        _dataStart = dataStart;
        _dataEnd = dataStart;
    }

    public void Dispose()
    {
        lock (_lock) {
            if (_isDisposed)
                return;

            _isDisposed = true;
            Release();
        }
    }

    public async Task Append(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (data.Length is < 1 or > MaxBlockLength)
            throw new ArgumentOutOfRangeException(nameof(data));

        BeginWrite(cancellationToken);
        try {
            var nextLength = checked(_length + data.Length);
            var bytes = new byte[RecordHeaderLength + data.Length + TagLength];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, data.Length);
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(4), _length);
            RandomNumberGenerator.Fill(bytes.AsSpan(12, 12));
            using var cipher = new AesGcm(_key, TagLength);
            cipher.Encrypt(bytes.AsSpan(12, 12), data.Span,
                bytes.AsSpan(RecordHeaderLength, data.Length), bytes.AsSpan(bytes.Length - TagLength),
                bytes.AsSpan(0, RecordHeaderLength));
            var nextEnd = checked(_dataEnd + bytes.Length);
            await RandomAccess.WriteAsync(_handle, bytes, _dataEnd, cancellationToken).ConfigureAwait(false);
            lock (_lock) {
                _dataEnd = nextEnd;
                Volatile.Write(ref _length, nextLength);
            }
        }
        catch {
            _hasWriteError = true;
            throw;
        }
        finally {
            EndWrite();
        }
    }

    public async Task Complete(CancellationToken cancellationToken)
    {
        BeginWrite(cancellationToken);
        try {
            var footer = new byte[FooterLength];
            "ACECFEND"u8.CopyTo(footer);
            BinaryPrimitives.WriteInt64LittleEndian(footer.AsSpan(8), _length);
            BinaryPrimitives.WriteInt64LittleEndian(footer.AsSpan(16), _dataEnd);
            RandomNumberGenerator.Fill(footer.AsSpan(24, 12));
            using var cipher = new AesGcm(_key, TagLength);
            cipher.Encrypt(footer.AsSpan(24, 12), [], [], footer.AsSpan(36), footer.AsSpan(0, 36));
            await RandomAccess.WriteAsync(_handle, footer, _dataEnd, cancellationToken).ConfigureAwait(false);
            RandomAccess.FlushToDisk(_handle);
            _isComplete = true;
        }
        catch {
            _hasWriteError = true;
            throw;
        }
        finally {
            EndWrite();
        }
    }

    public Stream CreateReader()
    {
        lock (_lock) {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            var reader = new Reader(this);
            _referenceCount++;
            return reader;
        }
    }

    // Private methods

    private void BeginWrite(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock) {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_isWriting || _isComplete || _hasWriteError)
                throw new InvalidOperationException("Encrypted content cannot be written in its current state.");

            _isWriting = true;
            _referenceCount++;
        }
    }

    private void EndWrite()
    {
        lock (_lock) {
            _isWriting = false;
            Release();
        }
    }

    private void Release()
    {
        lock (_lock) {
            if (--_referenceCount != 0)
                return;

            _handle.Dispose();
            CryptographicOperations.ZeroMemory(_key);
        }
    }

    private static byte[] DeriveKey(string identity, byte[] rootKey, ReadOnlySpan<byte> salt)
    {
        if (rootKey.Length != 32)
            throw new ArgumentException("A 32-byte encryption key is required.", nameof(rootKey));

        return HKDF.DeriveKey(HashAlgorithmName.SHA256, rootKey, 32, salt.ToArray(),
            Encoding.UTF8.GetBytes("ActualChat.EncryptedContentFile.v1:" + identity));
    }

    private static void ReadExactly(SafeFileHandle handle, Span<byte> buffer, long offset)
    {
        while (!buffer.IsEmpty) {
            var count = RandomAccess.Read(handle, buffer, offset);
            if (count == 0)
                throw new InvalidDataException("Truncated encrypted content.");

            buffer = buffer[count..];
            offset = checked(offset + count);
        }
    }

    // Nested types

    private sealed class Reader(EncryptedContentFile owner) : Stream
    {
        private readonly EncryptedContentFile _owner = owner;
        private readonly AesGcm _cipher = new(owner._key, TagLength);
        private readonly byte[] _header = new byte[RecordHeaderLength];
        private readonly byte[] _plaintext = new byte[MaxBlockLength];
        private readonly byte[] _encrypted = new byte[MaxBlockLength + TagLength];
        private long _physicalOffset = owner._dataStart;
        private long _logicalOffset;
        private long _position;
        private int _blockLength;
        private bool _isDisposed;
        private Exception? _error;

        public override bool CanRead => !_isDisposed;
        public override bool CanSeek => !_isDisposed;
        public override bool CanWrite => false;
        public override long Length => _owner.Length;
        public override long Position {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        protected override void Dispose(bool disposing)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _cipher.Dispose();
            CryptographicOperations.ZeroMemory(_plaintext);
            _owner.Release();
            base.Dispose(disposing);
        }

        public override int Read(byte[] buffer, int offset, int count)
            => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (!CanReadBuffer(buffer.Length))
                return 0;

            try {
                LoadBlock(false, default).GetAwaiter().GetResult();
                return CopyBlock(buffer);
            }
            catch (Exception error) {
                _error = error;
                CryptographicOperations.ZeroMemory(_plaintext);
                throw;
            }
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanReadBuffer(buffer.Length))
                return 0;

            try {
                await LoadBlock(true, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return CopyBlock(buffer.Span);
            }
            catch (Exception error) when (error is not OperationCanceledException) {
                _error = error;
                CryptographicOperations.ZeroMemory(_plaintext);
                throw;
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override long Seek(long offset, SeekOrigin origin)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            var position = origin switch {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(_position + offset),
                SeekOrigin.End => checked(Length + offset),
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            if (position < 0)
                throw new IOException("Cannot seek before the content.");

            _position = position;
            return position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        // Private methods

        private bool CanReadBuffer(int length)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_error != null)
                throw new InvalidDataException("Encrypted content reader failed.", _error);

            return length != 0 && _position < Length;
        }

        private async ValueTask LoadBlock(bool mustReadAsync, CancellationToken cancellationToken)
        {
            // Cached reads may complete synchronously; long seeks still need to let cancellation run.
            if (_position >= _logicalOffset && _position < _logicalOffset + _blockLength)
                return;

            if (_position < _logicalOffset) {
                _physicalOffset = _owner._dataStart;
                _logicalOffset = 0;
                _blockLength = 0;
            }
            if (_blockLength != 0) {
                _physicalOffset = checked(_physicalOffset + RecordHeaderLength + _blockLength + TagLength);
                _logicalOffset = checked(_logicalOffset + _blockLength);
                _blockLength = 0;
            }
            var scannedHeaders = 0;
            while (true) {
                if (mustReadAsync && ++scannedHeaders % 64 == 0)
                    await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
                cancellationToken.ThrowIfCancellationRequested();
                long availableLength;
                long dataEnd;
                lock (_owner._lock) {
                    availableLength = _owner._length;
                    dataEnd = _owner._dataEnd;
                }
                if (_physicalOffset > dataEnd - RecordHeaderLength - TagLength)
                    throw new InvalidDataException("Missing encrypted content record.");

                await ReadExactly(_header, _physicalOffset, mustReadAsync, cancellationToken).ConfigureAwait(false);
                var length = BinaryPrimitives.ReadInt32LittleEndian(_header);
                var offset = BinaryPrimitives.ReadInt64LittleEndian(_header.AsSpan(4));
                if (length is < 1 or > MaxBlockLength || offset != _logicalOffset
                    || length > availableLength - _logicalOffset)
                    throw new InvalidDataException("Invalid encrypted content record.");

                var nextPhysicalOffset = checked(_physicalOffset + RecordHeaderLength + length + TagLength);
                if (nextPhysicalOffset > dataEnd
                    || (_logicalOffset + length == availableLength && nextPhysicalOffset != dataEnd))
                    throw new InvalidDataException("Invalid encrypted content record bounds.");

                if (_position < _logicalOffset + length) {
                    await ReadExactly(_encrypted.AsMemory(0, length + TagLength),
                        _physicalOffset + RecordHeaderLength, mustReadAsync, cancellationToken).ConfigureAwait(false);
                    _cipher.Decrypt(_header.AsSpan(12), _encrypted.AsSpan(0, length),
                        _encrypted.AsSpan(length, TagLength), _plaintext.AsSpan(0, length), _header);
                    _blockLength = length;
                    return;
                }

                _physicalOffset = nextPhysicalOffset;
                _logicalOffset += length;
            }
        }

        private async ValueTask ReadExactly(
            Memory<byte> buffer, long offset, bool mustReadAsync, CancellationToken cancellationToken)
        {
            if (!mustReadAsync) {
                EncryptedContentFile.ReadExactly(_owner._handle, buffer.Span, offset);
                return;
            }

            while (!buffer.IsEmpty) {
                var count = await RandomAccess.ReadAsync(_owner._handle, buffer, offset, cancellationToken)
                    .ConfigureAwait(false);
                if (count == 0)
                    throw new InvalidDataException("Truncated encrypted content.");

                buffer = buffer[count..];
                offset = checked(offset + count);
            }
        }

        private int CopyBlock(Span<byte> buffer)
        {
            var offset = checked((int)(_position - _logicalOffset));
            var count = Math.Min(buffer.Length, _blockLength - offset);
            _plaintext.AsSpan(offset, count).CopyTo(buffer);
            _position += count;
            return count;
        }
    }
}
