using ActualChat.Invite.Backend;
using ActualChat.Invite.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;
using Stl.Generators;

namespace ActualChat.Invite;

internal class InvitesBackend : DbServiceBase<InviteDbContext>, IInvitesBackend
{
    private const string InviteCodeAlphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private readonly RandomStringGenerator _inviteCodeGenerator = new (10, InviteCodeAlphabet);

    private readonly IDbEntityConverter<DbInvite, Invite> _converter;

    public InvitesBackend(IServiceProvider services, IDbEntityConverter<DbInvite, Invite> converter) : base(services)
        => _converter = converter;

    // [ComputeMethod]
    public virtual async Task<IImmutableList<Invite>> GetAll(CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbInvites = await dbContext.Invites.ToListAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return dbInvites.Select(_converter.ToModel).ToImmutableArray();
    }

    // [ComputeMethod]
    public virtual async Task<Invite?> GetByCode(string inviteCode, CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbInvite = await dbContext.Invites.FirstOrDefaultAsync(x => x.Code == inviteCode, cancellationToken)
            .ConfigureAwait(false);
        return _converter.ToModel(dbInvite);
    }

    // [CommandHandler]
    public virtual async Task<Invite> Generate(
        IInvitesBackend.GenerateCommand command,
        CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating()) {
            _ = GetAll(default);
            return default!;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbInvite = _converter.ToEntity(command.Invite);
        dbInvite.Id = Ulid.NewUlid().ToString();
        dbInvite.Code = _inviteCodeGenerator.Next();
        dbInvite.Version = VersionGenerator.NextVersion();
        dbInvite.CreatedAt = Clocks.SystemClock.Now;
        dbInvite.ExpiresOn = Clocks.SystemClock.Now + TimeSpan.FromDays(7);
        dbContext.Invites.Add(dbInvite);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return _converter.ToModel(dbInvite);
    }

    // [CommandHandler]
    public virtual async Task UseInvite(
        IInvitesBackend.UseInviteCommand command,
        CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating()) {
            _ = GetAll(default);
            _ = GetByCode(command.Invite.Code, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var invite = command.Invite;
        var dbInvite = await dbContext.Invites
                .FirstOrDefaultAsync(x => x.Code == invite.Code, cancellationToken: cancellationToken)
                .ConfigureAwait(false)
            ?? throw new Exception($"Invite with code={invite.Code} not found");

        _converter.UpdateEntity(invite, dbInvite);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
