namespace ActualChat.Streaming.UnitTests;

// TODO(nerdbank): rewrite the round-trip / fan-out tests against the new
// Nerdbank.MessagePack-based CachingVideoFrameFormatter. Mirror of
// CachingAudioFrameFormatterTest. Original implementation is in git history.
public class CachingVideoFrameByteSerializerTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact(Skip = "Pending Nerdbank.MessagePack rewrite")]
    public void RoundTrip_Pending() { }
}
