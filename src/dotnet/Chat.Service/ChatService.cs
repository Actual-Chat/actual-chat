using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Security;
using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Redis;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;
// ToDo: split this to two services (ChatServiceFacade and the real one)
public partial class ChatService : DbServiceBase<ChatDbContext>, IChatService, IChatServiceFacade
{
    protected IAuthService Auth { get; init; }
    protected IUserInfoService UserInfos { get; init; }
    protected ISessionInfoService Sessions { get; init; }
    protected IAuthorServiceFacade Authors { get; init; }
    protected IDbEntityResolver<string, DbChat> DbChatResolver { get; init; }
    protected IDbEntityResolver<string, DbChatEntry> DbChatEntryResolver { get; init; }
    protected RedisSequenceSet<ChatService> IdSequences { get; init; }

    public ChatService(
        IAuthService authService,
        IUserInfoService userInfoService,
        IDbEntityResolver<string, DbChat> dbChatResolver,
        IDbEntityResolver<string, DbChatEntry> dbChatEntryResolver,
        RedisSequenceSet<ChatService> idSequences,
        IAuthorServiceFacade authorService,
        IServiceProvider services,
        ISessionInfoService sessions) : base(services)
    {
        Auth = authService;
        UserInfos = userInfoService;
        DbChatResolver = dbChatResolver;
        DbChatEntryResolver = dbChatEntryResolver;
        IdSequences = idSequences;
        Authors = authorService;
        Sessions = sessions;
    }

    // Queries

    public virtual async Task<Chat?> TryGet(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        await AssertHasPermissions(chatId, user.Id, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        var chat = await TryGet(chatId, cancellationToken).ConfigureAwait(false);
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
            ChatConstants.IdTiles.AssertIsTile(idRangeValue);
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
        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

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
        ChatConstants.IdTiles.AssertIsTile(idRange);

        await using var dbContext = CreateDbContext();
        var dbEntries = await dbContext.ChatEntries
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
        foreach (var idRange in ChatConstants.IdTiles.GetCoveringTiles(chatEntryId)) {
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
            var maxId = await dbContext.ChatEntries.ForUpdate() // To serialize inserts
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
            var compositeId = DbChatEntry.GetCompositeId(chatEntry.ChatId, chatEntry.Id);
            dbChatEntry = await dbContext.ChatEntries.ForUpdate()
                .FirstOrDefaultAsync(e => e.CompositeId == compositeId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(Invariant(
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

    private async Task<string> GetOrCreateAuthorId(
        Session session,
        ChatId chatId,
        User user,
        ChatDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var sessionInfo = await Auth.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        var authorId = sessionInfo.Options[$"{chatId}::authorId"] as string;
        if (authorId == null) {
            var author = await dbContext.Authors.FirstOrDefaultAsync(x => x.UserId == (string)user.Id, cancellationToken)
                .ConfigureAwait(false);
            if (author == null) {
                // ToDo: use IAuthorService here after splitting to Facade and Service type
                var authorInfo = await Authors.GetByUserId(session, user.Id, cancellationToken).ConfigureAwait(false);
                author = new DbAuthor(authorInfo) {
                    Id = Ulid.NewUlid().ToString(),
                    UserId = user.IsAuthenticated ? (string?)user.Id : null,
                };
                await dbContext.Authors.AddAsync(author, cancellationToken).ConfigureAwait(false);
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            authorId = author.Id;

            await Sessions.Update(new(session, new($"{chatId}::authorId", authorId)), cancellationToken)
                .ConfigureAwait(false);
        }
        return authorId;
    }
}

