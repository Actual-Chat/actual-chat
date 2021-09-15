using System;
using MessagePack;

namespace ActualChat.Streaming
{
    [MessagePackObject]
    public readonly struct StreamId : IEquatable<StreamId>
    {
        [Key(0)]
        public string Value { get; } // TODO(AY): -> Symbol?

        public StreamId(AudioRecordId id, int index)
            : this ($"{id.Value}-{index:D4}")
        { }

        private StreamId(string value)
            => Value = value;

        public override string ToString() => Value;

        public static implicit operator StreamId(string value) => new(value);
        public static implicit operator string(StreamId id) => id.Value;

        // Equality
        public bool Equals(StreamId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is StreamId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(StreamId left, StreamId right) => left.Equals(right);
        public static bool operator !=(StreamId left, StreamId right) => !left.Equals(right);
    }
}
