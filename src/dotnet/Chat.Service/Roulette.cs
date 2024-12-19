using ActualChat.Roulette;
using ActualChat.Users;

namespace ActualChat.Chat;

public class Roulette(IServiceProvider services) : IRoulette
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IAvatarsBackend AvatarsBackend { get; } = services.GetRequiredService<IAvatarsBackend>();
    private IAuthorsBackend AuthorsBackend { get; } = services.GetRequiredService<IAuthorsBackend>();
    private IUserPresencesBackend UserPresencesBackend { get; } = services.GetRequiredService<IUserPresencesBackend>();
    private IRouletteProfilesBackend RouletteProfilesBackend { get; } = services.GetRequiredService<IRouletteProfilesBackend>();
    private IRolesBackend RolesBackend { get; } = services.GetRequiredService<IRolesBackend>();
    private IRouletteBackend Backend { get; } = services.GetRequiredService<IRouletteBackend>();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<ChatCandidate>> FindChatCandidates(
        Session session,
        Preferences filter,
        CancellationToken cancellationToken)
    {
        // Find online users
        // Find users allowed participating in roulette
        // Find among them users that fit criteria
        // Try to exclude people you already talked to.
        // What if different profiles of the same user fit to the filter criteria?

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);

        var result = new List<ChatCandidate>();

        var sw = Stopwatch.GetTimestamp();
        var profilePrefs = await RouletteProfilesBackend.FindProfiles(filter, cancellationToken).ConfigureAwait(false);
        foreach (var profilePref in profilePrefs) {
            var profileId = profilePref.Id;
            var avatar = await AvatarsBackend.Get(profileId, cancellationToken).ConfigureAwait(false);
            if (avatar is null)
                continue;

            var userId = avatar.UserId;
            if (userId == account.Id)
                continue;

            var presence = await UserPresencesBackend.Get(userId, cancellationToken).ConfigureAwait(false);
            if (!presence.HasFlag(Presence.Online))
                continue;

            result.Add(new ChatCandidate(Profile.Create(avatar, profilePref)));
            if (result.Count >= 3)
                break;
        }

        // NOTE(DF): Delay is for test purpose only.
        var elapsed = (int)Stopwatch.GetElapsedTime(sw).TotalMilliseconds;
        if (elapsed < 1500)
            await Task.Delay(1500 - elapsed, cancellationToken);
        return result.ToImmutableArray();
    }

    // // [ComputeMethod]
    // public virtual async Task<ChatRoulette?> Get(Session session, ChatId chatId, CancellationToken cancellationToken)
    // {
    //     var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
    //     if (chat is null)
    //         return null;
    //
    //     if (!chat.IsChatRoulette())
    //         return null;
    //
    //     var authorId1 = new AuthorId(chatId, 1, AssumeValid.Option);
    //     var authorId2 = new AuthorId(chatId, 2, AssumeValid.Option);
    //     var author1 = await AuthorsBackend.Get(chatId, authorId1, AuthorsBackend_GetAuthorOption.Full, cancellationToken).ConfigureAwait(false);
    //     var author2 = await AuthorsBackend.Get(chatId, authorId2, AuthorsBackend_GetAuthorOption.Full, cancellationToken).ConfigureAwait(false);
    //     if (author1 is null || author2 is null)
    //         return null;
    //
    //     var profileId1 = author1.AvatarId;
    //     var profileId2 = author2.AvatarId;
    //     var chatRouletteId = new ChatRouletteId(profileId1, profileId2);
    //     var chatRoulette = await Backend.GetChatRoulette(chatRouletteId, cancellationToken).ConfigureAwait(false);
    //     return chatRoulette?.ToChatRoulette();
    // }

    // [ComputeMethod]
    public virtual async Task<ChatRouletteProfiles?> GetProfiles(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return null;

        if (!chat.IsChatRoulette())
            return null;

        var authorId1 = new AuthorId(chatId, 1, AssumeValid.Option);
        var authorId2 = new AuthorId(chatId, 2, AssumeValid.Option);
        var author1 = await AuthorsBackend.Get(chatId, authorId1, AuthorsBackend_GetAuthorOption.Full, cancellationToken).ConfigureAwait(false);
        var author2 = await AuthorsBackend.Get(chatId, authorId2, AuthorsBackend_GetAuthorOption.Full, cancellationToken).ConfigureAwait(false);
        if (author1 is null || author2 is null)
            return null;

        var profileId1 = author1.AvatarId;
        var profileId2 = author2.AvatarId;
        var chatRouletteId = new ChatRouletteId(profileId1, profileId2);
        var chatRoulette = await Backend.GetChatRoulette(chatRouletteId, cancellationToken).ConfigureAwait(false);
        if (chatRoulette is null)
            return null;

        var isOwner = authorId1 == chat.Rules.Author!.Id;
        var profile1 = await RouletteProfilesBackend.GetProfile(profileId1, cancellationToken).ConfigureAwait(false);
        var profile2 = await RouletteProfilesBackend.GetProfile(profileId2, cancellationToken).ConfigureAwait(false);
        return new ChatRouletteProfiles(chatRoulette.ToChatRoulette()) {
            OwnProfile = (isOwner ? profile1 : profile2) ?? Profile.None,
            PeerProfile = (isOwner ? profile2 : profile1) ?? Profile.None,
        };
    }

    public virtual async Task<ChatId> GetOrCreateChat(
        Session session,
        Symbol ownProfileId,
        Symbol peerProfileId,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var ownProfile = await RouletteProfilesBackend.GetProfile(ownProfileId, cancellationToken).ConfigureAwait(false);
        if (ownProfile is null || ownProfile.UserId != account.Id)
            return ChatId.None;

        var peerProfile = await RouletteProfilesBackend.GetProfile(peerProfileId, cancellationToken).ConfigureAwait(false);
        if (peerProfile is null || peerProfile.UserId == account.Id)
            return ChatId.None;

        var chatRouletteId = new ChatRouletteId(ownProfileId, peerProfileId);
        var chatRoulette = await Backend.GetChatRoulette(chatRouletteId, cancellationToken).ConfigureAwait(false);
        if (chatRoulette is not null)
            return chatRoulette.ChatId;

        var chatChange = Change.Create(new ChatDiff {
            Title = "Chat roulette",
            MediaId = ChatRoulette.MediaId,
            SystemTag = Constants.Chat.SystemTags.ChatRoulette
        });
        var chatCommand = new ChatsBackend_Change(ChatId.None, null, chatChange, account.Id);
        var chat = await Commander.Call(chatCommand, cancellationToken).ConfigureAwait(false);

        var ownAuthor = await AuthorsBackend.GetByUserId(chat.Id, account.Id, AuthorsBackend_GetAuthorOption.Full, cancellationToken).Require().ConfigureAwait(false);
        var changeOwnAvatarCommand = new AuthorsBackend_Upsert(chat.Id, ownAuthor.Id, UserId.None, null, new AuthorDiff { AvatarId = ownProfileId });
        await Commander.Call(changeOwnAvatarCommand, cancellationToken).ConfigureAwait(false);

        var addPeerUserCommand = new AuthorsBackend_Upsert(chat.Id, default, peerProfile.UserId, null, new AuthorDiff { AvatarId = peerProfileId });
        var peerAuthor = await Commander.Call(addPeerUserCommand, cancellationToken).ConfigureAwait(false);

        var ownerRole = await RolesBackend
            .GetSystem(chat.Id, SystemRole.Owner, cancellationToken)
            .Require()
            .ConfigureAwait(false);

        var promoteToOwnerCommand = new RolesBackend_Change(
            chat.Id,
            ownerRole.Id,
            ownerRole.Version,
            new Change<RoleDiff> {
                Update = new RoleDiff {
                    AuthorIds = new SetDiff<ApiArray<AuthorId>, AuthorId> {
                        AddedItems = [peerAuthor.Id],
                    },
                },
            });

        await Commander.Call(promoteToOwnerCommand, cancellationToken).ConfigureAwait(false);

        var user1 = ownProfile.UserId;
        var user2 = peerProfile.UserId;
        if (chatRouletteId.ProfileId1 != ownProfile.Id)
            (user1, user2) = (user2, user1);
        chatRoulette = new ChatRouletteFull(chatRouletteId) {
            ChatId = chat.Id,
            UserId1 = user1,
            UserId2 = user2,
        };
        var chatRouletteCommand = new RouletteBackend_ChangeChatRoulette(chatRouletteId, null, Change.Create(chatRoulette));
        await Commander.Call(chatRouletteCommand, cancellationToken).ConfigureAwait(false);

        return chat.Id;
    }

    // Commands

    //[CommandHandler]
    public virtual Task OnDeclineChatRoulette(Roulette_DeclineChatRoulette command, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
