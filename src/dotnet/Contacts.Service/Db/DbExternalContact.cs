using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Hashing;
using ActualLab.Versioning;

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

    public string Hash { get; set; } = "";

    public DateTime ModifiedAt {
        get => _modifiedAt.DefaultKind(DateTimeKind.Utc);
        set => _modifiedAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime CreatedAt {
        get => _createdAt.DefaultKind(DateTimeKind.Utc);
        set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public List<DbExternalContactLink> ExternalContactLinks { get; } = new();


    public DbExternalContact() { }
    public DbExternalContact(ExternalContactFull externalContactFull) => UpdateFrom(externalContactFull);

    public ExternalContactFull ToModel()
        => new(new ExternalContactId(Id), Version) {
            CreatedAt = CreatedAt.ToMoment(),
            ModifiedAt = ModifiedAt.ToMoment(),
            DisplayName = DisplayName,
            GivenName = GivenName,
            FamilyName = FamilyName,
            MiddleName = MiddleName,
            NamePrefix = NamePrefix,
            NameSuffix = NameSuffix,
            PhoneHashes = ExternalContactLinks.Select(x => x.ToPhoneHash()).SkipNullItems().ToApiSet(),
            EmailHashes = ExternalContactLinks.Select(x => x.ToEmailHash()).SkipNullItems().ToApiSet(),
            Hash = new HashString(Hash),
        };

    public void UpdateFrom(ExternalContactFull model)
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
        Hash = model.Hash;
        Version = model.Version;
        CreatedAt = model.CreatedAt.ToDateTimeClamped();
        ModifiedAt = model.ModifiedAt.ToDateTimeClamped();
        CreatedAt = model.CreatedAt.ToDateTimeClamped();

        var links = model.PhoneHashes.Select(DbExternalContactLink.GetPhoneLink)
            .Concat(model.EmailHashes.Select(DbExternalContactLink.GetEmailLink))
            .ToHashSet(StringComparer.Ordinal);
        var linksToAdd = links.Except(ExternalContactLinks.Select(x => x.Value), StringComparer.Ordinal).ToList();
        ExternalContactLinks.RemoveAll(x => !links.Contains(x.Value));
        ExternalContactLinks.AddRange(linksToAdd.Select(x => new DbExternalContactLink {
            DbExternalContactId = model.Id,
            Value = x,
        }));
    }
}
