using System.Text.Json.Serialization;

namespace ActualChat.Mathematics;

[DataContract]
public readonly struct LinearMap
{
    [DataMember(Order = 0)]
    public double[] SourcePoints { get; }
    [DataMember(Order = 1)]
    public double[] TargetPoints { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public int Length => SourcePoints.Length;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public (double Min, double Max) SourceRange => (SourcePoints[0], SourcePoints[^1]);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public (double Min, double Max) TargetRange => (TargetPoints[0], TargetPoints[^1]);

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public LinearMap(double[] sourcePoints, double[] targetPoints)
    {
        SourcePoints = sourcePoints;
        TargetPoints = targetPoints;
    }

    public override string ToString()
        => $"{GetType().Name}({{{SourcePoints.ToDelimitedString()}}} -> {{{TargetPoints.ToDelimitedString()}}})";

    public bool IsValid()
        => TargetPoints.Length == SourcePoints.Length
            && SourcePoints.IsStrictlyIncreasingSequence();

    public bool IsInversionValid()
        => TargetPoints.Length == SourcePoints.Length
            && TargetPoints.IsStrictlyIncreasingSequence();

    public double? Map(double value)
    {
        var leIndex = SourcePoints.IndexOfLowerOrEqual(value);
        if (leIndex < 0)
            return null;
        var leValue = SourcePoints[leIndex];
        if (leIndex == Length - 1) {
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            return leValue == value ? TargetPoints[leIndex] : null;
        }

        var geIndex = leIndex + 1;
        var geValue = SourcePoints[geIndex];
        var factor = (value - leValue) / (geValue - leValue);
        var tleValue = TargetPoints[leIndex];
        var tgeValue = TargetPoints[geIndex];
        return tleValue + (tgeValue - tleValue) * factor;
    }

    public LinearMap Clone()
        => new(SourcePoints[..], TargetPoints[..]);

    public LinearMap Invert()
        => new(TargetPoints, SourcePoints);

    public LinearMap Offset(double sourceOffset, double targetOffset)
        => new(
            SourcePoints.Select(x => x + sourceOffset).ToArray(),
            TargetPoints.Select(x => x + targetOffset).ToArray());

    public LinearMap Simplify(double maxError)
    {
        if (!IsValid())
            return this;
        var sourcePoints = new List<double>() { SourcePoints[0] };
        var targetPoints = new List<double>() { TargetPoints[0] };
        var lastI = 0;
        for (var i = 1; i < SourcePoints.Length - 1; i++) {
            var (x0, x, x1) = (SourcePoints[lastI], SourcePoints[i], SourcePoints[i + 1]);
            var k = (x - x0) / (x1 - x0);
            var (y0, y, y1) = (TargetPoints[lastI], TargetPoints[i], TargetPoints[i + 1]);
            var yy = y0 + k * (y1 - y0);
            if (Math.Abs(y - yy) < maxError)
                continue;
            sourcePoints.Add(x);
            targetPoints.Add(y);
            lastI = i;
        }
        sourcePoints.Add(SourcePoints[^1]);
        targetPoints.Add(TargetPoints[^1]);
        return new LinearMap(sourcePoints.ToArray(), targetPoints.ToArray());
    }

    public LinearMap AppendOrUpdateTail(LinearMap tail)
    {
        tail.Validate();
        var leIndex = SourcePoints.IndexOfLowerOrEqual(tail.SourcePoints[0]);
        if (leIndex < 0)
            return tail.Clone(); // We always return a map w/ new arrays from this method
        var sourcePoints = SourcePoints[..leIndex].Concat(tail.SourcePoints).ToArray();
        var targetPoints = TargetPoints[..leIndex].Concat(tail.TargetPoints).ToArray();
        return new LinearMap(sourcePoints, targetPoints);
    }

    public LinearMap Validate(bool requireInvertible = false)
    {
        if (!IsValid())
            throw new InvalidOperationException($"Invalid {GetType().Name}.");
        if (requireInvertible && !IsInversionValid())
            throw new InvalidOperationException($"Invalid {GetType().Name}.");
        return this;
    }
}
