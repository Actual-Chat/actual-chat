using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Contacts.IntegrationTests;

[CollectionDefinition(nameof(ContactCollection))]
public class ContactCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture("contacts", messageSink)
{
    private const AccountStatus NewAccountStatus = AccountStatus.Active;

    protected override TestAppHostOptions CreateAppHostOptions()
        => base.CreateAppHostOptions() with {
            AppConfigurationExtender = cfg => {
                cfg.AddInMemory(("UsersSettings:NewAccountStatus", NewAccountStatus.ToString()));
            },
        };
}
