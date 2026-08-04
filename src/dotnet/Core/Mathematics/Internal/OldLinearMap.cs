using System.Numerics;

namespace ActualChat.Mathematics.Internal;

[DataContract, MessagePackObject]
[MessagePackFormatter(typeof(OldLinearMapMessagePackFormatter))]
public readonly partial struct OldLinearMap
{
    [DataMember(Order = 0), Key(0)]
    public float[] SourcePoints => field ?? [];
    [DataMember(Order = 1), Key(1)]
    public float[] TargetPoints => field ?? [];

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
    public OldLinearMap(float[] sourcePoints, float[] targetPoints)
    {
        SourcePoints = sourcePoints;
        TargetPoints = targetPoints;
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
