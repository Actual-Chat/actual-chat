using System.Security.Claims;
using ActualChat.App.Server;
using ActualChat.Performance;
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
        _appHost = await NewAppHost();
        _tester = _appHost.NewWebClientTester();
        _sut = _appHost.Services.GetRequiredService<IExternalContacts>();
        _contacts = _appHost.Services.GetRequiredService<IContacts>();
        _commander = _appHost.Services.Commander();
    }

    public override async Task DisposeAsync()
    {
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
        var externalContact = new ExternalContact(new ExternalContactId(bob.Id, deviceId, NewDeviceContactId())) {
            DisplayName = "John White",
            GivenName = "John",
            FamilyName = "White",
            Phones = new[] { new Phone("1-234567890"), new Phone("2-345678901") }.ToApiSet(),
            Emails = new[] { "John.White@gmail.com", "John.White@icloud.com" }.ToApiSet(),
        };

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
        var externalContact = new ExternalContact(new ExternalContactId(bob.Id, deviceId, NewDeviceContactId())) {
            DisplayName = "John White",
            GivenName = "John",
            FamilyName = "White",
            Phones = new[] { new Phone("1-234567890"), new Phone("2-345678901") }.ToApiSet(),
            Emails = new[] { "John.White@gmail.com", "John.White@icloud.com" }.ToApiSet(),
        };

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
        var externalContact1 = new ExternalContact(new ExternalContactId(bob.Id, deviceId, NewDeviceContactId())) {
            DisplayName = "John White",
            GivenName = "John",
            FamilyName = "White",
            Phones = new[] { new Phone("1-234567890"), new Phone("2-345678901") }.ToApiSet(),
            Emails = new[] { "John.White@gmail.com", "John.White@icloud.com" }.ToApiSet(),
        };
        var externalContact2 = new ExternalContact(new ExternalContactId(bob.Id, deviceId, NewDeviceContactId())) {
            DisplayName = "Jack Snack",
            GivenName = "Jack",
            FamilyName = "Snack",
            Phones = new[] { new Phone("1-234567890"), new Phone("2-345678901") }.ToApiSet(),
            Emails = new[] { "Jack.Snack@gmail.com", "jack.snack@icloud.com" }.ToApiSet(),
        };

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
        var externalContact = new ExternalContact(new ExternalContactId(bob.Id, bobDeviceId, NewDeviceContactId())) {
            DisplayName = "Jack",
            GivenName = "Jack",
            Phones = new[] { jack.Phone, new Phone("1-11111111111") }.ToApiSet(),
        };

        // act
        await Add(externalContact);
        var bobContacts = await ListContacts();

        jack = await _tester.SignIn(Jack);
        var jackContacts = await ListContacts();

        // assert
        bobContacts.Select(x => x.Id)
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
        var externalContact = new ExternalContact(new ExternalContactId(bob.Id, bobDeviceId, NewDeviceContactId())) {
            DisplayName = "Jack",
            GivenName = "Jack",
            Phones = new[] { JackPhone, new Phone("1-11111111111") }.ToApiSet(),
        };

        // act
        await Add(externalContact);
        var bobContacts = await ListContacts();

        // assert
        bobContacts.Should().BeEmpty();

        // act
        var jack = await _tester.SignIn(Jack);
        var jackContacts = await ListContacts();

        bob = await _tester.SignIn(Bob);
        bobContacts = await ListContacts();

        // assert
        bobContacts.Select(x => x.Id)
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
        var externalContact = new ExternalContact(new ExternalContactId(bob.Id, botDeviceId, NewDeviceContactId())) {
            DisplayName = "Jack",
            GivenName = "Jack",
            Emails = new[] { JackEmail }.ToApiSet(),
        };

        // act
        await Add(externalContact);
        var bobContacts = await ListContacts();

        // assert
        bobContacts.Should().BeEmpty();

        // act
        var jack = await _tester.SignIn(Jack);
        var jackContacts = await ListContacts();

        bob = await _tester.SignIn(Bob);
        bobContacts = await ListContacts();

        // assert
        bobContacts.Select(x => x.Id)
            .Should()
            .BeEquivalentTo(new[] { BuildContactId(bob, jack) });
        jackContacts.Should().BeEmpty("external contacts have synced for Bob only");
    }

    [Fact]
    public async Task ShouldConnectByEmail()
    {
        // arrange
        var jack = await _tester.SignIn(Jack);

        var bobDeviceId = NewDeviceId();
        var bob = await _tester.SignIn(Bob);
        var externalContact = new ExternalContact(new ExternalContactId(bob.Id, bobDeviceId, NewDeviceContactId())) {
            DisplayName = "Jack",
            Emails = new () { JackEmail },
        };

        // act
        await Add(externalContact);
        var contacts = await ListContacts();

        // assert
        contacts.Select(x => x.Id)
            .Should()
            .BeEquivalentTo(new[] { BuildContactId(bob, jack) });
    }

    [Fact]
    public async Task ShouldCreateSingleContact()
    {
        // arrange
        await _tester.SignIn(Jack);

        var bobDeviceId = NewDeviceId();
        var bob = await _tester.SignIn(Bob);
        var externalContact = new ExternalContact(new ExternalContactId(bob.Id, bobDeviceId, NewDeviceContactId())) {
            DisplayName = "Jack",
            GivenName = "Jack",
            Phones = new[] { JackPhone }.ToApiSet(),
            Emails = new[] { JackEmail }.ToApiSet(),
        };

        // act
        await Add(externalContact);
        var bobContacts = await ListContacts();

        var jack = await _tester.SignIn(Jack);
        var jackContacts = await ListContacts();

        // assert
        bobContacts.Select(x => x.Id)
            .Should()
            .BeEquivalentTo(new[] { BuildContactId(bob, jack) });
        jackContacts.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldNotConnectWhenNoMatchingPhonesOrEmails()
    {
        // arrange
        await _tester.SignIn(Jack);

        var bobDeviceId = NewDeviceId();
        var bob = await _tester.SignIn(Bob);
        var externalContact = new ExternalContact(new ExternalContactId(bob.Id, bobDeviceId, NewDeviceContactId())) {
            DisplayName = "Jack",
            Phones = new () { new ("1-1111111111") },
            Emails = new () { "jack.2@some.com" },
        };

        // act
        await Add(externalContact);
        var contacts = await ListContacts();

        // assert
        contacts.Should().BeEmpty("no matching phones or emails");
    }

    [Theory]
    [InlineData(5)]
    [InlineData(37)]
    public async Task StressTest_AllUsersExist_AllAreConnected(int count)
    {
        // arrange
        var tracer = Tracer.Default = Out.NewTracer();
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
            using var _2 = tracer.Region("SignIn as " + account.User.Name);
            await _tester.SignIn(account.User);
            var externalContacts = Enumerable.Range(1, count)
                .Select(idx => NewExternalContact(account, deviceIds[i], idx))
                .ToArray();
            await Add(externalContacts);
        }

        // assert
        using var _3 = tracer.Region($"Assert {count} accounts");
        foreach (var account in accounts) {
            using var _4 = tracer.Region($"Assert contacts of '{account.FullName}'");
            await _tester.SignIn(account.User);
            var contacts = await ListContacts();
            var expectedContactIds =
                accounts.Where(x => x.Id != account.Id)
                    .Select(x => BuildContactId(account, x))
                    .ToList();
            contacts.Select(x => x.Id).Should().BeEquivalentTo(expectedContactIds);
        }
    }

    [Theory]
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

        // assert
        foreach (var u in accounts.Select(x => x.User)) {
            var account = await _tester.SignIn(u);
            account.IsGreetingCompleted.Should().BeTrue("every user account mast complete greeting on first sign in");
            var contacts = await ListContacts();
            contacts.Should().HaveCount(count - 1, "all are connected between each other and only self reference is excluded");
            var expectedContactIds =
                accounts.Where(x => x.Id != account.Id)
                    .Select(x => BuildContactId(account, x))
                    .ToList();
            contacts.Select(x => x.Id).Should().BeEquivalentTo(expectedContactIds);
        }
    }

    private ExternalContact NewExternalContact(AccountFull owner, Symbol deviceId, int i)
        => new (new ExternalContactId(owner.Id, deviceId, NewDeviceContactId())) {
            DisplayName = BuildUserName(i),
            Phones = new (new[] { BuildPhone(i) }),
            Emails = new (new[] { BuildEmail(i) }),
        };

    private Task<ApiArray<ExternalContact>> List(Symbol deviceId)
        => _sut.List(_tester.Session, deviceId, CancellationToken.None);

    private Task<ExternalContact[]> Add(params ExternalContact[] externalContacts)
        => externalContacts.Select(x
                => _commander.Call(new ExternalContacts_Change(_tester.Session, x.Id, null, Change.Create(x))))
            .Collect()!;

    private Task Update(ExternalContact externalContact)
        => _commander.Call(new ExternalContacts_Change(_tester.Session, externalContact.Id, null, Change.Update(externalContact)));

    private Task Remove(ExternalContact externalContact)
        => _commander.Call(new ExternalContacts_Change(_tester.Session, externalContact.Id, null, Change.Remove<ExternalContact>()));

    private async Task<List<Contact>> ListContacts()
    {
        var contactIds = await _contacts.ListIds(_tester.Session, CancellationToken.None);
        var contacts = await contactIds.Where(x => x.ChatId.Kind == ChatKind.Peer).Select(id => _contacts.Get(_tester.Session, id, CancellationToken.None)).Collect();
        return contacts.SkipNullItems().ToList();
    }

    private static Symbol NewDeviceId()
        => new (Guid.NewGuid().ToString());

    private Symbol NewDeviceContactId()
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
