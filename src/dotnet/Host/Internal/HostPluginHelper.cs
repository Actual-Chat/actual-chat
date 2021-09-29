using ActualChat.UI.Blazor;
using Stl.Plugins;

namespace ActualChat.Host.Internal
{
    public class HostPluginHelper
    {
        public string[] CssUrls { get; }
        public string[] ScriptUrls { get; }

        public HostPluginHelper(IServiceProvider services)
        {
            var uiModules = services.Plugins().GetPlugins<IBlazorUIModule>().ToArray();
            CssUrls = uiModules.SelectMany(
                    module => module.CssPaths.Select(
                        path => module.GetType().Assembly.GetContentUrl(path)))
                .ToArray();
            ScriptUrls ??= uiModules.SelectMany(
                    module => module.ScriptPaths.Select(
                        path => module.GetType().Assembly.GetContentUrl(path)))
                .ToArray();
        }
    }
}
