using System;
using System.Collections.Generic;
using Stl.Collections;
using Stl.DependencyInjection;
using Stl.Plugins;

namespace ActualChat.Hosting
{
    [Plugin]
    public abstract class Plugin : IHasCapabilities, IHasDependencies
    {
        private readonly ImmutableOptionSet? _capabilities = null;

        protected IPluginHost Plugins { get; } = null!;

        public string Name => Capabilities.Get<string>();
        public Version Version => Capabilities.Get<Version>();

        public ImmutableOptionSet Capabilities => _capabilities ?? ComputeCapabilities();
        public virtual IEnumerable<Type> Dependencies => Array.Empty<Type>();

        protected Plugin(IPluginInfoProvider.Query _) { }
        protected Plugin(IPluginHost plugins) => Plugins = plugins;

        protected virtual ImmutableOptionSet ComputeCapabilities()
        {
            var type = GetType();
            var version = type.Assembly.GetName(false).Version ?? new Version("0.1");
            return ImmutableOptionSet.Empty
                .Set(type.Name)
                .Set(version);
        }
    }
}
