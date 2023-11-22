using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.Transcription.Google;

namespace ActualChat.Transcription.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class TranscriptionServiceModule: HostModule<TranscriptSettings>
{
    public TranscriptionServiceModule(IServiceProvider moduleServices) : base(moduleServices) { }

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.IsServer())
            return; // Server-side only module

        services.AddSingleton<ITranscriber, GoogleTranscriber>();
    }
}
