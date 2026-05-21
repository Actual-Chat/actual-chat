namespace ActualChat.Notifications.IntegrationTests;

[CollectionDefinition(nameof(NotificationCollection))]
public class NotificationCollection : ICollectionFixture<AppHostFixture>;

public class AppHostFixture(IMessageSink messageSink) : ActualChat.Testing.Host.AppHostFixture("notification", messageSink);
