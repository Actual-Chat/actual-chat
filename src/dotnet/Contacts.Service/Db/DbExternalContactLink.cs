using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Contacts.Db;

[Table("ExternalContactLinks")]
[PrimaryKey(nameof(DbExternalContactId), nameof(Value))]
[Index(nameof(Value))]
[Index(nameof(IsChecked))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbExternalContactLink
{
    private const string PhonePrefix = "tel:";
    private const string EmailPrefix = "mailto:";

    [Column("ExternalContactId")] // TODO(AY): Rename to external_contact_id
    public string DbExternalContactId { get; set; } = "";
    public string Value { get; set; } = "";
    public bool IsChecked { get; set; }

    public string? ToPhoneHash()
        => IsPhoneLink(Value, out var hash) ? hash : null;

    public string? ToEmailHash()
        => IsEmailLink(Value, out var hash) ? hash : null;

    public static string GetPhoneLink(string phoneHash)
        => $"{PhonePrefix}{phoneHash}";

    public static string GetEmailLink(string emailHash)
        => $"{EmailPrefix}{emailHash}";

    public static bool IsPhoneLink(string link, out string hash)
    {
        hash = string.Empty;
        if (!link.OrdinalStartsWith(PhonePrefix))
            return false;

        hash = link[PhonePrefix.Length..];
        return true;
    }

    public static bool IsEmailLink(string link, out string hash)
    {
        hash = string.Empty;
        if (!link.OrdinalStartsWith(EmailPrefix))
            return false;

        hash = link[EmailPrefix.Length..];
        return true;
    }
}
