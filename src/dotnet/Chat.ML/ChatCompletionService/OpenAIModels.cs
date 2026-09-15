using OpenAI.Chat;

namespace ActualChat.Chat.ML;

#pragma warning disable OPENAI001 // TODO: remove once ChatReasoningEffortLevel is no longer [Experimental]
public static class OpenAIModels
{
    public static ChatReasoningEffortLevel? GetLowestReasoningEffort(string? modelId)
    {
        // Each reasoning family has its own floor: "none" came with gpt-5.1, gpt-5 stops at "minimal",
        // o-series at "low". Chat variants and older models answer HTTP 400 to any value, so null omits it.
        if (modelId.IsNullOrEmpty() || modelId.Contains("-chat"))
            return null;
        if (modelId.StartsWith("gpt-5."))
            return ChatReasoningEffortLevel.None;
        if (modelId == "gpt-5" || modelId.StartsWith("gpt-5-"))
            return ChatReasoningEffortLevel.Minimal;
        if (modelId is ['o', >= '0' and <= '9', ..])
            return ChatReasoningEffortLevel.Low;

        return null;
    }
}
