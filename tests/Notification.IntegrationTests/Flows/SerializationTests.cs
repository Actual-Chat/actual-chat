using ActualChat.Notification.Flows;
using ActualChat.Testing.Flows;

namespace ActualChat.Notification.IntegrationTests.Flows;

public class NotificationFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<NotificationFlow>(@out)
{
    // NotificationFlow has no own [DataMember] properties.
    protected override NotificationFlow CreatePopulated() => new();
}
