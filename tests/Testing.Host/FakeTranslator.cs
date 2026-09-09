using ActualChat.Chat;
using ActualChat.Transcription;

namespace ActualChat.Testing.Host;

/// <summary>
/// Deterministic translator. By default it answers in the target's own script and keeps the
/// source, so a stored translation reveals which text it was given; a test can swap the answer
/// on its own host's instance.
/// </summary>
public sealed class FakeTranslator(IServiceProvider services, string serviceKey = Constants.Translation.ServiceKey)
    : Translator(services, serviceKey)
{
    // Static: the retranscribe flow has to fail the keyed realtime instance it never resolves
    public static bool MustFailRealtime { get; set; }
    public Func<string, Language, string> Respond { get; set; } = Translated;
    private bool IsRealtime { get; } = serviceKey == Constants.Translation.RealtimeServiceKey;

    public static string Translated(string text, Language targetLanguage)
        => $"{targetLanguage.NativeName}: {text}";

    public static void Reset()
        => MustFailRealtime = false;

    public override Task<string> Translate(
        string textToTranslate,
        Language targetLanguage,
        TranslationResult[] context,
        string? contextHint = null,
        CancellationToken cancellationToken = default)
        => MustFailRealtime && IsRealtime
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
