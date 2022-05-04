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
    [ConcurrencyCheck] public long Version { get; set; }

    [Column(TypeName = "smallint")]
    public UserStatus Status { get; set; }
}
