using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Represents a country with its code and title.
/// </summary>
[DataContract]
[JsonConverter(typeof(StringLikeJsonConverter<Country>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<Country>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<Country>))]
[TypeConverter(typeof(StringLikeTypeConverter<Country>))]
[ParameterComparer(typeof(ByRefParameterComparer))] // Fine for Country
public sealed partial class Country : StringIdentifier, IStringIdentifier<Country>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<Country>();

    [IgnoreDataMember]
    public string Title { get; }
    [IgnoreDataMember]
    public bool IsUndefined { get; }

    // Factories and constructors

    internal Country(string id, string title) : base(id)
    {
        Title = title;
        IsUndefined = id.Length == 0;
    }

    // Equality

    public bool Equals(Country? other)
        => ReferenceEquals(this, other); // Fine for Country
    public override bool Equals(object? obj)
        => ReferenceEquals(this, obj); // Fine for Country

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Country? left, Country? right)
        => ReferenceEquals(left, right); // Fine for Country

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Country? left, Country? right)
        => !ReferenceEquals(left, right); // Fine for Country

    // Parsing

    public static Country Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<Country>(s);

    public static Country? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static Country? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out Country? result)
    {
        if (!s.IsNullOrEmpty() && (Countries.ById.TryGetValue(s, out result) || Countries.ById.TryGetValue(s.ToLower(), out result)))
            return true;

        result = null;
        return false;
    }
}
