using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Contacts.IntegrationTests;

[CollectionDefinition(nameof(ExternalContactCollection))]
public class ExternalContactCollection : ICollectionFixture<ExternalAppHostFixture>;

public class ExternalAppHostFixture(IMessageSink messageSink)
    : ActualChat.Testing.Host.AppHostFixture("ext-contacts", messageSink)
{
    private const AccountStatus NewAccountStatus = AccountStatus.Active;

    protected override TestAppHostOptions CreateHostOptions()
        => base.CreateHostOptions() with {
            AppConfigurationExtender = cfg => {
                cfg.AddInMemory(("UsersSettings:NewAccountStatus", NewAccountStatus.ToString()));
            },
        };
}
