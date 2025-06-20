using System.Text.RegularExpressions;

namespace ActualChat.Chat;

public class LanguageDetector(IServiceProvider services) : ChatCompletionBasedService(services, Constants.LanguageDetection.ServiceKey)
{
    private static readonly Regex NonWordRe = new (@"^[^\p{L}]+$");
    [field: AllowNull, MaybeNull]
    private string Prompt => field ??= File.ReadAllText(Settings.LanguageDetection.PromptFile).RequireNonEmpty();

    public async Task<IReadOnlyList<Language>> DetectLanguages(string content, CancellationToken cancellationToken)
    {
        if (!Settings.IsTranslationEnabled)
            return [];

        if (content.IsNullOrWhiteSpace() || NonWordRe.IsMatch(content))
            return [];

        var response = await Ask(Prompt, content, cancellationToken).ConfigureAwait(false);
        try {
            var json = response
                .OrdinalIgnoreCaseReplace("```json", "")
                .OrdinalReplace("`", "");
            return SystemJsonSerializer.Default.Read<string[]>(json)
                .Select(Language.ParseNullable)
                .SkipNullItems()
                .ToList();
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogError(e, "Could not parse language detection response: {Response}", response);
            return [];
        }
    }
}
