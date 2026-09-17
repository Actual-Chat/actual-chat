using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using ActualChat.Diagnostics;
using ActualChat.Module;
using ActualLab.Diagnostics;

namespace ActualChat.AI;

// Cloudflare Workers AI. The FLUX.2 models take multipart/form-data even for a text-only
// prompt - only flux-1-schnell accepts JSON, and it has no size parameter at all.

internal sealed class CloudflareImageGenerator(IServiceProvider services) : IImageGenerator
{
    public const string HttpClientName = "cloudflare-ai";

    // Undocumented, and absent from Cloudflare's published error table: the NSFW prompt filter
    private const int ContentFilterErrorCode = 3030;

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
    };

    private IServiceProvider Services { get; } = services;
    private CoreServerSettings Settings { get; } = services.GetRequiredService<CoreServerSettings>();
    private IHttpClientFactory HttpClientFactory { get; } = services.HttpClientFactory();
    private ILogger Log => field ??= Services.LogFor(GetType());

    public bool IsAvailable
        => !Settings.CloudflareAccountId.IsNullOrEmpty() && !Settings.CloudflareAIToken.IsNullOrEmpty();

    public async Task<GeneratedImage?> Generate(
        ImageGenerationRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAvailable)
            throw StandardError.Constraint("Cloudflare image generation is not configured.");

        using var activity = CoreServerInstruments.ActivitySource.StartActivity(GetType());
        using var content = BuildContent(request);
        using var httpClient = HttpClientFactory.CreateClient(HttpClientName);
        var url = $"https://api.cloudflare.com/client/v4/accounts/{Settings.CloudflareAccountId}"
            + $"/ai/run/{Settings.ImageGeneratorModel}";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Settings.CloudflareAIToken);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = Deserialize(json, response.StatusCode);
        if (result.Success != true) {
            var error = result.Errors.FirstOrDefault();
            if (error?.Code == ContentFilterErrorCode) {
                Log.LogDebug("Prompt rejected by the content filter: {Message}", error.Message);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return null;
            }

            throw StandardError.External(
                $"Image generation failed: {error?.Code} {error?.Message ?? response.StatusCode.ToString()}");
        }

        var data = Convert.FromBase64String(result.Result?.Image ?? "");
        if (data.Length == 0)
            throw StandardError.External("Image generation returned an empty image.");

        activity?.SetStatus(ActivityStatusCode.Ok);
        return new GeneratedImage(GetContentType(data), request.Width, request.Height, data);
    }

    // Private methods

    private static MultipartFormDataContent BuildContent(ImageGenerationRequest request)
    {
        var content = new MultipartFormDataContent {
            { new StringContent(request.Prompt), "prompt" },
            { new StringContent(request.Width.ToString()), "width" },
            { new StringContent(request.Height.ToString()), "height" },
        };
        if (request.Seed is { } seed)
            content.Add(new StringContent(seed.ToString()), "seed");

        return content;
    }

    private static CloudflareResponse Deserialize(string json, HttpStatusCode statusCode)
    {
        // Cloudflare answers 429 with the same envelope, so the body is parsed before the status is trusted
        try {
            return JsonSerializer.Deserialize<CloudflareResponse>(json, JsonOptions)
                ?? throw StandardError.External($"Image generation returned an empty response ({statusCode}).");
        }
        catch (JsonException e) {
            throw StandardError.External($"Image generation returned an unreadable response ({statusCode}): {e.Message}");
        }
    }

    private static string GetContentType(byte[] data)
        => data.Length >= 4 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
            ? "image/png"
            : "image/jpeg";

    // Nested types

    private sealed record CloudflareResponse
    {
        [JsonPropertyName("success")] public bool? Success { get; init; }
        [JsonPropertyName("result")] public CloudflareResult? Result { get; init; }
        [JsonPropertyName("errors")] public CloudflareError[] Errors { get; init; } = [];
    }

    private sealed record CloudflareResult
    {
        [JsonPropertyName("image")] public string? Image { get; init; }
    }

    private sealed record CloudflareError
    {
        [JsonPropertyName("code")] public int Code { get; init; }
        [JsonPropertyName("message")] public string? Message { get; init; }
    }
}
