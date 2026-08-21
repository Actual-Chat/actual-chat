using System.Text;
using ActualChat.Contacts;

namespace ActualChat.Chat;

/// <summary>
/// Bulk-creates test users, contacts, places and place chats to grow a dev account's
/// contact list to a realistic size. Backs the <c>/test-users</c> admin command.
/// </summary>
public sealed class TestDataGenerator(IServiceProvider services)
{
    public const string UserIdPrefix = "testuser";
    private const int Concurrency = 16;

    private static readonly string[] PlaceTitles = [
        "Acme Corp", "Bauhaus Studio", "Cosmic Rangers", "Delta Labs", "Evergreen Club",
        "Foundry Guild", "Granite Works", "Harbor Society", "Ironwood Team", "Jupiter Base",
    ];

    private IServiceProvider Services { get; } = services;
    private IAccountsBackend AccountsBackend { get; } = services.GetRequiredService<IAccountsBackend>();
    private ICommander Commander { get; } = services.Commander();
    private ILogger Log { get; } = services.LogFor<TestDataGenerator>();

    public async Task<string> Generate(
        Session session,
        AccountFull owner,
        Options options,
        CancellationToken cancellationToken)
    {
        var startIndex = await GetFirstFreeUserIndex(cancellationToken).ConfigureAwait(false);
        var userIds = Enumerable.Range(startIndex, options.UserCount)
            .Select(i => UserId.Parse(UserIdPrefix + i))
            .ToArray();

        Log.LogInformation("Creating {Count} test users starting from #{StartIndex}...",
            userIds.Length, startIndex);
        await userIds
            .Select(userId => CreateUser(owner.Id, userId, cancellationToken))
            .Collect(Concurrency, cancellationToken)
            .ConfigureAwait(false);

        var result = new StringBuilder();
        result.Append("Created ").Append(userIds.Length).Append(" test users (")
            .Append(userIds[0].Value).Append("..").Append(userIds[^1].Value)
            .Append(") - all of them are your contacts now.");
        for (var i = 0; i < options.PlaceCount; i++) {
            var memberIds = PickRandomSubset(userIds, options.PlaceSharePercent);
            var place = await CreatePlace(session, i, memberIds, cancellationToken).ConfigureAwait(false);
            result.Append("\nPlace \"").Append(place.Title).Append("\": ")
                .Append(memberIds.Length).Append(" members.");
        }
        return result.ToString();
    }

    // Private methods

    private async Task<int> GetFirstFreeUserIndex(CancellationToken cancellationToken)
    {
        // Test user ids are always allocated as a contiguous range starting from 0,
        // so a binary search over "does testuserN exist" finds the range's end.
        if (!await Exists(0).ConfigureAwait(false))
            return 0;

        var lastUsedIndex = 0;
        var firstFreeIndex = 1;
        while (await Exists(firstFreeIndex).ConfigureAwait(false)) {
            lastUsedIndex = firstFreeIndex;
            firstFreeIndex *= 2;
        }
        while (firstFreeIndex - lastUsedIndex > 1) {
            var midIndex = lastUsedIndex + ((firstFreeIndex - lastUsedIndex) / 2);
            if (await Exists(midIndex).ConfigureAwait(false))
                lastUsedIndex = midIndex;
            else
                firstFreeIndex = midIndex;
        }
        return firstFreeIndex;

        async Task<bool> Exists(int index) {
            var userId = UserId.Parse(UserIdPrefix + index);
            return await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false) != null;
        }
    }

    private async Task CreateUser(UserId ownerId, UserId userId, CancellationToken cancellationToken)
    {
        var (firstName, lastName) = RandomNameGenerator.Person.GenerateParts(userId.Value);
        var userInfo = new InternalUserInfo(userId, userId.Value, firstName, lastName) {
            AvatarBio = "Test user",
            HasGeneratedPicture = false,
        };
        await InternalAccounts.Create(Services, userInfo, cancellationToken).ConfigureAwait(false);
        await AddContact(ownerId, userId, cancellationToken).ConfigureAwait(false);
        await AddContact(userId, ownerId, cancellationToken).ConfigureAwait(false);
    }

    private Task AddContact(UserId ownerId, UserId userId, CancellationToken cancellationToken)
    {
        var contactId = ContactId.NewUser(ownerId, userId);
        var contact = new Contact(contactId) { State = ContactState.Regular };
        var command = new ContactsBackend_Change(contactId, null, Change.Create(contact));
        return Commander.Call(command, true, cancellationToken);
    }

    private async Task<Place> CreatePlace(
        Session session,
        int index,
        UserId[] memberIds,
        CancellationToken cancellationToken)
    {
        var title = PlaceTitles[index % PlaceTitles.Length];
        var isPublic = index % 2 == 0;
        var createPlaceCommand = new Places_Change {
            Session = session,
            PlaceId = null,
            ExpectedVersion = null,
            Change = Change.Create(new PlaceDiff {
                Title = title,
                Description = $"Test place with {memberIds.Length} test users",
                IsPublic = isPublic,
            }),
        };
        var place = await Commander.Call(createPlaceCommand, true, cancellationToken).ConfigureAwait(false);

        await CreatePlaceChat(session, place, "Welcome", Constants.Chat.SystemTags.Welcome, true, cancellationToken)
            .ConfigureAwait(false);
        var privateChat = await CreatePlaceChat(session, place, "Backstage", Symbol.Empty, false, cancellationToken)
            .ConfigureAwait(false);

        await Commander.Call(new Places_Invite {
            Session = session,
            PlaceId = place.Id,
            UserIds = memberIds,
        }, true, cancellationToken)
            .ConfigureAwait(false);
        // Public place chats can't be joined on someone else's behalf, so only the private
        // one gets explicit authors - that's what makes a chat-scoped mention list non-trivial.
        await Commander.Call(new Authors_Invite {
            Session = session,
            ChatId = privateChat.Id,
            UserIds = memberIds,
        }, true, cancellationToken)
            .ConfigureAwait(false);
        return place;
    }

    private Task<Chat> CreatePlaceChat(
        Session session,
        Place place,
        string title,
        Symbol systemTag,
        bool isPublic,
        CancellationToken cancellationToken)
    {
        var command = new Chats_Change {
            Session = session,
            ChatId = null,
            ExpectedVersion = null,
            Change = Change.Create(new ChatDiff {
                Title = title,
                Description = $"{title} chat of {place.Title}",
                IsPublic = isPublic,
                SystemTag = systemTag,
                PlaceId = place.Id,
            }),
        };
        return Commander.Call(command, true, cancellationToken);
    }

    private static UserId[] PickRandomSubset(UserId[] userIds, int percent)
    {
        var count = Math.Clamp(userIds.Length * percent / 100, 1, userIds.Length);
        var shuffled = userIds.ToArray();
        Random.Shared.Shuffle(shuffled);
        return shuffled[..count];
    }

    // Nested types

    public sealed record Options(int UserCount, int PlaceCount, int PlaceSharePercent);
}
