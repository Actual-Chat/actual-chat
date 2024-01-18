using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.Transcription.DeepGram;
using ActualChat.Transcription.Google;

namespace ActualChat.Transcription.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class TranscriptionServiceModule(IServiceProvider moduleServices)
    : HostModule<TranscriptSettings>(moduleServices)
{
    protected override TranscriptSettings ReadSettings()
        => Cfg.GetSettings<TranscriptSettings>(nameof(TranscriptSettings));

    protected override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        if (!HostInfo.HostKind.IsServer())
            return; // Server-side only module

        services.AddSingleton<ITranscriberFactory, TranscriberFactory>();
        services.AddSingleton<GoogleTranscriber>();
        services.AddSingleton<DeepGramTranscriber>();
    }
}
