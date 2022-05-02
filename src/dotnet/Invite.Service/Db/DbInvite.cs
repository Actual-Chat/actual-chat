using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Stl.Versioning;

namespace ActualChat.Invite.Db;

[Table("Invites")]
public class DbInvite : IHasId<string>, IHasVersion<long>
{
    private DateTime _createdAt;
    private DateTime _expiresOn;

    [Key] public string Id { get; set; } = null!;

    [ConcurrencyCheck]
    public long Version { get; set; }

    public string CreatedBy { get; set; } = "";

    public int Remaining { get; set; }
    public string Code { get; set; } = "";

    public DateTime CreatedAt {
        get => _createdAt.DefaultKind(DateTimeKind.Utc);
        set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime ExpiresOn {
        get => _expiresOn.DefaultKind(DateTimeKind.Utc);
        set => _expiresOn = value.DefaultKind(DateTimeKind.Utc);
    }

    public string? DetailsJson { get; set; }
}
