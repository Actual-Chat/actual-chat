using ActualChat.Performance;
using ActualChat.Search.Module;
using ActualChat.Testing.Host;
using ActualChat.Users;
using ActualLab.Generators;

namespace ActualChat.Search.IntegrationTests;

public class UserContactIndexingTest(ITestOutputHelper @out)
    : LocalAppHostTestBase( "contact_indexing_" + new RandomSymbolGenerator(length: 5, alphabet: Alphabet.AlphaLower).Next(),
        TestAppHostOptions.Default with {
            AppConfigurationExtender = cfg => {
                cfg.AddInMemory(($"{nameof(SearchSettings)}:{nameof(SearchSettings.IsSearchEnabled)}", "true"));
                cfg.AddInMemory(($"{nameof(SearchSettings)}:{nameof(SearchSettings.ContactIndexingDelay)}", "00:00:02"));
                cfg.AddInMemory(($"{nameof(SearchSettings)}:{nameof(SearchSettings.ContactIndexingSignalInterval)}", "00:00:00.5"));
            },
        },
        @out)
{
    private WebClientTester _tester = null!;
    private ISearchBackend _searchBackend = null!;
    private ICommander _commander = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Tracer.Default = Out.NewTracer();
        _tester = AppHost.NewWebClientTester(Out);
        _searchBackend = AppHost.Services.GetRequiredService<ISearchBackend>();
        _commander = AppHost.Services.Commander();
    }

    protected override async Task DisposeAsync()
    {
        Tracer.Default = Tracer.None;
        await _tester.DisposeAsync().AsTask();
    }

    [Fact(Skip = "Slow and unstable, must be fixed")]
    public async Task ShouldIndexAll()
    {
        // arrange
        var count = 50;
        var accounts = await _tester.CreateAccounts(count);

        // act
        var userId = accounts[^1].Id;

        // assert
        await Find(userId, "User", 20);
        var searchResults = await Find(userId, "User 3", 11);
        searchResults.Should().ContainSingle(x => x.SearchMatch.Text == "User 39");
        searchResults = await Find(userId, "User", 49, 50);
        searchResults.Should().NotContain(x => x.SearchMatch.Text == "User 49");

        // act
        for (int i = 30; i < 40; i++) {
            await _tester.SignIn(accounts[i].User);
            await _commander.Call(new Accounts_DeleteOwn(_tester.Session));
        }

        await _tester.SignIn(accounts[^1].User);
        await _commander.Call(new SearchBackend_Refresh(true));

        // assert
        searchResults = await Find(userId, "User 3", 1);
        searchResults.Should().BeEquivalentTo(ApiArray.New(BuildSearchResult(userId, accounts[3])));

        // act
        for (int i = 10; i < 20; i++) {
            var account = await _tester.SignIn(accounts[i].User);
            await _commander.Call(new Accounts_Update(_tester.Session,
                account with { LastName = "A" + i },
                account.Version));
        }

        // assert
        searchResults = await Find(userId, "User A", 10);
        var expected = accounts[10..20].Select(x => BuildSearchResult(userId, x.Id, "User A" + x.LastName)).ToArray();
        searchResults.Should().BeEquivalentTo(expected);
    }

    private async Task<ApiArray<ContactSearchResult>> Find(UserId userId, string criteria, int expectedCount, int requestCount = 20)
    {
        ContactSearchResultPage searchResults = ContactSearchResultPage.Empty;
        await TestExt.WhenMetAsync(async () => {
                searchResults = await _searchBackend.FindUserContacts(userId,
                    criteria,
                    0,
                    requestCount,
                    CancellationToken.None);
                Out.WriteLine("Found {0} out of expected {1}",
                        searchResults.Hits.Count,
                        expectedCount);
                searchResults.Offset.Should().Be(0);
                searchResults.Hits.Should().HaveCount(expectedCount);
            },
            TimeSpan.FromSeconds(10));
        return searchResults.Hits;
    }

    private static ContactSearchResult BuildSearchResult(UserId ownerId, AccountFull other)
        => BuildSearchResult(ownerId, other.Id, other.FullName);

    private static ContactSearchResult BuildSearchResult(UserId ownerId, UserId otherUserId, string fullName)
        => new (new ContactId(ownerId, new PeerChatId(ownerId, otherUserId).ToChatId()), SearchMatch.New(fullName));
}
