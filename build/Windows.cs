using System.Globalization;
using System.Management;


namespace Build;

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
            var query = "SELECT ProcessId,ParentProcessId,CommandLine FROM Win32_Process" + (pid != 0 ? " WHERE ParentProcessId=" + pid.ToString(CultureInfo.InvariantCulture) : "");
            var searcher = new ManagementObjectSearcher(query);
            var collection = searcher.Get().Cast<ManagementBaseObject>().Select(x => new ProcessInfo((uint)x["ProcessId"], (uint)x["ParentProcessId"], (string)x["CommandLine"]));
            return collection;
        }
        return result;
    }
}

public record ProcessInfo(uint ProcessId, uint ParentProcessId, string CommandLine);
