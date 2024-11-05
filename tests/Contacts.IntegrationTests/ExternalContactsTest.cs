using System.Security.Claims;
using ActualChat.Testing.Host;
using ActualChat.Users;
using FluentAssertions.Equivalency;

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

    private string JackEmail { get; } = UniqueNames.Email("jack");
    private Phone JackPhone { get; } = UniqueNames.Phone();

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
        await Add(externalContact);
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
        await Add(externalContact);

        externalContact = externalContact.WithoutPhone(new ("1-234567890"))
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
        await Add(externalContact1, externalContact2);
        await Remove(externalContact1);
        var externalContacts = await List(deviceId);

        // assert
        externalContacts.Should().BeEquivalentTo([externalContact2], Including);
    }

    [Fact]
    public async Task ShouldConnectByPhone_BothAccountsExistBeforeSync()
    {
        // arrange
        var jack = await SignInAsUniqueJack();

        var bobDeviceId = NewDeviceId();
        var bob = await _tester.SignInAsUniqueBob();
        var externalContact = new ExternalContactFull(new ExternalContactId(new UserDeviceId(bob.Id, bobDeviceId), NewDeviceContactId()))
            .WithPhone(JackPhone)
            .WithPhone(UniqueNames.Phone())
            .WithHash(_hasher);

        // act
        await Add(externalContact);
        var bobContacts = await ListContactIds(1);

        jack = await _tester.SignIn(jack);
        var jackContacts = await ListContactIds(0);

        // assert
        bobContacts
            .Should()
            .BeEquivalentTo([BuildContactId(bob, jack)]);
        jackContacts.Should().BeEmpty("external contacts have synced for Bob only");
    }

    [Fact]
    public async Task ShouldConnectByPhone_FriendAccountCreatedAfterSync()
    {
        // arrange
        var bobDeviceId = NewDeviceId();
        var bob = await _tester.SignInAsUniqueBob();
        var bobContacts0 = await ListContactIds(0);
        var externalContact = new ExternalContactFull(new ExternalContactId(new UserDeviceId(bob.Id, bobDeviceId), NewDeviceContactId()))
            .WithPhone(JackPhone)
            .WithPhone(UniqueNames.Phone())
            .WithHash(_hasher);

        // act
        await Add(externalContact);
        var bobContacts = await ListContactIds(bobContacts0.Count);

        // assert
        bobContacts.Count.Should().Be(bobContacts0.Count);

        // act
        var jack = await SignInAsUniqueJack();
        var jackContacts = await ListContactIds(0);

        bob = await _tester.SignIn(bob);
        bobContacts = await ListContactIds(1);

        // assert
        bobContacts
            .Should()
            .BeEquivalentTo([BuildContactId(bob, jack)]);
        jackContacts.Should().BeEmpty("external contacts have synced for Bob only");
    }

    [Fact]
    public async Task ShouldConnectByEmail_FriendAccountCreatedAfterSync()
    {
        // arrange
        var bobDeviceId = NewDeviceId();
        var bob = await _tester.SignInAsUniqueBob();
        var externalContact =
            new ExternalContactFull(new ExternalContactId(new UserDeviceId(bob.Id, bobDeviceId), NewDeviceContactId()))
                .WithEmail(JackEmail)
                .WithHash(_hasher);

        // act
        await Add(externalContact);
        var bobContacts = await ListContactIds(0);

        // assert
        bobContacts.Should().BeEmpty();

        // act
        var jack = await SignInAsUniqueJack();
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
        var jack = await SignInAsUniqueJack();

        var bobDeviceId = NewDeviceId();
        var bob = await _tester.SignInAsUniqueBob();
        var externalContact = NewExternalContact(bob, bobDeviceId)
            .WithEmail(JackEmail)
            .WithHash(_hasher);

        // act
        await Add(externalContact);
        var contacts = await ListContactIds(1);

        // assert
        contacts.Should().BeEquivalentTo([BuildContactId(bob, jack)]);
    }

    [Fact]
    public async Task ShouldCreateSingleContact()
    {
        // arrange
        var jack = await SignInAsUniqueJack();

        var bobDeviceId = NewDeviceId();
        var bob = await _tester.SignInAsUniqueBob();
        var externalContact = NewExternalContact(bob, bobDeviceId)
            .WithPhone(JackPhone)
            .WithEmail(JackEmail)
            .WithHash(_hasher);

        // act
        await Add(externalContact);
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
        await SignInAsUniqueJack();

        var bobDeviceId = NewDeviceId();
        var bob = await _tester.SignInAsUniqueBob();
        var externalContact = NewExternalContact(bob, bobDeviceId)
            .WithPhone(UniqueNames.Phone())
            .WithEmail("jack.2@some.com")
            .WithHash(_hasher);

        // act
        await Add(externalContact);
        var contacts = await ListContactIds(0);
        contacts.Should().BeEmpty("no matching phones or emails");
    }

    // Private methods

    private Task<ApiArray<ExternalContact>> List(Symbol deviceId)
        => _externalContacts.List(_tester.Session, deviceId, CancellationToken.None);

    private async Task Add(params ExternalContactFull[] externalContacts)
    {
        var changes = externalContacts.Select(x => new ExternalContactChange(x.Id, null, Change.Create(x)));
        var results = await _commander.Call(new ExternalContacts_BulkChange(_tester.Session, changes.ToApiArray()));
        results.Select(x => x.Value).Should().NotContainNulls();
        var errors = results.Select(x => x.Error).SkipNullItems().ToList();
        if (errors.Count > 0)
            throw new AggregateException("Failed to create external contacts", errors);
    }

    private Task<ApiArray<Result<ExternalContactFull?>>> Update(ExternalContactFull externalContactFull)
        => _commander.Call(new ExternalContacts_BulkChange(_tester.Session,
            [new ExternalContactChange(externalContactFull.Id, null, Change.Update(externalContactFull))]));

    private Task<ApiArray<Result<ExternalContactFull?>>> Remove(ExternalContactFull externalContactFull)
        => _commander.Call(new ExternalContacts_BulkChange(_tester.Session,
            [new ExternalContactChange(externalContactFull.Id, null, Change.Remove<ExternalContactFull>())]));

    private async Task<List<ContactId>> ListContactIds(int expectedCount)
        => await ComputedTest.When(async ct => {
            var contactIds = await ListContactIds(ct);
            contactIds.Should().HaveCountGreaterOrEqualTo(expectedCount);
            return contactIds;
        }, TimeSpan.FromSeconds(10));

    private async Task<List<ContactId>> ListContactIds(CancellationToken cancellationToken = default)
    {
        var ids = await _contacts.ListIds(_tester.Session, PlaceId.None, cancellationToken);
        return ids.Where(x => x.ChatId.Kind == ChatKind.Peer && !Constants.Chat.SystemChatIds.Contains(x.ChatId)).ToList();
    }

    private static ExternalContactFull NewExternalContact(AccountFull owner, Symbol ownerDeviceId)
        => new (new ExternalContactId(new UserDeviceId(owner.Id, ownerDeviceId), NewDeviceContactId()));

    private static Symbol NewDeviceId()
        => new (Guid.NewGuid().ToString());

    private static Symbol NewDeviceContactId()
        => new (Guid.NewGuid().ToString());

    private static ContactId BuildContactId(AccountFull owner, AccountFull friendAccount)
        => ContactId.Peer(owner.Id, friendAccount.Id);

    private static EquivalencyAssertionOptions<ExternalContactFull> Including(EquivalencyAssertionOptions<ExternalContactFull> o)
        => o.Including(x => x.Id).Including(x => x.Hash);

    private Task<AccountFull> SignInAsUniqueJack()
        => _tester.SignInAsNew("Jack", x => x.WithPhone(JackPhone).WithClaim(ClaimTypes.Email, JackEmail));
}
