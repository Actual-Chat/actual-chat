using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Stl.IO;
using Stl.Locking;

namespace ActualChat.UI.Blazor
{
    public static class JSRuntimeEx
    {
        private static readonly AsyncLockSet<string> ImportLocks = new(ReentryMode.CheckedFail);
        private static readonly ConcurrentDictionary<string, IJSObjectReference> CachedModules = new();

        public static async ValueTask<IJSObjectReference> Import(this IJSRuntime jsRuntime, string modulePath)
        {
            // You should never dispose modules returned by this method
            if (CachedModules.TryGetValue(modulePath, out var module))
                return module;
            using var _ = await ImportLocks.Lock(modulePath);
            module = await jsRuntime.InvokeAsync<IJSObjectReference>("import", modulePath);
            CachedModules.TryAdd(modulePath, module);
            return module;
        }

        public static ValueTask<IJSObjectReference> Import(
            this IJSRuntime jsRuntime,
            Assembly assembly, string relativeModulePath)
        {
            var libraryName = assembly.GetName().Name;
            var modulePath = Path.Combine($"./_content/{libraryName}/", relativeModulePath);
            return jsRuntime.Import(modulePath);
        }
    }
}
