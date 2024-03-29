using System.Globalization;
using System.Security.Claims;
using ActualChat.Performance;
using ActualChat.Queues;
using ActualChat.Testing.Assertion;
using ActualChat.Testing.Host;
using ActualChat.Users;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.Toolkit.HighPerformance;

namespace ActualChat.Contacts.IntegrationTests;

[Collection(nameof(ExternalContactCollection))]
public class ExternalContactsTest(ExternalAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ExternalAppHostFixture>(fixture, @out)
{
    private WebClientTester _tester = null!;
    private IExternalContacts _externalContacts = null!;
    private IQueues _queues = null!;
    private ICommander _commander = null!;
    private IAccounts _accounts = null!;
    private IContacts _contacts = null!;

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
        _accounts = services.GetRequiredService<IAccounts>();
        _contacts = services.GetRequiredService<IContacts>();
        _commander = services.Commander();
        _queues = services.Queues();

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
            .WithEmail("John.White@icloud.com");

        // act
        await Add(externalContact);
        var externalContacts = await List(deviceId);

        // assert
        externalContacts.Should()
            .BeEquivalentTo(new[] { externalContact }, o => o.ExcludingSystemProperties());
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
            .WithEmail("John.White@icloud.com");

        // act
        await Add(externalContact);

        externalContact = externalContact.WithoutPhone(new ("1-234567890"))
            .WithPhone(new ("1-4567890123"))
            .WithoutEmail("John.White@icloud.com")
            .WithEmail("John.White@somedomain.com");
        await Update(externalContact);

        var externalContacts = await List(deviceId);

        // assert
        externalContacts.Should()
            .BeEquivalentTo(new[] { externalContact }, o => o.ExcludingSystemProperties());
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
            .WithEmail("John.White@icloud.com");
        var externalContact2 = NewExternalContact(bob, deviceId)
            .WithPhone(new Phone("3-34567890"))
            .WithPhone(new Phone("4-345678901"))
            .WithEmail("Jack.Snack@gmail.com")
            .WithEmail("jack.snack@icloud.com");

        // act
        await Add(externalContact1, externalContact2);
        await Remove(externalContact1);
        var externalContacts = await List(deviceId);

        // assert
        externalContacts.Should()
            .BeEquivalentTo(new[] { externalContact2 }, o => o.ExcludingSystemProperties());
    }

    [Fact]
    public async Task ShouldConnectByPhone_BothAccountsExistBeforeSync()
    {
        // arrange
        var jack = await _tester.SignIn(Jack);

        var bobDeviceId = NewDeviceId();
        var bob = await _tester.SignIn(Bob);
        var externalContact = new ExternalContact(new ExternalContactId(bob.Id, bobDeviceId, NewDeviceContactId()))
            .WithPhone(jack.Phone)
            .WithPhone(new ("1-11111111111"));

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
        var externalContact = new ExternalContact(new ExternalContactId(bob.Id, bobDeviceId, NewDeviceContactId()))
            .WithPhone(JackPhone)
            .WithPhone(new ("1-11111111111"));

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
        var externalContact = new ExternalContact(new ExternalContactId(bob.Id, botDeviceId, NewDeviceContactId()))
            .WithEmail(JackEmail);

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
        var externalContact = NewExternalContact(bob, bobDeviceId).WithEmail(JackEmail);

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
        var externalContact = NewExternalContact(bob, bobDeviceId).WithPhone(JackPhone).WithEmail(JackEmail);

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
            .WithEmail("jack.2@some.com");

        // act
        await Add(externalContact);
        var contacts = await ListContactIds(0);
        contacts.Should().BeEmpty("no matching phones or emails");
    }

    private Task<ApiArray<ExternalContact>> List(Symbol deviceId)
        => _externalContacts.List(_tester.Session, deviceId, CancellationToken.None);

    private async Task Add(params ExternalContact[] externalContacts)
    {
        var changes = externalContacts.Select(x => new ExternalContactChange(x.Id, null, Change.Create(x)));
        var results = await _commander.Call(new ExternalContacts_BulkChange(_tester.Session, changes.ToApiArray()));
        results.Select(x => x.Value).Should().NotContainNulls();
        var errors = results.Select(x => x.Error).SkipNullItems().ToList();
        if (errors.Count > 0)
            throw new AggregateException("Failed to create external contacts", errors);
    }

    private Task<ApiArray<Result<ExternalContact?>>> Update(ExternalContact externalContact)
        => _commander.Call(new ExternalContacts_BulkChange(_tester.Session,
            ApiArray.New(new ExternalContactChange(externalContact.Id, null, Change.Update(externalContact)))));

    private Task<ApiArray<Result<ExternalContact?>>> Remove(ExternalContact externalContact)
        => _commander.Call(new ExternalContacts_BulkChange(_tester.Session,
            ApiArray.New(new ExternalContactChange(externalContact.Id, null, Change.Remove<ExternalContact>()))));

    private async Task AssertConnectedUsers(AccountFull account, AccountFull[] allAccounts)
    {
        await _queues.WhenProcessing();
        var userMap = allAccounts.ToDictionary(x => x.Id, x => x.User);
        var contactIds = await ListContactIds(allAccounts.Length - 1);
        var connectedUsers = contactIds.ConvertAll(GetUser).OrderBy(x => x.Name);
        var otherUsers = allAccounts.Where(x => x.Id != account.Id).Select(x => x.User).OrderBy(x => x.Name);
        connectedUsers.Should().BeEquivalentTo(otherUsers);
        return;

        User GetUser(ContactId x)
            => userMap[x.ChatId.IsPeerChat(out var peerChatId)
                ? peerChatId.UserIds.OtherThan(account.Id)
                : throw new Exception("Peer chat contact was expected")];
    }

    private async Task<List<ContactId>> ListContactIds(int expectedCount)
    {
        await TestExt.WhenMetAsync(async () => {
                var peerContactIds = await ListContactIds();
                peerContactIds.Should().HaveCountGreaterOrEqualTo(expectedCount);
            },
            TimeSpan.FromSeconds(10));

        return await ListContactIds();
    }

    private async Task<List<ContactId>> ListContactIds()
    {
        var ids = await _contacts.ListIds(_tester.Session, CancellationToken.None);
        return ids.Where(x => x.ChatId.Kind == ChatKind.Peer && !Constants.Chat.SystemChatIds.Contains(x.ChatId)).ToList();
    }


    private static ExternalContact NewExternalContact(AccountFull owner, Symbol ownerDeviceId)
        => new (new ExternalContactId(owner.Id, ownerDeviceId, NewDeviceContactId()));

    private static ExternalContact NewExternalContact(AccountFull owner, Symbol deviceId, string prefix, int i)
        => NewExternalContact(owner, deviceId).WithPhone(BuildPhone(prefix, i)).WithEmail(BuildEmail(prefix, i));

    private static Symbol NewDeviceId()
        => new (Guid.NewGuid().ToString());

    private static Symbol NewDeviceContactId()
        => new (Guid.NewGuid().ToString());

    private static ContactId BuildContactId(AccountFull owner, AccountFull friendAccount)
        => ContactId.Peer(owner.Id, friendAccount.Id);

    private static User BuildUser(string prefix, int i)
        => new User("", BuildUserName(i))
            .WithIdentity(new UserIdentity(GoogleDefaults.AuthenticationScheme,  $"{prefix}-{i.ToString("00000", CultureInfo.InvariantCulture)}"))
            .WithPhone(BuildPhone(prefix, i))
            .WithClaim(ClaimTypes.Email, BuildEmail(prefix, i));

    private static string BuildUserName(int i)
        => $"user{i:00000}";

    private static Phone BuildPhone(string prefix, int i)
        => new ($"1-{Math.Abs(prefix.GetDjb2HashCode() % 100000):00000}{i:00000}");

    private static string BuildEmail(string prefix, int i)
        => $"{prefix}.user.{i:00000}@actual.chat";
}
