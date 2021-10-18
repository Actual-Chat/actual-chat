using System.Diagnostics.CodeAnalysis;
using System.Security;
using ActualChat.Chat.Db;
using ActualChat.Redis;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

// [ComputeService, ServiceAlias(typeof(IChatService))]
public partial class ChatService : DbServiceBase<ChatDbContext>, IServerSideChatService
{
    protected IAuthService Auth { get; init; }
    protected IUserInfoService UserInfos { get; init; }
    protected IDbEntityResolver<string, DbChat> DbChatResolver { get; init; }
    protected IDbEntityResolver<string, DbChatEntry> DbChatEntryResolver { get; init; }
    protected RedisSequenceSet<ChatService> IdSequences { get; init; }

    public ChatService(IServiceProvider services) : base(services)
    {
        Auth = services.GetRequiredService<IAuthService>();
        UserInfos = services.GetRequiredService<IUserInfoService>();
        DbChatResolver = services.GetRequiredService<IDbEntityResolver<string, DbChat>>();
        DbChatEntryResolver = services.GetRequiredService<IDbEntityResolver<string, DbChatEntry>>();
        IdSequences = services.GetRequiredService<RedisSequenceSet<ChatService>>();
    }

    // Queries

    public virtual async Task<Chat?> TryGet(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        var chat = await TryGet(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return null;
        await AssertHasPermissions(chatId, user.Id, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return chat;
    }

    public virtual async Task<Chat?> TryGet(ChatId chatId, CancellationToken cancellationToken)
    {
        var dbChat = await DbChatResolver.TryGet(chatId, cancellationToken).ConfigureAwait(false);
        if (dbChat == null)
            return null;
        return dbChat.ToModel();
    }

    public virtual async Task<long> GetEntryCount(
        Session session,
        ChatId chatId,
        Range<long>? idRange,
        CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        await AssertHasPermissions(chatId, user.Id, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await GetEntryCount(chatId, idRange, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<long> GetEntryCount(
        ChatId chatId,
        Range<long>? idRange,
        CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext();
        var dbMessages = dbContext.ChatEntries.AsQueryable()
            .Where(m => m.ChatId == (string)chatId);

        if (idRange.HasValue) {
            var idRangeValue = idRange.GetValueOrDefault();
            ChatConstants.IdLogCover.AssertIsTile(idRangeValue);
            dbMessages = dbMessages.Where(m =>
                m.Id >= idRangeValue.Start && m.Id < idRangeValue.End);
        }

        return await dbMessages.LongCountAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<Range<long>> GetIdRange(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        await AssertHasPermissions(chatId, user.Id, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await GetIdRange(chatId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<Range<long>> GetIdRange(ChatId chatId, CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext();
        var firstId = await dbContext.ChatEntries.AsQueryable()
            .Where(e => e.ChatId == chatId.Value).OrderBy(e => e.Id).Select(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var lastId = await dbContext.ChatEntries.AsQueryable()
            .Where(e => e.ChatId == chatId.Value).OrderByDescending(e => e.Id).Select(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return (firstId, lastId);
    }

    public virtual async Task<ImmutableArray<ChatEntry>> GetEntries(
        Session session,
        ChatId chatId,
        Range<long> idRange,
        CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        await AssertHasPermissions(chatId, user.Id, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return await GetPage(chatId, idRange, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<ImmutableArray<ChatEntry>> GetPage(
        ChatId chatId,
        Range<long> idRange,
        CancellationToken cancellationToken)
    {
        ChatConstants.IdLogCover.AssertIsTile(idRange);

        await using var dbContext = CreateDbContext();
        var dbEntries = await dbContext.ChatEntries.AsQueryable()
            .Where(m => m.ChatId == (string)chatId && m.Id >= idRange.Start && m.Id < idRange.End)
            .OrderBy(m => m.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var entries = dbEntries.Select(m => m.ToModel()).ToImmutableArray();
        return entries;
    }

    // Permissions

    public virtual async Task<ChatPermissions> GetPermissions(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        return await GetPermissions(chatId, user.Id, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<ChatPermissions> GetPermissions(
        ChatId chatId,
        UserId userId,
        CancellationToken cancellationToken)
    {
        var chat = await TryGet(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return 0;
        if (chat.OwnerIds.Contains(userId))
            return ChatPermissions.All;
        if (ChatConstants.DefaultChatId == chatId)
            return ChatPermissions.All;
        if (chat.IsPublic)
            return ChatPermissions.Read;
        return 0;
    }

    // Protected methods
    [ComputeMethod]
    protected virtual async Task<Unit> AssertHasPermissions(
        Session session,
        ChatId chatId,
        ChatPermissions permissions,
        CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        return await AssertHasPermissions(chatId, (string)user.Id, permissions, cancellationToken)
            .ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<Unit> AssertHasPermissions(
        ChatId chatId,
        UserId userId,
        ChatPermissions permissions,
        CancellationToken cancellationToken)
    {
        var chatPermissions = await GetPermissions(chatId, userId, cancellationToken).ConfigureAwait(false);
        if ((chatPermissions & permissions) != permissions)
            throw new SecurityException("Not enough permissions.");
        return default;
    }

    [SuppressMessage("ReSharper", "RedundantArgumentDefaultValue")]
    protected void InvalidateChatPages(ChatId chatId, long chatEntryId, bool isUpdate = false)
    {
        if (!isUpdate)
            _ = GetEntryCount(chatId, null, default);
        foreach (var idRange in ChatConstants.IdLogCover.GetCoveringTiles(chatEntryId)) {
            _ = GetPage(chatId, idRange, default);
            if (!isUpdate)
                _ = GetEntryCount(chatId, idRange, default);
        }
    }

    private async Task<DbChatEntry> DbAddOrUpdate(
        ChatDbContext dbContext,
        ChatEntry chatEntry,
        CancellationToken cancellationToken)
    {
        // AK: Suspicious - probably can lead to performance issues
        // AY: Yes, but the goal is to have a dense sequence here;
        //     later we'll change this to something that's more performant.
        var isNew = chatEntry.Id == 0;
        DbChatEntry dbChatEntry;
        if (isNew) {
            var dbChatId = (string)chatEntry.ChatId;
            var maxId = await dbContext.ChatEntries.AsQueryable()
                .Where(e => e.ChatId == dbChatId)
                .OrderByDescending(e => e.Id)
                .Select(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            var id = await IdSequences.Next(dbChatId, maxId).ConfigureAwait(false);
            chatEntry = chatEntry with {
                Id = id,
                Version = VersionGenerator.NextVersion(),
            };
            dbChatEntry = new DbChatEntry(chatEntry);
            dbContext.Add(dbChatEntry);
        }
        else {
            dbChatEntry = await dbContext.FindAsync<DbChatEntry>(
                ComposeKey(DbChatEntry.GetCompositeId(chatEntry.ChatId, chatEntry.Id)),
                cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException(Invariant(
                    $"Chat entry with key {chatEntry.ChatId}, {chatEntry.Id} is not found"));
            chatEntry = chatEntry with {
                Version = VersionGenerator.NextVersion(dbChatEntry.Version),
            };
            dbChatEntry.UpdateFrom(chatEntry);
            dbContext.Update(dbChatEntry);
        }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbChatEntry;
    }
}

