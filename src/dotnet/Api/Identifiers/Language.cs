using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

// Equality is reference-based, so the usual Equals/GetHashCode pairing warnings don't apply

/// <summary>
/// Represents a language with its title and code.
/// </summary>
#pragma warning disable CS0659, CS0660, CS0661
[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringLikeJsonConverter<Language>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<Language>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<Language>))]
[TypeConverter(typeof(StringLikeTypeConverter<Language>))]
[ParameterComparer(typeof(ByRefParameterComparer))] // Fine for Language
public sealed partial class Language : StringIdentifier, IStringIdentifier<Language>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<Language>();
    private static readonly ILruCache<string, Language> UnknownCache = CreateCache<Language>(32);

    [IgnoreDataMember]
    public string ShortTitle { get; }
    [IgnoreDataMember]
    public string Title { get; }
    [IgnoreDataMember]
    public string IsoCode { get; }
    [IgnoreDataMember]
    public string NativeName { get; }
    [IgnoreDataMember]
    public LanguageSupport Support { get; }
    [IgnoreDataMember]
    public bool IsAnyEnglish { get; }
    [IgnoreDataMember]
    public bool IsAnySpanish { get; }

    // Factories and constructors

    private static Language NewUnknown(string languageTag)
        => new(languageTag, GetIsoCode(languageTag).ToUpper(), languageTag, support: LanguageSupport.None);

    internal Language(
        string value,
        string shortTitle,
        string title,
        string? nativeName = null,
        LanguageSupport support = LanguageSupport.Transcription)
        : base(value)
    {
        ShortTitle = shortTitle;
        Title = title;
        Support = support;
        IsoCode = GetIsoCode(value);
        NativeName = nativeName ?? title;
        IsAnyEnglish = shortTitle.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        IsAnySpanish = shortTitle.StartsWith("es", StringComparison.OrdinalIgnoreCase);
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

    static Language IStringLike<Language>.Parse(string? s)
        => ParseOrUnknown(s);

    public static Language? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static Language? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out Language? result)
    {
        if (!s.IsNullOrEmpty()
            && (Languages.ById.TryGetValue(s, out result) || Languages.ById.TryGetValue(s.ToLower(), out result)))
            return true;

        result = null;
        return false;
    }

    public static string GetIsoCode(string languageTag)
    {
        // The primary subtag, not languageTag[..2]: that would collapse "fil-PH" onto Finnish's "fi".
        var separatorIndex = languageTag.IndexOf('-');
        return (separatorIndex < 0 ? languageTag : languageTag[..separatorIndex]).ToLower();
    }

    // Protected/internal methods

    internal static Language ParseOrUnknown(string? s)
        // Formatters deserialize through this one, so a language added by a later release reads as
        // a placeholder rather than failing the whole payload; Parse itself stays strict.
        => TryParse(s, out var result) ? result
            : s.IsNullOrEmpty() ? throw StandardError.Format<Language>(s)
            : GetUnknown(s);

    // Private methods

    private static Language GetUnknown(string languageTag)
        => UnknownCache.TryGetValue(languageTag, out var cached) ? cached
            : UnknownCache.AddOrGet(languageTag, NewUnknown(languageTag));
}
