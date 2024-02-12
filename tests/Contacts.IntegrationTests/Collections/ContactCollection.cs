using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Contacts.IntegrationTests;

[CollectionDefinition(nameof(ContactCollection))]
public class ContactCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture(messageSink)
{
    private const AccountStatus NewAccountStatus = AccountStatus.Active;

    public override string DbInstanceName => "contacts";

    public override async Task InitializeAsync()
        => Host = await TestAppHostFactory.NewAppHost(MessageSink, DbInstanceName, TestAppHostOptions.Default with {
            AppConfigurationExtender = cfg => {
                cfg.AddInMemory(("UsersSettings:NewAccountStatus", NewAccountStatus.ToString()));
            }});
}
