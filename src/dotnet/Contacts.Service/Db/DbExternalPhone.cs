using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Contacts.Db;

[Table("ExternalPhones")]
[PrimaryKey(nameof(DbExternalContactId), nameof(Phone))]
[Index(nameof(Phone))]
[Index(nameof(DbExternalContactId))]
public class DbExternalPhone : IRequirementTarget
{
    [Column("ExternalContactId")]
    public string DbExternalContactId { get; set; } = "";
    public string Phone { get; set; } = "";
}
