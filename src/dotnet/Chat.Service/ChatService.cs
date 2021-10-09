using System.Diagnostics.CodeAnalysis;
using System.Security;
using ActualChat.Chat.Db;
using ActualChat.Redis;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat
{
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
            var user = await Auth.GetUser(session, cancellationToken);
            var chat = await TryGet(chatId, cancellationToken);
            if (chat == null)
                return null;
            await AssertHasPermissions(chatId, user.Id, ChatPermissions.Read, cancellationToken);
            return chat;
        }

        public virtual async Task<Chat?> TryGet(ChatId chatId, CancellationToken cancellationToken)
        {
            var dbChat = await DbChatResolver.TryGet(chatId, cancellationToken);
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
            var user = await Auth.GetUser(session, cancellationToken);
            await AssertHasPermissions(chatId, user.Id, ChatPermissions.Read, cancellationToken);
            return await GetEntryCount(chatId, idRange, cancellationToken);
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

            return await dbMessages.LongCountAsync(cancellationToken);
        }

        public virtual async Task<Range<long>> GetMinMaxId(
            Session session,
            ChatId chatId,
            CancellationToken cancellationToken)
        {
            var user = await Auth.GetUser(session, cancellationToken);
            await AssertHasPermissions(chatId, user.Id, ChatPermissions.Read, cancellationToken);
            return await GetMinMaxId(chatId, cancellationToken);
        }

        public virtual async Task<Range<long>> GetMinMaxId(ChatId chatId, CancellationToken cancellationToken)
        {
            await using var dbContext = CreateDbContext();
            var lastId = await dbContext.ChatEntries.AsQueryable()
                .Where(e => e.ChatId == (string) chatId)
                .OrderByDescending(e => e.Id)
                .Select(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken);
            return (0, lastId);
        }

        public virtual async Task<ImmutableArray<ChatEntry>> GetEntries(
            Session session,
            ChatId chatId,
            Range<long> idRange,
            CancellationToken cancellationToken)
        {
            var user = await Auth.GetUser(session, cancellationToken);
            await AssertHasPermissions(chatId, user.Id, ChatPermissions.Read, cancellationToken);
            return await GetPage(chatId, idRange, cancellationToken);
        }

        public virtual async Task<ImmutableArray<ChatEntry>> GetPage(
            ChatId chatId,
            Range<long> idRange,
            CancellationToken cancellationToken)
        {
            ChatConstants.IdLogCover.AssertIsTile(idRange);

            await using var dbContext = CreateDbContext();
            var dbEntries = await dbContext.ChatEntries.AsQueryable()
                .Where(m => m.ChatId == (string)chatId)
                .Where(m => m.Id >= idRange.Start && m.Id < idRange.End)
                .OrderBy(m => m.Id)
                .ToListAsync(cancellationToken);
            var entries = dbEntries.Select(m => m.ToModel()).ToImmutableArray();
            return entries;
        }

        // Permissions

        public virtual async Task<ChatPermissions> GetPermissions(
            Session session,
            ChatId chatId,
            CancellationToken cancellationToken)
        {
            var user = await Auth.GetUser(session, cancellationToken);
            return await GetPermissions(chatId, user.Id, cancellationToken);
        }

        public virtual async Task<ChatPermissions> GetPermissions(
            ChatId chatId,
            UserId userId,
            CancellationToken cancellationToken)
        {
            var chat = await TryGet(chatId, cancellationToken);
            if (chat == null)
                return 0;
            if (chat.OwnerIds.Contains(userId))
                return ChatPermissions.Owner;
            if (ChatConstants.DefaultChatId == chatId)
                return ChatPermissions.Owner;
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
            var user = await Auth.GetUser(session, cancellationToken);
            return await AssertHasPermissions(chatId, (string)user.Id, permissions, cancellationToken);
        }

        [ComputeMethod]
        protected virtual async Task<Unit> AssertHasPermissions(
            ChatId chatId,
            UserId userId,
            ChatPermissions permissions,
            CancellationToken cancellationToken)
        {
            var chatPermissions = await GetPermissions(chatId, userId, cancellationToken);
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
                var dbChatId = (string) chatEntry.ChatId;
                var maxId = await dbContext.ChatEntries.AsQueryable()
                    .Where(e => e.ChatId == dbChatId)
                    .OrderByDescending(e => e.Id)
                    .Select(e => e.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                var id = await IdSequences.Next(dbChatId, maxId);
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
                    cancellationToken);
                chatEntry = chatEntry with {
                    Version = VersionGenerator.NextVersion(dbChatEntry.Version),
                };
                dbChatEntry.UpdateFrom(chatEntry);
                dbContext.Update(dbChatEntry);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            return dbChatEntry;
        }
    }
}
