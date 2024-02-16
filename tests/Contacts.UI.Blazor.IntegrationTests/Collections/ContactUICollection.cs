namespace ActualChat.Contacts.UI.Blazor.IntegrationTests;

[CollectionDefinition(nameof(ContactUICollection))]
public class ContactUICollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink)
    : ActualChat.Testing.Host.AppHostFixture("contacts-ui", messageSink);
