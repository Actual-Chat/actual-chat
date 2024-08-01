using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ActualChat.Users.Db;

[Table("ChatUsages")]
public class DbChatUsage
{
    private DateTime _accessedAt;

    [Key] public string Id { get; set; } = null!;
    public ChatUsageListKind Kind { get; set; }
    public string UserId { get; set; } = "";
    public string ChatId { get; set; } = "";

    public static string ComposeId(UserId userId, ChatUsageListKind kind, ChatId chatId)
        => ComposeIdPrefix(userId, kind) + chatId.Id.Value;
    public static string ComposeIdPrefix(UserId userId, ChatUsageListKind kind)
        => $"{userId} {kind.Format()}:";

    public DateTime AccessedAt {
        get => _accessedAt.DefaultKind(DateTimeKind.Utc);
        set => _accessedAt = value.DefaultKind(DateTimeKind.Utc);
    }
}
