using System.Numerics;
using ActualChat.Mathematics.Internal;
using ActualChat.Serialization.Internal;

namespace ActualChat.Mathematics;

/// <summary>
/// Represents a single tile (aligned range) within a <see cref="TileLayer{T}"/>.
/// </summary>
[StructLayout(LayoutKind.Auto)]
[Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.OptOut)]
[DataContract]
[MessagePackObject(true, AllowPrivate = true)]
[MessagePackFormatter(typeof(TileMessagePackFormatter<>))]
public readonly partial struct Tile<T>
    where T : struct, INumber<T>
{
    [DataMember(Order = 0)]
    public Range<T> Range { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public TileLayer<T> Layer { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public T Start => Range.Start;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public T End => Range.End;

    public Tile(T start, T end, TileLayer<T> layer)
    {
        Range = new Range<T>(start, end);
        Layer = layer;
    }

    public Tile(Range<T> range, TileLayer<T> layer)
    {
        Range = range;
        Layer = layer;
    }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
    internal Tile(Range<T> range)
    {
        Range = range;
        Layer = null!;
    }

    public void Deconstruct(out T start, out T end)
    {
        if (Layer is null)
            throw Errors.UnboundTile();
        start = Range.Start;
        end = Range.End;
    }

    public override string ToString()
    {
        var typeName = GetType().Name;
        return Layer is null
            ? $"{typeName}(unbound)"
            : $"{typeName}({Range.Start}..{Range.End})";
    }

    public Tile<T> Next(int index = 1)
    {
        var offset = Layer.TileSize * T.CreateChecked(index);
        return new Tile<T>((Start + offset, End + offset), Layer);
    }

    public Tile<T> Prev(int index = 1)
        => Next(-index);
}
