using System.Runtime.InteropServices;
using System.Text;

namespace ActualChat.App.Maui.Video;

// TEMPORARY bring-up scaffolding — remove before commit.
// Client ILogger output is swallowed by the Mac Catalyst runtime, so native-decode
// diagnostics are written straight to fd 2 (stderr), which the launcher captures.
internal static class NativeVideoTrace
{
    [DllImport("libc", SetLastError = true)]
    private static extern nint write(int fd, byte[] buffer, nint count);

    public static void Log(string message)
    {
        var bytes = Encoding.UTF8.GetBytes("[NVD] " + message + "\n");
        write(2, bytes, bytes.Length);
    }
}
