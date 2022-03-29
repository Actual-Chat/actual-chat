using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Notification.Db;

[Table("ChatSubscriptions")]
[Index(nameof(UserId), nameof(ChatId))]
public class DbChatSubscriptions : IHasId<string>, IHasVersion<long>
{
    public DbChatSubscriptions() { }

    [Key] public string Id { get; set; } = null!;

    [ConcurrencyCheck]
    public long Version { get; set; }

    public string UserId { get; set; }

    public string ChatId { get; set; }
}
