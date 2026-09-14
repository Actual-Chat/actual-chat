using ActualChat.Chat;
using ActualChat.Transcription;

namespace ActualChat.Testing.Host;

/// <summary>
/// Deterministic translator. By default it answers in the target's own script and keeps the
/// source, so a stored translation reveals which text it was given; a test can swap the answer
/// or make it fail on its own host's instance.
/// </summary>
public sealed class FakeTranslator(IServiceProvider services, string serviceKey = Constants.Translation.ServiceKey)
    : Translator(services, serviceKey)
{
    // Per instance rather than static: test hosts share the process, and a static flag set by one
    // test collection failed the translations of another
    public bool MustFail { get; set; }
    public Func<string, Language, string> Respond { get; set; } = Translated;

    public static string Translated(string text, Language targetLanguage)
        => $"{targetLanguage.NativeName}: {text}";

    public static FakeTranslator Realtime(IServiceProvider services)
        => (FakeTranslator)services.GetRequiredKeyedService<Translator>(Constants.Translation.RealtimeServiceKey);

    public override Task<string> Translate(
        string textToTranslate,
        Language targetLanguage,
        TranslationResult[] context,
        string? contextHint = null,
        CancellationToken cancellationToken = default)
        => MustFail
            ? Task.FromException<string>(StandardError.External("Realtime translation failed."))
            : Task.FromResult(Respond(textToTranslate, targetLanguage));

    public override async IAsyncEnumerable<StringDiff> Stream(
        string textToTranslate,
        Language targetLanguage,
        TranslationResult[] context,
        string? contextHint = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return StringDiff.New(Respond(textToTranslate, targetLanguage), "");
    }
}
