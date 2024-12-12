using ActualChat.Roulette;
using ActualChat.Users;

namespace ActualChat.Chat;

public class Roulette(IServiceProvider services) : IRoulette
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IAvatarsBackend AvatarsBackend { get; } = services.GetRequiredService<IAvatarsBackend>();
    private IAuthorsBackend AuthorsBackend { get; } = services.GetRequiredService<IAuthorsBackend>();
    private IUserPresencesBackend UserPresencesBackend { get; } = services.GetRequiredService<IUserPresencesBackend>();
    private IRouletteProfilesBackend RouletteProfilesBackend { get; } = services.GetRequiredService<IRouletteProfilesBackend>();
    private IRolesBackend RolesBackend { get; } = services.GetRequiredService<IRolesBackend>();
    private IRouletteBackend Backend { get; } = services.GetRequiredService<IRouletteBackend>();
    private ICommander Commander { get; } = services.Commander();

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

        // For test only.
        await Task.Delay(1500, cancellationToken);
        return result.ToImmutableArray();
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
        chatRoulette = new ChatRoulette(chatRouletteId) {
            ChatId = chat.Id,
            UserId1 = user1,
            UserId2 = user2,
        };
        var chatRouletteCommand = new RouletteBackend_ChangeChatRoulette(chatRouletteId, null, Change.Create(chatRoulette));
        await Commander.Call(chatRouletteCommand, cancellationToken).ConfigureAwait(false);

        return chat.Id;
    }

    // Commands
}
