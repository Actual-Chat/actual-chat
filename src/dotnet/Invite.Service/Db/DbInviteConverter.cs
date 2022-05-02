using Stl.Fusion.EntityFramework;

namespace ActualChat.Invite.Db;

public class DbInviteConverter : DbEntityConverter<InviteDbContext, DbInvite, Invite>
{
    private readonly ITextSerializer _serializer;

    public DbInviteConverter(IServiceProvider services) : base(services)
        => _serializer = SystemJsonSerializer.Default;

    public override DbInvite NewEntity() => new ();

    public override Invite NewModel() => new ();

    public override void UpdateEntity(Invite source, DbInvite target)
    {
        target.Id = source.Id;
        target.Version = source.Version;
        target.Code = source.Code;
        target.Remaining = source.Remaining;
        target.ExpiresOn = source.ExpiresOn;
        target.CreatedAt = source.CreatedAt;
        target.CreatedBy = source.CreatedBy;
        target.DetailsJson = source.Details != null ? _serializer.Write(source.Details) : null;
    }

    public override Invite UpdateModel(DbInvite source, Invite target)
        => target with {
            Id = source.Id,
            Version = source.Version,
            Code = source.Code,
            Remaining = source.Remaining,
            ExpiresOn = source.ExpiresOn,
            CreatedAt = source.CreatedAt,
            CreatedBy = source.CreatedBy,
            Details = !source.DetailsJson.IsNullOrEmpty()
                ? _serializer.Read<InviteDetailsDiscriminator>(source.DetailsJson)
                : null,
        };
}
