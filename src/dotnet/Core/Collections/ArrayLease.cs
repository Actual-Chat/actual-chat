using System.Buffers;

namespace ActualChat.Collections;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly struct ArrayLease<T>(ArrayPool<T> pool, T[] array, bool mustClear) : IDisposable
{
    public readonly ArrayPool<T> Pool = pool;
    public readonly T[] Array = array;
    public readonly bool MustClear = mustClear;

    public Memory<T> Memory {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Array.AsMemory();
    }

    public Span<T> Span {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Array.AsSpan();
    }

    public ArraySegment<T> ArraySegment {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(Array);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
        => Pool.Return(Array, MustClear);
}
