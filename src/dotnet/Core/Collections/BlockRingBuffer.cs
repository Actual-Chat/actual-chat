using System.Buffers;

namespace ActualChat.Collections;

#pragma warning disable MA0022, RCS1210

/// <summary>
/// A ring buffer with 2x physical capacity that guarantees contiguous reads and writes.
/// Thread-safe for concurrent producer/consumer access.
/// </summary>
/// <remarks>
/// The buffer internally allocates 2*capacity slots. Writes always start at a physical
/// position below capacity, and may extend into [capacity, 2*capacity). When the write
/// position reaches or exceeds capacity, it wraps to 0 and a "wrap mark" is set.
/// The reader follows the same wrap mark, ensuring reads are always contiguous.
/// </remarks>
public sealed class BlockRingBuffer<T> : IDisposable
{
    private readonly Lock _lock = new();
    private readonly ArrayPool<T> _pool;
    private readonly T[] _buffer;
    private readonly int _capacity;

    private int _readPos;   // physical position in [0, 2*capacity)
    private int _writePos;  // physical position in [0, 2*capacity)
    private int _wrapPos;   // physical position where data ends before gap; -1 = no gap
    private volatile int _count; // valid readable items

    private Task<int>? _whenWrittenTask;

    public int Capacity => _capacity;
    public int Count => _count;

    public BlockRingBuffer(int minCapacity, ArrayPool<T>? pool = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minCapacity);

        _pool = pool ?? ArrayPool<T>.Shared;
        _buffer = _pool.Rent(minCapacity * 2);
        _capacity = _buffer.Length / 2;
        _wrapPos = -1;
    }

    public void Dispose()
    {
        Task<int>? wwt;
        lock (_lock) {
            wwt = _whenWrittenTask;
            _whenWrittenTask = null;
        }
        TrySetCanceled(wwt);
        _pool.Return(_buffer, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
    }

    /// <summary>
    /// Writes as much of <paramref name="data"/> as possible to the buffer.
    /// Returns true if all data was written, false if buffer was full (partial write).
    /// </summary>
    public bool TryWrite(ReadOnlySpan<T> data)
        => TryWrite(data, out _);

    /// <summary>
    /// Writes as much of <paramref name="data"/> as possible to the buffer.
    /// Returns true if all data was written, false if buffer was full (partial write).
    /// <paramref name="writtenCount"/> indicates how many items were actually written.
    /// </summary>
    public bool TryWrite(ReadOnlySpan<T> data, out int writtenCount)
    {
        if (data.IsEmpty) {
            writtenCount = 0;
            return true;
        }

        Task<int>? completedTask;
        lock (_lock) {
            var free = _capacity - _count;
            var toWrite = Math.Min(data.Length, free);

            if (toWrite > 0) {
                var remaining = data[..toWrite];
                while (remaining.Length > 0) {
                    Normalize();
                    int contiguous;
                    if (_wrapPos >= 0)
                        contiguous = _readPos - _writePos;
                    else
                        contiguous = _capacity - _writePos;

                    var n = Math.Min(remaining.Length, contiguous);
                    remaining[..n].CopyTo(_buffer.AsSpan(_writePos, n));
                    _writePos += n;
                    Interlocked.Add(ref _count, n);

                    if (_writePos >= _capacity && _wrapPos < 0) {
                        _wrapPos = _writePos;
                        _writePos = 0;
                    }
                    remaining = remaining[n..];
                }

                completedTask = _whenWrittenTask;
                _whenWrittenTask = null;
            }
            else
                completedTask = null;

            writtenCount = toWrite;
        }
        TrySetResult(completedTask, writtenCount);
        return writtenCount == data.Length;
    }

    /// <summary>
    /// Reads up to <paramref name="maxLength"/> contiguous items from the buffer.
    /// Returns true if data was read (<paramref name="data"/> is non-empty).
    /// Returns false if buffer is empty; <paramref name="whenReadyToRead"/> is a task to await before retrying.
    /// The read position is advanced immediately; the returned memory is valid
    /// until the buffer wraps (safe in SPSC as long as the caller processes promptly).
    /// </summary>
    public bool TryRead(int maxLength, out ReadOnlyMemory<T> data, [NotNullWhen(false)] out Task? whenReadyToRead)
    {
        if (maxLength <= 0) {
            data = default;
            whenReadyToRead = null;
            return true;
        }

        lock (_lock) {
            if (_count == 0) {
                data = default;
                _whenWrittenTask ??= AsyncTaskMethodBuilderExt.New<int>().Task;
                whenReadyToRead = _whenWrittenTask;
                return false;
            }

            Normalize();
            int contiguous;
            if (_wrapPos >= 0)
                contiguous = _wrapPos - _readPos;
            else
                contiguous = _writePos - _readPos;

            var n = Math.Min(Math.Min(maxLength, contiguous), _count);
            data = new ReadOnlyMemory<T>(_buffer, _readPos, n);

            _readPos += n;
            if (_wrapPos >= 0 && _readPos >= _wrapPos) {
                _readPos = 0;
                _wrapPos = -1;
            }

            Interlocked.Add(ref _count, -n);
        }
        whenReadyToRead = null;
        return true;
    }

    public void Clear()
    {
        lock (_lock) {
            _readPos = 0;
            _writePos = 0;
            _wrapPos = -1;
            Interlocked.Exchange(ref _count, 0);
        }
    }

    // Private methods

    // Must be called under _lock
    private void Normalize()
    {
        if (_wrapPos < 0 || _readPos < _wrapPos)
            return;

        _readPos = 0;
        _wrapPos = -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void TrySetResult(Task<int>? task, int result)
    {
        if (task != null)
            AsyncTaskMethodBuilderExt.FromTask(task).TrySetResult(result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void TrySetCanceled(Task<int>? task)
    {
        if (task != null)
            AsyncTaskMethodBuilderExt.FromTask(task).TrySetCanceled();
    }
}
