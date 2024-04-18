using System.Globalization;
using System.Security.Claims;
using ActualChat.Performance;
using ActualChat.Testing.Assertion;
using ActualChat.Testing.Host;
using ActualChat.Users;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.Toolkit.HighPerformance;

namespace ActualChat.Contacts.IntegrationTests;

[Collection(nameof(ExternalContactStressCollection))]
[Trait("Category", "Slow")]
public class ExternalContactStressTest(ExternalStressAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ExternalStressAppHostFixture>(fixture, @out)
{
    private WebClientTester _tester = null!;
    private ICommander _commander = null!;
    private IAccounts _accounts = null!;
    private IContacts _contacts = null!;

    protected override Task InitializeAsync()
    {
        Tracer.Default = Out.NewTracer(nameof(ExternalContactStressTest));
        _tester = AppHost.NewWebClientTester(Out);
        var services = AppHost.Services;
        _accounts = services.GetRequiredService<IAccounts>();
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

    [Theory(Skip = "flaky, must be fixed")]
    [InlineData(5)]
    [InlineData(37)]
    public async Task StressTest_AllUsersExist_AllAreConnected(int count)
    {
        // arrange
        var prefix = UniqueNames.Prefix();
        var tracer = Tracer.Default;
        using var __ = tracer.Region();
        var deviceIds = Enumerable.Repeat(0, count).Select(_ => NewDeviceId()).ToList();
        var accounts = new AccountFull[count];
        for (int i = 0; i < accounts.Length; i++) {
            using var _1 = tracer.Region($"Sign in as user #{i + 1} {prefix}");
            // TODO: find the way of fast user creation to perform real stress test 🙂
            accounts[i] = await _tester.SignIn(BuildUser(prefix, i + 1));
        }

        // act
        for (var i = 0; i < accounts.Length; i++) {
            var account = accounts[i];
            using var _2 = tracer.Region("Create external contacts " + account.User.Name);
            await _tester.SignIn(account.User);
            var externalContacts = Enumerable.Range(1, count)
                .Select(idx => NewExternalContact(account, deviceIds[i], prefix, idx))
                .ToArray();
            await Add(externalContacts);
        }

        // assert
        using var _3 = tracer.Region($"Assert {count} accounts");
        var userMap = accounts.ToDictionary(x => x.Id, x => x.User);
        foreach (var account in accounts) {
            using var _4 = tracer.Region($"Assert contacts of {account.User.Name} #({account.User.Id})");
            await _tester.SignIn(account.User);
            await AssertConnectedUsers(account, userMap);
        }
    }

    [Theory(Skip = "flaky, must be fixed")]
    [InlineData(5)]
    [InlineData(37)]
    public async Task StressTest_UsersCreatedSequentially_AllAreConnected(int count)
    {
        // arrange
        var prefix = UniqueNames.Prefix();
        var accounts = new AccountFull[count];
        var deviceIds = Enumerable.Repeat(0, count).Select(_ => NewDeviceId()).ToList();

        // act
        for (int i = 0; i < accounts.Length; i++) {
            var account = accounts[i] = await _tester.SignIn(BuildUser(prefix, i + 1));
            var externalContacts = Enumerable.Range(1, count)
                .Select(idx => NewExternalContact(account, deviceIds[i], prefix, idx))
                .ToArray();
            await Add(externalContacts);
        }

        var userMap = accounts.ToDictionary(x => x.Id, x => x.User);
        foreach (var u in accounts.Select(x => x.User)) {
            // act
            var account = await _tester.SignIn(u);

            // assert
            await TestExt.When(async () => {
                    var acc = await _accounts.GetOwn(_tester.Session, CancellationToken.None);
                    acc.IsGreetingCompleted.Should().BeTrue();
                },
                TimeSpan.FromSeconds(5));
            await AssertConnectedUsers(account, userMap);
        }
    }

    private async Task Add(params ExternalContactFull[] externalContacts)
    {
        var changes = externalContacts.Select(x => new ExternalContactChange(x.Id, null, Change.Create(x)));
        var results = await _commander.Call(new ExternalContacts_BulkChange(_tester.Session, changes.ToApiArray()));
        results.Select(x => x.Value).Should().NotContainNulls();
        var errors = results.Select(x => x.Error).SkipNullItems().ToList();
        if (errors.Count > 0)
            throw new AggregateException("Failed to create external contacts", errors);
    }

    private async Task AssertConnectedUsers(AccountFull account, Dictionary<UserId, User> userMap)
    {
        var contactIds = await ListContactIds(account, userMap.Count - 1);
        var connectedUsers = contactIds.ConvertAll(GetUser).OrderBy(x => x.Name);
        var otherUsers = userMap.Values.Where(x => x.Id != account.Id).OrderBy(x => x.Name);
        connectedUsers.Should().BeEquivalentTo(otherUsers);
        return;

        User GetUser(ContactId x)
            => userMap[x.ChatId.IsPeerChat(out var peerChatId)
                ? peerChatId.UserIds.OtherThan(account.Id)
                : throw new Exception("Peer chat contact was expected")];
    }

    private async Task<List<ContactId>> ListContactIds(AccountFull account, int expectedCount)
    {
        await TestExt.When(async () => {
                var peerContactIds = await ListContactIds();
                peerContactIds.Should().HaveCountGreaterOrEqualTo(expectedCount, $"for {account.User.Name}");
            },
            TimeSpan.FromSeconds(10));

        return await ListContactIds();
    }

    private async Task<List<ContactId>> ListContactIds()
    {
        var ids = await _contacts.ListIds(_tester.Session, PlaceId.None, CancellationToken.None);
        return ids.Where(x => x.ChatId.Kind == ChatKind.Peer && !Constants.Chat.SystemChatIds.Contains(x.ChatId)).ToList();
    }


    private static ExternalContactFull NewExternalContact(AccountFull owner, Symbol ownerDeviceId)
        => new (new ExternalContactId(new UserDeviceId(owner.Id, ownerDeviceId), NewDeviceContactId()));

    private static ExternalContactFull NewExternalContact(AccountFull owner, Symbol deviceId, string prefix, int i)
        => NewExternalContact(owner, deviceId).WithPhone(BuildPhone(prefix, i)).WithEmail(BuildEmail(prefix, i));

    private static Symbol NewDeviceId()
        => new (Guid.NewGuid().ToString());

    private static Symbol NewDeviceContactId()
        => new (Guid.NewGuid().ToString());

    private static User BuildUser(string prefix, int i)
        => new User("", BuildUserName(prefix, i))
            .WithIdentity(new UserIdentity(GoogleDefaults.AuthenticationScheme,  $"{prefix}-{i.ToString("00000", CultureInfo.InvariantCulture)}"))
            .WithPhone(BuildPhone(prefix, i))
            .WithClaim(ClaimTypes.Email, BuildEmail(prefix, i));

    private static string BuildUserName(string prefix, int i)
        => $"user-{prefix}-{i:00000}";

    private static Phone BuildPhone(string prefix, int i)
        => new ($"1-{Math.Abs(prefix.GetDjb2HashCode() % 100000):00000}{i:00000}");

    private static string BuildEmail(string prefix, int i)
        => $"{prefix}.user.{i:00000}@actual.chat";
}
