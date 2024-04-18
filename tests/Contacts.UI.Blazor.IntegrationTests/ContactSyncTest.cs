using System.Security.Claims;
using ActualChat.Contacts.UI.Blazor.Services;
using ActualChat.Security;
using ActualChat.Testing.Assertion;
using ActualChat.Testing.Host;
using ActualChat.Users;
using Microsoft.AspNetCore.Authentication.Google;
using ActualLab.Generators;

namespace ActualChat.Contacts.UI.Blazor.IntegrationTests;

[Collection(nameof(ContactUICollection))]
public class ContactSyncTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester _tester = null!;
    private IExternalContacts _externalContacts = null!;

    private Symbol DeviceId { get; set; } = RandomStringGenerator.Default.Next();
    private List<ExternalContactFull> DeviceContacts { get; set; } = new ();
    private static Phone BobPhone { get; } = new ("1-2345678901");
    private static string BobEmail => "bob@actual.chat";

    private static User Bob { get; } = new User("", "BobAdmin")
        .WithIdentity(new UserIdentity(GoogleDefaults.AuthenticationScheme, "111"))
        .WithPhone(BobPhone)
        .WithClaim(ClaimTypes.Email, BobEmail);

    private static Phone JackPhone => new ("1-3456789012");
    private static string JackEmail => "jack@actual.chat";

    private static Phone JanePhone => new ("1-3456789012");
    private static string JaneEmail => "jane@actual.chat";

    protected override async Task InitializeAsync()
    {
        var deviceContacts = new Mock<DeviceContacts>();
        deviceContacts.SetupGet(x => x.DeviceId).Returns(() => DeviceId);
        deviceContacts.Setup(x => x.List(It.IsAny<CancellationToken>())).Returns(() => Task.FromResult(DeviceContacts.ToApiArray()));
        _externalContacts = AppHost.Services.GetRequiredService<IExternalContacts>();
        _tester = AppHost.NewWebClientTester( Out,services => {
            TrueSessionResolver? sessionResolver = null;
            services.AddSingleton(c => {
                    sessionResolver ??= new TrueSessionResolver(c);
                    sessionResolver.Replace(_tester.Session);
                    return sessionResolver;
                })
                .AddAlias<ISessionResolver, TrueSessionResolver>(ServiceLifetime.Scoped);

            services.AddSingleton(deviceContacts.Object);
        });

        await _tester.AppHost.SignIn(_tester.Session, new User("BobAdmin"));
        await _tester.SignOut();
    }

    protected override Task DisposeAsync()
        => _tester.DisposeSilentlyAsync().AsTask();

    [Fact(Skip = "TODO(FC): Fix for CI")]
    public async Task ShouldAddAndUpdate()
    {
        // arrange
        var bob = await _tester.SignInClientSide(Bob);
        DeviceContacts.Add(NewExternalContact(bob).WithPhone(JackPhone).WithEmail(JackEmail));

        // act
        var scope = new UIHub(_tester.ClientServices);
        var sut = new ContactSync(scope);
        sut.Start();

        // assert
        var externalContacts = await ListExternalContacts(1);
        externalContacts.Should().BeEquivalentTo(DeviceContacts, options => options.ExcludingSystemProperties());
        await scope.DisposeAsync();

        // arrange
        DeviceContacts[0] = DeviceContacts[0].WithoutPhone(JackPhone).WithPhone(new Phone("1-1002003000"));
        DeviceContacts.Add(NewExternalContact(bob).WithPhone(JanePhone).WithEmail(JaneEmail));

        // act
        scope = new UIHub(_tester.ClientServices);
        sut = new ContactSync(scope);
        sut.Start();

        // assert
        externalContacts = await ListExternalContacts(2);
        externalContacts.Should().BeEquivalentTo(DeviceContacts, options => options.ExcludingSystemProperties());
        await scope.DisposeAsync();
    }

    private ExternalContactFull NewExternalContact(AccountFull owner)
        => new (new ExternalContactId(new UserDeviceId(owner.Id, DeviceId), RandomStringGenerator.Default.Next()));

    private async Task<ApiArray<ExternalContact>> ListExternalContacts(int expectedCount)
        => await ComputedTest.When(async ct => {
            var externalContacts = await ListExternalContacts(ct);
            externalContacts.Should().HaveCountGreaterOrEqualTo(expectedCount);
            return externalContacts;
        }, TimeSpan.FromSeconds(10));

    private Task<ApiArray<ExternalContact>> ListExternalContacts(CancellationToken cancellationToken = default)
        => _externalContacts.List2(_tester.Session, DeviceId, cancellationToken);
}
