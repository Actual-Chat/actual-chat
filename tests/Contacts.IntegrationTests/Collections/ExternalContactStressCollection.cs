using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Contacts.IntegrationTests;

[CollectionDefinition(nameof(ExternalContactStressCollection))]
public class ExternalContactStressCollection : ICollectionFixture<ExternalStressAppHostFixture>;

public class ExternalStressAppHostFixture(IMessageSink messageSink)
    : ActualChat.Testing.Host.AppHostFixture("ext-contacts-stress", messageSink)
{
    private const AccountStatus NewAccountStatus = AccountStatus.Active;

    protected override TestAppHostOptions CreateAppHostOptions()
        => base.CreateAppHostOptions() with {
            AppConfigurationExtender = cfg => {
                cfg.AddInMemory(("UsersSettings:NewAccountStatus", NewAccountStatus.ToString()));
            },
        };
}
