using System.Numerics;

namespace ActualChat.Mathematics;

[DataContract]
public readonly struct LinearMap
{
    private readonly float[]? _data;

    [DataMember(Order = 0)]
    public float[] Data => _data ?? Array.Empty<float>();

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
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

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public int Length => _data == null ? 0 : _data.Length >> 1;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsEmpty => _data == null || _data.Length == 0;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public Range<float> XRange => new(Points[0].X, Points[^1].X);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public Range<float> YRange => new(Points[0].Y, Points[^1].Y);

    public Vector2 this[int index] => Points[index];
    public LinearMap this[Range range] => new(Points[range]);

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public LinearMap(params float[] data)
    {
        if (0 != (data.Length & 1))
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
    {
        var points = Points;
        return points.IsStrictlyIncreasingXSequence()
            || points.Length == 2 && points[0] == points[1];
    }

    public bool IsInversionValid()
    {
        var points = Points;
        return points.IsStrictlyIncreasingYSequence()
            || (points.Length == 2 && points[0] == points[1]);
    }

    public LinearMap RequireValid(bool requireInvertible = false)
    {
        if (!IsValid())
            throw StandardError.Constraint($"Invalid {GetType().GetName()}.");
        if (requireInvertible && !IsInversionValid())
            throw StandardError.Constraint($"Invalid {GetType().GetName()}.");
        return this;
    }

    public float? TryMap(float value)
    {
        var points = Points;
        var i = points.IndexOfLowerOrEqualX(value);
        if (i < 0)
            return null;
        var p0 = points[i];
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (p0.X == value)
            return p0.Y;
        if (i == Length - 1)
            return null;

        var p1 = points[i + 1];
        var k = (value - p0.X) / (p1.X - p0.X);
        return p0.Y + (k * (p1.Y - p0.Y));
    }

    public float Map(float value)
        => TryMap(value) ?? throw new ArgumentOutOfRangeException(nameof(value), "Can't map provided value.");

    public LinearMap Move(float xOffset, float yOffset)
        => Move(new Vector2(xOffset, yOffset));

    public LinearMap Move(Vector2 offset)
    {
        var data = Data;
        var newData = new float[data.Length];
        for (var i = 0; i < data.Length; i++) {
            newData[i] = data[i] + offset.X;
            i++;
            newData[i] = data[i] + offset.Y;
        }
        return new LinearMap(newData);
    }

    public LinearMap Append(Vector2 point)
    {
        var data = Data;
        if (data.Length == 0) return new LinearMap(point);

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

    public LinearMap TrySimplifyToPoint(float minSourceDistance = 1e-6f, float minTargetDistance = 1e-6f)
    {
        if (Length != 2)
            return this;
        var d = this[0] - this[1];
        return Math.Abs(d.X) >= minSourceDistance || Math.Abs(d.Y) >= minTargetDistance
            ? this
            : new LinearMap(this[0]);
    }

    public LinearMap Simplify(float yEpsilon)
    {
        var points = Points;
        if (points.Length <= 2)
            return this;
        var pStart = points[0];
        var newData = new List<float>() { pStart.X, pStart.Y };
        var lastI = 0;
        for (var i = 1; i < points.Length - 1; i++) {
            var (p0, p, p1) = (this[lastI], this[i], this[i + 1]);
            var k = (p.X - p0.X) / (p1.X - p0.X);
            var y = p0.Y + k * (p1.Y - p0.Y);
            if (Math.Abs(p.Y - y) < yEpsilon)
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

    public LinearMap AppendOrUpdateTail(LinearMap tail, float xEpsilon)
    {
        tail.RequireValid();
        var points = Points;
        var i = points.IndexOfLowerOrEqualX(tail[0].X - xEpsilon);
        if (i < 0)
            return tail;
        var cutIndex = i + 1;
        return new LinearMap(points[..cutIndex], tail.Points);
    }
}
