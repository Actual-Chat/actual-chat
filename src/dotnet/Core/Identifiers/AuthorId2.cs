using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

[DataContract]
[JsonConverter(typeof(StringIdentifierJsonConverter<AuthorId2>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<AuthorId2>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<AuthorId2>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed class AuthorId2(string value, ChatId2 chatId, long localId, AssumeValid _)
    : StringIdentifier(value), IStringIdentifier<AuthorId2>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<AuthorId2>();
    private static readonly ILruCache<string, AuthorId2> Cache = CreateCache<AuthorId2>(256);

    [IgnoreDataMember]
    public ChatId2 ChatId { get; } = chatId;
    [IgnoreDataMember]
    public long LocalId { get; } = localId;

    // Factories

    public static AuthorId2 New(ChatId2 chatId, long localId)
        => new(Format(chatId, localId), chatId, localId, AssumeValid.Option);

    // Equality

    public bool Equals(AuthorId2? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is AuthorId2 other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(AuthorId2? left, AuthorId2? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(AuthorId2? left, AuthorId2? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(ChatId2 chatId, long localId)
        => $"{chatId.Value}:{localId.Format()}";

    public static AuthorId2 Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<AuthorId2>(s);

    public static bool TryParse(string? s, [NotNullWhen(true)] out AuthorId2? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        var chatIdLength = s.OrdinalIndexOf(":");
        if (chatIdLength == -1)
            return false;

        if (!ChatId2.TryParse(s[..chatIdLength], out var chatId))
            return false;

        var tail = s[(chatIdLength + 1)..];
        if (!NumberExt.TryParseLong(tail, out var localId))
            return false;

        result = new AuthorId2(s, chatId, localId, AssumeValid.Option);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
