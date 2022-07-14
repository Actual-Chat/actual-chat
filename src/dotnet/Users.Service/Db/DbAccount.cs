using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl.Versioning;

namespace ActualChat.Users.Db;

public class DbAccount : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    [Column(TypeName = "smallint")]
    public AccountStatus Status { get; set; }

    public string AvatarId { get; set; } = "";

    public Account ToModel(Account model)
        => model with {
            Id = Id,
            Status = Status,
            AvatarId = AvatarId,
            Version = Version,
        };

    public void UpdateFrom(Account model)
    {
        Id = model.Id;
        Version = model.Version;
        Status = model.Status;
        AvatarId = model.AvatarId;
    }
}
