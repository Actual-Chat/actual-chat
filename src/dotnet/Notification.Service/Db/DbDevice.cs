using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Stl.Versioning;

namespace ActualChat.Notification.Db;

[Table("Devices")]
[Index(nameof(UserId))]
public class DbDevice : IHasId<string>, IHasVersion<long>
{
    public DbDevice() { }

    [Key] public string Id { get; set; } = null!;

    [ConcurrencyCheck]
    public long Version { get; set; }

    public string UserId { get; set; }

    public string Type { get; set; }
}
