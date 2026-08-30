using System.Buffers;

namespace ActualChat.Collections;

#pragma warning disable MA0022, RCS1210

/// <summary>
/// A ring buffer with 2x physical capacity that guarantees contiguous reads and writes.
/// Thread-safe for concurrent single-producer/single-consumer access.
/// </summary>
/// <remarks>
/// The buffer internally allocates 2*capacity slots. Writes always start at a physical
/// position below capacity, and may extend into [capacity, 2*capacity). When the write
/// position reaches or exceeds capacity, it wraps to 0 and a "wrap mark" is set.
/// TryRead copies data into a caller-provided Span, reading across wrap boundaries.
/// Bidirectional signaling: whenReadyToRead (for consumers) and whenReadyToWrite (for producers).
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
    private bool _isDisposed;

    private Task<int>? _whenReadyToRead;
    private Task<int>? _whenReadyToWrite;

    public int Capacity => _capacity;
    public int Count => _count;
    public int RemainingCapacity => _capacity - _count;
    public bool IsEmpty => _count == 0;
    public bool IsFull => _count >= _capacity;

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
        Task<int>? t1, t2;
        lock (_lock) {
            if (_isDisposed)
                return;

            _isDisposed = true;
            (t1, t2) = (_whenReadyToRead, _whenReadyToWrite);
            (_whenReadyToRead, _whenReadyToWrite) = (null, null);
            // Under the lock, because TryWrite and TryRead copy under it too: outside, a producer
            // could still be writing into an array the pool had handed to someone else.
            _pool.Return(_buffer, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
        TrySetCanceled(t1);
        TrySetCanceled(t2);
    }

    /// <summary>
    /// Writes as much of <paramref name="data"/> as possible to the buffer.
    /// Returns true if all data was written, false if buffer was full (partial write).
    /// </summary>
    public bool TryWrite(ReadOnlySpan<T> data)
        => TryWrite(data, out _, out _);

    /// <summary>
    /// Writes as much of <paramref name="data"/> as possible to the buffer.
    /// Returns true if all data was written, false if buffer was full (partial write).
    /// <paramref name="writtenCount"/> indicates how many items were actually written.
    /// </summary>
    public bool TryWrite(ReadOnlySpan<T> data, out int writtenCount)
        => TryWrite(data, out writtenCount, out _);

    /// <summary>
    /// Writes as much of <paramref name="data"/> as possible to the buffer.
    /// Returns true if all data was written, false if buffer was full (partial write).
    /// <paramref name="writtenCount"/> indicates how many items were actually written.
    /// When false, <paramref name="whenReadyToWrite"/> is a task that completes when the consumer frees space.
    /// </summary>
    public bool TryWrite(ReadOnlySpan<T> data, out int writtenCount, [NotNullWhen(false)] out Task? whenReadyToWrite)
    {
        if (data.IsEmpty) {
            writtenCount = 0;
            whenReadyToWrite = null;
            return true;
        }

        Task<int>? completedWhenReadyToWrite;
        lock (_lock) {
            if (_isDisposed) {
                // Cancelled, not null and not completed: null breaks NotNullWhen(false), and a
                // completed task turns a wait loop into a spin.
                writtenCount = 0;
                whenReadyToWrite = Task.FromCanceled(new CancellationToken(true));
                return false;
            }

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

                completedWhenReadyToWrite = _whenReadyToRead;
                _whenReadyToRead = null;
            }
            else
                completedWhenReadyToWrite = null;

            writtenCount = toWrite;
            if (writtenCount < data.Length)
                whenReadyToWrite = _whenReadyToWrite ??= AsyncTaskMethodBuilderExt.New<int>().Task;
            else
                whenReadyToWrite = null;
        }
        TrySetResult(completedWhenReadyToWrite, writtenCount);
        return writtenCount == data.Length;
    }

    /// <summary>
    /// Reads exactly <paramref name="destination"/>.Length items from the buffer.
    /// Returns true only when the full destination is filled.
    /// Returns false when buffer has fewer items than needed — data stays in buffer,
    /// <paramref name="whenReadyToRead"/> signals next write.
    /// </summary>
    public bool TryRead(Span<T> destination, [NotNullWhen(false)] out Task? whenReadyToRead)
    {
        if (destination.IsEmpty) {
            whenReadyToRead = null;
            return true;
        }

        Task<int>? completedWhenReadyToWrite;
        lock (_lock) {
            if (_isDisposed) {
                // See TryWrite - a completed task here spun WindowsAudioCapture's Enumerate.
                whenReadyToRead = Task.FromCanceled(new CancellationToken(true));
                return false;
            }

            if (_count < destination.Length) {
                _whenReadyToRead ??= AsyncTaskMethodBuilderExt.New<int>().Task;
                whenReadyToRead = _whenReadyToRead;
                return false;
            }

            var toRead = destination.Length;
            var written = 0;
            while (written < toRead) {
                Normalize();
                int contiguous;
                if (_wrapPos >= 0)
                    contiguous = _wrapPos - _readPos;
                else
                    contiguous = _writePos - _readPos;

                var n = Math.Min(toRead - written, contiguous);
                _buffer.AsSpan(_readPos, n).CopyTo(destination.Slice(written, n));
                _readPos += n;
                written += n;

                if (_wrapPos >= 0 && _readPos >= _wrapPos) {
                    _readPos = 0;
                    _wrapPos = -1;
                }
            }

            Interlocked.Add(ref _count, -toRead);

            completedWhenReadyToWrite = _whenReadyToWrite;
            _whenReadyToWrite = null;
        }
        TrySetResult(completedWhenReadyToWrite, destination.Length);
        whenReadyToRead = null;
        return true;
    }

    /// <summary>
    /// Returns a task that completes when new data is written to the buffer,
    /// or null if the buffer already contains data. Does not consume any data.
    /// </summary>
    public Task? WhenReadyToRead()
    {
        lock (_lock) {
            if (_isDisposed)
                return Task.FromCanceled(new CancellationToken(true));

            return _count > 0
                ? null
                : _whenReadyToRead ??= AsyncTaskMethodBuilderExt.New<int>().Task;
        }
    }

    public void Clear()
    {
        Task<int>? completedReadTask;
        lock (_lock) {
            _readPos = 0;
            _writePos = 0;
            _wrapPos = -1;
            Interlocked.Exchange(ref _count, 0);
            completedReadTask = _whenReadyToWrite;
            _whenReadyToWrite = null;
        }
        TrySetResult(completedReadTask, 0);
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
