namespace ActualChat.Contacts.IntegrationTests;

[CollectionDefinition(nameof(ExternalContactStressCollection))]
public class ExternalContactStressCollection : ICollectionFixture<ExternalStressAppHostFixture>;

public class ExternalStressAppHostFixture(IMessageSink messageSink)
    : ActualChat.Testing.Host.AppHostFixture("ext-contacts-stress", messageSink);
