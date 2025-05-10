using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<Emoji>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<Emoji>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<Emoji>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<Emoji>))]
[ParameterComparer(typeof(ByRefParameterComparer))] // Fine for Emoji
public sealed partial class Emoji : StringIdentifier, IStringIdentifier<Emoji>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<Emoji>();

    [IgnoreDataMember]
    public string Title { get; }

    // Factories and constructors

    internal Emoji(string id, string title) : base(id)
        => Title = title;

    // Equality

    public bool Equals(Emoji? other)
        => ReferenceEquals(this, other); // Fine for Emoji
    public override bool Equals(object? obj)
        => ReferenceEquals(this, obj); // Fine for Emoji

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Emoji? left, Emoji? right)
        => ReferenceEquals(left, right); // Fine for Emoji

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Emoji? left, Emoji? right)
        => !ReferenceEquals(left, right); // Fine for Emoji

    // Parsing

    public static Emoji Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<Emoji>(s);

    public static Emoji? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static Emoji? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out Emoji? result)
    {
        if (!s.IsNullOrEmpty() && Emojis.ById.TryGetValue(s, out result))
            return true;

        result = null;
        return false;
    }
}
