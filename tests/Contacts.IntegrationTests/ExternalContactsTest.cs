using System.Security.Claims;
using ActualChat.Performance;
using ActualChat.Testing.Assertion;
using ActualChat.Testing.Host;
using ActualChat.Users;
using FluentAssertions.Equivalency;
using Microsoft.AspNetCore.Authentication.Google;

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

    private static string BobEmail => "bob@actual.chat";
    private static Phone BobPhone => new ("1-2345678901");
    private static User Bob { get; } = new User("", $"Bob-{nameof(ExternalContactsTest)}")
        .WithIdentity(new UserIdentity(GoogleDefaults.AuthenticationScheme, "111"))
        .WithPhone(BobPhone)
        .WithClaim(ClaimTypes.Email, BobEmail);

    private static string JackEmail => "jack@actual.chat";
    private static Phone JackPhone => new ("1-3456789012");
    private static User Jack { get; } = new User("", $"JackAdmin-{nameof(ExternalContactsTest)}")
        .WithIdentity(new UserIdentity(GoogleDefaults.AuthenticationScheme, "222"))
        .WithPhone(JackPhone)
        .WithClaim(ClaimTypes.Email, JackEmail);

    protected override Task InitializeAsync()
    {
        Tracer.Default = Out.NewTracer();
        _tester = AppHost.NewWebClientTester(Out);
        var services = AppHost.Services;
        _externalContacts = services.GetRequiredService<IExternalContacts>();
        _hasher = services.GetRequiredService<ExternalContactHasher>();
        _contacts = services.GetRequiredService<IContacts>();
        _commander = services.Commander();

        FluentAssertions.Formatting.Formatter.AddFormatter(new UserFormatter());
        return Task.CompletedTask;
    }

    protected override async Task DisposeAsync()
    {
        Tracer.Default = Tracer.None;
        foreach (var formatter in FluentAssertions.Formatting.Formatter.Formatters.OfType<UserFormatter>().ToList())
            FluentAssertions.Formatting.Formatter.RemoveFormatter(formatter);
        await _tester.DisposeAsync().AsTask();
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
        var bob = await _tester.SignIn(Bob);
        var externalContact = NewExternalContact(bob, deviceId)
            .WithPhone(new Phone("1-234567890"))
            .WithPhone(new Phone("2-345678901"))
            .WithEmail("John.White@gmail.com")
            .WithEmail("John.White@icloud.com")
            .WithHash(_hasher);

        // act
        await Add(externalContact);
        var externalContacts = await List(deviceId);

        // assert
        externalContacts.Should().BeEquivalentTo(new[] { externalContact }, Including);
    }

    [Fact]
    public async Task ShouldUpdate()
    {
        // arrange
        var deviceId = NewDeviceId();
        var bob = await _tester.SignIn(Bob);
        var externalContact = NewExternalContact(bob, deviceId)
            .WithPhone(new Phone("1-234567890"))
            .WithPhone(new Phone("2-345678901"))
            .WithEmail("John.White@gmail.com")
            .WithEmail("John.White@icloud.com")
            .WithHash(_hasher);

        // act
        await Add(externalContact);

        externalContact = externalContact.WithoutPhone(new ("1-234567890"))
            .WithPhone(new ("1-4567890123"))
            .WithoutEmail("John.White@icloud.com")
            .WithEmail("John.White@somedomain.com");
        await Update(externalContact);

        var externalContacts = await List(deviceId);

        // assert
        externalContacts.Should().BeEquivalentTo(new[] { externalContact }, Including);
    }

    [Fact]
    public async Task ShouldRemove()
    {
        // arrange
        var deviceId = NewDeviceId();
        var bob = await _tester.SignIn(Bob);
        var externalContact1 = NewExternalContact(bob, deviceId)
            .WithPhone(new Phone("1-234567890"))
            .WithPhone(new Phone("2-345678901"))
            .WithEmail("John.White@gmail.com")
            .WithEmail("John.White@icloud.com")
            .WithHash(_hasher);
        var externalContact2 = NewExternalContact(bob, deviceId)
            .WithPhone(new Phone("3-34567890"))
            .WithPhone(new Phone("4-345678901"))
            .WithEmail("Jack.Snack@gmail.com")
            .WithEmail("jack.snack@icloud.com")
            .WithHash(_hasher);

        // act
        await Add(externalContact1, externalContact2);
        await Remove(externalContact1);
        var externalContacts = await List(deviceId);

        // assert
        externalContacts.Should().BeEquivalentTo(new[] { externalContact2 }, Including);
    }

    [Fact]
    public async Task ShouldConnectByPhone_BothAccountsExistBeforeSync()
    {
        // arrange
        var jack = await _tester.SignIn(Jack);

        var bobDeviceId = NewDeviceId();
        var bob = await _tester.SignIn(Bob);
        var externalContact = new ExternalContactFull(new ExternalContactId(new UserDeviceId(bob.Id, bobDeviceId), NewDeviceContactId()))
            .WithPhone(jack.Phone)
            .WithPhone(new ("1-11111111111"))
            .WithHash(_hasher);

        // act
        await Add(externalContact);
        var bobContacts = await ListContactIds(1);

        jack = await _tester.SignIn(Jack);
        var jackContacts = await ListContactIds(0);

        // assert
        bobContacts
            .Should()
            .BeEquivalentTo(new[] { BuildContactId(bob, jack) });
        jackContacts.Should().BeEmpty("external contacts have synced for Bob only");
    }

    [Fact]
    public async Task ShouldConnectByPhone_FriendAccountCreatedAfterSync()
    {
        // arrange
        var bobDeviceId = NewDeviceId();
        var bob = await _tester.SignIn(Bob);
        var bobContacts0 = await ListContactIds(0);
        var externalContact = new ExternalContactFull(new ExternalContactId(new UserDeviceId(bob.Id, bobDeviceId), NewDeviceContactId()))
            .WithPhone(JackPhone)
            .WithPhone(new ("1-11111111111"))
            .WithHash(_hasher);

        // act
        await Add(externalContact);
        var bobContacts = await ListContactIds(bobContacts0.Count);

        // assert
        bobContacts.Count.Should().Be(bobContacts0.Count);

        // act
        var jack = await _tester.SignIn(Jack);
        var jackContacts = await ListContactIds(0);

        bob = await _tester.SignIn(Bob);
        bobContacts = await ListContactIds(1);

        // assert
        bobContacts
            .Should()
            .BeEquivalentTo(new[] { BuildContactId(bob, jack) });
        jackContacts.Should().BeEmpty("external contacts have synced for Bob only");
    }

    [Fact]
    public async Task ShouldConnectByEmail_FriendAccountCreatedAfterSync()
    {
        // arrange
        var botDeviceId = NewDeviceId();
        var bob = await _tester.SignIn(Bob);
        var externalContact =
            new ExternalContactFull(new ExternalContactId(new UserDeviceId(bob.Id, botDeviceId), NewDeviceContactId()))
                .WithEmail(JackEmail)
                .WithHash(_hasher);

        // act
        await Add(externalContact);
        var bobContacts = await ListContactIds(0);

        // assert
        bobContacts.Should().BeEmpty();

        // act
        var jack = await _tester.SignIn(Jack);
        var jackContacts = await ListContactIds(0);

        bob = await _tester.SignIn(Bob);
        bobContacts = await ListContactIds(1);

        // assert
        bobContacts.Should().BeEquivalentTo(new[] { BuildContactId(bob, jack) });
        jackContacts.Should().BeEmpty("external contacts have synced for Bob only");
    }

    [Fact]
    public async Task ShouldConnectByEmail()
    {
        // arrange
        var jack = await _tester.SignIn(Jack);

        var bobDeviceId = NewDeviceId();
        var bob = await _tester.SignIn(Bob);
        var externalContact = NewExternalContact(bob, bobDeviceId)
            .WithEmail(JackEmail)
            .WithHash(_hasher);

        // act
        await Add(externalContact);
        var contacts = await ListContactIds(1);

        // assert
        contacts.Should().BeEquivalentTo(new[] { BuildContactId(bob, jack) });
    }

    [Fact]
    public async Task ShouldCreateSingleContact()
    {
        // arrange
        await _tester.SignIn(Jack);

        var bobDeviceId = NewDeviceId();
        var bob = await _tester.SignIn(Bob);
        var externalContact = NewExternalContact(bob, bobDeviceId)
            .WithPhone(JackPhone)
            .WithEmail(JackEmail)
            .WithHash(_hasher);

        // act
        await Add(externalContact);
        var bobContacts = await ListContactIds(1);

        var jack = await _tester.SignIn(Jack);
        var jackContacts = await ListContactIds(0);

        // assert
        bobContacts.Should().BeEquivalentTo(new[] { BuildContactId(bob, jack) });
        jackContacts.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldNotConnectWhenNoMatchingPhonesOrEmails()
    {
        // arrange
        await _tester.SignIn(Jack);

        var bobDeviceId = NewDeviceId();
        var bob = await _tester.SignIn(Bob);
        var externalContact = NewExternalContact(bob, bobDeviceId)
            .WithPhone(new ("1-1111111111"))
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
            ApiArray.New(new ExternalContactChange(externalContactFull.Id, null, Change.Update(externalContactFull)))));

    private Task<ApiArray<Result<ExternalContactFull?>>> Remove(ExternalContactFull externalContactFull)
        => _commander.Call(new ExternalContacts_BulkChange(_tester.Session,
            ApiArray.New(new ExternalContactChange(externalContactFull.Id, null, Change.Remove<ExternalContactFull>()))));

    private async Task<List<ContactId>> ListContactIds(int expectedCount)
        => await ComputedTest.When(async ct => {
            var contactIds = await ListContactIds(ct);
            contactIds.Should().HaveCountGreaterOrEqualTo(expectedCount);
            return contactIds;
        }, TimeSpan.FromSeconds(10));

    private async Task<List<ContactId>> ListContactIds(CancellationToken cancellationToken = default)
    {
        var ids = await _contacts.ListIds(_tester.Session, cancellationToken);
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
}
