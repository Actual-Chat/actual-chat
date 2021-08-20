using System;
using System.Text.Json.Serialization;
using Stl.Serialization;
using Stl.Time;

namespace ActualChat
{
    public readonly struct TimeRange : IEquatable<TimeRange>
    {
        public Moment Start { get; }
        public Moment End { get; }
        [JsonIgnore, Newtonsoft.Json.JsonIgnore]
        public TimeSpan Duration => End - Start;

        [JsonConstructor, Newtonsoft.Json.JsonConstructor]
        public TimeRange(Moment start, Moment end)
        {
            Start = start;
            End = end;
        }

        public void Deconstruct(out Moment start, out Moment end)
        {
            start = Start;
            end = End;
        }

        public override string ToString()
            => SystemJsonSerializer.Default.Write(this, GetType());

        public static TimeRange Parse(string value)
            => SystemJsonSerializer.Default.Reader.Read<TimeRange>(value);

        public static implicit operator TimeRange((Moment Start, Moment End) source)
            => new(source.Start, source.End);
        public static implicit operator TimeRange((Moment Start, TimeSpan Duration) source)
            => new(source.Start, source.Start + source.Duration);

        // Equality

        public bool Equals(TimeRange other)
            => Start.Equals(other.Start) && End.Equals(other.End);
        public override bool Equals(object? obj)
            => obj is TimeRange other && Equals(other);
        public override int GetHashCode()
            => HashCode.Combine(Start, End);
        public static bool operator ==(TimeRange left, TimeRange right) => left.Equals(right);
        public static bool operator !=(TimeRange left, TimeRange right) => !left.Equals(right);
    }
}
