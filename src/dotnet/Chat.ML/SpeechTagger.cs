using System.Text.Json;
using ActualChat.AI;
using ActualLab.IO;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Services;

namespace ActualChat.Chat.ML;

#pragma warning disable OPENAI001

public sealed record SpeechTagRequest(string Text, Language? Language);

public sealed record SpeechTagResult(ApiArray<SpeechSpan> Spans, int PromptVersion);

public interface ISpeechTagger
{
    Task<SpeechTagResult?> Tag(SpeechTagRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Asks the LLM for filler, weak and profane words in a transcript and keeps only the items
/// that can be re-located in the text.
/// </summary>
public class SpeechTagger(SpeechTagger.Options settings, IServiceProvider services) : ISpeechTagger
{
    public class Options
    {
        public FilePath PromptFile { get; set; } = "";
        public int PromptVersion { get; set; } = 1;
    }

    public const string ServiceKey = nameof(SpeechTagger);
    private const int MaxSynonyms = 3;

    private static readonly JsonElement ResponseSchema = JsonDocument.Parse(
        """
        {
          "type": "object",
          "properties": {
            "items": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "class": { "type": "string", "enum": ["filledPause", "filler", "weak", "profanity"] },
                  "word": { "type": "string" },
                  "occurrence": { "type": "integer" },
                  "synonyms": { "type": "array", "items": { "type": "string" } }
                },
                "required": ["class", "word", "occurrence", "synonyms"],
                "additionalProperties": false
              }
            }
          },
          "required": ["items"],
          "additionalProperties": false
        }
        """).RootElement;

    private Options Settings { get; } = settings;
    private Kernel Kernel => field ??= services.GetRequiredService<Kernel>();
    private IChatCompletionService Completion => field ??= Kernel.GetRequiredService<IChatCompletionService>(ServiceKey);
    private IPromptHelpers PromptHelpers => field ??= services.GetRequiredService<IPromptHelpers>();
    private ILogger Log => field ??= services.LogFor(GetType());
    private string PromptTemplate => field ??= File.ReadAllText(Settings.PromptFile).Trim();

    public async Task<SpeechTagResult?> Tag(SpeechTagRequest request, CancellationToken cancellationToken)
    {
        if (request.Text.IsNullOrWhiteSpace())
            return new SpeechTagResult(ApiArray<SpeechSpan>.Empty, Settings.PromptVersion);

        try {
            var systemMessage = PromptHelpers.BuildPrompt(PromptTemplate, new Dictionary<string, string> {
                { "LANGUAGE", request.Language?.ToString() ?? "unknown" },
            });
            var history = new ChatHistory();
            history.AddSystemMessage(systemMessage);
            history.AddUserMessage(request.Text);
            var executionSettings = new OpenAIPromptExecutionSettings {
                Temperature = 0,
                ReasoningEffort = OpenAIModels.GetLowestReasoningEffort(Completion.GetModelId()),
                ResponseFormat = ChatResponseFormat.ForJsonSchema(
                    ResponseSchema, "speech_tags", "Filler, weak and profane words found in a transcript"),
            };
            var response = await Completion
                .GetChatMessageContentAsync(history, executionSettings, Kernel, cancellationToken)
                .ConfigureAwait(false);
            var spans = ParseResponse(request.Text, response.Content ?? "");
            return new SpeechTagResult(spans, Settings.PromptVersion);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogError(e, "Speech tagging failed");
            return null;
        }
    }

    public static ApiArray<SpeechSpan> ParseResponse(string text, string json)
    {
        json = json.Replace("```json", "", StringComparison.OrdinalIgnoreCase).Replace("```", "").Trim();
        using var doc = JsonDocument.Parse(json);
        var spans = new List<SpeechSpan>();
        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray()) {
            var kind = item.GetProperty("class").GetString() switch {
                "filledPause" => SpeechSpanKind.FilledPause,
                "filler" => SpeechSpanKind.Filler,
                "weak" => SpeechSpanKind.Weak,
                "profanity" => SpeechSpanKind.Profanity,
                _ => (SpeechSpanKind?)null,
            };
            if (kind is null)
                continue;

            var word = item.GetProperty("word").GetString()?.Trim();
            if (word.IsNullOrEmpty() || !item.TryGetProperty("occurrence", out var occurrence))
                continue;

            var range = SpanLocator.Locate(text, word, occurrence.GetInt32());
            if (range is null)
                continue;

            var synonyms = item.TryGetProperty("synonyms", out var s) && s.ValueKind == JsonValueKind.Array
                ? s.EnumerateArray()
                    .Select(x => x.GetString()?.Trim())
                    .Where(x => !x.IsNullOrEmpty())
                    .Take(MaxSynonyms)
                    .ToApiArray()
                : ApiArray<string>.Empty;
            spans.Add(new SpeechSpan(kind.Value, word.ToLower(), range.Value.Start, range.Value.End - range.Value.Start, synonyms!));
        }
        return spans.OrderBy(s => s.Start).ToApiArray();
    }
}

public sealed class SpeechTaggerStub : ISpeechTagger
{
    public Task<SpeechTagResult?> Tag(SpeechTagRequest request, CancellationToken cancellationToken)
        => Task.FromResult<SpeechTagResult?>(new SpeechTagResult(ApiArray<SpeechSpan>.Empty, 0));
}
