using System.Text.Json.Serialization;

namespace ActualChat.Mathematics;

[DataContract]
public readonly struct LinearMap
{
    private readonly double[] _sourcePoints;
    private readonly double[] _targetPoints;

    [DataMember(Order = 0)]
    public double[] SourcePoints => _sourcePoints ?? Array.Empty<double>();
    [DataMember(Order = 1)]
    public double[] TargetPoints => _targetPoints ?? Array.Empty<double>();

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public int Length => SourcePoints.Length;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsEmpty => SourcePoints.Length == 0;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public (double Min, double Max) SourceRange => (SourcePoints[0], SourcePoints[^1]);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public (double Min, double Max) TargetRange => (TargetPoints[0], TargetPoints[^1]);

    public (double SourcePoint, double TargetPoint) this[int index]
        => (SourcePoints[index], TargetPoints[index]);
    public LinearMap this[Range range]
        => new(SourcePoints[range], TargetPoints[range]);

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public LinearMap(double[] sourcePoints, double[] targetPoints)
    {
        _sourcePoints = sourcePoints;
        _targetPoints = targetPoints;
    }

    public LinearMap(double sourcePoint, double targetPoint)
        : this(new [] { sourcePoint }, new [] { targetPoint })
    { }

    public LinearMap((double Source, double Target) point)
        : this(new [] { point.Source }, new [] { point.Target })
    { }

    public LinearMap((double Source, double Target) point1, (double Source, double Target) point2)
        : this(new [] { point1.Source, point2.Source }, new [] { point1.Target, point2.Target })
    { }

    public LinearMap((double Source, double Target) point1, (double Source, double Target) point2, (double Source, double Target) point3)
        : this(new [] { point1.Source, point2.Source, point3.Source }, new [] { point1.Target, point2.Target, point3.Target })
    { }

    public LinearMap(params (double Source, double Target)[] points)
        : this(points.Select(p => p.Source).ToArray(), points.Select(p => p.Target).ToArray())
    { }

    public override string ToString()
        => $"{{{SourcePoints.ToDelimitedString()}}} -> {{{TargetPoints.ToDelimitedString(", ")}}}";

    public bool IsValid() => IsValid(SourcePoints, TargetPoints);
    public bool IsInversionValid() => IsValid(TargetPoints, SourcePoints);

    public LinearMap AssertValid(bool requireInvertible = false)
    {
        if (!IsValid())
            throw new InvalidOperationException($"Invalid {GetType().Name}.");
        if (requireInvertible && !IsInversionValid())
            throw new InvalidOperationException($"Invalid {GetType().Name}.");
        return this;
    }

    public double? TryMap(double value)
    {
        var leIndex = SourcePoints.IndexOfLowerOrEqual(value);
        if (leIndex < 0)
            return null;
        var leValue = SourcePoints[leIndex];
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (leValue == value)
            return TargetPoints[leIndex];
        if (leIndex == Length - 1)
            return null;

        var geIndex = leIndex + 1;
        var geValue = SourcePoints[geIndex];
        var factor = (value - leValue) / (geValue - leValue);
        var tleValue = TargetPoints[leIndex];
        var tgeValue = TargetPoints[geIndex];
        return tleValue + (tgeValue - tleValue) * factor;
    }

    public double Map(double value)
        => TryMap(value) ?? throw new ArgumentOutOfRangeException(nameof(value), "Can't map provided value.");

    public LinearMap Clone()
        => new(SourcePoints[..], TargetPoints[..]);

    public LinearMap Invert()
        => new(TargetPoints, SourcePoints);

    public LinearMap Offset(double sourceOffset, double targetOffset)
        => new(
            SourcePoints.Select(x => x + sourceOffset).ToArray(),
            TargetPoints.Select(x => x + targetOffset).ToArray());

    public LinearMap Append(double sourcePoint, double targetPoint)
    {
        if (IsEmpty) return new LinearMap(sourcePoint, targetPoint);

        var sourcePoints = new double[Length + 1];
        var targetPoints = new double[Length + 1];
        SourcePoints.CopyTo(sourcePoints, 0);
        TargetPoints.CopyTo(targetPoints, 0);
        sourcePoints[^1] = sourcePoint;
        targetPoints[^1] = targetPoint;
        return new LinearMap(sourcePoints, targetPoints);
    }

    public LinearMap TryAppend(double sourcePoint, double targetPoint, double sourceEpsilon)
    {
        if (IsEmpty) return Append(sourceEpsilon, targetPoint);

        var lastSourcePoint = SourcePoints[^1];
        return sourcePoint < lastSourcePoint + sourceEpsilon
            ? this
            : Append(sourcePoint, targetPoint);
    }

    public LinearMap TrySimplifyToPoint(double minSourceDistance = 1e-6, double minTargetDistance = 1e-6)
    {
        if (Length != 2)
            return this;
        var ((x0, y0), (x1, y1)) = (this[0], this[1]);
        if (Math.Abs(x1 - x0) >= minSourceDistance)
            return this;
        if (Math.Abs(y1 - y0) >= minTargetDistance)
            return this;
        return new LinearMap(x0, y0);
    }

    public LinearMap Simplify(double minDistance)
    {
        var sourcePoints = new List<double>() { SourcePoints[0] };
        var targetPoints = new List<double>() { TargetPoints[0] };
        var lastI = 0;
        for (var i = 1; i < SourcePoints.Length - 1; i++) {
            var (x0, x, x1) = (SourcePoints[lastI], SourcePoints[i], SourcePoints[i + 1]);
            var k = (x - x0) / (x1 - x0);
            var (y0, y, y1) = (TargetPoints[lastI], TargetPoints[i], TargetPoints[i + 1]);
            var yy = y0 + k * (y1 - y0);
            if (Math.Abs(y - yy) < minDistance)
                continue;
            sourcePoints.Add(x);
            targetPoints.Add(y);
            lastI = i;
        }
        sourcePoints.Add(SourcePoints[^1]);
        targetPoints.Add(TargetPoints[^1]);
        return new LinearMap(sourcePoints.ToArray(), targetPoints.ToArray());
    }

    public LinearMap AppendOrUpdateTail(LinearMap tail, double sourceEpsilon)
    {
        tail.AssertValid();
        var leIndex = SourcePoints.IndexOfLowerOrEqual(tail.SourcePoints[0] - sourceEpsilon);
        if (leIndex < 0)
            return tail.Clone(); // We always return a map w/ new arrays from this method
        var cutIndex = leIndex + 1;
        var sourcePoints = SourcePoints[..cutIndex].Concat(tail.SourcePoints).ToArray();
        var targetPoints = TargetPoints[..cutIndex].Concat(tail.TargetPoints).ToArray();
        return new LinearMap(sourcePoints, targetPoints);
    }

    private static bool IsValid(double[] x, double[] y)
    {
        if (y.Length != x.Length)
            return false;
        if (x.IsStrictlyIncreasingSequence())
            return true;
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        return x.Length == 2 && x[0] == x[1] && y[0] == y[1];
    }
}
