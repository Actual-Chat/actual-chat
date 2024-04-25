using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Hashing;
using ActualLab.Versioning;

namespace ActualChat.Contacts.Db;

[Table("ExternalContactsHashes")]
public class DbExternalContactsHash : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private DateTime _createdAt;
    private DateTime _modifiedAt;

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string Hash { get; set; } = "";

    public DateTime ModifiedAt {
        get => _modifiedAt.DefaultKind(DateTimeKind.Utc);
        set => _modifiedAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime CreatedAt {
        get => _createdAt.DefaultKind(DateTimeKind.Utc);
        set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DbExternalContactsHash() { }
    public DbExternalContactsHash(ExternalContactsHash externalContact) => UpdateFrom(externalContact);

    public ExternalContactsHash ToModel()
        => new(new UserDeviceId(Id), Version) {
            CreatedAt = CreatedAt.ToMoment(),
            ModifiedAt = ModifiedAt.ToMoment(),
            Hash = new HashString(Hash),
        };

    public void UpdateFrom(ExternalContactsHash model)
    {
        this.RequireSameOrEmptyId(model.Id);
        model.Id.Require();
        model.RequireSomeVersion();

        Id = model.Id;
        Hash = model.Hash;
        Version = model.Version;
        CreatedAt = model.CreatedAt.ToDateTimeClamped();
        ModifiedAt = model.ModifiedAt.ToDateTimeClamped();
        CreatedAt = model.CreatedAt.ToDateTimeClamped();
    }
}
