using System.Buffers;

namespace ActualChat.Hashing;

#pragma warning disable CA5350, CA5351

public readonly struct StreamHashInput(Stream stream)
{
    public readonly Stream Stream = stream;

    // ReSharper disable once InconsistentNaming
    public async Task<HashOutput16> MD5(CancellationToken cancellationToken = default)
    {
        var bytes = await System.Security.Cryptography.MD5.HashDataAsync(Stream, cancellationToken).ConfigureAwait(false);
        var hash = new HashOutput16();
        bytes.CopyTo(hash.Bytes);
        return hash;
    }

    public async Task<HashOutput20> SHA1(CancellationToken cancellationToken = default)
    {
        var bytes = await System.Security.Cryptography.SHA1.HashDataAsync(Stream, cancellationToken).ConfigureAwait(false);
        var hash = new HashOutput20();
        bytes.CopyTo(hash.Bytes);
        return hash;
    }

    public async Task<HashOutput32> SHA256(CancellationToken cancellationToken = default)
    {
        var bytes = await System.Security.Cryptography.SHA256.HashDataAsync(Stream, cancellationToken).ConfigureAwait(false);
        var hash = new HashOutput32();
        bytes.CopyTo(hash.Bytes);
        return hash;
    }


    // ReSharper disable once InconsistentNaming
    public async Task<HashOutput32> Blake2s(CancellationToken cancellationToken = default)
    {
        var hasher = Blake2Fast.Blake2s.CreateIncrementalHasher();
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try {
            int readCount;
            while ((readCount = await Stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                hasher.Update(buffer.AsSpan(0, readCount));
            var bytes = hasher.Finish();
            var hash = new HashOutput32();
            bytes.CopyTo(hash.Bytes);
            return hash;
        }
        finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // ReSharper disable once InconsistentNaming
    public async Task<HashOutput64> Blake2b(CancellationToken cancellationToken = default)
    {
        var hasher = Blake2Fast.Blake2b.CreateIncrementalHasher();
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try {
            int readCount;
            while ((readCount = await Stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                hasher.Update(buffer.AsSpan(0, readCount));
            var bytes = hasher.Finish();
            var hash = new HashOutput64();
            bytes.CopyTo(hash.Bytes);
            return hash;
        }
        finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
