using System.Security.Claims;
using ActualChat.Performance;
using ActualChat.Testing.Host;
using ActualChat.Users;
using Mjml.Net.Extensions;

namespace ActualChat.Search.IntegrationTests;

[Collection(nameof(SearchCollection))]
public class ContactIndexingTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester _tester = null!;
    private ISearchBackend _sut = null!;
    private ICommander _commander = null!;

    protected override Task InitializeAsync()
    {
        Tracer.Default = Out.NewTracer();
        _tester = AppHost.NewWebClientTester(Out);
        _sut = AppHost.Services.GetRequiredService<ISearchBackend>();
        _commander = AppHost.Services.Commander();
        return Task.CompletedTask;
    }

    protected override async Task DisposeAsync()
    {
        Tracer.Default = Tracer.None;
        await _tester.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task ShouldFindAddedUsers()
    {
        // arrange
        var count = 50;
        var accounts = new AccountFull[count];
        for (int i = 0; i < accounts.Length; i++)
            accounts[i] = await _tester.SignIn(User(i));

        // act
        var userId = accounts[^1].Id;

        // assert
        await TestExt.WhenMetAsync(async () => {
            var results = await Find(userId, "User 3");
            results.Should().HaveCount(11);
        }, TimeSpan.FromSeconds(10));
    }

    private static User User(int i)
        => new User("", $"User {i}").WithClaim(ClaimTypes.GivenName, $"User")
            .WithClaim(ClaimTypes.Surname, i.ToInvariantString());

    private async Task<ApiArray<ContactSearchResult>> Find(UserId ownerId, string criteria)
    {
        var searchResults = await _sut.FindUserContacts(ownerId,
            criteria,
            0,
            20,
            CancellationToken.None);
        searchResults.Offset.Should().Be(0);
        return searchResults.Hits;
    }
}
