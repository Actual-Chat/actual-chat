using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Contacts.IntegrationTests;

[CollectionDefinition(nameof(ExternalContactStressCollection))]
public class ExternalContactStressCollection : ICollectionFixture<ExternalStressAppHostFixture>;

public class ExternalStressAppHostFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture(messageSink)
{
    private const AccountStatus NewAccountStatus = AccountStatus.Active;

    public override string DbInstanceName => "external-contacts-stress";

    public override async Task InitializeAsync()
        => Host = await TestAppHostFactory.NewAppHost(MessageSink, DbInstanceName, TestAppHostOptions.Default with {
            AppConfigurationExtender = cfg => {
                cfg.AddInMemory(("UsersSettings:NewAccountStatus", NewAccountStatus.ToString()));
            }});
}
