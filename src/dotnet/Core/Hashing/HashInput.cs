namespace ActualChat.Hashing;

#pragma warning disable CA5350, CA5351

public readonly ref struct HashInput(ReadOnlySpan<byte> bytes)
{
    public readonly ReadOnlySpan<byte> Bytes = bytes;

    // ReSharper disable once InconsistentNaming
    public HashOutput16 MD5()
    {
        var hash = new HashOutput16();
        System.Security.Cryptography.MD5.HashData(Bytes, hash.Bytes);
        return hash;
    }

    public HashOutput20 SHA1()
    {
        var hash = new HashOutput20();
        System.Security.Cryptography.SHA1.HashData(Bytes, hash.Bytes);
        return hash;
    }

    public HashOutput32 SHA256()
    {
        var hash = new HashOutput32();
        System.Security.Cryptography.SHA256.HashData(Bytes, hash.Bytes);
        return hash;
    }

    // ReSharper disable once InconsistentNaming
    public HashOutput32 Blake2s()
    {
        var hash = new HashOutput32();
        Blake2Fast.Blake2s.ComputeAndWriteHash(Bytes, hash.Bytes);
        return hash;
    }

    // ReSharper disable once InconsistentNaming
    public HashOutput64 Blake2b()
    {
        var hash = new HashOutput64();
        Blake2Fast.Blake2b.ComputeAndWriteHash(Bytes, hash.Bytes);
        return hash;
    }
}
