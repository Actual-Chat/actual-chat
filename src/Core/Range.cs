using System;
using System.Text.Json.Serialization;
using Stl.Serialization;

namespace ActualChat
{
    public readonly struct Range<T> : IEquatable<Range<T>>
        where T : notnull
    {
        public T Start { get; } // Typically inclusive
        public T End { get; } // Typically exclusive

        [JsonConstructor, Newtonsoft.Json.JsonConstructor]
        public Range(T start, T end)
        {
            Start = start;
            End = end;
        }

        public void Deconstruct(out T start, out T end)
        {
            start = Start;
            end = End;
        }

        public override string ToString()
            => SystemJsonSerializer.Default.Write(this, GetType());

        public static Range<T> Parse(string value)
            => SystemJsonSerializer.Default.Reader.Read<Range<T>>(value);

        public static implicit operator Range<T>((T Start, T End) source)
            => new(source.Start, source.End);

        // Equality

        public bool Equals(Range<T> other)
            => Start.Equals(other.Start) && End.Equals(other.End);
        public override bool Equals(object? obj)
            => obj is Range<T> other && Equals(other);
        public override int GetHashCode()
            => HashCode.Combine(Start, End);
        public static bool operator ==(Range<T> left, Range<T> right)
            => left.Equals(right);
        public static bool operator !=(Range<T> left, Range<T> right)
            => !left.Equals(right);
    }
}
