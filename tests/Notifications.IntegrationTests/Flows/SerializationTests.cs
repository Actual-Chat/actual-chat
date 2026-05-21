using ActualChat.Notifications.Flows;
using ActualChat.Testing.Flows;

namespace ActualChat.Notifications.IntegrationTests.Flows;

public class NotificationFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<NotificationFlow>(@out)
{
    // NotificationFlow has no own [DataMember] properties.
    protected override NotificationFlow CreatePopulated() => new();
}
