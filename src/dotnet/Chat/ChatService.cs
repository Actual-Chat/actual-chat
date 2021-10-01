using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reactive;
using System.Security;
using ActualChat.Chat.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.Authentication;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.Operations;

namespace ActualChat.Chat
{
    // [ComputeService, ServiceAlias(typeof(IChatService))]
    public class ChatService : DbServiceBase<ChatDbContext>, IServerSideChatService
    {
        protected IAuthService Auth { get; init; }
        protected IUserInfoService UserInfos { get; init; }
        protected IDbEntityResolver<string, DbChat> DbChatResolver { get; init; }
        protected IDbEntityResolver<string, DbChatEntry> DbChatEntryResolver { get; init; }

        public ChatService(IServiceProvider services) : base(services)
        {
            Auth = services.GetRequiredService<IAuthService>();
            UserInfos = services.GetRequiredService<IUserInfoService>();
            DbChatResolver = services.GetRequiredService<IDbEntityResolver<string, DbChat>>();
            DbChatEntryResolver = services.GetRequiredService<IDbEntityResolver<string, DbChatEntry>>();
        }

        // Commands
        public virtual async Task<ChatEntry> CreateEntry(ChatCommands.CreateEntry command, CancellationToken cancellationToken)
        {
            var chatEntry = command.Entry;
            var context = CommandContext.GetCurrent();
            if (Computed.IsInvalidating()) {
                var invChatEntry = context.Operation().Items.Get<ChatEntry>();
                _ = GetIdRange(chatEntry.ChatId, default);
                InvalidateChatPages(chatEntry.ChatId, invChatEntry.Id, false);
                return null!;
            }

            await AssertHasPermissions(chatEntry.ChatId, chatEntry.AuthorId, ChatPermissions.Write, cancellationToken);

            await using var dbContext = await CreateCommandDbContext(cancellationToken);
            var dbChatEntry = await DbAddOrUpdate(dbContext, chatEntry, cancellationToken);
            chatEntry = dbChatEntry.ToModel();
            context.Operation().Items.Set(chatEntry);
            return chatEntry;
        }

        public virtual async Task<ChatEntry> UpdateEntry(ChatCommands.UpdateEntry command, CancellationToken cancellationToken)
        {
            var chatEntry = command.Entry;
            var context = CommandContext.GetCurrent();
            if (Computed.IsInvalidating()) {
                var invChatEntry = context.Operation().Items.Get<ChatEntry>();
                _ = GetIdRange(chatEntry.ChatId, default);
                InvalidateChatPages(chatEntry.ChatId, invChatEntry.Id, true);
                return null!;
            }

            await AssertHasPermissions(chatEntry.ChatId, chatEntry.AuthorId, ChatPermissions.Write, cancellationToken);

            await using var dbContext = await CreateCommandDbContext(cancellationToken);
            var dbChatEntry = await DbAddOrUpdate(dbContext, chatEntry, cancellationToken);
            chatEntry = dbChatEntry.ToModel();
            context.Operation().Items.Set(chatEntry);
            return chatEntry;
        }

        // Queries
        public virtual async Task<Chat> Create(ChatCommands.Create command, CancellationToken cancellationToken)
        {
            var (session, title) = command;
            var context = CommandContext.GetCurrent();
            if (Computed.IsInvalidating())
                return null!; // Nothing to invalidate

            var user = await Auth.GetUser(session, cancellationToken);
            user.MustBeAuthenticated();

            await using var dbContext = await CreateCommandDbContext(cancellationToken);
            var now = Clocks.SystemClock.Now;
            var id = (ChatId) Ulid.NewUlid().ToString();
            var dbChat = new DbChat() {
                Id = id,
                Version = VersionGenerator.NextVersion(),
                Title = title,
                AuthorId = user.Id,
                CreatedAt = now,
                IsPublic = false,
                Owners = new List<DbChatOwner> { new () { ChatId = id, UserId = user.Id } },
            };
            dbContext.Add(dbChat);
            await dbContext.SaveChangesAsync(cancellationToken);

            var chat = dbChat.ToModel();
            context.Operation().Items.Set(chat);
            return chat;
        }

        public virtual async Task<ChatEntry> Post(ChatCommands.Post command, CancellationToken cancellationToken)
        {
            var (session, chatId, text) = command;
            var context = CommandContext.GetCurrent();
            if (Computed.IsInvalidating()) {
                var invChatEntry = context.Operation().Items.Get<ChatEntry>();
                _ = GetIdRange(chatId, default);
                InvalidateChatPages(chatId, invChatEntry.Id, false);
                return null!;
            }

            var user = await Auth.GetUser(session, cancellationToken);
            user.MustBeAuthenticated();
            await AssertHasPermissions(chatId, user.Id, ChatPermissions.Write, cancellationToken);

            var dbContext = await CreateCommandDbContext(cancellationToken);
            var now = Clocks.SystemClock.Now;
            var chatEntry = new ChatEntry(chatId, 0) {
                AuthorId = (string) user.Id,
                BeginsAt = now,
                EndsAt = now,
                Content = text,
                ContentType = ChatContentType.Text,
            };
            var dbChatEntry = await DbAddOrUpdate(dbContext, chatEntry, cancellationToken);
            chatEntry = dbChatEntry.ToModel();
            context.Operation().Items.Set(chatEntry);
            return chatEntry;
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

        public virtual async Task<Range<long>> GetIdRange(
            Session session,
            ChatId chatId,
            CancellationToken cancellationToken)
        {
            var user = await Auth.GetUser(session, cancellationToken);
            await AssertHasPermissions(chatId, user.Id, ChatPermissions.Read, cancellationToken);
            return await GetIdRange(chatId, cancellationToken);
        }

        public virtual async Task<Range<long>> GetIdRange(ChatId chatId, CancellationToken cancellationToken)
        {
            await using var dbContext = CreateDbContext();
            var lastId = await dbContext.ChatEntries.AsQueryable()
                .Where(e => e.ChatId == (string)chatId)
                .OrderByDescending(e => e.Id)
                .Select(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken);
            return (0, lastId);
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
                var id = 1 + await dbContext.ChatEntries.AsQueryable()
                    .Where(e => e.ChatId == dbChatId)
                    .OrderByDescending(e => e.Id)
                    .Select(e => e.Id)
                    .FirstOrDefaultAsync(cancellationToken);
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
                    cancellationToken) ?? throw new InvalidOperationException($"dbChatEntry {chatEntry.ChatId} is null");
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
