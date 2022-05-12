using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl.Versioning;

namespace ActualChat.Users.Db;

public class DbUserProfile : IHasId<string>, IHasVersion<long>
{
    /// <summary>
    /// Stores same value as <see cref="DbUser.Id"/>
    /// </summary>
    [Key] public string Id { get; set; } = null!;
    // [Key] public string UserId { get; set; } = null!;
    // string IHasId<string>.Id => UserId;
    [ConcurrencyCheck] public long Version { get; set; }

    [Column(TypeName = "smallint")]
    public UserStatus Status { get; set; }

    public string AvatarId { get; set; } = "";
}
