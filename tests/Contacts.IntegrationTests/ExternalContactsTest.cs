using System.Security.Claims;
using ActualChat.Testing.Host;
using AwesomeAssertions.Equivalency;

namespace ActualChat.Contacts.IntegrationTests;

[Collection(nameof(ExternalContactCollection))]
public class ExternalContactsTest(ExternalAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ExternalAppHostFixture>(fixture, @out)
{
    private WebClientTester _tester = null!;
    private IExternalContacts _externalContacts = null!;
    private ICommander _commander = null!;
    private IContacts _contacts = null!;
    private ExternalContactHasher _hasher = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _tester = AppHost.NewWebClientTester(Out);
        var services = AppHost.Services;
        _externalContacts = services.GetRequiredService<IExternalContacts>();
        _hasher = services.GetRequiredService<ExternalContactHasher>();
        _contacts = services.GetRequiredService<IContacts>();
        _commander = services.Commander();
    }

    protected override async Task DisposeAsync()
    {
        await _tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldReturnEmptyForAnonymous()
    {
        // arrange
        var deviceId = NewDeviceId();

        // act
        var externalContacts = await List(deviceId);

        // assert
        externalContacts.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldAdd()
    {
        // arrange
        var deviceId = NewDeviceId();
        var bob = await _tester.SignInAsUniqueBob();
        var externalContact = NewExternalContact(bob, deviceId)
            .WithPhone(UniqueNames.Phone())
            .WithPhone(UniqueNames.Phone())
            .WithEmail(UniqueNames.Email("John.White", "gmail.com"))
            .WithEmail(UniqueNames.Email("John.White", "icloud.com"))
            .WithHash(_hasher);

        // act
        await _tester.SaveExternalContacts(externalContact);
        var externalContacts = await List(deviceId);

        // assert
        externalContacts.Should().BeEquivalentTo([externalContact], Including);
    }

    [Fact]
    public async Task ShouldUpdate()
    {
        // arrange
        var deviceId = NewDeviceId();
        var bob = await _tester.SignInAsUniqueBob();
        var externalContact = NewExternalContact(bob, deviceId)
            .WithPhone(UniqueNames.Phone())
            .WithPhone(UniqueNames.Phone())
            .WithEmail(UniqueNames.Email("John.White", "gmail.com"))
            .WithEmail(UniqueNames.Email("John.White", "icloud.com"))
            .WithHash(_hasher);

        // act
        await _tester.SaveExternalContacts(externalContact);

        externalContact = externalContact.WithoutPhone(Phone.Parse("1-234567890"))
            .WithPhone(UniqueNames.Phone())
            .WithoutEmail(UniqueNames.Email("John.White", "icloud.com"))
            .WithEmail("John.White@somedomain.com");
        await Update(externalContact);

        var externalContacts = await List(deviceId);

        // assert
        externalContacts.Should().BeEquivalentTo([externalContact], Including);
    }

    [Fact]
    public async Task ShouldRemove()
    {
        // arrange
        var deviceId = NewDeviceId();
        var bob = await _tester.SignInAsUniqueBob();
        var externalContact1 = NewExternalContact(bob, deviceId)
            .WithPhone(UniqueNames.Phone())
            .WithPhone(UniqueNames.Phone())
            .WithEmail(UniqueNames.Email("John.White", "gmail.com"))
            .WithEmail(UniqueNames.Email("John.White", "icloud.com"))
            .WithHash(_hasher);
        var externalContact2 = NewExternalContact(bob, deviceId)
            .WithPhone(UniqueNames.Phone())
            .WithPhone(UniqueNames.Phone())
            .WithEmail("Jack.Snack@gmail.com")
            .WithEmail("jack.snack@icloud.com")
            .WithHash(_hasher);

        // act
        await _tester.SaveExternalContacts(externalContact1, externalContact2);
        await Remove(externalContact1);
        var externalContacts = await List(deviceId);

        // assert
        externalContacts.Should().BeEquivalentTo([externalContact2], Including);
    }

    [Fact]
    public async Task ShouldConnectByPhone_BothAccountsExistBeforeSync()
    {
        // arrange
        var jackIdentities = NewJackIdentities();
        var jack = await SignInAsUniqueJack(jackIdentities);

        var bobDeviceId = NewDeviceId();
        var bob = await _tester.SignInAsUniqueBob();
        var externalContact = new ExternalContactFull(ExternalContactId.New(UserDeviceId.New(bob.Id, bobDeviceId), NewDeviceContactId()))
            .WithPhone(jackIdentities.Phone)
            .WithPhone(UniqueNames.Phone())
            .WithHash(_hasher);

        // act
        await _tester.SaveExternalContacts(externalContact);
        var bobContacts = await ListContactIds(1);

        jack = await _tester.SignIn(jack);
        var jackContacts = await ListContactIds(0);

        // assert
        bobContacts
            .Should()
            .BeEquivalentTo([BuildContactId(bob, jack)]);
        jackContacts.Should().BeEmpty("external contacts have synced for Bob only");
    }

    [Fact(Skip = "Flaky")] // TODO(FC): fix
    public async Task ShouldConnectByPhone_FriendAccountCreatedAfterSync()
    {
        // arrange
        var jackIdentities = NewJackIdentities();
        var bobDeviceId = NewDeviceId();
        var bob = await _tester.SignInAsUniqueBob();
        var bobContacts0 = await ListContactIds(0);
        var externalContact = new ExternalContactFull(ExternalContactId.New(UserDeviceId.New(bob.Id, bobDeviceId), NewDeviceContactId()))
            .WithPhone(jackIdentities.Phone)
            .WithPhone(UniqueNames.Phone())
            .WithHash(_hasher);

        // act
        await _tester.SaveExternalContacts(externalContact);
        var bobContacts = await ListContactIds(bobContacts0.Count);

        // assert
        bobContacts.Count.Should().Be(bobContacts0.Count);

        // act
        var jack = await SignInAsUniqueJack(jackIdentities);
        var jackContacts = await ListContactIds(0);

        bob = await _tester.SignIn(bob);
        bobContacts = await ListContactIds(1);

        // assert
        bobContacts
            .Should()
            .BeEquivalentTo([BuildContactId(bob, jack)]);
        jackContacts.Should().BeEmpty("external contacts have synced for Bob only");
    }

    [Fact(Skip = "Flaky")]
    public async Task ShouldConnectByEmail_FriendAccountCreatedAfterSync()
    {
        // arrange
        var jackIdentities = NewJackIdentities();
        var bobDeviceId = NewDeviceId();
        var bob = await _tester.SignInAsUniqueBob();
        var externalContact =
            new ExternalContactFull(ExternalContactId.New(UserDeviceId.New(bob.Id, bobDeviceId), NewDeviceContactId()))
                .WithEmail(jackIdentities.Email)
                .WithHash(_hasher);

        // act
        await _tester.SaveExternalContacts(externalContact);
        var bobContacts = await ListContactIds(0);

        // assert
        bobContacts.Should().BeEmpty();

        // act
        var jack = await SignInAsUniqueJack(jackIdentities);
        var jackContacts = await ListContactIds(0);

        bob = await _tester.SignIn(bob);
        bobContacts = await ListContactIds(1);

        // assert
        bobContacts.Should().BeEquivalentTo([BuildContactId(bob, jack)]);
        jackContacts.Should().BeEmpty("external contacts have synced for Bob only");
    }

    [Fact]
    public async Task ShouldConnectByEmail()
    {
        // arrange
        var jackIdentities = NewJackIdentities();
        var jack = await SignInAsUniqueJack(jackIdentities);

        var bobDeviceId = NewDeviceId();
        var bob = await _tester.SignInAsUniqueBob();
        var externalContact = NewExternalContact(bob, bobDeviceId)
            .WithEmail(jackIdentities.Email)
            .WithHash(_hasher);

        // act
        await _tester.SaveExternalContacts(externalContact);
        var contacts = await ListContactIds(1);

        // assert
        contacts.Should().BeEquivalentTo([BuildContactId(bob, jack)]);
    }

    [Fact]
    public async Task ShouldCreateSingleContact()
    {
        // arrange
        var jackIdentities = NewJackIdentities();
        var jack = await SignInAsUniqueJack(jackIdentities);

        var bobDeviceId = NewDeviceId();
        var bob = await _tester.SignInAsUniqueBob();
        var externalContact = NewExternalContact(bob, bobDeviceId)
            .WithPhone(jackIdentities.Phone)
            .WithEmail(jackIdentities.Email)
            .WithHash(_hasher);

        // act
        await _tester.SaveExternalContacts(externalContact);
        var bobContacts = await ListContactIds(1);

        jack = await _tester.SignIn(jack);
        var jackContacts = await ListContactIds(0);

        // assert
        bobContacts.Should().BeEquivalentTo([BuildContactId(bob, jack)]);
        jackContacts.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldNotConnectWhenNoMatchingPhonesOrEmails()
    {
        // arrange
        var jackIdentities = NewJackIdentities();
        await SignInAsUniqueJack(jackIdentities);

        var bobDeviceId = NewDeviceId();
        var bob = await _tester.SignInAsUniqueBob();
        var externalContact = NewExternalContact(bob, bobDeviceId)
            .WithPhone(UniqueNames.Phone())
            .WithEmail("jack.2@some.com")
            .WithHash(_hasher);

        // act
        await _tester.SaveExternalContacts(externalContact);
        var contacts = await ListContactIds(0);
        contacts.Should().BeEmpty("no matching phones or emails");
    }

    [Theory(Skip = "Flaky")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkerShouldCreateContactsWithDisplayNameFromExternalContact(bool jackIsCreatedFirst)
    {
        // arrange
        var jackIdentities = NewJackIdentities();
        var tester2 = AppHost.NewWebClientTester(Out);
        AccountFull jack = null!;
        if (jackIsCreatedFirst)
            jack = await SignInAsUniqueJack(tester2, jackIdentities);

        var bobDeviceId = NewDeviceId();
        var bob = await _tester.SignInAsUniqueBob();
        var sessionBob = _tester.Session;
        const string contactDisplayName = "Jack Awesome";
        var externalContact = NewExternalContact(bob, bobDeviceId)
            .WithPhone(jackIdentities.Phone)
            .WithEmail(jackIdentities.Email)
            .WithDisplayName(contactDisplayName)
            .WithHash(_hasher);

        // act
        await _tester.SaveExternalContacts(externalContact);
        if (!jackIsCreatedFirst)
            jack = await SignInAsUniqueJack(tester2, jackIdentities);

        var bobContacts = await ListContactIds(sessionBob, 1);

        // assert
        var contactId = BuildContactId(bob, jack);
        bobContacts.Should().BeEquivalentTo([contactId]);

        await ComputedTest.When(async ct => {
            var contact1 = await _contacts.Get(sessionBob, contactId, ct);
            contact1.Should().NotBeNull();
            contact1.ExternalContactName.Should().Be(contactDisplayName);
            return contact1;
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task UpdateExternalContactNameTest()
    {
        // arrange
        var jackIdentities = NewJackIdentities();
        var tester2 = AppHost.NewWebClientTester(Out);
        var jack = await SignInAsUniqueJack(tester2, jackIdentities);

        var bobDeviceId = NewDeviceId();
        var bob = await _tester.SignInAsUniqueBob();
        var sessionBob = _tester.Session;
        const string contactDisplayName = "Jack Awesome";
        var externalContact = NewExternalContact(bob, bobDeviceId)
            .WithPhone(jackIdentities.Phone)
            .WithEmail(jackIdentities.Email)
            .WithDisplayName(contactDisplayName)
            .WithHash(_hasher);

        // act
        var result = await _tester.SaveExternalContacts(externalContact);
        var bobContacts = await ListContactIds(sessionBob, 1);

        // assert
        var contactId = BuildContactId(bob, jack);
        bobContacts.Should().BeEquivalentTo([contactId]);

        await ComputedTest.When(async ct => {
            var contact1 = await _contacts.Get(sessionBob, contactId, ct);
            contact1.Should().NotBeNull();
            contact1.ExternalContactName.Should().Be(contactDisplayName);
            return contact1;
        }, TimeSpan.FromSeconds(10));

        const string contactDisplayName2 = "Jack Awesome Super";
        externalContact = result[0]
            .WithDisplayName(contactDisplayName2)
            .WithHash(_hasher);

        // act
        await _tester.SaveExternalContacts(externalContact);

        // assert
        await ComputedTest.When(async ct => {
            var contact1 = await _contacts.Get(sessionBob, contactId, ct);
            contact1.Should().NotBeNull();
            contact1.ExternalContactName.Should().Be(contactDisplayName2);
            return contact1;
        }, TimeSpan.FromSeconds(10));

        // act
        await _tester.DeleteExternalContacts(externalContact.Id);

        // assert
        await ComputedTest.When(async ct => {
            var contact1 = await _contacts.Get(sessionBob, contactId, ct);
            contact1.Should().NotBeNull();
            contact1.ExternalContactName.Should().BeEmpty();
            return contact1;
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RejectsOversizedContactBatch()
    {
        // arrange
        const int clientBatchSize = 100;
        var account = await _tester.SignInAsUniqueBob();
        var deviceId = NewDeviceId();
        var clientBatch = NewRemovalBatch(account, deviceId, clientBatchSize);
        var oversizedBatch = NewRemovalBatch(
            account,
            deviceId,
            ExternalContacts_BulkChange.MaxChangeCount + 1);

        // act
        var clientBatchResult = await _commander.Call(
            new ExternalContacts_BulkChange(_tester.Session, clientBatch));
        var act = () => _commander.Call(
            new ExternalContacts_BulkChange(_tester.Session, oversizedBatch));

        // assert
        clientBatchResult.Should().HaveCount(clientBatchSize);
        clientBatchResult.Should().OnlyContain(x => x.Error == null);
        await act.Should().ThrowAsync<Exception>().WithMessage("*contact batch cannot contain more than*");
    }

    [Fact]
    public async Task RejectsOversizedContactPayload()
    {
        // arrange
        var account = await _tester.SignInAsUniqueBob();
        var deviceId = NewDeviceId();
        var longName = new string('n', ExternalContacts_BulkChange.MaxNameLength + 1);
        var tooManyHashes = Enumerable
            .Range(0, ExternalContacts_BulkChange.MaxHashCount + 1)
            .Select(i => i.Format())
            .ToApiSet();

        // act
        var longNameAct = () => _commander.Call(new ExternalContacts_BulkChange(_tester.Session,
            [NewCreation(account, deviceId, x => x with { DisplayName = longName })]));
        var manyHashesAct = () => _commander.Call(new ExternalContacts_BulkChange(_tester.Session,
            [NewCreation(account, deviceId, x => x with { PhoneHashes = tooManyHashes })]));

        // assert
        await longNameAct.Should().ThrowAsync<Exception>();
        await manyHashesAct.Should().ThrowAsync<Exception>()
            .WithMessage("*cannot have more than*");
    }

    // Private methods

    private Task<ExternalContact[]> List(Symbol deviceId)
        => _externalContacts.List(_tester.Session, deviceId, CancellationToken.None);

    private Task<Result<ExternalContactFull?>[]> Update(ExternalContactFull externalContactFull)
        => _commander.Call(new ExternalContacts_BulkChange(_tester.Session,
            [new ExternalContactChange(externalContactFull.Id, null, Change.Update(externalContactFull))]));

    private Task<Result<ExternalContactFull?>[]> Remove(ExternalContactFull externalContactFull)
        => _commander.Call(new ExternalContacts_BulkChange(_tester.Session,
            [new ExternalContactChange(externalContactFull.Id, null, Change.Remove<ExternalContactFull>())]));

    private Task<List<ContactId>> ListContactIds(int expectedCount)
        => ListContactIds(_tester.Session, expectedCount);

    private Task<List<ContactId>> ListContactIds(Session session,int expectedCount)
        => ComputedTest.When(async ct => {
            var contactIds = await ListContactIds(session, ct);
            contactIds.Should().HaveCountGreaterThanOrEqualTo(expectedCount);
            return contactIds;
        }, TimeSpan.FromSeconds(10));

    private async Task<List<ContactId>> ListContactIds(Session session, CancellationToken cancellationToken = default)
    {
        var ids = await _contacts.ListIds(session, null, cancellationToken);
        return ids
            .Where(x => x.ChatId.Kind == ChatKind.Peer)
            .ToList();
    }

    private static ExternalContactFull NewExternalContact(AccountFull owner, Symbol ownerDeviceId)
        => new (ExternalContactId.New(UserDeviceId.New(owner.Id, ownerDeviceId), NewDeviceContactId()));

    private static ExternalContactChange[] NewRemovalBatch(AccountFull owner, Symbol ownerDeviceId, int count)
        => Enumerable.Range(0, count)
            .Select(_ => new ExternalContactChange(
                ExternalContactId.New(UserDeviceId.New(owner.Id, ownerDeviceId), NewDeviceContactId()),
                null,
                Change.Remove<ExternalContactFull>()))
            .ToArray();

    private static ExternalContactChange NewCreation(
        AccountFull owner,
        Symbol ownerDeviceId,
        Func<ExternalContactFull, ExternalContactFull> configure)
    {
        var id = ExternalContactId.New(UserDeviceId.New(owner.Id, ownerDeviceId), NewDeviceContactId());
        return new ExternalContactChange(id, null, Change.Create(configure.Invoke(new ExternalContactFull(id))));
    }

    private static Symbol NewDeviceId()
        => new (Guid.NewGuid().ToString());

    private static Symbol NewDeviceContactId()
        => new (Guid.NewGuid().ToString());

    private static ContactId BuildContactId(AccountFull owner, AccountFull friendAccount)
        => ContactId.NewUser(owner.Id, friendAccount.Id);

    private static EquivalencyOptions<ExternalContactFull> Including(EquivalencyOptions<ExternalContactFull> o)
        => o.Including(x => x.Id).Including(x => x.Hash);

    private Task<AccountFull> SignInAsUniqueJack(Identities identities)
        => SignInAsUniqueJack(_tester, identities);

    private Task<AccountFull> SignInAsUniqueJack(WebClientTester tester, Identities identities)
        => tester.SignInAsNew("Jack", x => x
            .WithPhoneIdentity(identities.Phone)
            .WithClaim(ClaimTypes.Email, identities.Email));

    private Identities NewJackIdentities()
        => NewIdentities("jack");

    private Identities NewIdentities(string owner)
        => new (UniqueNames.Phone(), UniqueNames.Email(owner));

    // Nested types

    private sealed record Identities(Phone Phone, string Email);
}
