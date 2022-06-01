using Stl.Versioning;

namespace ActualChat.Chat;

[DataContract]
public sealed record ChatRole(
    [property: DataMember] Symbol Id, // Corresponds to DbChatRole.Id
    [property: DataMember] string Name = ""
    ) : IHasId<Symbol>, IHasVersion<long>
{
    public static ChatRole Everyone { get; } = new(":-1", "Everyone");
    public static ChatRole Users { get; } = new(":-2", "Users");
    public static ChatRole UnauthenticatedUsers { get; } = new(":-3", "Unauthenticated Users");
    public static ChatRole Owners { get; } = new(":-10", "Owners");

    public static IReadOnlyDictionary<Symbol, ChatRole> SystemRoles { get; } =
        new[] { Owners, Users, UnauthenticatedUsers, Everyone }.ToDictionary(r => r.Id);

    private string? _chatId;

    [DataMember] public long Version { get; init; } = 0;
    [DataMember] public string Picture { get; set; } = "";
    [DataMember] public ImmutableHashSet<Symbol> AuthorIds { get; init; } = ImmutableHashSet<Symbol>.Empty;

    public string ChatId {
        get {
            if (_chatId != null)
                return _chatId;
            if (TryParseId(Id, out var chatId, out _)) {
                _chatId = chatId;
                return chatId;
            }
            _chatId = "none";
            return _chatId;
        }
    }

    public bool IsSystem
        => ChatId.Length == 0;

    public static bool TryParseId(string chatRoleId, out string chatId, out long localId)
    {
        chatId = "";
        localId = 0;
        if (chatRoleId.IsNullOrEmpty())
            return false;
        var chatIdLength = chatRoleId.OrdinalIndexOf(":");
        if (chatIdLength == -1)
            return false;
        chatId = chatRoleId.Substring(0, chatIdLength);
        var tail = chatRoleId.Substring(chatIdLength + 1);
        return long.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out localId);
    }

    public static void ParseId(string chatRoleId, out string chatId, out long localId)
    {
        if (!TryParseId(chatRoleId, out chatId, out localId))
            throw new FormatException("Invalid chat role ID format.");
    }

    public static bool IsValidId(string chatRoleId)
        => TryParseId(chatRoleId, out var chatId, out _) && Chat.IsValidId(chatId);
}
