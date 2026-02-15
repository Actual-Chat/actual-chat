namespace ActualChat.Asr;

/// <summary>Downloads Parakeet model files from HuggingFace with local caching.</summary>
public sealed class ParakeetModelDownloader(string? cacheDir = null, ILogger? log = null)
{
    private const string HuggingFaceBaseUrl = "https://huggingface.co/istupakov/parakeet-tdt-0.6b-v3-onnx/resolve/main/";

    private static readonly string DefaultCacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".cache", "actualchat", "parakeet-tdt-0.6b-v3");

    private readonly string _cacheDir = cacheDir ?? DefaultCacheDir;
    private readonly ILogger _log = log ?? NullLogger.Instance;

    public record ModelFiles(
        string PreprocessorPath,
        string EncoderPath,
        string DecoderJointPath,
        string VocabPath,
        string ConfigPath);

    /// <summary>Downloads all model files (INT8 by default) and returns their paths.</summary>
    public async Task<ModelFiles> EnsureDownloadedAsync(
        bool useInt8 = true,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_cacheDir);

        var suffix = useInt8 ? ".int8" : "";
        var files = new[] {
            "nemo128.onnx",
            $"encoder-model{suffix}.onnx",
            $"decoder_joint-model{suffix}.onnx",
            "vocab.txt",
            "config.json",
        };

        foreach (var file in files)
            await DownloadFileAsync(file, cancellationToken).ConfigureAwait(false);

        return new ModelFiles(
            PreprocessorPath: Path.Combine(_cacheDir, "nemo128.onnx"),
            EncoderPath: Path.Combine(_cacheDir, $"encoder-model{suffix}.onnx"),
            DecoderJointPath: Path.Combine(_cacheDir, $"decoder_joint-model{suffix}.onnx"),
            VocabPath: Path.Combine(_cacheDir, "vocab.txt"),
            ConfigPath: Path.Combine(_cacheDir, "config.json"));
    }

    private async Task DownloadFileAsync(string filename, CancellationToken cancellationToken)
    {
        var localPath = Path.Combine(_cacheDir, filename);
        if (File.Exists(localPath)) {
            _log.LogInformation("Cached: {FileName}", filename);
            return;
        }

        var url = HuggingFaceBaseUrl + filename;
        _log.LogInformation("Downloading: {FileName}...", filename);

        using var httpClient = new HttpClient();
        using var response = await httpClient
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        var tempPath = localPath + ".tmp";

        var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        try {
            var buffer = new byte[81920];
            long downloadedBytes = 0;
            var lastProgressReport = 0;

            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0) {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                downloadedBytes += bytesRead;

                if (totalBytes.HasValue) {
                    var progress = (int)(downloadedBytes * 100 / totalBytes.Value);
                    if (progress >= lastProgressReport + 10) {
                        lastProgressReport = progress;
                        _log.LogInformation("  {FileName}: {Progress}% ({Downloaded:N0} / {Total:N0} bytes)",
                            filename,
                            progress,
                            downloadedBytes,
                            totalBytes.Value);
                    }
                }
            }
        }
        finally {
            await fileStream.DisposeAsync().ConfigureAwait(false);
            await contentStream.DisposeAsync().ConfigureAwait(false);
        }

        File.Move(tempPath, localPath, overwrite: true);
        _log.LogInformation("Downloaded: {FileName}", filename);
    }
}
