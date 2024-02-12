using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Contacts.IntegrationTests;

[CollectionDefinition(nameof(ExternalContactCollection))]
public class ExternalContactCollection : ICollectionFixture<ExternalAppHostFixture>;

public class ExternalAppHostFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture(messageSink)
{
    private const AccountStatus NewAccountStatus = AccountStatus.Active;

    public override string DbInstanceName => "external-contacts";

    public override async Task InitializeAsync()
        => Host = await TestAppHostFactory.NewAppHost(MessageSink, DbInstanceName, TestAppHostOptions.Default with {
            AppConfigurationExtender = cfg => {
                cfg.AddInMemory(("UsersSettings:NewAccountStatus", NewAccountStatus.ToString()));
            }});
}
