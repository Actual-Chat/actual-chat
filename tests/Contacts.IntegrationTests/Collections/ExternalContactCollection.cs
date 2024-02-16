namespace ActualChat.Contacts.IntegrationTests;

[CollectionDefinition(nameof(ExternalContactCollection))]
public class ExternalContactCollection : ICollectionFixture<ExternalAppHostFixture>;

public class ExternalAppHostFixture(IMessageSink messageSink)
    : ActualChat.Testing.Host.AppHostFixture("ext-contacts", messageSink);
