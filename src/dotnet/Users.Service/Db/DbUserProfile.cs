using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl.Versioning;

namespace ActualChat.Users.Db;

public class DbUserProfile : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    [Column(TypeName = "smallint")]
    public UserStatus Status { get; set; }

    public string AvatarId { get; set; } = "";

    public UserProfile ToModel(UserProfile model)
        => model with {
            Id = Id,
            Status = Status,
            AvatarId = AvatarId,
            Version = Version,
        };

    public void UpdateFrom(UserProfile model)
    {
        Id = model.Id;
        Version = model.Version;
        Status = model.Status;
        AvatarId = model.AvatarId;
    }
}
