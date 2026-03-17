using System.Buffers;

namespace ActualChat.Collections;

public static class ArrayPoolExt
{
    public static ArrayLease<T> Lease<T>(this ArrayPool<T> pool, int minLength)
    {
        var array = pool.Rent(minLength);
        return new ArrayLease<T>(pool, array, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
    }

    public static ArrayOwner<T> LeaseArrayOwner<T>(this ArrayPool<T> pool, int minLength)
    {
        var array = pool.Rent(minLength);
        return ArrayOwner.New(pool, array, array.Length, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
    }
}
