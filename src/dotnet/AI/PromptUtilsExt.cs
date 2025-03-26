namespace ActualChat.AI;

public static class PromptUtilsExt
{
    public static string BuildPrompt(this IPromptUtils promptUtils, string promptTemplate, params IEnumerable<(string Key, string Value)> variables)
        => promptUtils.BuildPrompt(promptTemplate, variables.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));
}
