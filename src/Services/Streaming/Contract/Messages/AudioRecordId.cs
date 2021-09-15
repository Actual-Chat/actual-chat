using System;
using MessagePack;

namespace ActualChat.Streaming
{
    [MessagePackObject]
    public readonly struct AudioRecordId : IEquatable<AudioRecordId>
    {
        [Key(0)]
        public string Value { get; } // TODO(AY): -> Symbol?

        public AudioRecordId(string value)
            => Value = value;

        public override string ToString() => Value;

        public static implicit operator AudioRecordId(string value) => new(value);
        public static implicit operator string(AudioRecordId id) => id.Value;

        // Equality
        public bool Equals(AudioRecordId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is AudioRecordId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(AudioRecordId left, AudioRecordId right) => left.Equals(right);
        public static bool operator !=(AudioRecordId left, AudioRecordId right) => !left.Equals(right);
    }
}
