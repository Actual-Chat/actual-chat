namespace ActualChat.Contacts.IntegrationTests;

[CollectionDefinition(nameof(ContactCollection))]
public class ContactCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink)
    : ActualChat.Testing.Host.AppHostFixture("contacts", messageSink);
