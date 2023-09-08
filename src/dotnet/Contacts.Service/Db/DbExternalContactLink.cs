using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Contacts.Db;

[Table("ExternalContactLinks")]
[PrimaryKey(nameof(DbExternalContactId), nameof(Value))]
[Index(nameof(Value))]
public class DbExternalContactLink
{
    private const string PhonePrefix = "tel:";
    private const string EmailPrefix = "mailto:";

    [Column("ExternalContactId")]
    public string DbExternalContactId { get; set; } = "";
    public string Value { get; set; } = "";

    public string? ToPhoneHash()
        => Value.OrdinalStartsWith(PhonePrefix) ? Value[PhonePrefix.Length..] : null;

    public string? ToEmailHash()
        => Value.OrdinalStartsWith(EmailPrefix) ? Value[EmailPrefix.Length..] : null;

    public static string GetPhoneLink(string phoneHash)
        => $"{PhonePrefix}{phoneHash}";

    public static string GetEmailLink(string emailHash)
        => $"{EmailPrefix}{emailHash}";
}
