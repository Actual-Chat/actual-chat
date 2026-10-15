using ActualChat.Contacts.Db;
using ActualChat.Testing.Host;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Contacts.IntegrationTests;

[Collection(nameof(ContactCollection))]
public class ContactsArchiveTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester _tester = null!;
    private IContacts _contacts = null!;
    private IContactsBackend _contactsBackend = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _tester = AppHost.NewWebClientTester(Out);
        _contacts = AppHost.Services.GetRequiredService<IContacts>();
        _contactsBackend = AppHost.Services.GetRequiredService<IContactsBackend>();
    }

    protected override async Task DisposeAsync()
    {
        await _tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ArchivedChatShouldLeaveContactListAndEnterArchive()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        var (chatId, _) = await _tester.CreateChat(false);
        var contactId = ContactId.NewAny(bob.Id, chatId);
        await WaitForContact(contactId, true);

        // act
        await Archive(contactId);

        // assert
        await TestWait.When(async ct => {
            var contactIds = await _contacts.ListIds(_tester.Session, null, ct);
            contactIds.Should().NotContain(contactId);

            var count = await _contacts.GetArchivedCount(_tester.Session, null, ct);
            count.Should().Be(1);

            var archivedIds = await ListArchivedIds(null, ct);
            archivedIds.Should().Equal(contactId);

            var contact = await _contacts.GetForChat(_tester.Session, chatId, ct);
            contact!.IsArchived.Should().BeTrue();
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task UnarchivedChatShouldReturnToContactList()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        var (chatId, _) = await _tester.CreateChat(false);
        var contactId = ContactId.NewAny(bob.Id, chatId);
        await WaitForContact(contactId, true);
        await Archive(contactId);

        // act
        await Unarchive(contactId);

        // assert
        await TestWait.When(async ct => {
            var contactIds = await _contacts.ListIds(_tester.Session, null, ct);
            contactIds.Should().Contain(contactId);

            var count = await _contacts.GetArchivedCount(_tester.Session, null, ct);
            count.Should().Be(0);

            var contact = await _contacts.GetForChat(_tester.Session, chatId, ct);
            contact!.IsArchived.Should().BeFalse();
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ArchiveShouldListMostRecentlyArchivedFirst()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        var contactIds = new List<ContactId>();
        for (var i = 0; i < 3; i++) {
            var (chatId, _) = await _tester.CreateChat(false);
            contactIds.Add(ContactId.NewAny(bob.Id, chatId));
        }
        foreach (var contactId in contactIds)
            await WaitForContact(contactId, true);

        // act
        // Archived in the reverse order of creation, with a pause that keeps ArchivedAt apart
        foreach (var contactId in contactIds.AsEnumerable().Reverse()) {
            await Archive(contactId);
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        // assert
        await TestWait.When(async ct => {
            var archivedIds = await ListArchivedIds(null, ct);
            archivedIds.Should().Equal(contactIds);
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ArchiveShouldBePagedByTiles()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        var contactIds = new List<ContactId>();
        for (var i = 0; i < 3; i++) {
            var (chatId, _) = await _tester.CreateChat(false);
            var contactId = ContactId.NewAny(bob.Id, chatId);
            await WaitForContact(contactId, true);
            await Archive(contactId);
            await Task.Delay(TimeSpan.FromMilliseconds(50));
            contactIds.Add(contactId);
        }

        // act
        var tile = new Tile<int>(new Range<int>(1, 3), Constants.Contacts.ArchiveIndexTiles);
        var archivedIds = await _contactsBackend.ListArchivedIds(bob.Id, null, tile, CancellationToken.None);

        // assert
        archivedIds.Should().Equal(contactIds.AsEnumerable().Reverse().Skip(1).Take(2));
    }

    [Fact]
    public async Task NewMessageShouldNotReviveArchivedContact()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        var (chatId, _) = await _tester.CreateChat(false);
        var contactId = ContactId.NewAny(bob.Id, chatId);
        await WaitForContact(contactId, true);
        await Archive(contactId);

        // act
        var createCmd = new ContactsBackend_Change(contactId, null, Change.Create(new Contact(contactId) {
            State = ContactState.Regular,
        }));
        var result = await _tester.Commander.Call(createCmd);

        // assert
        result!.IsArchived.Should().BeTrue();
        var contactIds = await _contactsBackend.ListIds(bob.Id, null, CancellationToken.None);
        contactIds.Should().NotContain(contactId);
    }

    [Fact]
    public async Task BlockedContactShouldBeArchivedAndStayOutOfArchiveList()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        var alice = (await _tester.CreateAccounts(1))[0];
        var contactId = ContactId.NewUser(bob.Id, alice.Id);
        await _tester.CreatePeerContact(bob, alice);

        // act
        var blockCmd = new Contacts_SetIsBlocked { Session = _tester.Session, Id = contactId, IsBlocked = true };
        await _tester.Commander.Call(blockCmd);

        // assert
        var contact = await _contactsBackend.Get(bob.Id, contactId, CancellationToken.None);
        contact.IsArchived.Should().BeTrue();
        contact.IsBlocked.Should().BeTrue();
        var isBlocked = await _contactsBackend.IsBlocked(bob.Id, alice.Id, CancellationToken.None);
        isBlocked.Should().BeTrue();
        var count = await _contactsBackend.GetArchivedCount(bob.Id, null, CancellationToken.None);
        count.Should().Be(0, "blocked contacts are listed in the blocked list, not in the archive");
    }

    [Fact]
    public async Task UnblockedContactShouldStayArchived()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        var alice = (await _tester.CreateAccounts(1))[0];
        var contactId = ContactId.NewUser(bob.Id, alice.Id);
        await _tester.CreatePeerContact(bob, alice);
        var blockCmd = new Contacts_SetIsBlocked { Session = _tester.Session, Id = contactId, IsBlocked = true };
        await _tester.Commander.Call(blockCmd);

        // act
        var unblockCmd = new Contacts_SetIsBlocked { Session = _tester.Session, Id = contactId, IsBlocked = false };
        await _tester.Commander.Call(unblockCmd);

        // assert
        var contact = await _contactsBackend.Get(bob.Id, contactId, CancellationToken.None);
        contact.IsBlocked.Should().BeFalse();
        contact.IsArchived.Should().BeTrue();
        var count = await _contactsBackend.GetArchivedCount(bob.Id, null, CancellationToken.None);
        count.Should().Be(1);
    }

    [Fact]
    public async Task MigrationShouldMoveBlockedContactIntoArchive()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        var alice = (await _tester.CreateAccounts(1))[0];
        var contactId = ContactId.NewUser(bob.Id, alice.Id);
        await InsertLegacyBlockedContact(contactId, alice.Id);

        // act
        await _tester.Commander.Call(new ContactsBackend_MigrateBlocked(contactId), true);

        // assert
        var contact = await _contactsBackend.Get(bob.Id, contactId, CancellationToken.None);
        contact.IsArchived.Should().BeTrue();
        contact.IsBlocked.Should().BeTrue();
        var blocked = await _contactsBackend.ListBlockedForMigration(null, 10_000, CancellationToken.None);
        blocked.Select(x => x.Id).Should().NotContain(contactId);
    }

    [Fact]
    public async Task MigrationShouldMarkAlreadyArchivedContactAsBlocked()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        var alice = (await _tester.CreateAccounts(1))[0];
        var contactId = ContactId.NewUser(bob.Id, alice.Id);
        await _tester.CreatePeerContact(bob, alice);
        await Archive(contactId);
        await InsertLegacyBlockedContact(contactId, alice.Id);

        // act
        await _tester.Commander.Call(new ContactsBackend_MigrateBlocked(contactId), true);

        // assert
        var contact = await _contactsBackend.Get(bob.Id, contactId, CancellationToken.None);
        contact.IsArchived.Should().BeTrue();
        contact.IsBlocked.Should().BeTrue();
        await using var db = await AppHost.Services.DbHub<ContactsDbContext>().CreateDbContext();
        var legacyCount = await db.Contacts.CountAsync(c => c.Id == contactId.Value);
        legacyCount.Should().Be(0, "the migrated contact leaves the contact table");
    }

    [Fact]
    public async Task ArchivingSomeoneElsesContactShouldFail()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        var (chatId, _) = await _tester.CreateChat(false);
        var alice = (await _tester.CreateAccounts(1))[0];
        var foreignContactId = ContactId.NewAny(alice.Id, chatId);

        // act
        var archive = () => Archive(foreignContactId);

        // assert
        await archive.Should().ThrowAsync<Exception>();
        var count = await _contactsBackend.GetArchivedCount(alice.Id, null, CancellationToken.None);
        count.Should().Be(0);
        bob.Id.Should().NotBe(alice.Id);
    }

    [Fact]
    public async Task FreezingChatShouldRemoveArchivedContactToo()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        var (chatId, _) = await _tester.CreateChat(false);
        var contactId = ContactId.NewAny(bob.Id, chatId);
        await WaitForContact(contactId, true);
        await Archive(contactId);

        // act
        var freezeCmd = new Chats_Change {
            Session = _tester.Session,
            ChatId = chatId,
            ExpectedVersion = null,
            Change = Change.Update(new ChatDiff { IsFrozen = true }),
        };
        await _tester.Commander.Call(freezeCmd);
        await Task.Delay(TimeSpan.FromSeconds(1)); // Contacts react to the chat change via an event

        // assert
        var contact = await _contactsBackend.Get(bob.Id, contactId, CancellationToken.None);
        contact.IsArchived.Should().BeFalse("a frozen chat is removed from every list, the archive included");
        var count = await _contactsBackend.GetArchivedCount(bob.Id, null, CancellationToken.None);
        count.Should().Be(0);
    }

    [Fact]
    public async Task ArchivedContactShouldAcceptPinAndRename()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        var alice = (await _tester.CreateAccounts(1))[0];
        var contactId = ContactId.NewUser(bob.Id, alice.Id);
        await _tester.CreatePeerContact(bob, alice);
        await Archive(contactId);
        var archived = await _contactsBackend.Get(bob.Id, contactId, CancellationToken.None);

        // act
        var changed = archived with { IsPinned = true, PeerContactName = "Renamed" };
        var changeCmd = new Contacts_Change {
            Session = _tester.Session,
            Id = contactId,
            ExpectedVersion = archived.Version,
            Change = Change.Update(changed),
        };
        await _tester.Commander.Call(changeCmd);

        // assert
        var contact = await _contactsBackend.Get(bob.Id, contactId, CancellationToken.None);
        contact.IsArchived.Should().BeTrue();
        contact.IsPinned.Should().BeTrue();
        contact.PeerContactName.Should().Be("Renamed");
        contact.Version.Should().BeGreaterThan(archived.Version);
    }

    [Fact]
    public async Task RemovingArchivedContactShouldHonorItsVersion()
    {
        // arrange
        var bob = await _tester.SignInAsUniqueBob();
        var (chatId, _) = await _tester.CreateChat(false);
        var contactId = ContactId.NewAny(bob.Id, chatId);
        await WaitForContact(contactId, true);
        await Archive(contactId);
        var archived = await _contactsBackend.Get(bob.Id, contactId, CancellationToken.None);

        // act
        var removeCmd = new ContactsBackend_Change(contactId, archived.Version, Change.Remove<Contact>());
        await _tester.Commander.Call(removeCmd, true);

        // assert
        var count = await _contactsBackend.GetArchivedCount(bob.Id, null, CancellationToken.None);
        count.Should().Be(0);
    }

    private async Task InsertLegacyBlockedContact(ContactId contactId, UserId otherUserId)
    {
        await using var db = await AppHost.Services.DbHub<ContactsDbContext>().CreateDbContext(true);
        var contact = new Contact(contactId, 1) {
            UserId = otherUserId,
            State = ContactState.Blocked,
            TouchedAt = Clocks.SystemClock.Now,
        };
        db.Contacts.Add(new DbContact(contact));
        await db.SaveChangesAsync();
    }

    private async Task WaitForContact(ContactId contactId, bool mustExist)
        => await TestWait.When(async ct => {
            var contact = await _contactsBackend.Get(contactId.OwnerId, contactId, ct);
            contact.HasVersion().Should().Be(mustExist);
        }, TimeSpan.FromSeconds(10));

    private Task Archive(ContactId contactId)
        => _tester.Commander.Call(new Contacts_Archive { Session = _tester.Session, Id = contactId });

    private Task Unarchive(ContactId contactId)
        => _tester.Commander.Call(new Contacts_Unarchive { Session = _tester.Session, Id = contactId });

    private Task<ContactId[]> ListArchivedIds(PlaceId? placeId, CancellationToken cancellationToken)
    {
        var tile = Constants.Contacts.ArchiveIndexTiles.GetCoveringTiles(new Range<int>(0, 20)).First();
        return _contacts.ListArchivedIds(_tester.Session, placeId, tile, cancellationToken);
    }
}
