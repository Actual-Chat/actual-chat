using System.Numerics;
using MemoryPack;

namespace ActualChat.Mathematics;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[StructLayout(LayoutKind.Auto)]
public readonly partial record struct LinearMapDiff(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] LinearMap Suffix,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] bool IsRewrite = false
) : ICanBeNone<LinearMapDiff>
{
    public static LinearMapDiff None => default;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Suffix.IsEmpty && !IsRewrite;

    public static LinearMapDiff New(LinearMap map, LinearMap baseMap, Vector2 epsilon)
    {
        var mapPoints = map.Points;
        var commonPrefixLength = GetCommonPrefixLength(baseMap.Points, mapPoints, epsilon);
        var maxLength = Math.Max(baseMap.Length, map.Length);
        if (commonPrefixLength == maxLength)
            return None;

        var result = new LinearMap(mapPoints[commonPrefixLength..]);
        return result.Length == 0
            ? new LinearMapDiff(new LinearMap(mapPoints), true)
            : new LinearMapDiff(result);
    }

    public override string ToString()
        => IsNone
            ? "Δ{}"
            : IsRewrite
                ? $"Δ*{Suffix}"
                : $"Δ{Suffix}";

    public LinearMap ApplyTo(LinearMap baseMap, float xEpsilon)
        => IsNone
            ? baseMap
            : baseMap.GetPrefix(Suffix.XRange.Start, xEpsilon).AppendOrUpdateSuffix(Suffix, xEpsilon);

    // Private methods

    private static int GetCommonPrefixLength(ReadOnlySpan<Vector2> a, ReadOnlySpan<Vector2> b, Vector2 epsilon)
    {
        for (var i = 0; i < a.Length; i++) {
            if (i >= b.Length)
                return i;

            var d = a[i] - b[i];
            if (Math.Abs(d.X) > epsilon.X || Math.Abs(d.Y) > epsilon.Y)
                return i;
        }
        return a.Length;
    }
}
