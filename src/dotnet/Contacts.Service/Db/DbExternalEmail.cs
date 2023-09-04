using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Contacts.Db;

[Table("ExternalEmails")]
[PrimaryKey(nameof(DbExternalContactId), nameof(Email))]
[Index(nameof(Email))]
[Index(nameof(DbExternalContactId))]
public class DbExternalEmail : IRequirementTarget
{
    [Column("ExternalContactId")]
    public string DbExternalContactId { get; set; } = "";
    public string Email { get; set; } = "";
}
