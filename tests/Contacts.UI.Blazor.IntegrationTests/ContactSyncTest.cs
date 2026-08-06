using System.Security.Claims;
using ActualChat.Security;
using ActualChat.Testing.Host;
using ActualChat.Testing.Host.Assertion;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Generators;
using Microsoft.AspNetCore.Authentication.Google;

namespace ActualChat.Contacts.UI.Blazor.IntegrationTests;

[Collection(nameof(ContactUICollection))]
public class ContactSyncTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester _tester = null!;
    private IExternalContacts _externalContacts = null!;

    private Symbol DeviceId { get; set; } = RandomStringGenerator.Default.Next();
    private List<ExternalContactFull> DeviceContacts { get; set; } = new ();
    private static Phone BobPhone { get; } = Phone.Parse("1-2345678901");
    private static string BobEmail => $"bob{Constants.Team.EmailSuffix}";

    private static AccountFull Bob { get; } = new AccountFull("BobAdmin")
        .WithIdentity(new UserIdentity(GoogleDefaults.AuthenticationScheme, "111"))
        .WithPhoneIdentity(BobPhone)
        .WithPhone(BobPhone)
        .WithClaim(ClaimTypes.Email, BobEmail);

    private static Phone JackPhone => Phone.Parse("1-3456789012");
    private static string JackEmail => $"jack{Constants.Team.EmailSuffix}";

    private static Phone JanePhone => Phone.Parse("1-3456789012");
    private static string JaneEmail => $"jane{Constants.Team.EmailSuffix}";

    protected override async Task InitializeAsync()
    {
        var deviceContacts = new Mock<DeviceContacts>(MockBehavior.Loose);
        deviceContacts.SetupGet(x => x.DeviceId).Returns(() => DeviceId);
        deviceContacts.Setup(x => x.List(It.IsAny<CancellationToken>())).Returns(() => Task.FromResult(DeviceContacts.ToArray()));
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

        await _tester.AppHost.SignIn(_tester.Session, new AccountFull("BobAdmin"));
        await _tester.SignOut();
    }

    protected override async Task DisposeAsync()
    {
        await _tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact(Skip = "TODO(FC): Fix for CI")]
    public async Task ShouldAddAndUpdate()
    {
        // arrange
        var bob = await _tester.SignInClientSide(Bob);
        DeviceContacts.Add(NewExternalContact(bob).WithPhone(JackPhone).WithEmail(JackEmail));

        // act
        var hub = new UIHub(_tester.ClientServices);
        var sut = new ContactSync(hub);
        sut.Start();

        // assert
        var externalContacts = await ListExternalContacts(1);
        externalContacts.Should().BeEquivalentTo(DeviceContacts, options => options.ExcludingSystemProperties());
        await hub.DisposeAsync();

        // arrange
        DeviceContacts[0] = DeviceContacts[0].WithoutPhone(JackPhone).WithPhone(Phone.Parse("1-1002003000"));
        DeviceContacts.Add(NewExternalContact(bob).WithPhone(JanePhone).WithEmail(JaneEmail));

        // act
        hub = new UIHub(_tester.ClientServices);
        sut = new ContactSync(hub);
        sut.Start();

        // assert
        externalContacts = await ListExternalContacts(2);
        externalContacts.Should().BeEquivalentTo(DeviceContacts, options => options.ExcludingSystemProperties());
        await hub.DisposeAsync();
    }

    private ExternalContactFull NewExternalContact(AccountFull owner)
        => new (ExternalContactId.New(UserDeviceId.New(owner.Id, DeviceId), RandomStringGenerator.Default.Next()));

    private async Task<ExternalContact[]> ListExternalContacts(int expectedCount)
        => await ComputedTest.When(async ct => {
            var externalContacts = await ListExternalContacts(ct);
            externalContacts.Should().HaveCountGreaterThanOrEqualTo(expectedCount);
            return externalContacts;
        }, TimeSpan.FromSeconds(10));

    private Task<ExternalContact[]> ListExternalContacts(CancellationToken cancellationToken = default)
        => _externalContacts.List(_tester.Session, DeviceId, cancellationToken);
}
