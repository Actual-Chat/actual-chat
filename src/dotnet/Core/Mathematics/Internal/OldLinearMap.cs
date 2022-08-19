using System.Numerics;

namespace ActualChat.Mathematics.Internal;

[DataContract]
public readonly struct OldLinearMap
{
    private readonly float[] _sourcePoints;
    private readonly float[] _targetPoints;

    [DataMember(Order = 0)]
    public float[] SourcePoints => _sourcePoints ?? Array.Empty<float>();
    [DataMember(Order = 1)]
    public float[] TargetPoints => _targetPoints ?? Array.Empty<float>();

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public OldLinearMap(float[] sourcePoints, float[] targetPoints)
    {
        _sourcePoints = sourcePoints;
        _targetPoints = targetPoints;
        if (sourcePoints.Length != targetPoints.Length)
            throw new ArgumentOutOfRangeException(nameof(targetPoints));
    }

    public LinearMap ToLinearMap()
    {
        var xPoints = SourcePoints;
        var yPoints = TargetPoints;
        var points = new Vector2[SourcePoints.Length];
        for (var i = 0; i < points.Length; i++ )
            points[i] = new Vector2(xPoints[i], yPoints[i]);
        return new LinearMap(points);
    }
}
