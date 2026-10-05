using System.Collections.Immutable;
using ActualChat.Db;
using ActualChat.OAuth;
using ActualChat.OAuth.Module;
using OpenIddict.Abstractions;

namespace ActualChat.Users.UnitTests;

public class OAuthGrantsBackendTest
{
    [Fact]
    public void ConsentLockShouldMatchThePreSplitApiProxyLock()
    {
        // arrange
        var userId = UserId.New();
        var applicationId = "application";
        var oldKey = DbLockKey.New("@ActualChat_OAuth_ActualLabProxies_OAuthGrantsProxy", userId, applicationId);

        // act
        var key = OAuthGrantsBackend.GetConsentLockKey(userId, applicationId);

        // assert
        key.Should().Be(oldKey);
        key.CombinedKey.Should().Be(oldKey.CombinedKey);
        key.Should().NotBe(DbLockKey.New(typeof(OAuthGrants), userId, applicationId));
    }

    [Fact]
    public async Task ClientLookupShouldNotShareCachedMissesOrMetadataAcrossSessions()
    {
        // arrange
        var application = new object();
        var applications = new Mock<IOpenIddictApplicationManager>(MockBehavior.Strict);
        applications.SetupSequence(x => x.FindByClientIdAsync("client", default))
            .ReturnsAsync((object?)null)
            .ReturnsAsync(application)
            .ReturnsAsync(application);
        applications.Setup(x => x.GetRedirectUrisAsync(application, default))
            .ReturnsAsync(ImmutableArray<string>.Empty);
        applications.Setup(x => x.GetPermissionsAsync(application, default))
            .ReturnsAsync(ImmutableArray<string>.Empty);
        applications.SetupSequence(x => x.GetDisplayNameAsync(application, default))
            .ReturnsAsync("Registered client")
            .ReturnsAsync("Updated client");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(applications.Object);
        services.AddSingleton(new OAuthSettings());
        services.AddSingleton(new Mock<IAccounts>(MockBehavior.Strict).Object);
        services.AddSingleton(new Mock<IAccountsBackend>(MockBehavior.Strict).Object);
        services.AddSingleton(new Mock<ISessionsBackend>(MockBehavior.Strict).Object);
        services.AddFusion()
            .AddComputeService<IOAuthGrantsBackend, OAuthGrantsBackend>()
            .AddComputeService<IOAuthGrants, OAuthGrants>();
        using var provider = services.BuildServiceProvider();
        var grants = provider.GetRequiredService<IOAuthGrants>();

        // act
        var missing = await Computed.Capture(() => grants.GetClient(Session.New(), "client", default));
        var registered = await Computed.Capture(() => grants.GetClient(Session.New(), "client", default));
        var updated = await grants.GetClient(Session.New(), "client", default);

        // assert
        missing.Value.Should().BeNull();
        registered.Value.Should().NotBeNull();
        registered.Value!.ClientName.Should().Be("Registered client");
        updated.Should().NotBeNull();
        updated!.ClientName.Should().Be("Updated client");
        applications.Verify(x => x.FindByClientIdAsync("client", default), Times.Exactly(3));
    }
}
