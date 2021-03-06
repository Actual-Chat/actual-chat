using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Stl.Versioning;

namespace ActualChat.Invite.Db;

[Table("Invites")]
[Index(nameof(SearchKey), nameof(Remaining))]
public class DbInvite : IHasId<string>, IHasVersion<long>
{
    private static ITextSerializer<InviteDetails> DetailsSerializer { get; } =
        SystemJsonSerializer.Default.ToTyped<InviteDetails>();

    private DateTime _createdAt;
    private DateTime _expiresOn;

    public DbInvite() { }
    public DbInvite(Invite invite) => UpdateFrom(invite);

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string SearchKey { get; set; } = "";
    public int Remaining { get; set; }
    public string CreatedBy { get; set; } = "";

    public DateTime CreatedAt {
        get => _createdAt.DefaultKind(DateTimeKind.Utc);
        set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime ExpiresOn {
        get => _expiresOn.DefaultKind(DateTimeKind.Utc);
        set => _expiresOn = value.DefaultKind(DateTimeKind.Utc);
    }

    public string? DetailsJson { get; set; }

    public Invite ToModel()
        => new() {
            Id = Id,
            Version = Version,
            Remaining = Remaining,
            ExpiresOn = ExpiresOn,
            CreatedAt = CreatedAt,
            CreatedBy = CreatedBy,
            Details = DetailsJson.IsNullOrEmpty() ? null : DetailsSerializer.Read(DetailsJson),
        };

    public void UpdateFrom(Invite model)
    {
        Id = model.Id;
        Version = model.Version;
        Remaining = model.Remaining;
        ExpiresOn = model.ExpiresOn;
        CreatedAt = model.CreatedAt;
        CreatedBy = model.CreatedBy;

        var details = model.Details;
        SearchKey = details?.GetSearchKey() ?? "";
        DetailsJson = details == null ? null : DetailsSerializer.Write(details);
    }
}
