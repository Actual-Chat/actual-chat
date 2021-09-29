using ActualChat.Host;

namespace ActualChat.Testing.Host
{
    public static class TestPlaywrightExt
    {
        public static PlaywrightTester NewPlaywrightTester(this AppHost appHost)
            => new(appHost);
    }
}
