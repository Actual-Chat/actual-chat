using ActualChat.Chat.Db;
using ActualChat.Users;
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

    [ComputeMethod]
    public virtual async Task<bool> CheckIfInviteCodeUsed(Session session, string chatId, CancellationToken cancellationToken)
    {
        var options = await _auth.GetOptions(session, cancellationToken).ConfigureAwait(false);
        if (!options.Items.TryGetValue("InviteCode::ChatId", out var inviteChatId))
            return false;
        if (!string.Equals(chatId, inviteChatId as string, StringComparison.Ordinal))
            return false;
        return true;
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
            Id = Ulid.NewUlid().ToString().ToLowerInvariant(),
            Version = VersionGenerator.NextVersion(),
            CreatedAt = Clocks.SystemClock.Now,
            State = InviteCodeState.Active,
            Value = _inviteCodeGenerator.Next(), // TODO: add reprocessing in case uniqueness conflicts
        };
        var dbInviteCode = new DbInviteCode(inviteCode);
        dbContext.Add(dbInviteCode);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        inviteCode = dbInviteCode.ToModel();
        context.Operation().Items.Set(inviteCode);
        return inviteCode;
    }

    // [CommandHandler]
    public virtual async Task UseInviteCode(IInviteCodesBackend.UseInviteCodeCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, inviteCode) = command;
        var updateOptionCommand = new ISessionOptionsBackend.UpsertCommand(
            session,
            new("InviteCode::Id", inviteCode.Id.Value));
        await _commander.Call(updateOptionCommand, true, cancellationToken).ConfigureAwait(false);
        var updateOptionCommand2 = new ISessionOptionsBackend.UpsertCommand(
            session,
            new("InviteCode::ChatId", inviteCode.ChatId.Value));
        await _commander.Call(updateOptionCommand2, true, cancellationToken).ConfigureAwait(false);
    }
}
