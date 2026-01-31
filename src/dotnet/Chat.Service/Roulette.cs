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
    private UrlMapper UrlMapper { get; } = services.UrlMapper();

    // [ComputeMethod]
    public virtual async Task<IReadOnlyList<ChatCandidate>> FindChatCandidates(
        Session session,
        Symbol profileId,
        Preferences filter,
        CancellationToken cancellationToken)
    {
        // Find online users
        // Find users allowed participating in roulette
        // Find among them users that fit criteria
        // Try to exclude people you already talked to.
        // What if different profiles of the same user fit to the filter criteria?

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var profile = await RouletteProfilesBackend.GetProfile(profileId, cancellationToken).ConfigureAwait(false);
        if (profile == null || profile.UserId != account.Id)
            return [];

        var result = new List<ChatCandidate>();

        var profilePrefs = await RouletteProfilesBackend.FindProfiles(account.Id, profileId, filter, cancellationToken).ConfigureAwait(false);
        foreach (var profilePref in profilePrefs) {
            var profileId2 = profilePref.Id;
            var avatar = await AvatarsBackend.Get(profileId2, cancellationToken).ConfigureAwait(false);
            if (avatar is null)
                continue;

            var userId = avatar.UserId;
            if (userId == account.Id)
                continue;

            var presence = await UserPresencesBackend.Get(userId, cancellationToken).ConfigureAwait(false);
            if (!presence.HasFlag(Presence.Online))
                continue;

            result.Add(new ChatCandidate(Profile.New(avatar, profilePref)));
            if (result.Count >= 3)
                break;
        }

        return result.ToArray();
    }

    // [ComputeMethod]
    public virtual async Task<ChatRouletteProfiles?> GetProfiles(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var profilesInternal = await GetProfilesInternal(session, chatId, cancellationToken).ConfigureAwait(false);
        if (profilesInternal is null)
            return null;

        var ownProfile = await RouletteProfilesBackend.GetProfile(profilesInternal.OwnProfileId, cancellationToken).ConfigureAwait(false);
        var peerProfile = await RouletteProfilesBackend.GetProfile(profilesInternal.PeerProfileId, cancellationToken).ConfigureAwait(false);
        return new ChatRouletteProfiles(profilesInternal.ChatRoulette.ToChatRoulette()) {
            OwnProfile = ownProfile,
            PeerProfile = peerProfile,
        };
    }

    [ComputeMethod]
    protected virtual async Task<ChatRouletteProfilesInternal?> GetProfilesInternal(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return null;

        if (!chat.IsChatRoulette())
            return null;

        var chatRouletteId = await RouletteExt.GetChatRouletteId(chatId, AuthorsBackend, cancellationToken).ConfigureAwait(false);
        if (chatRouletteId is null)
            return null;

        var chatRoulette = await Backend.GetChatRoulette(chatRouletteId, cancellationToken).ConfigureAwait(false);
        if (chatRoulette is null)
            return null;

        var ownUserId = chat.Rules.Account.Require().Id;
        var ownProfileId = chatRoulette.UserId1 == ownUserId ? chatRoulette.ProfileId1 : chatRoulette.ProfileId2;
        var peerProfileId = chatRoulette.UserId1 == ownUserId ? chatRoulette.ProfileId2 : chatRoulette.ProfileId1;
        return new ChatRouletteProfilesInternal(chatRoulette, ownProfileId, peerProfileId);
    }

    public record ChatRouletteProfilesInternal(
        ChatRouletteFull ChatRoulette,
        Symbol OwnProfileId,
        Symbol PeerProfileId);

    public virtual async Task<ChatId?> GetOrCreateChat(
        Session session,
        Symbol ownProfileId,
        Symbol peerProfileId,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var ownProfile = await RouletteProfilesBackend.GetProfile(ownProfileId, cancellationToken).ConfigureAwait(false);
        if (ownProfile is null || ownProfile.UserId != account.Id)
            return null;

        var peerProfile = await RouletteProfilesBackend.GetProfile(peerProfileId, cancellationToken).ConfigureAwait(false);
        if (peerProfile is null || peerProfile.UserId == account.Id)
            return null;

        var chatRouletteId = ChatRouletteId.New(ownProfileId, peerProfileId);
        var chatRoulette = await Backend.GetChatRoulette(chatRouletteId, cancellationToken).ConfigureAwait(false);
        if (chatRoulette is not null)
            return chatRoulette.ChatId;

        var chatChange = Change.Create(new ChatDiff {
            Title = "Chat roulette",
            MediaId = ChatRoulette.MediaId,
            SystemTag = Constants.Chat.SystemTags.ChatRoulette
        });
        var chatCommand = new ChatsBackend_Change(null, null, chatChange, account.Id);
        var chat = await Commander.Call(chatCommand, cancellationToken).ConfigureAwait(false);

        var ownAuthor = await AuthorsBackend.GetByUserId(chat.Id, account.Id, RequestedAuthorKind.Full, cancellationToken).Require().ConfigureAwait(false);
        var changeOwnAvatarCommand = new AuthorsBackend_Upsert(chat.Id, ownAuthor.Id, null, null, new AuthorDiff { AvatarId = ownProfileId });
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
                    AuthorIds = new SetDiff<AuthorId[], AuthorId> {
                        AddedItems = [peerAuthor.Id],
                    },
                },
            });

        await Commander.Call(promoteToOwnerCommand, cancellationToken).ConfigureAwait(false);

        var user1 = ownProfile.UserId;
        var user2 = peerProfile.UserId;
        if (chatRouletteId.ProfileId1 != ownProfile.Id)
            (user1, user2) = (user2, user1);
        var diff = new ChatRouletteFullDiff {
            ChatId = chat.Id,
            UserId1 = user1,
            UserId2 = user2,
            InitiatedBy = account.Id
        };
        var chatRouletteCommand = new RouletteBackend_ChangeChatRoulette(chatRouletteId, null, Change.Create(diff));
        await Commander.Call(chatRouletteCommand, cancellationToken).ConfigureAwait(false);

        return chat.Id;
    }

    public virtual Task<bool> EnableChatRouletteUI(Session session, CancellationToken cancellationToken)
        => Task.FromResult(!UrlMapper.IsVoxt);

    // Commands

    //[CommandHandler]
    public virtual async Task OnDeclineChatRoulette(Roulette_DeclineChatRoulette command, CancellationToken cancellationToken)
    {
        var (session, chatId) = command;
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var profiles = await GetProfilesInternal(session, chatId, cancellationToken).ConfigureAwait(false);
        if (profiles is null)
            return;

        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return;

        chat.Rules.Require(ChatPermissions.Leave);
        var chatRoulette = profiles.ChatRoulette;
        var ownUserId = chat.Rules.Account.Require().Id;
        if (chatRoulette.UserId1 != ownUserId && chatRoulette.UserId2 != ownUserId)
            throw StandardError.Constraint("Only chat roulette member can decline it.");

        if (chatRoulette.CompletedBy is null) {
            var diff = new ChatRouletteFullDiff {
                CompletedBy = ownUserId,
                CompleteReason = CompleteChatRouletteReason.Leave
            };
            var chatRouletteCommand = new RouletteBackend_ChangeChatRoulette(chatRoulette.Id, chatRoulette.Version, Change.Update(diff));
            await Commander.Call(chatRouletteCommand, cancellationToken).ConfigureAwait(false);
        }

        // NOTE(DF): All chat roulette members are the owners, and we don't check if there are other owners.
        var ownAuthor = chat.Rules.Author.Require();
        var upsertCommand = new AuthorsBackend_Upsert(
            chatId, ownAuthor.Id, default, ownAuthor.Version,
            new AuthorDiff() { HasLeft = true });
        await Commander.Call(upsertCommand, cancellationToken).ConfigureAwait(false);
    }
}
