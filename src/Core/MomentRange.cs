using System;
using System.Text.Json.Serialization;
using Stl.Serialization;
using Stl.Time;

namespace ActualChat
{
    public readonly struct MomentRange : IEquatable<MomentRange>
    {
        public Moment Start { get; } // Typically inclusive
        public Moment End { get; } // Typically exclusive
        [JsonIgnore, Newtonsoft.Json.JsonIgnore]
        public TimeSpan Duration => End - Start;

        [JsonConstructor, Newtonsoft.Json.JsonConstructor]
        public MomentRange(Moment start, Moment end)
        {
            Start = start;
            End = end;
        }

        public MomentRange(Moment start, TimeSpan duration)
        {
            Start = start;
            End = start + duration;
        }

        public void Deconstruct(out Moment start, out Moment end)
        {
            start = Start;
            end = End;
        }

        public override string ToString()
            => SystemJsonSerializer.Default.Write(this, GetType());

        public static MomentRange Parse(string value)
            => SystemJsonSerializer.Default.Reader.Read<MomentRange>(value);

        public static implicit operator MomentRange((Moment Start, Moment End) source)
            => new(source.Start, source.End);
        public static implicit operator MomentRange((Moment Start, TimeSpan Duration) source)
            => new(source.Start, source.Duration);

        // Equality

        public bool Equals(MomentRange other)
            => Start == other.Start && End == other.End;
        public override bool Equals(object? obj)
            => obj is MomentRange other && Equals(other);
        public override int GetHashCode()
            => HashCode.Combine(Start, End);
        public static bool operator ==(MomentRange left, MomentRange right) => left.Equals(right);
        public static bool operator !=(MomentRange left, MomentRange right) => !left.Equals(right);
    }
}
