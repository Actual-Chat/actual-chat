using Microsoft.SemanticKernel.ChatCompletion;

namespace ActualChat.Chat.ML;

public static class TokenEstimator
{
    public static int Estimate(ChatHistory chatHistory)
    {
        // Estimate roughly
        var tokens = 0;
        foreach (var chatMessage in chatHistory)
            tokens += CountWords(chatMessage.Content ?? "");
        return (int)Math.Ceiling(tokens * 2.4);
    }

    // Private methods

    private static int CountWords(string text)
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
