using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Represents a user interest or topic.
/// </summary>
[DataContract]
[JsonConverter(typeof(StringLikeJsonConverter<Interest>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<Interest>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<Interest>))]
[TypeConverter(typeof(StringLikeTypeConverter<Interest>))]
[ParameterComparer(typeof(ByRefParameterComparer))] // Fine for Interest
public sealed partial class Interest : StringIdentifier, IStringIdentifier<Interest>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<Interest>();

    [IgnoreDataMember]
    public string Title { get; }

    // Factories and constructors

    internal Interest(string id, string title) : base(id)
        => Title = title;

    // Equality

    public bool Equals(Interest? other)
        => ReferenceEquals(this, other); // Fine for Interest
    public override bool Equals(object? obj)
        => ReferenceEquals(this, obj); // Fine for Interest

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Interest? left, Interest? right)
        => ReferenceEquals(left, right); // Fine for Interest

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Interest? left, Interest? right)
        => !ReferenceEquals(left, right); // Fine for Interest

    // Parsing

    public static Interest Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<Interest>(s);

    public static Interest? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static Interest? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out Interest? result)
    {
        if (!s.IsNullOrEmpty() && Interests.ById.TryGetValue(s, out result))
            return true;

        result = null;
        return false;
    }
}
