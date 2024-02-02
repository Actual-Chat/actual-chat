using Microsoft.Toolkit.HighPerformance;

namespace ActualChat.Hashing;

#pragma warning disable CA1721

public interface IHashOutput
{
    public static abstract int Size { get; }
    public Span<byte> Bytes { get; }
    public uint First4Bytes { get; }
    public ulong First8Bytes { get; }
    public Int128 First16Bytes { get; }

    public int Count<T>() where T : unmanaged;
    public T Item<T>(int index) where T : unmanaged;
    public Span<T> AsSpan<T>() where T : unmanaged;
}

[InlineArray(16)]
public struct HashOutput16 : IHashOutput, IEquatable<HashOutput16>
{
    private byte _byte0;

    public static int Size => 16;
    public Span<byte> Bytes => MemoryMarshal.CreateSpan(ref _byte0, Size);
    public uint First4Bytes => Unsafe.As<byte, uint>(ref _byte0);
    public ulong First8Bytes => Unsafe.As<byte, ulong>(ref _byte0);
    public Int128 First16Bytes => Unsafe.As<byte, Int128>(ref _byte0);

    public override string ToString()
        => this.Base16();

    public unsafe int Count<T>() where T : unmanaged
        => Size / sizeof(T);

    public T Item<T>(int index) where T : unmanaged
        => AsSpan<T>()[index];

    public Span<T> AsSpan<T>()
        where T : unmanaged
        => MemoryMarshal.CreateSpan(ref this, 1).Cast<HashOutput16, T>();

    // Equality

    public bool Equals(HashOutput16 other) => Bytes.SequenceEqual(other.Bytes);
    public override int GetHashCode() => (int)First4Bytes;
    public override bool Equals(object? obj) => obj is HashOutput16 other && Equals(other);
    public static bool operator ==(HashOutput16 left, HashOutput16 right) => left.Equals(right);
    public static bool operator !=(HashOutput16 left, HashOutput16 right) => !left.Equals(right);
}

[InlineArray(20)]
public struct HashOutput20 : IHashOutput, IEquatable<HashOutput20>
{
    private byte _byte0;

    public static int Size => 20;
    public Span<byte> Bytes => MemoryMarshal.CreateSpan(ref _byte0, Size);
    public uint First4Bytes => Unsafe.As<byte, uint>(ref _byte0);
    public ulong First8Bytes => Unsafe.As<byte, ulong>(ref _byte0);
    public Int128 First16Bytes => Unsafe.As<byte, Int128>(ref _byte0);

    public override string ToString()
        => this.Base16();

    public unsafe int Count<T>() where T : unmanaged
        => Size / sizeof(T);

    public T Item<T>(int index) where T : unmanaged
        => AsSpan<T>()[index];

    public Span<T> AsSpan<T>()
        where T : unmanaged
        => MemoryMarshal.CreateSpan(ref this, 1).Cast<HashOutput20, T>();

    // Equality

    public bool Equals(HashOutput20 other) => Bytes.SequenceEqual(other.Bytes);
    public override int GetHashCode() => (int)First4Bytes;
    public override bool Equals(object? obj) => obj is HashOutput20 other && Equals(other);
    public static bool operator ==(HashOutput20 left, HashOutput20 right) => left.Equals(right);
    public static bool operator !=(HashOutput20 left, HashOutput20 right) => !left.Equals(right);
}

[InlineArray(32)]
public struct HashOutput32 : IHashOutput, IEquatable<HashOutput32>
{
    private byte _byte0;

    public static int Size => 32;
    public Span<byte> Bytes => MemoryMarshal.CreateSpan(ref _byte0, Size);
    public uint First4Bytes => Unsafe.As<byte, uint>(ref _byte0);
    public ulong First8Bytes => Unsafe.As<byte, ulong>(ref _byte0);
    public Int128 First16Bytes => Unsafe.As<byte, Int128>(ref _byte0);

    public override string ToString()
        => this.Base16();

    public unsafe int Count<T>() where T : unmanaged
        => Size / sizeof(T);

    public T Item<T>(int index) where T : unmanaged
        => AsSpan<T>()[index];

    public Span<T> AsSpan<T>()
        where T : unmanaged
        => MemoryMarshal.CreateSpan(ref this, 1).Cast<HashOutput32, T>();

    // Equality

    public bool Equals(HashOutput32 other) => Bytes.SequenceEqual(other.Bytes);
    public override int GetHashCode() => (int)First4Bytes;
    public override bool Equals(object? obj) => obj is HashOutput32 other && Equals(other);
    public static bool operator ==(HashOutput32 left, HashOutput32 right) => left.Equals(right);
    public static bool operator !=(HashOutput32 left, HashOutput32 right) => !left.Equals(right);
}

[InlineArray(64)]
public struct HashOutput64 : IHashOutput, IEquatable<HashOutput64>
{
    private byte _byte0;

    public static int Size => 64;
    public Span<byte> Bytes => MemoryMarshal.CreateSpan(ref _byte0, Size);
    public uint First4Bytes => Unsafe.As<byte, uint>(ref _byte0);
    public ulong First8Bytes => Unsafe.As<byte, ulong>(ref _byte0);
    public Int128 First16Bytes => Unsafe.As<byte, Int128>(ref _byte0);

    public override string ToString()
        => this.Base16();

    public unsafe int Count<T>() where T : unmanaged
        => Size / sizeof(T);

    public T Item<T>(int index) where T : unmanaged
        => AsSpan<T>()[index];

    public Span<T> AsSpan<T>()
        where T : unmanaged
        => MemoryMarshal.CreateSpan(ref this, 1).Cast<HashOutput64, T>();

    // Equality

    public bool Equals(HashOutput64 other) => Bytes.SequenceEqual(other.Bytes);
    public override int GetHashCode() => (int)First4Bytes;
    public override bool Equals(object? obj) => obj is HashOutput64 other && Equals(other);
    public static bool operator ==(HashOutput64 left, HashOutput64 right) => left.Equals(right);
    public static bool operator !=(HashOutput64 left, HashOutput64 right) => !left.Equals(right);
}
