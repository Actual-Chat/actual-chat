using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Notification.Db;

[Table("MutedChatSubscriptions")]
[Index(nameof(UserId), nameof(ChatId))]
public class DbMutedChatSubscription : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    public DbMutedChatSubscription() { }

    [Key] public string Id { get; set; } = null!;

    [ConcurrencyCheck]
    public long Version { get; set; }

    public string UserId { get; set; } = null!;

    public string ChatId { get; set; } = null!;
}
