using ActualChat.App.Server;
using ActualChat.Performance;
using ActualChat.Testing.Assertion;
using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Contacts.IntegrationTests;

public class ContactsTest(ITestOutputHelper @out) : AppHostTestBase(@out)
{
    private WebClientTester _tester = null!;
    private AppHost _appHost = null!;
    private IContacts _contacts = null!;
    private IContactsBackend _contactsBackend = null!;
    private IAccounts _accounts = null!;

    public override async Task InitializeAsync()
    {
        Tracer.Default = Out.NewTracer();
        _appHost = await NewAppHost();
        _tester = _appHost.NewWebClientTester();
        _contacts = _appHost.Services.GetRequiredService<IContacts>();
        _contactsBackend = _appHost.Services.GetRequiredService<IContactsBackend>();
        _accounts = _tester.AppServices.GetRequiredService<IAccounts>();
        FluentAssertions.Formatting.Formatter.AddFormatter(new UserFormatter());
    }

    public override async Task DisposeAsync()
    {
        Tracer.Default = Tracer.None;
        foreach (var formatter in FluentAssertions.Formatting.Formatter.Formatters.OfType<UserFormatter>().ToList())
            FluentAssertions.Formatting.Formatter.RemoveFormatter(formatter);
        await _tester.DisposeAsync().AsTask();
        _appHost.Dispose();
    }

    [Fact]
    public async Task ShouldListNotOwnedChats()
    {
        // arrange
        var bob = await _tester.SignInAsBob();
        await _tester.SignInAsAlice();
        var (placeRootChatId, _) = await _tester.CreateChat(x => x with {
            Kind = ChatKind.Place,
            IsPublic = true,
        });
        var placeId = placeRootChatId.PlaceId;
        await _tester.InviteToPlace(placeRootChatId.PlaceId, bob.Id);
        var (publicChatId, publicChatInviteId) = await _tester.CreateChat(true);
        var (privateChatId, privateChatInviteId) = await _tester.CreateChat(false);
        var (placePublicChatId, publicPlaceChatInviteId) = await _tester.CreateChat(x => x with {
            IsPublic = true,
            Kind = null,
            PlaceId = placeId,
        });
        var (placePrivateChatId, privatePlaceChatInviteId) = await _tester.CreateChat(x => x with {
            IsPublic = false,
            Kind = null,
            PlaceId = placeId,
        });

        // act
        await _tester.SignIn(bob.User);
        await _tester.JoinChat(publicChatId, publicChatInviteId);
        await _tester.JoinChat(privateChatId, privateChatInviteId);
        await _tester.JoinChat(placePublicChatId, publicPlaceChatInviteId);
        await _tester.JoinChat(placePrivateChatId, privatePlaceChatInviteId);

        // assert
        var expectedNonPlaceChatIds = new[] {
            new ContactId(bob.Id, publicChatId),
            new ContactId(bob.Id, privateChatId),
        };
        var expectedPlaceChatIds = new[] {
            new ContactId(bob.Id, placePublicChatId),
            new ContactId(bob.Id, placePrivateChatId),
        };
        await TestExt.WhenMetAsync(async () => {
                var contactIds = await ListIds(PlaceId.None);
                contactIds.Should().BeEquivalentTo(expectedNonPlaceChatIds);
            },
            TimeSpan.FromSeconds(10));
        await TestExt.WhenMetAsync(async () => {
                var contactIds = await ListIds(placeId);
                contactIds.Should().BeEquivalentTo(expectedPlaceChatIds);
            },
            TimeSpan.FromSeconds(10));
        var contactIds = await ListIdsEntryForSearch();
        contactIds.Should()
            .BeEquivalentTo(new[] {
                new ContactId(bob.Id, publicChatId),
                new ContactId(bob.Id, privateChatId),
                new ContactId(bob.Id, placePrivateChatId),
                new ContactId(bob.Id, placePublicChatId.PlaceChatId.PlaceId.ToRootChatId()),
            });
    }

    [Fact]
    public async Task ShouldListOwnedChats()
    {
        // arrange
        var alice = await _tester.SignInAsAlice();

        // act
        var (placeRootChatId, _) = await _tester.CreateChat(x => x with {
            Kind = ChatKind.Place,
            IsPublic = true,
        });
        var placeId = placeRootChatId.PlaceId;
        await _tester.InviteToPlace(placeRootChatId.PlaceId, alice.Id);
        var (publicChatId, _) = await _tester.CreateChat(true);
        var (privateChatId, _) = await _tester.CreateChat(false);
        var (placePublicChatId, _) = await _tester.CreateChat(x => x with {
            IsPublic = true,
            Kind = null,
            PlaceId = placeId,
        });
        var (placePrivateChatId, _) = await _tester.CreateChat(x => x with {
            IsPublic = false,
            Kind = null,
            PlaceId = placeId,
        });

        // assert
        var expectedNonPlaceChatIds = new[] {
            new ContactId(alice.Id, publicChatId),
            new ContactId(alice.Id, privateChatId),
        };
        var expectedPlaceChatIds = new[] {
            new ContactId(alice.Id, placePublicChatId),
            new ContactId(alice.Id, placePrivateChatId),
        };
        await TestExt.WhenMetAsync(async () => {
                var contactIds = await ListIds(PlaceId.None);
                contactIds.Should().BeEquivalentTo(expectedNonPlaceChatIds);
            },
            TimeSpan.FromSeconds(10));
        await TestExt.WhenMetAsync(async () => {
                var contactIds = await ListIds(placeId);
                contactIds.Should().BeEquivalentTo(expectedPlaceChatIds);
            },
            TimeSpan.FromSeconds(10));
        await TestExt.WhenMetAsync(async () => {
                var contactIds = await ListIdsEntryForSearch();
                contactIds.Should()
                    .BeEquivalentTo(new[] {
                        new ContactId(alice.Id, publicChatId),
                        new ContactId(alice.Id, privateChatId),
                        new ContactId(alice.Id, placePrivateChatId),
                        new ContactId(alice.Id, placePublicChatId.PlaceChatId.PlaceId.ToRootChatId()),
                    });
            },
            TimeSpan.FromSeconds(10));
    }

    private async Task<List<ContactId>> ListIds(PlaceId placeId)
    {
        var contactIds = await _contacts.ListIds(_tester.Session, placeId, CancellationToken.None);
        return contactIds.Where(x => !Constants.Chat.SystemChatIds.Contains(x.ChatId)).ToList();
    }

    private async Task<List<ContactId>> ListIdsEntryForSearch()
    {
        var account = await _accounts.GetOwn(_tester.Session, CancellationToken.None);
        var contactIds = await _contactsBackend.ListIdsForEntrySearch(account.Id, CancellationToken.None);
        return contactIds.Where(x => !Constants.Chat.SystemChatIds.Contains(x.ChatId)).ToList();
    }
}
