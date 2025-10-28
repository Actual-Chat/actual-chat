using System.Buffers;

namespace ActualChat.Collections;

public class BlockRingBuffer<T>: IDisposable
{
    private readonly IMemoryOwner<T> _bufferOwner;
    private readonly int _mask;
    private int _writeIndex; // Producer's write position
    private int _readIndex;  // Consumer's read position
    private int _pendingReadIndex; // Tracks pending consumption
    private TaskCompletionSource _whenPulledTcs;
    private TaskCompletionSource _whenPushedTcs;

    public int Capacity => _mask;

    public int Count {
        get {
            var read = Volatile.Read(ref _readIndex);
            var write = Volatile.Read(ref _writeIndex);
            return (write - read) & _mask;
        }
    }

    public bool IsEmpty => _writeIndex == _readIndex;
    public bool IsFull => Count >= Capacity;
    public int RemainingCapacity => Math.Max(0, Capacity - Count);
    public bool HasRemainingCapacity => !IsFull;

    public Task WhenPulled => _whenPulledTcs.Task;
    public Task WhenPushed => _whenPushedTcs.Task;

    public BlockRingBuffer(int minCapacity)
        : this(MemoryPool<T>.Shared.Rent((int)Bits.GreaterOrEqualPowerOf2((ulong)Math.Max(2, minCapacity + 1))))
    { }

    private BlockRingBuffer(IMemoryOwner<T> bufferOwner)
    {
        var buffer = bufferOwner.Memory;
        if (!Bits.IsPowerOf2((ulong)buffer.Length))
            throw new ArgumentOutOfRangeException(nameof(buffer));
        if (buffer.Length < 2)
            throw new ArgumentOutOfRangeException(nameof(buffer), "Buffer must have at least 2 elements");

        // Use one less than buffer length to distinguish empty from full
        _mask = buffer.Length - 1;
        _bufferOwner = bufferOwner;
        _writeIndex = 0;
        _readIndex = 0;
        _pendingReadIndex = 0;
        _whenPushedTcs = TaskCompletionSourceExt.New();
        _whenPulledTcs = TaskCompletionSourceExt.New();
        _whenPulledTcs.TrySetResult();
    }
    public void Dispose()
    {
        _whenPulledTcs.TrySetCanceled();
        _whenPushedTcs.TrySetCanceled();
        _bufferOwner.Dispose();
    }

    public bool TryPush(ReadOnlySpan<T> data)
    {
        var length = data.Length;
        if (length == 0)
            return true;

        // For SPSC, read consumer position once
        var currentRead = Volatile.Read(ref _readIndex);
        var currentWrite = Volatile.Read(ref _writeIndex);

        // Calculate available space
        var used = (currentWrite - currentRead) & _mask;
        if (used + length > Capacity) {
            _whenPulledTcs = TaskCompletionSourceExt.New();
            return false; // Not enough space
        }

        // Write data with wraparound support
        var buffer = _bufferOwner.Memory;
        var writePos = currentWrite & _mask;
        if (writePos + length <= buffer.Length)
            // No wraparound needed
            data.CopyTo(buffer.Span.Slice(writePos, length));
        else {
            // Handle wraparound
            var firstPart = buffer.Length - writePos;
            data[..firstPart].CopyTo(buffer.Span.Slice(writePos, firstPart));
            data[firstPart..].CopyTo(buffer.Span[..(length - firstPart)]);
        }

        // Update write index (single producer, so this is safe)
        Volatile.Write(ref _writeIndex, currentWrite + length);
        _whenPushedTcs?.TrySetResult();

        return true;
    }

    public bool TryPull(int length, [NotNullWhen(true)] out IMemoryOwner<T>? block)
    {
        if (length <= 0) {
            block = null;
            return false;
        }

        // Check if there's already a pending consumption
        var currentRead = Volatile.Read(ref _pendingReadIndex);
        var currentWrite = Volatile.Read(ref _writeIndex);
        var available = (currentWrite - currentRead) & _mask;

        if (available < length) {
            block = null;
            _whenPushedTcs = TaskCompletionSourceExt.New();
            return false;
        }

        // Reserve this consumption by updating pending read index
        if (Interlocked.CompareExchange(ref _pendingReadIndex, currentRead + length, currentRead) != currentRead) {
            // Another thread updated pending read index, fail
            // We should not get here as we use a single consumer, just in case
            block = null;
            _whenPushedTcs = TaskCompletionSourceExt.New();
            return false;
        }

        var readPosition = currentRead & _mask;
        var buffer = _bufferOwner.Memory;
        if (readPosition + length <= buffer.Length) {
            // No wraparound, can use zero-copy
            block = new ConsumableBlock(buffer.Slice(readPosition, length), buffer: this);
            return true;
        }

        // Wraparound required, need to copy to contiguous memory
        var owner = MemoryPool<T>.Shared.Rent(length);
        try {
            var firstPart = buffer.Length - readPosition;
            var span = owner.Memory.Span;
            buffer.Span.Slice(readPosition, firstPart).CopyTo(span[..firstPart]);
            buffer.Span[..(length - firstPart)].CopyTo(span.Slice(firstPart, length - firstPart));
            block = new ConsumableBlock(owner.Memory[..length], owner, this);
            return true;
        }
        catch {
            owner.Dispose();
            // Reset pending read index on failure
            Volatile.Write(ref _pendingReadIndex, currentRead);
            throw;
        }
    }

    public IMemoryOwner<T> Pull(int length)
        => TryPull(length, out var block)
            ? block
            : throw StandardError.Unavailable("Not enough data to pull");

    public void Clear()
    {
        _readIndex = 0;
        _writeIndex = 0;
        _pendingReadIndex = 0;
        _whenPulledTcs.TrySetResult();
        // there is no need to reset _whenPushedTcs as we are trying to push into an empty buffer
        // _whenPushedTcs.TrySetResult();
    }


    // Internal methods

    internal ReadOnlySpan<T> GetAvailableContinuousData()
    {
        var readIndex = Volatile.Read(ref _readIndex);
        var writeIndex = Volatile.Read(ref _writeIndex);

        var available = (writeIndex - readIndex) & _mask;
        if (available == 0)
            return ReadOnlySpan<T>.Empty;

        var buffer = _bufferOwner.Memory;
        var readPos = readIndex & _mask;
        var contiguous = Math.Min(available, buffer.Length - readPos);
        return buffer.Span.Slice(readPos, contiguous);
    }

    // Private methods

    private void CommitConsume(int length)
    {
        if (length <= 0)
            return;

        var currentRead = Volatile.Read(ref _readIndex);
        var currentWrite = Volatile.Read(ref _writeIndex);

        var available = (currentWrite - currentRead) & _mask;
        if (available < length)
            throw StandardError.Internal("Cannot commit more than available data");

        Volatile.Write(ref _readIndex, currentRead + length);
        _whenPulledTcs?.TrySetResult();
    }

    // Nested types

    private readonly struct ConsumableBlock : IMemoryOwner<T>
    {
        private readonly IMemoryOwner<T>? _rented;
        private readonly BlockRingBuffer<T>? _buffer;

        public Memory<T> Memory { get; }

        internal ConsumableBlock(Memory<T> memory, IMemoryOwner<T>? rented = null, BlockRingBuffer<T>? buffer = null)
        {
            Memory = memory;
            _rented = rented;
            _buffer = buffer;
        }

        public void Dispose()
        {
            _rented?.Dispose();

            // Auto-commit the consumed length when disposing
            if (_buffer != null && !Memory.IsEmpty)
                _buffer.CommitConsume(Memory.Length);
        }

        public static implicit operator ReadOnlyMemory<T>(ConsumableBlock block) => block.Memory;
    }
}
