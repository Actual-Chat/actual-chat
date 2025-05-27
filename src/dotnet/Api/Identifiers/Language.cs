using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<Language>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<Language>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<Language>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<Language>))]
[ParameterComparer(typeof(ByRefParameterComparer))] // Fine for Language
public sealed partial class Language : StringIdentifier, IStringIdentifier<Language>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<Language>();

    [IgnoreDataMember]
    public string ShortTitle { get; }
    [IgnoreDataMember]
    public string Title { get; }
    [IgnoreDataMember]
    public bool IsAnyEnglish { get; }

    // Factories and constructors

    internal Language(string value, string shortTitle, string title)
        : base(value)
    {
        ShortTitle = shortTitle;
        Title = title;
        IsAnyEnglish = shortTitle.OrdinalIgnoreCaseStartsWith("en");
    }

    // Equality

    public bool Equals(Language? other)
        => ReferenceEquals(this, other); // Fine for Language
    public override bool Equals(object? obj)
        => ReferenceEquals(this, obj); // Fine for Language

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Language? left, Language? right)
        => ReferenceEquals(left, right); // Fine for Language

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Language? left, Language? right)
        => !ReferenceEquals(left, right); // Fine for Language

    // Parsing

    public static Language Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<Language>(s);

    public static Language? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static Language? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out Language? result)
    {
        if (!s.IsNullOrEmpty() && (Languages.ById.TryGetValue(s, out result) || Languages.ById.TryGetValue(s.ToLowerInvariant(), out result)))
            return true;

        result = null;
        return false;
    }
}
