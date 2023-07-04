using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.Transcription.Google;

namespace ActualChat.Transcription.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class TranscriptionServiceModule: HostModule
{
    public TranscriptionServiceModule(IServiceProvider services) : base(services) { }

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.IsServer())
            return; // Server-side only module

        services.AddSingleton<ITranscriber, GoogleTranscriber>();
    }
}
