﻿using System;
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
using Stl.Async;
using Stl.Fusion;
using Stl.CommandR;
using Stl.Fusion.Authentication;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.Operations;
using Stl.Generators;

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
            var id = RandomStringGenerator.Default.Next(8, RandomStringGenerator.Base32Alphabet);
            var dbChat = new DbChat() {
                Id = id,
                Version = VersionGenerator.NextVersion(),
                Title = title,
                CreatorId = user.Id,
                CreatedAt = now,
                IsPublic = false,
                Owners = new List<DbChatOwner>() { new () { ChatId = id, UserId = user.Id } },
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
                GetLastEntryId(chatId, default).Ignore();
                InvalidateChatPages(chatId, invChatEntry.Id, false);
                return null!;
            }

            var user = await Auth.GetUser(session, cancellationToken);
            user.MustBeAuthenticated();
            await AssertHasPermissions(chatId, user.Id, ChatPermissions.Write, cancellationToken);

            await using var dbContext = await CreateCommandDbContext(cancellationToken);
            var now = Clocks.SystemClock.Now;
            var id = 1 + await dbContext.ChatEntries.AsQueryable()
                .Where(e => e.ChatId == chatId)
                .OrderByDescending(e => e.Id)
                .Select(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken);
            var dbChatEntry = new DbChatEntry() {
                CompositeId = DbChatEntry.GetCompositeId(chatId, id),
                ChatId = chatId,
                Id = id,
                Version = VersionGenerator.NextVersion(),
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
            var chat = await TryGet(chatId, cancellationToken);
            if (chat == null)
                return null;
            await AssertHasPermissions(chatId, user.Id, ChatPermissions.Read, cancellationToken);
            return chat;
        }

        public virtual async Task<Chat?> TryGet(
            string chatId, CancellationToken cancellationToken = default)
        {
            var dbChat = await DbChatResolver.TryGet(chatId, cancellationToken);
            if (dbChat == null)
                return null;
            return dbChat.ToModel();
        }

        public virtual async Task<long> GetEntryCount(
            Session session, string chatId, Range<long>? idRange,
            CancellationToken cancellationToken = default)
        {
            var user = await Auth.GetUser(session, cancellationToken);
            await AssertHasPermissions(chatId, user.Id, ChatPermissions.Read, cancellationToken);
            return await GetEntryCount(chatId, idRange, cancellationToken);
        }

        public virtual async Task<long> GetEntryCount(
            string chatId, Range<long>? idRange,
            CancellationToken cancellationToken = default)
        {
            await using var dbContext = CreateDbContext();
            var dbMessages = dbContext.ChatEntries.AsQueryable()
                .Where(m => m.ChatId == chatId);

            if (idRange.HasValue) {
                var idRangeValue = idRange.GetValueOrDefault();
                ChatConstants.IdLogCover.AssertIsTile(idRangeValue);
                dbMessages = dbMessages.Where(m =>
                    m.Id >= idRangeValue.Start && m.Id < idRangeValue.End);
            }

            return await dbMessages.LongCountAsync(cancellationToken);
        }

        public virtual async Task<ChatPage> GetPage(
            Session session, string chatId, Range<long> idRange, CancellationToken cancellationToken = default)
        {
            var user = await Auth.GetUser(session, cancellationToken);
            await AssertHasPermissions(chatId, user.Id, ChatPermissions.Read, cancellationToken);
            return await GetPage(chatId, idRange, cancellationToken);
        }

        public virtual async Task<ChatPage> GetPage(
            string chatId, Range<long> idRange, CancellationToken cancellationToken = default)
        {
            ChatConstants.IdLogCover.AssertIsTile(idRange);

            await using var dbContext = CreateDbContext();
            var dbEntries = await dbContext.ChatEntries.AsQueryable()
                .Where(m => m.ChatId == chatId)
                .Where(m => m.Id >= idRange.Start && m.Id < idRange.End)
                .OrderBy(m => m.Id)
                .ToListAsync(cancellationToken);
            var entries = dbEntries.Select(m => m.ToModel()).ToImmutableArray();
            return new ChatPage() { Entries = entries };
        }

        public virtual async Task<long> GetLastEntryId(
            Session session, string chatId, CancellationToken cancellationToken = default)
        {
            var user = await Auth.GetUser(session, cancellationToken);
            await AssertHasPermissions(chatId, user.Id, ChatPermissions.Read, cancellationToken);
            return await GetLastEntryId(chatId, cancellationToken);
        }

        public virtual async Task<long> GetLastEntryId(string chatId, CancellationToken cancellationToken = default)
        {
            await using var dbContext = CreateDbContext();
            var lastId = await dbContext.ChatEntries.AsQueryable()
                .Where(e => e.ChatId == chatId)
                .OrderByDescending(e => e.Id)
                .Select(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken);
            return lastId;
        }

        // Permissions

        public virtual async Task<ChatPermissions> GetPermissions(
            Session session, string chatId, CancellationToken cancellationToken = default)
        {
            var user = await Auth.GetUser(session, cancellationToken);
            return await GetPermissions(chatId, user.Id, cancellationToken);
        }

        public virtual async Task<ChatPermissions> GetPermissions(
            string chatId, string userId, CancellationToken cancellationToken = default)
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
            Session session, string chatId, ChatPermissions permissions,
            CancellationToken cancellationToken)
        {
            var user = await Auth.GetUser(session, cancellationToken);
            return await AssertHasPermissions(chatId, user.Id, permissions, cancellationToken);
        }

        [ComputeMethod]
        protected virtual async Task<Unit> AssertHasPermissions(
            string chatId, string userId, ChatPermissions permissions,
            CancellationToken cancellationToken)
        {
            var chatPermissions = await GetPermissions(chatId, userId, cancellationToken);
            if ((chatPermissions & permissions) != permissions)
                throw new SecurityException("Not enough permissions.");
            return default;
        }

        protected void InvalidateChatPages(string chatId, long chatEntryId, bool isUpdate = false)
        {
            if (!isUpdate)
                GetEntryCount(chatId, null, default).Ignore();
            foreach (var idRange in ChatConstants.IdLogCover.GetCoveringTiles(chatEntryId)) {
                GetPage(chatId, idRange, default).Ignore();
                if (!isUpdate)
                    GetEntryCount(chatId, idRange, default).Ignore();
            }
        }
    }
}
