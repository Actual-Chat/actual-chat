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
using Stl.Text;

namespace ActualChat.Chat
{
    // [ComputeService, ServiceAlias(typeof(IChatService))]
    public class ChatService : DbServiceBase<ChatDbContext>, IChatService
    {
        private readonly Lazy<IMessageParser> _messageParserLazy;
        protected IAuthService AuthService { get; }
        protected IUserInfoService UserInfos { get; }
        protected IMessageParser MessageParser => _messageParserLazy.Value;

        public ChatService(IServiceProvider services) : base(services)
        {
            AuthService = services.GetRequiredService<IAuthService>();
            UserInfos = services.GetRequiredService<IUserInfoService>();
            _messageParserLazy = new Lazy<IMessageParser>(services.GetRequiredService<IMessageParser>);
        }

        // Commands

        public virtual async Task<ChatEntry> Post(
            ChatCommands.AddText command, CancellationToken cancellationToken = default)
        {
            var (session, chatId, text) = command;
            var context = CommandContext.GetCurrent();
            if (Computed.IsInvalidating()) {
                var invChatEntry = context.Operation().Items.Get<ChatEntry>();
                PseudoGetTail(chatId, default).Ignore();
                return null!;
            }

            var user = await AuthService.GetUser(session, cancellationToken);
            user.MustBeAuthenticated();
            var cp = await GetPermissions(session, chatId, cancellationToken);
            if ((cp & ChatPermission.Write) != ChatPermission.Write)
                throw new SecurityException("You can't post to this chat.");
            var parsedMessage = await MessageParser.Parse(text, cancellationToken);

            await using var dbContext = await CreateCommandDbContext(cancellationToken);
            var now = Clocks.SystemClock.Now;
            var dbChatEntry = new DbChatEntry() {
                ChatId = chatId,
                ContentType = ChatContentType.Text,
                UserId = user.Id,
                BeginsAt = now,
                EndsAt = now,
                Content = parsedMessage.Format(),
            };
            dbContext.Add(dbChatEntry);
            await dbContext.SaveChangesAsync(cancellationToken);

            var chatEntry = dbChatEntry.ToModel();
            context.Operation().Items.Set(chatEntry);
            return chatEntry;
        }

        // Queries

        public virtual async Task<Chat?> TryGet(
            string chatId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(chatId))
                return null;
            return new Chat(chatId) {
                OwnerIds = ImmutableHashSet<string>.Empty,
                IsPublic = false,
            };
        }

        public virtual async Task<ChatPermission> GetPermissions(
            Session session, string chatId, CancellationToken cancellationToken = default)
        {
            var user = await AuthService.GetUser(session, cancellationToken);
            if (!user.IsAuthenticated)
                return 0;
            var chat = await TryGet(chatId, cancellationToken);
            if (chat == null)
                return 0;
            if (chat.OwnerIds.Contains(user.Id))
                return ChatPermission.Owner;
            if (chat.IsPublic)
                return ChatPermission.Read;
            return 0;
        }

        public virtual async Task<ChatPage> GetTail(Session session, string chatId, int limit, CancellationToken cancellationToken = default)
        {
            var cp = await GetPermissions(session, chatId, cancellationToken);
            if ((cp & ChatPermission.Read) != ChatPermission.Read)
                throw new SecurityException("You can't access this chat.");
            return await GetTail(chatId, limit, cancellationToken);
        }

        public virtual async Task<long> GetMessageCount(
            string chatId, TimeSpan? period = null, CancellationToken cancellationToken = default)
        {
            await PseudoGetTail(chatId, default);
            await using var dbContext = CreateDbContext();
            var dbMessages = dbContext.ChatEntries.AsQueryable();
            if (period.HasValue) {
                var minCreatedAt = Clocks.SystemClock.UtcNow - period.Value;
                dbMessages = dbMessages.Where(m => m.ChatId == chatId && m.BeginsAt >= minCreatedAt);
            } else {
                dbMessages = dbMessages.Where(m => m.ChatId == chatId);
            }
            return await dbMessages.LongCountAsync(cancellationToken);
        }

        // Protected methods

        [ComputeMethod]
        protected virtual async Task<ChatPage> GetTail(string chatId, int limit, CancellationToken cancellationToken = default)
        {
            if (limit is < 1 or > 1000)
                throw new ArgumentOutOfRangeException(nameof(limit));
            await PseudoGetTail(chatId, default);
            await using var dbContext = CreateDbContext();

            // Fetching messages
            var dbEntries = await dbContext.ChatEntries.AsQueryable()
                .Where(m => m.ChatId == chatId)
                .OrderByDescending(m => m.Id)
                .Take(limit)
                .ToListAsync(cancellationToken);
            var entries = dbEntries.Select(m => m.ToModel()).ToImmutableList();

            // Fetching users in parallel
            var users = new Dictionary<Symbol, UserInfo>();
            foreach (var entriesPack in entries.PackBy(128)) {
                var usersPack = await Task.WhenAll(
                    entriesPack
                        .Where(e => !users.ContainsKey(e.UserId))
                        .Select(e => UserInfos.TryGet(e.UserId, cancellationToken)));
                foreach (var user in usersPack)
                    if (user != null)
                        users.TryAdd(user.Id, user);
            }

            return new ChatPage(chatId, limit) {
                Entries = entries,
                Users = users.ToImmutableDictionary(),
            };
        }

        // Invalidation-related

        [ComputeMethod]
        protected virtual Task<Unit> PseudoGetTail(string chatId, CancellationToken cancellationToken)
            => TaskEx.UnitTask;
    }
}
