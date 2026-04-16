using ActualChat.Media.Flows;
using ActualChat.Testing.Flows;

namespace ActualChat.Media.UnitTests.Flows;

public class LinkPreviewFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<LinkPreviewFlow>(@out)
{
    protected override LinkPreviewFlow CreatePopulated() => new() {
        // From ThrottledFlow
        SuccessCount = 10,
        FailCount = 2,
        NextRunAt = new Moment(new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc)),
    };
}

public class PreviewThumbnailUpdateFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<PreviewThumbnailUpdateFlow>(@out)
{
    protected override PreviewThumbnailUpdateFlow CreatePopulated() => new() {
        // From ThrottledFlow
        SuccessCount = 10,
        FailCount = 2,
        NextRunAt = new Moment(new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc)),
    };
}

public class UploadProcessingFlowSerializationTest(ITestOutputHelper @out)
    : FlowSerializationTestBase<UploadProcessingFlow>(@out)
{
    // UploadProcessingFlow has no own [DataMember] properties — verifies the
    // round-trip works for flows with only base-class state (none here, since
    // Flow<TResult> has [IgnoreMember] on all its properties).
    protected override UploadProcessingFlow CreatePopulated() => new();
}
