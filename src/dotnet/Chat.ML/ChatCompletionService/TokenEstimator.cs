using Microsoft.SemanticKernel.ChatCompletion;

namespace ActualChat.Chat.ML;

public class TokenEstimator
{
    public Task<int> Estimate(ChatHistory chatHistory, CancellationToken cancellationToken)
    {
        // Estimate roughly
        var tokens = 0;
        foreach (var chatMessage in chatHistory)
            tokens += CountWords(chatMessage.Content ?? "");
        return Task.FromResult((int)Math.Ceiling(tokens * 2.4));
    }

    private int CountWords(string text)
    {
        int wordCount = 0, index = 0;

        // skip whitespace until first word
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;

        while (index < text.Length)
        {
            // check if current char is part of a word
            while (index < text.Length && !char.IsWhiteSpace(text[index]))
                index++;

            wordCount++;

            // skip whitespace until next word
            while (index < text.Length && char.IsWhiteSpace(text[index]))
                index++;
        }
        return wordCount;
    }
}
