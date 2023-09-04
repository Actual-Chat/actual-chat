using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl.Versioning;

namespace ActualChat.Contacts.Db;

[Table("ExternalContacts")]
public class DbExternalContact : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private DateTime _createdAt;
    private DateTime _modifiedAt;

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string DisplayName { get; set; } = "";
    public string GivenName { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public string MiddleName { get; set; } = "";
    public string NamePrefix { get; set; } = "";
    public string NameSuffix { get; set; } = "";

    public DateTime ModifiedAt {
        get => _modifiedAt.DefaultKind(DateTimeKind.Utc);
        set => _modifiedAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime CreatedAt {
        get => _createdAt.DefaultKind(DateTimeKind.Utc);
        set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public List<DbExternalPhone> ExternalPhones { get; } = new();
    public List<DbExternalEmail> ExternalEmails { get; } = new();

    public DbExternalContact() { }
    public DbExternalContact(ExternalContact externalContact) => UpdateFrom(externalContact);

    public ExternalContact ToModel()
        => new(new ExternalContactId(Id), Version) {
            CreatedAt = CreatedAt.ToMoment(),
            ModifiedAt = ModifiedAt.ToMoment(),
            DisplayName = DisplayName,
            GivenName = GivenName,
            FamilyName = FamilyName,
            MiddleName = MiddleName,
            NamePrefix = NamePrefix,
            NameSuffix = NameSuffix,
            Phones = ExternalPhones.Select(x => new Phone(x.Phone)).ToApiSet(),
            Emails = ExternalEmails.Select(x => x.Email).ToApiSet(StringComparer.OrdinalIgnoreCase),
        };

    public void UpdateFrom(ExternalContact model)
    {
        this.RequireSameOrEmptyId(model.Id);
        model.Id.Require();
        model.RequireSomeVersion();

        Id = model.Id;
        DisplayName = model.DisplayName;
        GivenName = model.GivenName;
        FamilyName = model.FamilyName;
        MiddleName = model.MiddleName;
        NamePrefix = model.NamePrefix;
        NameSuffix = model.NameSuffix;
        Version = model.Version;
        CreatedAt = model.CreatedAt.ToDateTimeClamped();
        ModifiedAt = model.ModifiedAt.ToDateTimeClamped();
        CreatedAt = model.CreatedAt.ToDateTimeClamped();

        var phonesToAdd = model.Phones.Except(ExternalPhones.Select(x => new Phone(x.Phone))).ToList();
        ExternalPhones.RemoveAll(x => !model.Phones.Contains(new Phone(x.Phone)));
        ExternalPhones.AddRange(phonesToAdd.Select(x => new DbExternalPhone {
            DbExternalContactId = model.Id,
            Phone = x,
        }));

        var emailsToAdd = model.Emails.Except(ExternalEmails.Select(x => x.Email), StringComparer.OrdinalIgnoreCase).ToList();
        ExternalEmails.RemoveAll(x => !model.Emails.Contains(x.Email));
        ExternalEmails.AddRange(emailsToAdd.Select(x => new DbExternalEmail {
            DbExternalContactId = model.Id,
            Email = x,
        }));
    }
}
