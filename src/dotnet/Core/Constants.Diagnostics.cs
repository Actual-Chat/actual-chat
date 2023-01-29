namespace ActualChat;

public static partial class Constants
{
    public static class Diagnostics
    {
        public static class Wasm
        {
            // ComputedMonitor track which computed instances & states are
            // most frequently updated / invalidated. Super useful.
            public static bool ComputedMonitor { get; } = false;
            // TaskEventListener is invaluable in any scenario when app hangs.
            // NOTE: It requires <TrimmerRootAssembly Include="System.Private.CoreLib" />,
            // otherwise it simply won't work!
            public static bool TaskEventListener { get; } = false;
            // TaskMonitor is not quite useful, since if everything hangs, it hangs as well
            public static bool TaskMonitor { get; } = false;
        }
    }
}
