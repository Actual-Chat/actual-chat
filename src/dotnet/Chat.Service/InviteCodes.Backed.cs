using ActualChat.Chat.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Generators;

namespace ActualChat.Chat;

partial class InviteCodes
{
    private const string InviteCodeAlphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private readonly RandomStringGenerator _inviteCodeGenerator = new (10, InviteCodeAlphabet);

    public virtual async Task<InviteCode?> GetByValue(string inviteCode, CancellationToken cancellationToken)
    {
        if (inviteCode.IsNullOrEmpty())
            return null;

        var dbContext = CreateDbContext();
        var now = Clocks.SystemClock.UtcNow;
        await using var _ = dbContext.ConfigureAwait(false);
        var dbInviteCode = await dbContext.InviteCodes
            .Where(c => c.State == InviteCodeState.Active && c.ExpiresOn>now)
            .Where(c => c.Value == inviteCode)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbInviteCode?.ToModel();
    }

    public virtual async Task<ImmutableArray<InviteCode>> Get(
        string chatId,
        string userId,
        CancellationToken cancellationToken)
    {
        if (chatId.IsNullOrEmpty() || userId.IsNullOrEmpty())
            return ImmutableArray.Create<InviteCode>();

        var dbContext = CreateDbContext();
        var now = Clocks.SystemClock.UtcNow;
        await using var _ = dbContext.ConfigureAwait(false);
        var dbInviteCodes = await dbContext.InviteCodes
            .Where(c => c.ChatId == chatId && c.CreatedBy == userId)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbInviteCodes.Select(c => c.ToModel()).ToImmutableArray();
    }

    // [CommandHandler]
    public virtual async Task<InviteCode> Generate(
        IInviteCodesBackend.GenerateCommand command,
        CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            var invInviteCode = context.Operation().Items.Get<InviteCode>()!;
            _ = Get(invInviteCode.ChatId, invInviteCode.CreatedBy, default);
            _ = GetByValue(invInviteCode.Value, default);
            return null!;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var inviteCode = command.InviteCode with {
            Id = Ulid.NewUlid().ToString(),
            Version = VersionGenerator.NextVersion(),
            CreatedAt = Clocks.SystemClock.Now,
            State = InviteCodeState.Active,
            Value = _inviteCodeGenerator.Next() // TODO: add reprocessing in case uniqueness conflicts
        };
        var dbInviteCode = new DbInviteCode(inviteCode);
        dbContext.Add(dbInviteCode);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        inviteCode = dbInviteCode.ToModel();
        context.Operation().Items.Set(inviteCode);
        return inviteCode;
    }
}
