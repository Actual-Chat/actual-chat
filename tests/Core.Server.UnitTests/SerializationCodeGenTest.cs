using ActualChat.Flows;
using ActualChat.Flows.Infrastructure;
using FlowData = ActualChat.Flows.Infrastructure.FlowData;

namespace ActualChat.Core.Server.UnitTests;

public class SerializationCodeGenTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void BasicTest()
    {
        SerializationCodeGen.ValidateType<Change<string>>();
        SerializationCodeGen.ValidateType<FlowReadiness>();
        SerializationCodeGen.ValidateType<FlowId>();
        SerializationCodeGen.ValidateType<FlowData>();
        SerializationCodeGen.ValidateType<FlowResumeEvent>();
        SerializationCodeGen.ValidateType<IndexingFlowCursor<ChatId>>();
    }
}
