using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Flows.Db;

[Table("_Flows")]
[Index(nameof(CanResume))]
public sealed class DbFlow
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; } = "";

    [ConcurrencyCheck]
    public long Version { get; set; }

    public bool CanResume { get; set; }
    [MaxLength(250)]
    public string Step { get; set; } = "";
    public byte[]? Data { get; set; }
}
