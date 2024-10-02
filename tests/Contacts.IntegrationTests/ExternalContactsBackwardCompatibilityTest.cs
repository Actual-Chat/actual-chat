using System.Security.Claims;
using ActualChat.Testing.Host;
using ActualChat.Users;
using ActualLab.Rpc;
using FluentAssertions.Equivalency;
using Microsoft.AspNetCore.Authentication.Google;

namespace ActualChat.Contacts.IntegrationTests;

[Collection(nameof(ExternalContactCollection))]
public class ExternalContactsBackwardCompatibilityTest(ExternalAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ExternalAppHostFixture>(fixture, @out)
{
    private WebClientTester _tester = null!;
    private IExternalContacts _externalContacts = null!;
    private ICommander _commander = null!;

    private static string BobEmail => "bob@actual.chat";
    private static Phone BobPhone => new ("1-2345678901");
    private static User Bob { get; } = new User("", $"Bob-{nameof(ExternalContactsTest)}")
        .WithIdentity(new UserIdentity(GoogleDefaults.AuthenticationScheme, "111"))
        .WithPhone(BobPhone)
        .WithClaim(ClaimTypes.Email, BobEmail);

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _tester = AppHost.NewWebClientTester(Out);
        var services = AppHost.Services;
        _externalContacts = services.GetRequiredService<IExternalContacts>();
        services.GetRequiredService<IContacts>();
        _commander = services.Commander();
    }

    protected override async Task DisposeAsync()
    {
        await _tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public void ShouldExposeCorrectRpcLegacyMethods()
    {
        var serviceRegistry = AppHost.Services.RpcHub().ServiceRegistry;
        var mExternalContactList = RpcMethodDef.ComposeFullName(nameof(IExternalContacts), "List");

        var r = serviceRegistry.GetServerMethodResolver(new VersionSet($"Api={Constants.Api.StringVersion}"));
        Out.WriteLine(r.ToString());
        r.MethodByFullName.Should().BeNull();

        var testedVersions = new[] { "", "Api=1.5", "Api=1.10", "Api=1.10.0.0" };
        foreach (var version in testedVersions) {
            r = serviceRegistry.GetServerMethodResolver(new VersionSet(version));
            Out.WriteLine(r.ToString());
            r.MethodByFullName.SingleOrDefault(x => x.Key == mExternalContactList).Should().NotBeNull();
        }

        r = serviceRegistry.GetServerMethodResolver(new VersionSet("Api=1.11"));
        Out.WriteLine(r.ToString());
        (r.MethodByFullName?.SingleOrDefault(x => x.Key == mExternalContactList) ?? null).Should().BeNull();
    }

    [Fact]
    public async Task ShouldAddWithoutHash()
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
        externalContacts.Should().BeEquivalentTo([externalContact], Including);
    }

    [Fact]
    public async Task ShouldUpdateWithoutHash()
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
        externalContacts.Should().BeEquivalentTo([externalContact], Including);
    }

    [Fact]
    public async Task ShouldRemoveWithoutHash()
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
        externalContacts.Should().BeEquivalentTo(new[] { externalContact2 }, Including);
    }

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

    private static ExternalContactFull NewExternalContact(AccountFull owner, Symbol ownerDeviceId)
        => new (new ExternalContactId(new UserDeviceId(owner.Id, ownerDeviceId), NewDeviceContactId()));

    private static Symbol NewDeviceId()
        => new (Guid.NewGuid().ToString());

    private static Symbol NewDeviceContactId()
        => new (Guid.NewGuid().ToString());

    private static EquivalencyAssertionOptions<ExternalContactFull> Including(EquivalencyAssertionOptions<ExternalContactFull> o)
        => o.Including(x => x.Id);
}
