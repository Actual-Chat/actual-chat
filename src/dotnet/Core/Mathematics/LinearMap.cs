using System.Numerics;
using MemoryPack;

namespace ActualChat.Mathematics;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public readonly partial struct LinearMap
{
    public static LinearMap Zero { get; } = new(Vector2.Zero);

    private readonly float[]? _data;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public float[] Data => _data ?? Array.Empty<float>();

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public ReadOnlySpan<Vector2> Points {
        get {
            var data = _data;
            if (data == null)
                return ReadOnlySpan<Vector2>.Empty;
            ref var dataRef =
                ref Unsafe.As<float, Vector2>(
                    ref MemoryMarshal.GetArrayDataReference(data));
            return MemoryMarshal.CreateReadOnlySpan(ref dataRef, data.Length >> 1);
        }
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public int Length => _data == null ? 0 : _data.Length >> 1;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public bool IsEmpty => _data == null || _data.Length == 0;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public bool IsDegenerate => Data.Length < 4;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public Range<float> XRange => IsEmpty ? default : new Range<float>(Points[0].X, Points[^1].X);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public Range<float> YRange => IsEmpty ? default : new Range<float>(Points[0].Y, Points[^1].Y);

    public Vector2 this[int index] => Points[index];
    public LinearMap this[Range range] => new(Points[range]);

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public LinearMap(params float[] data)
    {
        if ((data.Length & 1) != 0)
            throw new ArgumentOutOfRangeException(nameof(data));

        _data = data;
    }

    public LinearMap(ReadOnlySpan<Vector2> points)
    {
        if (points.Length == 0) {
            _data = Array.Empty<float>();
            return;
        }

        var data = MemoryMarshal.Cast<Vector2, float>(points);
        _data = new float[data.Length];
        data.CopyTo(_data);
    }

    public LinearMap(Vector2 point)
        : this(point.X, point.Y) { }
    public LinearMap(Vector2 point1, Vector2 point2)
        : this(point1.X, point1.Y, point2.X, point2.Y) { }
    public LinearMap(Vector2 point1, Vector2 point2, Vector2 point3)
        : this(point1.X, point1.Y, point2.X, point2.Y, point3.X, point3.Y) { }

    public LinearMap(ReadOnlySpan<Vector2> head, ReadOnlySpan<Vector2> tail)
    {
        if (head.Length == 0 && tail.Length == 0)
            _data = Array.Empty<float>();
        else {
            _data = new float[(head.Length + tail.Length) << 1];
            var fHead = MemoryMarshal.Cast<Vector2, float>(head);
            var fTail = MemoryMarshal.Cast<Vector2, float>(tail);
            fHead.CopyTo(_data);
            fTail.CopyTo(_data.AsSpan(fHead.Length));
        }
    }

    public override string ToString()
        => $"{{{ Data.ToDelimitedString() }}}";

    public bool IsValid()
        => Points.IsStrictlyIncreasingXSequence();

    public bool IsInversionValid()
        => Points.IsStrictlyIncreasingYSequence();

    public LinearMap RequireValid(bool requireInvertible = false)
    {
        if (!IsValid())
            throw StandardError.Constraint($"Invalid {GetType().GetName()}.");
        if (requireInvertible && !IsInversionValid())
            throw StandardError.Constraint($"Invalid {GetType().GetName()}.");
        return this;
    }

    public float? TryMap(float x, out int indexOfLowerOrEqualX)
    {
        var points = Points;
        indexOfLowerOrEqualX = points.IndexOfLowerOrEqualX(x);
        if (indexOfLowerOrEqualX < 0)
            return null;

        var p0 = points[indexOfLowerOrEqualX];
        if (indexOfLowerOrEqualX == Length - 1)
            return x > p0.X ? null : p0.Y;

        var p1 = points[indexOfLowerOrEqualX + 1];
        var dx = p1.X - p0.X;
        if (dx <= 0)
            return p0.X;

        var k = (x - p0.X) / dx;
        return p0.Y + (k * (p1.Y - p0.Y));
    }

    public float? TryMap(float value)
        => TryMap(value, out _);

    public float Map(float x, out int indexOfLowerOrEqualX)
    {
        var points = Points;
        indexOfLowerOrEqualX = points.IndexOfLowerOrEqualX(x);
        if (indexOfLowerOrEqualX < 0)
            return Points.Length == 0 ? 0 : Points[0].Y;

        var p0 = points[indexOfLowerOrEqualX];
        if (indexOfLowerOrEqualX == Length - 1)
            return p0.Y;

        var p1 = points[indexOfLowerOrEqualX + 1];
        var dx = p1.X - p0.X;
        if (dx <= 0)
            return p0.X;

        var k = (x - p0.X) / dx;
        return p0.Y + (k * (p1.Y - p0.Y));
    }

    public float Map(float value)
        => Map(value, out _);

    public LinearMap Move(float xOffset, float yOffset)
        => Move(new Vector2(xOffset, yOffset));

    public LinearMap Move(Vector2 offset)
    {
        if (Data.Length == 0)
            return this;

        var data = Data;
        var newData = new float[data.Length];
        for (var i = 0; i < data.Length; i++) {
            newData[i] = data[i] + offset.X;
            i++;
            newData[i] = data[i] + offset.Y;
        }
        return new LinearMap(newData);
    }

    public LinearMap GetPrefix(float x, float xEpsilon = 0)
    {
        var splitPoint = new Vector2(x, Map(x, out var indexOfLowerOrEqualX));
        if (indexOfLowerOrEqualX < 0)
            return new LinearMap(splitPoint);
        if (indexOfLowerOrEqualX == Length - 1)
            return this;

        var i2 = indexOfLowerOrEqualX << 1;
        return new LinearMap(Data[..i2]).TryAppend(splitPoint, xEpsilon);
    }

    public LinearMap GetSuffix(float x, float xEpsilon = 0)
    {
        var splitPoint = new Vector2(x, Map(x, out var indexOfLowerOrEqualX));
        if (indexOfLowerOrEqualX < 0)
            return this;
        if (indexOfLowerOrEqualX == Length - 1)
            return new LinearMap(splitPoint);

        var i2 = indexOfLowerOrEqualX << 1;
        return new LinearMap(Data[i2..]).TryPrepend(splitPoint, xEpsilon);
    }

    public (LinearMap Prefix, LinearMap Suffix) Split(float x, float xEpsilon = 0)
    {
        var splitPoint = new Vector2(x, Map(x, out var indexOfLowerOrEqualX));
        if (indexOfLowerOrEqualX < 0)
            return (new LinearMap(splitPoint), this);
        if (indexOfLowerOrEqualX == Length - 1)
            return (this, new LinearMap(splitPoint));

        var i2 = indexOfLowerOrEqualX << 1;
        var prefix = new LinearMap(Data[..i2]).TryAppend(splitPoint, xEpsilon);
        var suffix = new LinearMap(Data[i2..]).TryPrepend(splitPoint, xEpsilon);
        return (prefix, suffix);
    }

    public LinearMap Prepend(Vector2 point)
    {
        var data = Data;
        if (data.Length == 0)
            return new LinearMap(point);

        var newData = new float[data.Length + 2];
        data.AsSpan().CopyTo(newData.AsSpan(1));
        newData[0] = point.X;
        newData[1] = point.Y;
        return new LinearMap(newData);
    }

    public LinearMap TryPrepend(Vector2 point, float xEpsilon = 0)
        => IsEmpty || point.X <= Points[0].X - xEpsilon
            ? Prepend(point)
            : this;

    public LinearMap Append(Vector2 point)
    {
        var data = Data;
        if (data.Length == 0)
            return new LinearMap(point);

        var newData = new float[data.Length + 2];
        data.AsSpan().CopyTo(newData);
        newData[^2] = point.X;
        newData[^1] = point.Y;
        return new LinearMap(newData);
    }

    public LinearMap TryAppend(Vector2 point, float xEpsilon)
        => IsEmpty || point.X >= Points[^1].X + xEpsilon
            ? Append(point)
            : this;


    public LinearMap AppendOrUpdateSuffix(LinearMap suffix, float xEpsilon = 0)
    {
        if (suffix.IsEmpty)
            return this;

        suffix.RequireValid();
        var points = Points;
        var i = points.IndexOfLowerOrEqualX(suffix[0].X - xEpsilon);
        if (i < 0)
            return suffix;
        if (points[i].X < suffix[0].X)
            return new LinearMap(points[..(i + 1)], suffix.Points);

        return i == 0 ? suffix : new LinearMap(points[..i], suffix.Points);
    }

    public LinearMap TrySimplifyToPoint(Vector2 epsilon)
    {
        if (Length != 2)
            return this;

        var d = this[0] - this[1];
        return Math.Abs(d.X) <= epsilon.X && Math.Abs(d.Y) <= epsilon.Y
            ? new LinearMap(this[0])
            : this;
    }

    public LinearMap Simplify(Vector2 epsilon)
    {
        var points = Points;
        if (points.Length <= 2)
            return this;

        var pStart = points[0];
        var newData = new List<float>() { pStart.X, pStart.Y };
        var lastI = 0;
        for (var i = 1; i < points.Length - 1; i++) {
            var (p0, p, p1) = (this[lastI], this[i], this[i + 1]);
            var dX = p1.X - p0.X;
            if (Math.Abs(dX) < epsilon.X)
                continue;

            var k = (p.X - p0.X) / dX;
            var y = p0.Y + k * (p1.Y - p0.Y);
            if (Math.Abs(p.Y - y) < epsilon.Y)
                continue;

            newData.Add(p.X);
            newData.Add(p.Y);
            lastI = i;
        }
        var pEnd = points[^1];
        newData.Add(pEnd.X);
        newData.Add(pEnd.Y);
        return new LinearMap(newData.ToArray());
    }
}
