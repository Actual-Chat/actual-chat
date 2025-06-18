namespace ActualChat.AI;

public static class PromptHelpersExt
{
    public static string BuildPrompt(
        this IPromptHelpers promptHelpers,
        string promptTemplate,
        params (string Key, string Value)[] variables)
        => promptHelpers.BuildPrompt(promptTemplate, variables.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));
}
