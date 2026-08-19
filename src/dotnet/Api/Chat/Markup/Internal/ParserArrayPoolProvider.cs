using System.Buffers;
using System.Numerics;
using Pidgin;
using Pidgin.Configuration;

namespace ActualChat.Chat;

// Pidgin rents a buffer for its "expected tokens" bookkeeping on every failed alternative - which
// is most of what a backtracking grammar does - and for the char runs behind ManyString. A
// sampling profile put ArrayPool.Shared's Rent/Return plus its Monitor traffic at ~25% of parse
// time. A parse runs on one thread and returns buffers in the order it took them, so a
// thread-static stack per size class serves the same purpose without any of that bookkeeping.

/// <summary>
/// The array pool <see cref="MarkupParser"/> hands to Pidgin. Only the two element types Pidgin
/// actually rents for a char grammar are pooled; anything else falls back to the shared pool.
/// </summary>
internal sealed class ParserArrayPoolProvider : IArrayPoolProvider
{
    public static readonly IArrayPoolProvider Instance = new ParserArrayPoolProvider();
    public ArrayPool<T> GetArrayPool<T>()
    {
        // Both comparisons fold away at JIT time, so this is not a runtime type check.
        if (typeof(T) == typeof(Expected<char>))
            return (ArrayPool<T>)(object)ExpectedCharArrayPool.Instance;
        if (typeof(T) == typeof(char))
            return (ArrayPool<T>)(object)CharArrayPool.Instance;

        return ArrayPool<T>.Shared;
    }
}

/// <summary>
/// Thread-static buffer stacks for Pidgin's <see cref="Expected{TToken}"/> lists. Deliberately
/// not generic: a <c>[ThreadStatic]</c> field on a generic type is resolved through a runtime
/// helper on every access, while this one compiles to a direct TLS read.
/// </summary>
internal sealed class ExpectedCharArrayPool : ArrayPool<Expected<char>>
{
    [ThreadStatic]
    private static Expected<char>[][][]? _buckets;
    [ThreadStatic]
    private static int[]? _counts;
    public static readonly ArrayPool<Expected<char>> Instance = new ExpectedCharArrayPool();

    public override Expected<char>[] Rent(int minimumLength)
    {
        var bucket = ParserArrayPool.GetBucket(minimumLength);
        if (bucket < 0)
            return Shared.Rent(minimumLength);

        var counts = _counts;
        if (counts != null && counts[bucket] > 0) {
            var index = counts[bucket] - 1;
            var buffer = _buckets![bucket][index];
            if (buffer != null) {
                _buckets[bucket][index] = null!;
                counts[bucket] = index;
                return buffer;
            }
        }

        return new Expected<char>[ParserArrayPool.GetLength(bucket)];
    }

    public override void Return(Expected<char>[] array, bool clearArray = false)
    {
        var bucket = ParserArrayPool.GetExactBucket(array.Length);
        if (bucket < 0) {
            Shared.Return(array, clearArray);
            return;
        }

        if (clearArray)
            Array.Clear(array);

        var counts = _counts ??= new int[ParserArrayPool.BucketCount];
        var count = counts[bucket];
        if (count >= ParserArrayPool.MaxPerBucket)
            return; // Over the per-thread budget, so let the GC have it

        (_buckets ??= ParserArrayPool.CreateBuckets<Expected<char>>())[bucket][count] = array;
        counts[bucket] = count + 1;
    }
}

/// <summary>
/// Thread-static buffer stacks for the char runs behind <c>ManyString</c> - see
/// <see cref="ExpectedCharArrayPool"/> for why this isn't one generic type.
/// </summary>
internal sealed class CharArrayPool : ArrayPool<char>
{
    [ThreadStatic]
    private static char[][][]? _buckets;
    [ThreadStatic]
    private static int[]? _counts;
    public static readonly ArrayPool<char> Instance = new CharArrayPool();

    public override char[] Rent(int minimumLength)
    {
        var bucket = ParserArrayPool.GetBucket(minimumLength);
        if (bucket < 0)
            return Shared.Rent(minimumLength);

        var counts = _counts;
        if (counts != null && counts[bucket] > 0) {
            var index = counts[bucket] - 1;
            var buffer = _buckets![bucket][index];
            if (buffer != null) {
                _buckets[bucket][index] = null!;
                counts[bucket] = index;
                return buffer;
            }
        }

        return new char[ParserArrayPool.GetLength(bucket)];
    }

    public override void Return(char[] array, bool clearArray = false)
    {
        var bucket = ParserArrayPool.GetExactBucket(array.Length);
        if (bucket < 0) {
            Shared.Return(array, clearArray);
            return;
        }

        if (clearArray)
            Array.Clear(array);

        var counts = _counts ??= new int[ParserArrayPool.BucketCount];
        var count = counts[bucket];
        if (count >= ParserArrayPool.MaxPerBucket)
            return;

        (_buckets ??= ParserArrayPool.CreateBuckets<char>())[bucket][count] = array;
        counts[bucket] = count + 1;
    }
}

/// <summary>
/// Size-class arithmetic and buffer allocation shared by the pools above.
/// </summary>
internal static class ParserArrayPool
{
    // Pidgin's PooledList starts at 16 items; 16K is past anything this grammar asks it for,
    // and the cap bounds what a parsing thread can hold on to.
    public const int MinLog2 = 4;
    public const int MaxLog2 = 14;
    public const int BucketCount = MaxLog2 - MinLog2 + 1;
    public const int MaxPerBucket = 8;

    public static int GetLength(int bucket)
        => 1 << (bucket + MinLog2);

    public static T[][][] CreateBuckets<T>()
    {
        var buckets = new T[BucketCount][][];
        for (var i = 0; i < BucketCount; i++)
            buckets[i] = new T[MaxPerBucket][];
        return buckets;
    }

    // Rounds up to the next power of two, so the rented buffer is always >= minimumLength
    public static int GetBucket(int minimumLength)
    {
        if (minimumLength is <= 0 or > (1 << MaxLog2))
            return -1;

        var log2 = BitOperations.Log2((uint)Math.Max(minimumLength - 1, 1)) + 1;
        return Math.Max(log2, MinLog2) - MinLog2;
    }

    public static int GetExactBucket(int length)
    {
        if (!BitOperations.IsPow2(length))
            return -1;

        var log2 = BitOperations.Log2((uint)length);
        return log2 is >= MinLog2 and <= MaxLog2 ? log2 - MinLog2 : -1;
    }
}
