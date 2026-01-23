using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.Db;

[Table("AccountIdentities")]
[Index(nameof(Id))]
public class DbAccountIdentity : IHasId<string>
{
    [Key]
    public string Id { get; set; } = "";
    [Column("AccountId")]
    public string DbAccountId { get; set; } = "";
    public string Secret { get; set; } = "";
}
