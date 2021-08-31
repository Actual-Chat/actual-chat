using ActualChat.Host;

namespace ActualChat.Testing
{
    public static class TestPlaywrightEx
    {
        public static PlaywrightTester NewPlaywrightTester(this AppHost appHost)
            => new(appHost);
    }
}
