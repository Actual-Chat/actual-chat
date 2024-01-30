using ActualChat.App.Server;
using ActualChat.Performance;
using ActualChat.Testing.Assertion;
using ActualChat.Testing.Host;
using ActualChat.Users;
using ActualLab.Fusion.Extensions;

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

        // non-place
        var (publicChatId, publicChatInviteId) = await _tester.CreateChat(true);
        var (privateChatId, privateChatInviteId) = await _tester.CreateChat(false);

        // public place
        var (publicPlaceRootChatId, _) = await _tester.CreateChat(x => x with {
            Kind = ChatKind.Place,
            IsPublic = true,
        });
        var publicPlaceId = publicPlaceRootChatId.PlaceId;
        var (publicPlacePublicChatId, publicPlacePublicChatInviteId) = await _tester.CreateChat(x => x with {
            IsPublic = true,
            Kind = null,
            PlaceId = publicPlaceId,
        });
        var (publicPlacePrivateChatId, publicPlacePrivateChatInviteId) = await _tester.CreateChat(x => x with {
            IsPublic = false,
            Kind = null,
            PlaceId = publicPlaceId,
        });

        // private place
        var (privatePlaceRootChatId, _) = await _tester.CreateChat(x => x with {
            Kind = ChatKind.Place,
            IsPublic = false,
        });
        var privatePlaceId = privatePlaceRootChatId.PlaceId;
        var (privatePlacePublicChatId, privatePlacePublicChatInviteId) = await _tester.CreateChat(x => x with {
            IsPublic = true,
            Kind = null,
            PlaceId = privatePlaceId,
        });
        var (privatePlacePrivateChatId, privatePlacePrivateChatInviteId) = await _tester.CreateChat(x => x with {
            IsPublic = false,
            Kind = null,
            PlaceId = privatePlaceId,
        });

        // act
        await _tester.InviteToPlace(publicPlaceRootChatId.PlaceId, bob.Id);
        await _tester.InviteToPlace(privatePlaceRootChatId.PlaceId, bob.Id);
        await _tester.SignIn(bob.User);
        await _tester.JoinChat(publicChatId, publicChatInviteId);
        await _tester.JoinChat(privateChatId, privateChatInviteId);
        await _tester.JoinChat(publicPlacePublicChatId, publicPlacePublicChatInviteId);
        await _tester.JoinChat(publicPlacePrivateChatId, publicPlacePrivateChatInviteId);
        await _tester.JoinChat(privatePlacePublicChatId, privatePlacePublicChatInviteId);
        await _tester.JoinChat(privatePlacePrivateChatId, privatePlacePrivateChatInviteId);

        // assert
        var expectedNonPlaceChatIds = new[] {
            new ContactId(bob.Id, publicChatId),
            new ContactId(bob.Id, privateChatId),
        };
        var expectedPublicPlaceChatIds = new[] {
            new ContactId(bob.Id, publicPlacePublicChatId),
            new ContactId(bob.Id, publicPlacePrivateChatId),
        };
        var expectedPrivatePlaceChatIds = new[] {
            new ContactId(bob.Id, privatePlacePublicChatId),
            new ContactId(bob.Id, privatePlacePrivateChatId),
        };
        var contactIds = await ListIds(PlaceId.None);
        contactIds.Should().BeEquivalentTo(expectedNonPlaceChatIds);

        contactIds = await ListIds(publicPlaceId);
        contactIds.Should().BeEquivalentTo(expectedPublicPlaceChatIds);

        contactIds = await ListIds(privatePlaceId);
        contactIds.Should().BeEquivalentTo(expectedPrivatePlaceChatIds);

        contactIds = await ListIdsForEntrySearch();
        contactIds.Should()
            .BeEquivalentTo(new[] {
                new ContactId(bob.Id, publicChatId),
                new ContactId(bob.Id, privateChatId),
                new ContactId(bob.Id, publicPlacePrivateChatId),
                new ContactId(bob.Id, publicPlaceId.ToRootChatId()),
                new ContactId(bob.Id, privatePlacePrivateChatId),
                new ContactId(bob.Id, privatePlaceId.ToRootChatId()),
            });

        contactIds = await ListIdsForContactSearch();
        contactIds.Should()
            .BeEquivalentTo(new[] {
                new ContactId(bob.Id, privateChatId),
                new ContactId(bob.Id, publicPlacePrivateChatId),
                new ContactId(bob.Id, privatePlacePublicChatId),
                new ContactId(bob.Id, privatePlacePrivateChatId),
                new ContactId(bob.Id, privatePlaceId.ToRootChatId()),
            });
    }

    [Fact]
    public async Task ShouldListOwnedChats()
    {
        // arrange
        var bob = await _tester.SignInAsBob();

        // act
        // non-place
        var (publicChatId, _) = await _tester.CreateChat(true);
        var (privateChatId, _) = await _tester.CreateChat(false);

        // public place
        var (publicPlaceRootChatId, _) = await _tester.CreateChat(x => x with {
            Kind = ChatKind.Place,
            IsPublic = true,
        });
        var publicPlaceId = publicPlaceRootChatId.PlaceId;
        var (publicPlacePublicChatId, _) = await _tester.CreateChat(x => x with {
            IsPublic = true,
            Kind = null,
            PlaceId = publicPlaceId,
        });
        var (publicPlacePrivateChatId, _) = await _tester.CreateChat(x => x with {
            IsPublic = false,
            Kind = null,
            PlaceId = publicPlaceId,
        });

        // private place
        var (privatePlaceRootChatId, _) = await _tester.CreateChat(x => x with {
            Kind = ChatKind.Place,
            IsPublic = false,
        });
        var privatePlaceId = privatePlaceRootChatId.PlaceId;
        var (privatePlacePublicChatId, _) = await _tester.CreateChat(x => x with {
            IsPublic = true,
            Kind = null,
            PlaceId = privatePlaceId,
        });
        var (privatePlacePrivateChatId, _) = await _tester.CreateChat(x => x with {
            IsPublic = false,
            Kind = null,
            PlaceId = privatePlaceId,
        });

        // act, assert
        var expectedNonPlaceChatIds = new[] {
            new ContactId(bob.Id, publicChatId),
            new ContactId(bob.Id, privateChatId),
        };
        var expectedPublicPlaceChatIds = new[] {
            new ContactId(bob.Id, publicPlacePublicChatId),
            new ContactId(bob.Id, publicPlacePrivateChatId),
        };
        var expectedPrivatePlaceChatIds = new[] {
            new ContactId(bob.Id, privatePlacePublicChatId),
            new ContactId(bob.Id, privatePlacePrivateChatId),
        };
        var contactIds = await ListIds(PlaceId.None);
        contactIds.Should().BeEquivalentTo(expectedNonPlaceChatIds);

        contactIds = await ListIds(publicPlaceId);
        contactIds.Should().BeEquivalentTo(expectedPublicPlaceChatIds);

        contactIds = await ListIds(privatePlaceId);
        contactIds.Should().BeEquivalentTo(expectedPrivatePlaceChatIds);

        contactIds = await ListIdsForEntrySearch();
        contactIds.Should()
            .BeEquivalentTo(new[] {
                new ContactId(bob.Id, publicChatId),
                new ContactId(bob.Id, privateChatId),
                new ContactId(bob.Id, publicPlacePrivateChatId),
                new ContactId(bob.Id, publicPlaceId.ToRootChatId()),
                new ContactId(bob.Id, privatePlacePrivateChatId),
                new ContactId(bob.Id, privatePlaceId.ToRootChatId()),
            });

        contactIds = await ListIdsForContactSearch();
        contactIds.Should()
            .BeEquivalentTo(new[] {
                new ContactId(bob.Id, privateChatId),
                new ContactId(bob.Id, publicPlacePrivateChatId),
                new ContactId(bob.Id, privatePlacePublicChatId),
                new ContactId(bob.Id, privatePlacePrivateChatId),
                new ContactId(bob.Id, privatePlaceId.ToRootChatId()),
            });
    }

    private async Task<List<ContactId>> ListIds(PlaceId placeId)
    {
        var contactIds = await _contacts.ListIds(_tester.Session, placeId, CancellationToken.None);
        return contactIds.Where(x => !Constants.Chat.SystemChatIds.Contains(x.ChatId)).ToList();
    }

    private async Task<List<ContactId>> ListIdsForEntrySearch()
    {
        var account = await _accounts.GetOwn(_tester.Session, CancellationToken.None);
        var contactIds = await _contactsBackend.ListIdsForEntrySearch(account.Id, CancellationToken.None);
        return contactIds.Where(x => !Constants.Chat.SystemChatIds.Contains(x.ChatId)).OrderBy(x => x.Id).ToList();
    }

    private async Task<List<ContactId>> ListIdsForContactSearch()
    {
        var account = await _accounts.GetOwn(_tester.Session, CancellationToken.None);
        var contactIds = await _contactsBackend.ListIdsForContactSearch(account.Id, CancellationToken.None);
        return contactIds.Where(x => !Constants.Chat.SystemChatIds.Contains(x.ChatId)).OrderBy(x => x.Id).ToList();
    }
}
