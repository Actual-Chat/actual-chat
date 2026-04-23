namespace ActualChat.Streaming.UnitTests;

// TODO(nerdbank): rewrite the round-trip / fan-out tests against the new
// Nerdbank.MessagePack-based CachingAudioFrameFormatter. Original implementation
// is in git history (pre Nerdbank migration).
public class CachingAudioFrameFormatterTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact(Skip = "Pending Nerdbank.MessagePack rewrite")]
    public void RoundTrip_Pending() { }
}
