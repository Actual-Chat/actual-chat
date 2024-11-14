using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;

namespace ActualChat.Chat.Db;

[Table("UserLinks")]
public class DbUserLink : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private DateTime _createdAt;

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public UserLinkKind Kind { get; set; }
    public string TargetId { get; set; } = "";

    public DateTime CreatedAt {
        get => _createdAt.DefaultKind(DateTimeKind.Utc);
        set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DbUserLink() { }

    public DbUserLink(UserLink userLink)
    {
        var id = userLink.Id.Value;
        this.RequireSameOrEmptyId(id);
        userLink.RequireSomeVersion();

        Id = id;
        Version = userLink.Version;
        CreatedAt = userLink.CreatedAt.ToDateTimeClamped();
        Kind = userLink.Kind;
        TargetId = userLink.TargetId;
    }

    public UserLink ToModel()
        => new (new UserLinkId(Id), Version) {
            CreatedAt = CreatedAt,
            Kind = Kind,
            TargetId = TargetId,
        };
}
