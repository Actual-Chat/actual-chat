using System;
using System.Linq;
using ActualChat.UI.Blazor;
using Stl.Plugins;

namespace ActualChat.Host.Internal
{
    public class HostPluginHelper
    {
        public string[] StyleSheetPaths { get; }
        public string[] ScriptPaths { get; }

        public HostPluginHelper(IServiceProvider services)
        {
            var uiModules = services.Plugins().GetPlugins<IBlazorUIModule>().ToArray();
            StyleSheetPaths = uiModules.SelectMany(
                    module => module.StyleSheetPaths.Select(
                        path => module.GetType().Assembly.GetContentPath(path)))
                .ToArray();
            ScriptPaths ??= uiModules.SelectMany(
                    module => module.ScriptPaths.Select(
                        path => module.GetType().Assembly.GetContentPath(path)))
                .ToArray();
        }
    }
}
