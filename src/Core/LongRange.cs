using System;
using System.Text.Json.Serialization;
using Stl.Serialization;

namespace ActualChat
{
    public readonly struct LongRange : IEquatable<LongRange>
    {
        public long Start { get; } // Typically inclusive
        public long End { get; } // Typically exclusive
        [JsonIgnore, Newtonsoft.Json.JsonIgnore]
        public long Size => End - Start;

        [JsonConstructor, Newtonsoft.Json.JsonConstructor]
        public LongRange(long start, long end)
        {
            Start = start;
            End = end;
        }

        public void Deconstruct(out long start, out long end)
        {
            start = Start;
            end = End;
        }

        public override string ToString()
            => SystemJsonSerializer.Default.Write(this, GetType());

        public static LongRange Parse(string value)
            => SystemJsonSerializer.Default.Reader.Read<LongRange>(value);

        public static implicit operator LongRange((long Start, long End) source)
            => new(source.Start, source.End);

        // Equality

        public bool Equals(LongRange other)
            => Start == other.Start && End == other.End;
        public override bool Equals(object? obj)
            => obj is LongRange other && Equals(other);
        public override int GetHashCode()
            => HashCode.Combine(Start, End);
        public static bool operator ==(LongRange left, LongRange right) => left.Equals(right);
        public static bool operator !=(LongRange left, LongRange right) => !left.Equals(right);
    }
}
