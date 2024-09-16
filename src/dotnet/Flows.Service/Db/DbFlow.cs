using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Flows.Db;

[Table("_Flows")]
[Index(nameof(HardResumeAt))]
public sealed class DbFlow
{
    private DateTime? _hardResumeAt;

    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; } = "";
    [ConcurrencyCheck]
    public long Version { get; set; }

    [MaxLength(250)]
    public string Step { get; set; } = "";

    public DateTime? HardResumeAt {
        get => _hardResumeAt.DefaultKind(DateTimeKind.Utc);
        set => _hardResumeAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public byte[]? Data { get; set; }
}
