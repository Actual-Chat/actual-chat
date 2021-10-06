using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Build;



internal static class WinApi
{
    public const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, SW_HIDE = 0x0;
    public const nuint VK_A = 0x41, VK_RETURN = 0x0D;

    [DllImport("User32", ExactSpelling = true, EntryPoint = "PostMessageW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [SupportedOSPlatform("windows5.0")]
    public static extern int PostMessage(IntPtr hWnd, uint Msg, nuint wParam, nuint lParam);

    [DllImport("User32", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [SupportedOSPlatform("windows5.0")]
    internal static extern int ShowWindow(IntPtr hWnd, uint nCmdShow);
}

internal static class Windows
{
    public static List<ProcessInfo> GetChildProcesses(uint pid)
    {
        var root = GetChildProcessesInternal(pid);
        var result = new List<ProcessInfo>(32);
        var stack = new Stack<ProcessInfo>(root);
        while (stack.TryPop(out var current)) {
            foreach (var child in GetChildProcessesInternal(current.ProcessId)) {
                stack.Push(child);
            }
            result.Add(current);
        }

        static IEnumerable<ProcessInfo> GetChildProcessesInternal(uint pid)
        {
            var query = "SELECT ProcessId,ParentProcessId,CommandLine FROM Win32_Process" + (pid != 0 ? " WHERE ParentProcessId=" + pid.ToString() : "");
            var searcher = new ManagementObjectSearcher(query);
            var collection = searcher.Get().Cast<ManagementBaseObject>().Select(x => new ProcessInfo((uint)x["ProcessId"], (uint)x["ParentProcessId"], (string)x["CommandLine"]));
            return collection;
        }
        return result;
    }
}

public record ProcessInfo(uint ProcessId, uint ParentProcessId, string CommandLine);
