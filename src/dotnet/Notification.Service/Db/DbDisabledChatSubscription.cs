using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Notification.Db;

[Table("DisabledChatSubscriptions")]
[Index(nameof(UserId), nameof(ChatId))]
public class DbDisabledChatSubscription : IHasId<string>, IHasVersion<long>
{
    public DbDisabledChatSubscription() { }

    [Key] public string Id { get; set; } = null!;

    [ConcurrencyCheck]
    public long Version { get; set; }

    public string UserId { get; set; } = null!;

    public string ChatId { get; set; } = null!;
}
