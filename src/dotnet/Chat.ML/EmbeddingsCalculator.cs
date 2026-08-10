using System.Text;

namespace ActualChat.Chat.ML;

public interface IEmbeddingsCalculator
{
    Task<double[]> CalculateVector(string text, CancellationToken cancellationToken);
    double CosineSimilarity(double[] vector1, double[] vector2);
    double[] Normalize(double[] vector);
}

public class EmbeddingsCalculator : IEmbeddingsCalculator
{
    public const string HttpClientName = nameof(EmbeddingsCalculator);

    private readonly Uri? _predictionsUri;

    private readonly JsonSerializerOptions _jsonSerializerOptions = new (JsonSerializerOptions.Default) {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private IHttpClientFactory HttpClientFactory { get; }

    public EmbeddingsCalculator(EmbeddingSettings embeddingSettings, IHttpClientFactory httpClientFactory)
    {
        HttpClientFactory = httpClientFactory;
        if (!embeddingSettings.PredictionsUri.IsNullOrEmpty())
            _predictionsUri = new Uri(embeddingSettings.PredictionsUri, UriKind.Absolute);
    }

    public async Task<double[]> CalculateVector(string text, CancellationToken cancellationToken)
    {
        if (_predictionsUri is null)
            throw StandardError.Internal("PredictionsUri is not configured at EmbeddingSettings.");

        using var client = HttpClientFactory.CreateClient(HttpClientName);

        // TODO(AK): Tokenize and limit text to MaxTokenCount
        // TODO(AK): Use OpenAI compatible embeddings API!
        // var limitedText = text.Length > MaxTokenCount ? text[..512] : text;
        var json = JsonSerializer.Serialize(new Request(text), _jsonSerializerOptions);
        var jsonContent = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(_predictionsUri!, jsonContent, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw StandardError.External("Failed to retrieve dense vectors");

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<Response>(responseBody, _jsonSerializerOptions)?.Data;
        return result![0].Embedding;
        // var result = JsonSerializer.Deserialize<double[][]>(responseBody, _jsonSerializerOptions);
        // return Normalize(result![0]);
    }

    public double CosineSimilarity(double[] vector1, double[] vector2)
    {
        double dotProduct = vector1.Zip(vector2, (v1, v2) => v1 * v2).Sum();
        double magnitude1 = Math.Sqrt(vector1.Sum(v => v * v));
        double magnitude2 = Math.Sqrt(vector2.Sum(v => v * v));
        return dotProduct / (magnitude1 * magnitude2);
    }

    public double[] Normalize(double[] vector)
    {
        double magnitude = Math.Sqrt(vector.Sum(v => v * v));
        return vector.Select(v => v / magnitude).ToArray();
    }

    private record Request(string Input, string Model = "snowflake-arctic-embed-l-v2.0");

    private record Response(
        string Id,
        string Object,
        long Created,
        string Model,
        EmbeddingData[] Data,
        Usage Usage
    );

    private record EmbeddingData(
        int Index,
        string Object,
        double[] Embedding
    );

    private record Usage(
        int PromptTokens,
        int TotalTokens,
        int CompletionTokens,
        object? PromptTokensDetails
    );
}
