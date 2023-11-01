using System.Security.Claims;
using ActualChat.App.Server;
using ActualChat.Performance;
using ActualChat.Testing.Assertion;
using ActualChat.Testing.Host;
using ActualChat.Users;
using Microsoft.AspNetCore.Authentication.Google;

namespace ActualChat.Contacts.IntegrationTests;

public class ExternalContactsTest(ITestOutputHelper @out) : AppHostTestBase(@out)
{
    private WebClientTester _tester = null!;
    private IExternalContacts _sut = null!;
    private AppHost _appHost = null!;
    private ICommander _commander = null!;
    private IAccounts _accounts = null!;
    private IContacts _contacts = null!;

    private static string BobEmail => "bob@actual.chat";
    private static Phone BobPhone => new ("1-2345678901");
    private static User Bob { get; } = new User("", "BobAdmin")
        .WithIdentity(new UserIdentity(GoogleDefaults.AuthenticationScheme, "111"))
        .WithPhone(BobPhone)
        .WithClaim(ClaimTypes.Email, BobEmail);

    private static string JackEmail => "jack@actual.chat";
    private static Phone JackPhone => new ("1-3456789012");
    private static User Jack { get; } = new User("", "JackAdmin")
        .WithIdentity(new UserIdentity(GoogleDefaults.AuthenticationScheme, "222"))
        .WithPhone(JackPhone)
        .WithClaim(ClaimTypes.Email, JackEmail);

    public override async Task InitializeAsync()
    {
        Tracer.Default = Out.NewTracer();
        _appHost = await NewAppHost();
        _tester = _appHost.NewWebClientTester();
        _sut = _appHost.Services.GetRequiredService<IExternalContacts>();
        _accounts = _appHost.Services.GetRequiredService<IAccounts>();
        _contacts = _appHost.Services.GetRequiredService<IContacts>();
        _commander = _appHost.Services.Commander();
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
        var externalContact = new ExternalContact(new ExternalContactId(bob.Id, bobDeviceId, NewDeviceContactId()))
            .WithPhone(JackPhone)
            .WithPhone(new ("1-11111111111"));

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

    [Theory]
    [InlineData(5)]
    [InlineData(37)]
    public async Task StressTest_AllUsersExist_AllAreConnected(int count)
    {
        // arrange
        var tracer = Tracer.Default;
        using var __ = tracer.Region();
        var deviceIds = Enumerable.Repeat(0, count).Select(_ => NewDeviceId()).ToList();
        var accounts = new AccountFull[count];
        for (int i = 0; i < accounts.Length; i++) {
            using var _1 = tracer.Region($"Sign in as user #{i + 1}");
            // TODO: find the way of fast user creation to perform real stress test 🙂
            accounts[i] = await _tester.SignIn(BuildUser(i + 1));
        }

        // act
        for (var i = 0; i < accounts.Length; i++) {
            var account = accounts[i];
            using var _2 = tracer.Region("Create external contacts " + account.User.Name);
            await _tester.SignIn(account.User);
            var externalContacts = Enumerable.Range(1, count)
                .Select(idx => NewExternalContact(account, deviceIds[i], idx))
                .ToArray();
            await Add(externalContacts);
        }

        // assert
        using var _3 = tracer.Region($"Assert {count} accounts");
        foreach (var account in accounts) {
            using var _4 = tracer.Region($"Assert contacts of {account.User.Name} #({account.User.Id})");
            await _tester.SignIn(account.User);
            await AssertConnectedUsers(account, accounts);
        }
    }

    [Theory(Skip = "Flaky")]
    [InlineData(5)]
    [InlineData(37)]
    public async Task StressTest_UsersCreatedSequentially_AllAreConnected(int count)
    {
        // arrange
        var accounts = new AccountFull[count];
        var deviceIds = Enumerable.Repeat(0, count).Select(_ => NewDeviceId()).ToList();

        // act
        for (int i = 0; i < accounts.Length; i++) {
            var account = accounts[i] = await _tester.SignIn(BuildUser(i + 1));
            var externalContacts = Enumerable.Range(1, count)
                .Select(idx => NewExternalContact(account, deviceIds[i], idx))
                .ToArray();
            await Add(externalContacts);
        }

        foreach (var u in accounts.Select(x => x.User)) {
            // act
            var account = await _tester.SignIn(u);

            // assert
            await TestExt.WhenMetAsync(async () => {
                    var acc = await _accounts.GetOwn(_tester.Session, CancellationToken.None);
                    acc.IsGreetingCompleted.Should().BeTrue();
                },
                TimeSpan.FromSeconds(5));
            await AssertConnectedUsers(account, accounts);
        }
    }

    private Task<ApiArray<ExternalContact>> List(Symbol deviceId)
        => _sut.List(_tester.Session, deviceId, CancellationToken.None);

    private async Task Add(params ExternalContact[] externalContacts)
    {
        var changes = externalContacts.Select(x => new ExternalContactChange(x.Id, null, Change.Create(x)));
        var results = await _commander.Call(new ExternalContacts_BulkChange(_tester.Session, changes.ToApiArray()));
        results.Select(x => x.Value).Should().NotContainNulls();
        var errors = results.Select(x => x.Error).SkipNullItems().ToList();
        if (errors.Count > 0)
            throw new AggregateException("Failed to create external contacts", errors);
    }

    private Task Update(ExternalContact externalContact)
        => _commander.Call(new ExternalContacts_BulkChange(_tester.Session,
            ApiArray.New(new ExternalContactChange(externalContact.Id, null, Change.Update(externalContact)))));

    private Task Remove(ExternalContact externalContact)
        => _commander.Call(new ExternalContacts_BulkChange(_tester.Session,
            ApiArray.New(new ExternalContactChange(externalContact.Id, null, Change.Remove<ExternalContact>()))));

    private async Task AssertConnectedUsers(AccountFull account, AccountFull[] allAccounts)
    {
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

    private static ExternalContact NewExternalContact(AccountFull owner, Symbol deviceId, int i)
        => NewExternalContact(owner, deviceId).WithPhone(BuildPhone(i)).WithEmail(BuildEmail(i));

    private static Symbol NewDeviceId()
        => new (Guid.NewGuid().ToString());

    private static Symbol NewDeviceContactId()
        => new (Guid.NewGuid().ToString());

    private static ContactId BuildContactId(AccountFull owner, AccountFull friendAccount)
        => ContactId.Peer(owner.Id, friendAccount.Id);

    private static User BuildUser(int i)
        => new User("", BuildUserName(i))
            .WithIdentity(new UserIdentity(GoogleDefaults.AuthenticationScheme, i.ToString("00000")))
            .WithPhone(BuildPhone(i))
            .WithClaim(ClaimTypes.Email, BuildEmail(i));

    private static string BuildUserName(int i)
        => $"user{i:00000}";

    private static Phone BuildPhone(int i)
        => new ($"1-11111{i:00000}");

    private static string BuildEmail(int i)
        => $"user.{i:00000}@actual.chat";
}
