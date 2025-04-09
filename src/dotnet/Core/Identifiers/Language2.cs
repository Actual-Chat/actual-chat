using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

[DataContract]
[JsonConverter(typeof(StringIdentifierJsonConverter<Language2>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<Language2>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<Language2>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed class Language2(string value, string shortTitle, string title, AssumeValid _)
    : StringIdentifier(value), IStringIdentifier<Language2>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<Language2>();

    [IgnoreDataMember]
    public string ShortTitle { get; } = shortTitle;
    [IgnoreDataMember]
    public string Title { get; } = title;
    [IgnoreDataMember]
    public bool IsAnyEnglish { get; } = shortTitle.OrdinalStartsWith("en");

    // Equality

    public bool Equals(Language2? other)
        => ReferenceEquals(this, other); // Fine for Language
    public override bool Equals(object? obj)
        => ReferenceEquals(this, obj); // Fine for Language

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Language2? left, Language2? right)
        => ReferenceEquals(left, right); // Fine for Language

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Language2? left, Language2? right)
        => !ReferenceEquals(left, right); // Fine for Language

    // Parsing

    public static Language2 Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<Language2>(s);

    public static bool TryParse(string? s, [NotNullWhen(true)] out Language2? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Languages2.Map.TryGetValue(s, out var language)) {
            result = language;
            return true;
        }
        if (Languages2.Map.TryGetValue(s.ToLowerInvariant(), out language)) {
            result = language;
            return true;
        }

        return false;
    }
}
