using System.Security.Claims;
using ActualChat.App.Server;
using ActualChat.Contacts.UI.Blazor.Services;
using ActualChat.Security;
using ActualChat.Testing.Host;
using ActualChat.Users;
using Microsoft.AspNetCore.Authentication.Google;
using Stl.Generators;

namespace ActualChat.Contacts.UI.Blazor.IntegrationTests;

public class ContactSyncTest(ITestOutputHelper @out) : AppHostTestBase(@out)
{
    private WebClientTester _tester = null!;
    private AppHost _appHost = null!;
    private IExternalContacts _externalContacts = null!;

    private Symbol DeviceId { get; set; } = RandomStringGenerator.Default.Next();
    private List<ExternalContact> DeviceContacts { get; set; } = new ();
    private static Phone BobPhone => new ("1-2345678901");
    private static string BobEmail => "bob@actual.chat";

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
        var deviceContacts = new Mock<DeviceContacts>();
        deviceContacts.SetupGet(x => x.DeviceId).Returns(() => DeviceId);
        deviceContacts.Setup(x => x.List(It.IsAny<CancellationToken>())).Returns(() => Task.FromResult(DeviceContacts.ToApiArray()));
        _appHost = await NewAppHost( serverUrls: "http://localhost:7080");
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

    [Fact]
    public async Task ShouldAdd()
    {
        // arrange
        var bob = await _tester.SignIn(Bob);
        DeviceContacts.Add(NewExternalContact(bob).WithPhone(JackPhone).WithEmail(JackEmail));

        // act
        var sut = _tester.ClientServices.GetRequiredService<ContactSync>();
        sut.Start();

        // assert
        await ListExternalContacts(1);
    }

    private ExternalContact NewExternalContact(AccountFull account)
        => new (new ExternalContactId(account.Id, DeviceId, RandomStringGenerator.Default.Next()));

    private async Task<ApiArray<ExternalContact>> ListExternalContacts(int expectedCount)
    {
        ApiArray<ExternalContact> externalContacts;
        await TestExt.WhenMetAsync(async () => {
                externalContacts = await ListExternalContacts();
                externalContacts.Should().HaveCountGreaterOrEqualTo(expectedCount);
            },
            TimeSpan.FromSeconds(10));

        return externalContacts;
    }

    private Task<ApiArray<ExternalContact>> ListExternalContacts()
        => _externalContacts.List(_tester.Session, DeviceId, CancellationToken.None);
}
