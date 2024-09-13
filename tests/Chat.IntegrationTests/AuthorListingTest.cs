using ActualChat.Chat.Db;
using ActualChat.Testing.Host;
using ActualChat.Users;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class AuthorListingTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatCollection.AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private IAuthorsBackend AuthorsBackend { get; } = fixture.AppHost.Services.GetRequiredService<IAuthorsBackend>();
    private DbHub<ChatDbContext> DbHub { get; } = fixture.AppHost.Services.DbHub<ChatDbContext>();

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 2)]
    [InlineData(2, 2, 2)]
    [InlineData(100, 20, 99)]
    public async Task ShouldListChanged(int placeCount, int memberCount, int limit)
    {
        // arrange
        var lastChanged = await GetLastChanged();
        var accounts = await Tester.CreateAccounts(memberCount);
        var places = await CreatePlaces(placeCount, accounts);

        // act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;
        var retrieved = await AuthorsBackend.BatchChangedPlaceAuthors(lastChanged?.Version ?? 0,
                long.MaxValue,
                lastChanged?.Id ?? AuthorId.None,
                limit,
                cancellationToken)
            .ToApiArrayAsync(cancellationToken)
            .Flatten();

        // assert
        var chatIds = places.Select(x => x.Id.ToRootChatId()).ToHashSet();
        var userIds = accounts.Select(x => x.Id).ToHashSet();
        foreach (var author in retrieved) {
            author.IsPlaceAuthor.Should().BeTrue();
            chatIds.Should().Contain(author.Id.ChatId);
            userIds.Should().Contain(author.UserId);
        }
    }

    private async Task<Place[]> CreatePlaces(int placeCount, AccountFull[] members)
    {
        var places = new Place[placeCount];
        for (int i = 0; i < placeCount; i++) {
            var place = await Tester.CreatePlace(i % 2 == 0, UniqueNames.Place(i));
            foreach (var account in members)
                await Tester.InviteToPlace(place.Id, account);
            places[i] = place;
        }

        return places;
    }

    private async Task<AuthorFull?> GetLastChanged(CancellationToken cancellationToken = default)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbAuthor = await dbContext.Authors
            .OrderByDescending(x => x.Version)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return dbAuthor?.ToModel();
    }
}
