using System.Buffers;

namespace ActualChat.Hashing;

public static class ServerHashInputExt
{
    public static HashOutput32 Blake3(this HashInput input)
    {
        var hash = new HashOutput32();
        global::Blake3.Hasher.Hash(input.Bytes, hash.Bytes);
        return hash;
    }

    public static async Task<HashOutput32> Blake3(this StreamHashInput input, CancellationToken cancellationToken = default)
    {
        var blake3Hasher = global::Blake3.Hasher.New();
        var buffer = ArrayPool<byte>.Shared.Rent(16384);
        try {
            int readCount;
            while ((readCount = await input.Stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                blake3Hasher.Update(buffer.AsSpan(0, readCount)); // The description of Update lists buffer size requirements
            var blake3Hash = blake3Hasher.Finalize();
            var hash = new HashOutput32();
            blake3Hash.AsSpan().CopyTo(hash.Bytes);
            return hash;
        }
        finally {
            ArrayPool<byte>.Shared.Return(buffer);
            blake3Hasher.Dispose();
        }
    }
}
