using System.Buffers;

namespace ActualChat.Collections;

public class BlockRingBuffer<T>
{
    private readonly T[] _buffer; // Fixed-size backing array
    private readonly int _mask;
    private int _writeIndex; // Producer's write position
    private int _readIndex;  // Consumer's read position
    private int _pendingReadIndex; // Tracks pending consumption

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

    public BlockRingBuffer(int minCapacity)
        : this(new T[Bits.GreaterOrEqualPowerOf2((ulong)Math.Max(2, minCapacity + 1))])
    { }

    public BlockRingBuffer(T[] buffer)
    {
        if (!Bits.IsPowerOf2((ulong)buffer.Length))
            throw new ArgumentOutOfRangeException(nameof(buffer));
        if (buffer.Length < 2)
            throw new ArgumentOutOfRangeException(nameof(buffer), "Buffer must have at least 2 elements");

        // Use one less than buffer length to distinguish empty from full
        _mask = buffer.Length - 1;
        _buffer = buffer;
        _writeIndex = 0;
        _readIndex = 0;
        _pendingReadIndex = 0;
    }

    public bool TryPush(ReadOnlySpan<T> data)
    {
        var length = data.Length;
        if (length == 0)
            return true;

        if (length > Capacity)
            return false; // Can't produce larger than capacity

        // For SPSC, read consumer position once
        var currentRead = Volatile.Read(ref _readIndex);
        var currentWrite = Volatile.Read(ref _writeIndex);

        // Calculate available space
        var used = (currentWrite - currentRead) & _mask;
        if (used + length > Capacity)
            return false; // Not enough space

        // Write data with wraparound support
        var writePos = currentWrite & _mask;
        if (writePos + length <= _buffer.Length)
            // No wraparound needed
            data.CopyTo(_buffer.AsSpan(writePos, length));
        else {
            // Handle wraparound
            var firstPart = _buffer.Length - writePos;
            data[..firstPart].CopyTo(_buffer.AsSpan(writePos, firstPart));
            data[firstPart..].CopyTo(_buffer.AsSpan(0, length - firstPart));
        }

        // Update write index (single producer, so this is safe)
        Volatile.Write(ref _writeIndex, currentWrite + length);

        return true;
    }

    public bool TryPull(int length, [NotNullWhen(true)] out IMemoryOwner<T>? block)
    {
        if (length <= 0) {
            block = null;
            return false;
        }

        // Check if there's already a pending consumption
        var currentPending = Volatile.Read(ref _pendingReadIndex);
        var currentRead = Volatile.Read(ref _readIndex);

        // If there's pending consumption, we can't provide a new block
        if (currentPending != currentRead) {
            block = null;
            return false;
        }

        var currentWrite = Volatile.Read(ref _writeIndex);
        var available = (currentWrite - currentRead) & _mask;

        if (available < length) {
            block = null;
            return false;
        }

        // Reserve this consumption by updating pending read index
        if (Interlocked.CompareExchange(ref _pendingReadIndex, currentRead + length, currentRead) != currentRead) {
            // Another thread updated pending read index, fail
            block = null;
            return false;
        }

        var readPos = currentRead & _mask;

        if (readPos + length <= _buffer.Length) {
            // No wraparound, can use zero-copy
            block = new ConsumableBlock(new Memory<T>(_buffer, readPos, length), buffer: this);
            return true;
        }

        // Wraparound required, need to copy to contiguous memory
        var rented = ArrayPool<T>.Shared.Rent(length);
        try {
            var firstPart = _buffer.Length - readPos;
            _buffer.AsSpan(readPos, firstPart).CopyTo(rented.AsSpan(0, firstPart));
            _buffer.AsSpan(0, length - firstPart).CopyTo(rented.AsSpan(firstPart, length - firstPart));
            block = new ConsumableBlock(new Memory<T>(rented, 0, length), rented, this);
            return true;
        }
        catch {
            ArrayPool<T>.Shared.Return(rented);
            // Reset pending read index on failure
            Volatile.Write(ref _pendingReadIndex, currentRead);
            throw;
        }
    }

    public IMemoryOwner<T> Pull(int length)
        => TryPull(length, out var block)
            ? block
            : throw StandardError.Unavailable("Not enough data to pull");

    public ReadOnlySpan<T> GetAvailableContinuousData()
    {
        var readIndex = Volatile.Read(ref _readIndex);
        var writeIndex = Volatile.Read(ref _writeIndex);

        var available = (writeIndex - readIndex) & _mask;
        if (available == 0)
            return ReadOnlySpan<T>.Empty;

        var readPos = readIndex & _mask;
        var contiguous = Math.Min(available, _buffer.Length - readPos);
        return _buffer.AsSpan(readPos, contiguous);
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
            throw new InvalidOperationException("Cannot commit more than available data");

        Volatile.Write(ref _readIndex, currentRead + length);
        Volatile.Write(ref _pendingReadIndex, currentRead + length);
    }

    // Nested types

    private readonly struct ConsumableBlock : IMemoryOwner<T>
    {
        private readonly T[]? _rented;
        private readonly BlockRingBuffer<T>? _buffer;

        public Memory<T> Memory { get; }

        internal ConsumableBlock(Memory<T> memory, T[]? rented = null, BlockRingBuffer<T>? buffer = null)
        {
            Memory = memory;
            _rented = rented;
            _buffer = buffer;
        }

        public void Dispose()
        {
            if (_rented != null)
                ArrayPool<T>.Shared.Return(_rented);

            // Auto-commit the consumed length when disposing
            if (_buffer != null && !Memory.IsEmpty)
                _buffer.CommitConsume(Memory.Length);
        }

        public static implicit operator ReadOnlyMemory<T>(ConsumableBlock block) => block.Memory;
        public bool IsValid => !Memory.IsEmpty;
    }
}
