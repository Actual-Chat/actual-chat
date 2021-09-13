using System.IO;
using System.Reflection;

namespace ActualChat
{
    public static class AssemblyEx
    {
        public static string GetContentUrl(this Assembly assembly, string relativePath)
            => Path.Combine($"./_content/{assembly.GetName().Name}/", relativePath);
    }
}
