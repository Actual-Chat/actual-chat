using ActualChat.Testing.Host;

namespace ActualChat.Contacts.IntegrationTests;

[Collection(nameof(ContactCollection))]
public class ContactsTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester _tester = null!;
    private IContacts _contacts = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _tester = AppHost.NewWebClientTester(Out);
        _contacts = AppHost.Services.GetRequiredService<IContacts>();
    }

    protected override async Task DisposeAsync()
    {
        await _tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldListNotOwnedChats()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        await _tester.SignInAsAlice();
        // non-place
        var (publicChatId, publicChatInviteId) = await _tester.CreateChat(true);
        var (privateChatId, privateChatInviteId) = await _tester.CreateChat(false);

        // public place
        var publicPlace = await _tester.CreatePlace(true);
        var (publicPlacePublicChatId, publicPlacePublicChatInviteId) = await _tester.CreateChat(x => x with {
            IsPublic = true,
            Kind = null,
            PlaceId = publicPlace.Id,
        });
        var (publicPlacePrivateChatId, publicPlacePrivateChatInviteId) = await _tester.CreateChat(x => x with {
            IsPublic = false,
            Kind = null,
            PlaceId = publicPlace.Id,
        });

        // private place
        var privatePlace = await _tester.CreatePlace(false);
        var (privatePlacePublicChatId, privatePlacePublicChatInviteId) = await _tester.CreateChat(x => x with {
            IsPublic = true,
            Kind = null,
            PlaceId = privatePlace.Id,
        });
        var (privatePlacePrivateChatId, privatePlacePrivateChatInviteId) = await _tester.CreateChat(x => x with {
            IsPublic = false,
            Kind = null,
            PlaceId = privatePlace.Id,
        });

        // act
        await _tester.InviteToPlace(publicPlace.Id, bob.Id);
        await _tester.InviteToPlace(privatePlace.Id, bob.Id);
        await _tester.SignIn(bob);
        await _tester.JoinChat(publicChatId, publicChatInviteId);
        await _tester.JoinChat(privateChatId, privateChatInviteId);
        await _tester.JoinChat(publicPlacePublicChatId, publicPlacePublicChatInviteId);
        await _tester.JoinChat(publicPlacePrivateChatId, publicPlacePrivateChatInviteId);
        await _tester.JoinChat(privatePlacePublicChatId, privatePlacePublicChatInviteId);
        await _tester.JoinChat(privatePlacePrivateChatId, privatePlacePrivateChatInviteId);
        await Task.Delay(TimeSpan.FromSeconds(1));

        // assert
        await ComputedTest.When(async ct => {
            var expectedNonPlaceChatIds = new[] {
                ContactId.NewAny(bob.Id, publicChatId),
                ContactId.NewAny(bob.Id, privateChatId),
            };
            var expectedPublicPlaceChatIds = new[] {
                ContactId.NewAny(bob.Id, publicPlacePublicChatId),
                ContactId.NewAny(bob.Id, publicPlacePrivateChatId),
            };
            var expectedPrivatePlaceChatIds = new[] {
                ContactId.NewAny(bob.Id, privatePlacePublicChatId),
                ContactId.NewAny(bob.Id, privatePlacePrivateChatId),
            };
            var contactIds = await ListIds(null, ct);
            contactIds.Should().BeEquivalentTo(expectedNonPlaceChatIds);

            contactIds = await ListIds(publicPlace.Id, ct);
            contactIds.Should().BeEquivalentTo(expectedPublicPlaceChatIds);

            contactIds = await ListIds(privatePlace.Id, ct);
            contactIds.Should().BeEquivalentTo(expectedPrivatePlaceChatIds);
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ShouldListOwnedChats()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();

        // act
        // non-place
        var (publicChatId, _) = await _tester.CreateChat(true);
        var (privateChatId, _) = await _tester.CreateChat(false);

        // public place
        var publicPlace = await _tester.CreatePlace(true);
        var (publicPlacePublicChatId, _) = await _tester.CreateChat(x => x with {
            IsPublic = true,
            Kind = null,
            PlaceId = publicPlace.Id,
        });
        var (publicPlacePrivateChatId, _) = await _tester.CreateChat(x => x with {
            IsPublic = false,
            Kind = null,
            PlaceId = publicPlace.Id,
        });

        // private place
        var privatePlace = await _tester.CreatePlace(false);
        var (privatePlacePublicChatId, _) = await _tester.CreateChat(x => x with {
            IsPublic = true,
            Kind = null,
            PlaceId = privatePlace.Id,
        });
        var (privatePlacePrivateChatId, _) = await _tester.CreateChat(x => x with {
            IsPublic = false,
            Kind = null,
            PlaceId = privatePlace.Id,
        });

        // act, assert
        await ComputedTest.When(async ct => {
            var expectedNonPlaceChatIds = new[] {
                ContactId.NewAny(bob.Id, publicChatId),
                ContactId.NewAny(bob.Id, privateChatId),
            };
            var expectedPublicPlaceChatIds = new[] {
                ContactId.NewAny(bob.Id, publicPlacePublicChatId),
                ContactId.NewAny(bob.Id, publicPlacePrivateChatId),
            };
            var expectedPrivatePlaceChatIds = new[] {
                ContactId.NewAny(bob.Id, privatePlacePublicChatId),
                ContactId.NewAny(bob.Id, privatePlacePrivateChatId),
            };
            var contactIds = await ListIds(null, ct);
            contactIds.Should().BeEquivalentTo(expectedNonPlaceChatIds);

            contactIds = await ListIds(publicPlace.Id, ct);
            contactIds.Should().BeEquivalentTo(expectedPublicPlaceChatIds);

            contactIds = await ListIds(privatePlace.Id, ct);
            contactIds.Should().BeEquivalentTo(expectedPrivatePlaceChatIds);
        }, TimeSpan.FromSeconds(10));
    }

    private async Task<List<ContactId>> ListIds(PlaceId? placeId, CancellationToken cancellationToken = default)
    {
        var contactIds = await _contacts.ListIds(_tester.Session, placeId, cancellationToken);
        return contactIds.Where(x => !x.ChatId.IsSystem).ToList();
    }
}
