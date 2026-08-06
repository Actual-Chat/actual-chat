using ActualChat.Module;

namespace ActualChat.Transcription.Module;

public sealed class TranscriptionServiceModule(IServiceProvider moduleServices)
    : HostModule<TranscriptionSettings>(moduleServices)
{
    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.HasRole(HostRole.OneBackendServer))
            return;

        services.AddSingleton<ITranscriberRegistry>(c => new TranscriberRegistry(
            c.GetRequiredService<TranscriptionSettings>(),
            c.GetServices<ITranscriber>(),
            c.GetServices<IOfflineTranscriber>()));
        services.AddSingleton<ITranscriberSelector>(c => new TranscriberSelector(
            c.GetRequiredService<ITranscriberRegistry>(),
            c));

        if (Settings.UseFakeTranscriber) {
            // Registered alone so the ranking can't route around it in tests.
            services.AddSingleton<ITranscriber, FakeTranscriber>();
            return;
        }

        var coreSettings = Cfg.Settings<CoreServerSettings>(nameof(CoreSettings));
        services.AddSingleton<ITranscriber, GoogleTranscriber>();
        services.AddSingleton<ITranscriber, DeepgramTranscriber>();
        if (!coreSettings.SonioxKey.IsNullOrEmpty()) {
            services.AddSingleton<ITranscriber, SonioxTranscriber>();
            services.AddSingleton<IOfflineTranscriber, SonioxOfflineTranscriber>();
        }
        if (!coreSettings.ElevenLabsKey.IsNullOrEmpty()) {
            services.AddSingleton<ITranscriber, ElevenLabsTranscriber>();
            services.AddSingleton<IOfflineTranscriber, ElevenLabsOfflineTranscriber>();
        }
        if (Constants.Transcription.IsRetranscriptionEnabled && !coreSettings.OpenAIKey.IsNullOrEmpty())
            // Not TryAddEnumerable: it rejects factory descriptors, which have no
            // implementation type to tell them apart, and throws at startup.
            services.AddSingleton<IOfflineTranscriber>(c => new OpenAITranscriber(
                new OpenAITranscriber.Options { ApiKey = coreSettings.OpenAIKey },
                c));

        // Deepgram's SDK logs to its own sink; silence it.
        Deepgram.Logger.Log.Initialize(Deepgram.Logger.LogLevel.Disable);
    }
}
