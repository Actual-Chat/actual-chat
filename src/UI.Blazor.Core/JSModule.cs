using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace ActualChat.UI.Blazor
{
    public class JSModule
    {
        private readonly ConcurrentDictionary<string, IJSObjectReference> _cache = new();
        private readonly IJSRuntime _jsRuntime;

        public JSModule(IJSRuntime jsRuntime)
            => _jsRuntime = jsRuntime;

        public async ValueTask<IJSObjectReference> Import(string modulePath)
        {
            // You should never dispose modules returned by this method
            if (_cache.TryGetValue(modulePath, out var module))
                return module;
            module = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", modulePath);
            _cache.TryAdd(modulePath, module);
            return module;
        }

        public ValueTask<IJSObjectReference> Import(Assembly assembly, string relativeModulePath)
        {
            var modulePath = assembly.GetContentPath(relativeModulePath);
            return Import(modulePath);
        }
    }
}
