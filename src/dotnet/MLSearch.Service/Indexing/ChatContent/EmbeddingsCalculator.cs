using System.Text;
using ActualChat.MLSearch.Module;

namespace ActualChat.MLSearch.Indexing.ChatContent;

public interface IEmbeddingsCalculator
{
    Task<double[]> CalculateVector(string text);
    double CosineSimilarity(double[] vector1, double[] vector2);
    double[] Normalize(double[] vector);
}

public class EmbeddingsCalculator : IEmbeddingsCalculator
{
    private readonly Uri? _predictionsUri;

    private readonly JsonSerializerOptions _jsonSerializerOptions = new (JsonSerializerOptions.Default)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public EmbeddingsCalculator(EmbeddingsCalculatorSettings embeddingsSettings)
    {
        if (!embeddingsSettings.PredictionsUri.IsNullOrEmpty())
            _predictionsUri = new Uri(embeddingsSettings.PredictionsUri, UriKind.Absolute);
    }

    public async Task<double[]> CalculateVector(string text)
    {
        if (_predictionsUri is null)
            throw StandardError.Internal("Predications uri is not configured.");

        using var client = new HttpClient();

        var json = JsonSerializer.Serialize(new Request(text), _jsonSerializerOptions);
        var jsonContent = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(_predictionsUri!, jsonContent);
        if (!response.IsSuccessStatusCode)
            throw StandardError.External("Failed to retrieve dense vectors");

        var responseBody = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<double[][]>(responseBody, _jsonSerializerOptions);
        return result![0];
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

    private record Request(string Input);
}
