using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Overrides ==/Equals but not GetHashCode (provided by base)

/// <summary>
/// Identifies one import session of a chat or Place. A new session gets a new ID, so consent,
/// batch receipts and uploads keyed by it never carry over into a restarted import.
/// </summary>
[DataContract]
[JsonConverter(typeof(StringLikeJsonConverter<ChatImportId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<ChatImportId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<ChatImportId>))]
[TypeConverter(typeof(StringLikeTypeConverter<ChatImportId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class ChatImportId : StringIdentifier, IStringIdentifier<ChatImportId>
{
    private static readonly ILruCache<string, ChatImportId> Cache = CreateCache<ChatImportId>(32);

    public const char Delimiter = ':';
    public const int MaxTokenLength = 100;

    [IgnoreDataMember]
    public ChatId ChatId { get; }
    [IgnoreDataMember]
    public string Token { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override ShardKey ShardKey => ChatId.ShardKey;

    // Factories and constructors

    public static ChatImportId New(ChatId chatId, string token)
    {
        if (!IsValidToken(token))
            throw new ArgumentOutOfRangeException(nameof(token));

        return new(Format(chatId, token), chatId, token);
    }

    private ChatImportId(string value, ChatId chatId, string token) : base(value)
    {
        ChatId = chatId;
        Token = token;
    }

    // Equality

    public bool Equals(ChatImportId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is ChatImportId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ChatImportId? left, ChatImportId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ChatImportId? left, ChatImportId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(ChatId chatId, string token)
        => $"{chatId.Value}{Delimiter}{token}";

    public static ChatImportId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ChatImportId>(s);

    public static ChatImportId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static ChatImportId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out ChatImportId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        var chatIdLength = s.IndexOf(Delimiter);
        if (chatIdLength < 0 || !ChatId.TryParse(s[..chatIdLength], out var chatId))
            return false;

        var token = s[(chatIdLength + 1)..];
        if (!IsValidToken(token))
            return false;

        result = new ChatImportId(s, chatId, token);
        result = Cache.AddOrGet(s, result);
        return true;
    }

    // Private methods

    private static bool IsValidToken(string token)
        => token.Length is > 0 and <= MaxTokenLength && !token.Contains(Delimiter);
}
