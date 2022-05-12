using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl.Versioning;

namespace ActualChat.Users.Db;

public class DbUserProfile : IHasId<string>, IHasVersion<long>
{
    /// <summary>
    /// Stores same value as <see cref="DbUser.Id"/>
    /// </summary>
    [Key] public string UserId { get; set; } = null!;
    string IHasId<string>.Id => UserId;
    [ConcurrencyCheck] public long Version { get; set; }

    [Column(TypeName = "smallint")]
    public UserStatus Status { get; set; }

    public string AvatarId { get; set; } = "";

    public UserProfile ToModel(UserProfile model)
        => model with {
            Id = UserId,
            Status = Status,
            AvatarId = AvatarId,
            Version = Version
        };

    public void UpdateFrom(UserProfile model)
    {
        UserId = model.Id;
        Version = model.Version;
        Status = model.Status;
        AvatarId = model.AvatarId;
    }
}
