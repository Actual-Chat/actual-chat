using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.Transcription.Google;
using Stl.Plugins;

namespace ActualChat.Transcription.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class TranscriptionModule: HostModule
{
    public TranscriptionModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public TranscriptionModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
            return; // Server-side only module

        services.AddSingleton<ITranscriber, GoogleTranscriber>();
    }
}
