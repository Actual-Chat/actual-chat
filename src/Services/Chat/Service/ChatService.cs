using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reactive;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Chat.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion;
using Stl.Async;
using Stl.Collections;
using Stl.CommandR;
using Stl.Fusion.Authentication;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.Operations;
using Stl.Generators;
using Stl.Text;

namespace ActualChat.Chat
{
    // [ComputeService, ServiceAlias(typeof(IChatService))]
    public class ChatService : DbServiceBase<ChatDbContext>, IServerSideChatService
    {
        protected IAuthService Auth { get; init; }
        protected IUserInfoService UserInfos { get; init; }
        protected IDbEntityResolver<string, DbChat> DbChatResolver { get; init; }
        protected IDbEntityResolver<string, DbChatEntry> DbChatEntryResolver { get; init; }
        protected Func<string> ChatIdGenerator { get; init; }

        public ChatService(IServiceProvider services) : base(services)
        {
            Auth = services.GetRequiredService<IAuthService>();
            UserInfos = services.GetRequiredService<IUserInfoService>();
            DbChatResolver = services.GetRequiredService<IDbEntityResolver<string, DbChat>>();
            DbChatEntryResolver = services.GetRequiredService<IDbEntityResolver<string, DbChatEntry>>();
            ChatIdGenerator = () => Ulid.NewUlid().ToString();
        }

        // Commands

        public virtual async Task<Chat> Create(ChatCommands.Create command, CancellationToken cancellationToken = default)
        {
            var (session, title) = command;
            var context = CommandContext.GetCurrent();
            if (Computed.IsInvalidating())
                return null!; // Nothing to invalidate

            var user = await Auth.GetUser(session, cancellationToken);
            user.MustBeAuthenticated();

            await using var dbContext = await CreateCommandDbContext(cancellationToken);
            var now = Clocks.SystemClock.Now;
            var chatId = ChatIdGenerator.Invoke();
            var dbChat = new DbChat() {
                Id = chatId,
                Title = title,
                CreatorId = user.Id,
                CreatedAt = now,
                IsPublic = false,
                Owners = new List<DbChatOwner>() { new () { ChatId = chatId, UserId = user.Id } },
            };
            dbContext.Add(dbChat);
            await dbContext.SaveChangesAsync(cancellationToken);

            var chat = dbChat.ToModel();
            context.Operation().Items.Set(chat);
            return chat;
        }

        public virtual async Task<ChatEntry> Post(
            ChatCommands.Post command, CancellationToken cancellationToken = default)
        {
            var (session, chatId, text) = command;
            var context = CommandContext.GetCurrent();
            if (Computed.IsInvalidating()) {
                var invChatEntry = context.Operation().Items.Get<ChatEntry>();
                PseudoGetTail(chatId, default).Ignore();
                return null!;
            }

            var user = await Auth.GetUser(session, cancellationToken);
            user.MustBeAuthenticated();
            await AssertHasPermissions(chatId, user.Id, ChatPermissions.Write, cancellationToken);

            await using var dbContext = await CreateCommandDbContext(cancellationToken);
            var now = Clocks.SystemClock.Now;
            var dbChatEntry = new DbChatEntry() {
                ChatId = chatId,
                CreatorId = user.Id,
                BeginsAt = now,
                EndsAt = now,
                ContentType = ChatContentType.Text,
                Content = text,
            };
            dbContext.Add(dbChatEntry);
            await dbContext.SaveChangesAsync(cancellationToken);

            var chatEntry = dbChatEntry.ToModel();
            context.Operation().Items.Set(chatEntry);
            return chatEntry;
        }

        // Queries

        public virtual async Task<Chat?> TryGet(
            Session session, string chatId, CancellationToken cancellationToken = default)
        {
            var user = await Auth.GetUser(session, cancellationToken);
            await AssertHasPermissions(chatId, user.Id, ChatPermissions.Read, cancellationToken);
            return await TryGet(chatId, cancellationToken);
        }

        public virtual async Task<Chat?> TryGet(
            string chatId, CancellationToken cancellationToken = default)
        {
            var dbChat = await DbChatResolver.TryGet(chatId, cancellationToken);
            if (dbChat == null)
                return null;
            return dbChat.ToModel();
        }

        public virtual async Task<ChatPermissions> GetUserPermissions(
            string chatId, string userId, CancellationToken cancellationToken = default)
        {
            var chat = await TryGet(chatId, cancellationToken);
            if (chat == null)
                return 0;
            if (chat.OwnerIds.Contains(userId))
                return ChatPermissions.Owner;
            if (chat.IsPublic)
                return ChatPermissions.Read;
            return 0;
        }

        public virtual async Task<long> GetEntryCount(
            string chatId, TimeRange? timeRange,
            CancellationToken cancellationToken = default)
        {
            await using var dbContext = CreateDbContext();
            var dbMessages = dbContext.ChatEntries.AsQueryable()
                .Where(m => m.ChatId == chatId);

            if (timeRange.HasValue) {
                var vTimeRange = timeRange.GetValueOrDefault();
                if (!TimeLogCover.Default.IsValidSpan(vTimeRange))
                    throw new InvalidOperationException($"Invalid {nameof(timeRange)}.");
                var timeRangeStart = vTimeRange.Start.ToDateTimeClamped();
                var timeRangeEnd = vTimeRange.End.ToDateTimeClamped();
                dbMessages = dbMessages.Where(m => m.BeginsAt >= timeRangeStart && m.EndsAt < timeRangeEnd);
            }

            return await dbMessages.LongCountAsync(cancellationToken);
        }

        public virtual async Task<ImmutableList<ChatEntry>> GetTail(
            Session session, string chatId, CancellationToken cancellationToken = default)
        {
            var user = await Auth.GetUser(session, cancellationToken);
            await AssertHasPermissions(chatId, user.Id, ChatPermissions.Read, cancellationToken);
            return await GetTail(chatId, cancellationToken);
        }

        public virtual async Task<ImmutableList<ChatEntry>> GetTail(
            string chatId, CancellationToken cancellationToken = default)
        {
            var limit = 64;
            await PseudoGetTail(chatId, default);
            await using var dbContext = CreateDbContext();

            // Fetching messages
            var dbEntries = await dbContext.ChatEntries.AsQueryable()
                .Where(m => m.ChatId == chatId)
                .OrderByDescending(m => m.Id)
                .Take(limit)
                .ToListAsync(cancellationToken);
            var entries = dbEntries.Select(m => m.ToModel()).ToImmutableList();
            return entries;
        }

        // Protected methods

        [ComputeMethod]
        protected virtual async Task AssertHasPermissions(
            string chatId, string userId, ChatPermissions permissions,
            CancellationToken cancellationToken)
        {
            var chatPermissions = await GetUserPermissions(chatId, userId, cancellationToken);
            if ((chatPermissions & permissions) != permissions)
                throw new SecurityException("Not enough permissions.");
        }

        [ComputeMethod]
        protected virtual async Task AssertHasPermissions(
            Session session, string chatId, ChatPermissions permissions,
            CancellationToken cancellationToken)
        {
            var user = await Auth.GetUser(session, cancellationToken);
            await AssertHasPermissions(chatId, user.Id, permissions, cancellationToken);
        }

        // Invalidation-related

        [ComputeMethod]
        protected virtual Task<Unit> PseudoGetTail(string chatId, CancellationToken cancellationToken)
            => TaskEx.UnitTask;

    }
}
