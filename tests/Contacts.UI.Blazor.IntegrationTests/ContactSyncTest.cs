using System.Security.Claims;
using ActualChat.App.Server;
using ActualChat.Contacts.UI.Blazor.Services;
using ActualChat.Security;
using ActualChat.Testing.Assertion;
using ActualChat.Testing.Host;
using ActualChat.Users;
using Microsoft.AspNetCore.Authentication.Google;
using ActualLab.Generators;

namespace ActualChat.Contacts.UI.Blazor.IntegrationTests;

public class ContactSyncTest(ITestOutputHelper @out) : AppHostTestBase(@out)
{
    private WebClientTester _tester = null!;
    private AppHost _appHost = null!;
    private IExternalContacts _externalContacts = null!;

    private Symbol DeviceId { get; set; } = RandomStringGenerator.Default.Next();
    private List<ExternalContact> DeviceContacts { get; set; } = new ();
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

    public override async Task InitializeAsync()
    {
        var deviceContacts = new Mock<DeviceContacts>();
        deviceContacts.SetupGet(x => x.DeviceId).Returns(() => DeviceId);
        deviceContacts.Setup(x => x.List(It.IsAny<CancellationToken>())).Returns(() => Task.FromResult(DeviceContacts.ToApiArray()));
        _appHost = await NewAppHost(TestAppHostOptions.Default with { ServerUrls = "http://localhost:7080" });
        _externalContacts = _appHost.Services.GetRequiredService<IExternalContacts>();
        _tester = _appHost.NewWebClientTester(services => {
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

    public override async Task DisposeAsync()
    {
        await _tester.DisposeSilentlyAsync();
        _appHost.DisposeSilently();
    }

    [Fact(Skip = "TODO(FC): Fix for CI")]
    public async Task ShouldAddAndUpdate()
    {
        // arrange
        var bob = await _tester.SignIn(Bob);
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

    private ExternalContact NewExternalContact(AccountFull owner)
        => new (new ExternalContactId(owner.Id, DeviceId, RandomStringGenerator.Default.Next()));

    private async Task<ApiArray<ExternalContact>> ListExternalContacts(int expectedCount)
    {
        await TestExt.WhenMetAsync(async () => {
                var externalContacts = await ListExternalContacts();
                externalContacts.Should().HaveCountGreaterOrEqualTo(expectedCount);
            },
            TimeSpan.FromSeconds(10));

        return await ListExternalContacts();
    }

    private Task<ApiArray<ExternalContact>> ListExternalContacts()
        => _externalContacts.List(_tester.Session, DeviceId, CancellationToken.None);
}
