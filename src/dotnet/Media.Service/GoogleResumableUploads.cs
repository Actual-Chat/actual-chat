using System.Net;
using Google.Cloud.Storage.V1;

namespace ActualChat.Media;

internal class GoogleResumableUploads(StorageClient client)
{
    private HttpClient HttpClient => StorageClient.Service.HttpClient;

    public StorageClient StorageClient { get; } = client;

    public async Task<long?> GetUploadStatusAsync(string sessionUrl, CancellationToken cancellationToken)
    {
        // https://docs.cloud.google.com/storage/docs/performing-resumable-uploads#status-check
        var request = new HttpRequestMessage(HttpMethod.Put, sessionUrl);
        request.Headers.TryAddWithoutValidation("Content-Length", "0");
        request.Headers.TryAddWithoutValidation("Content-Range", "bytes */*");

        var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound)
            throw StandardError.Upload.NotFound();

        // Not finished
        if (response.StatusCode is HttpStatusCode.PermanentRedirect) {
            if (response.Headers.TryGetValues("Range", out var values)) {
                string range = values.First();
                var parts = range.Replace("bytes=", "").Split('-');
                long lastByte = long.Parse(parts[1], CultureInfo.InvariantCulture);
                return lastByte + 1;
            }
            return 0;
        }

        response.EnsureSuccessStatusCode();
        return null;
    }

    public async Task<bool> UploadChunk(
        string sessionUrl,
        byte[] buffer,
        long offset,
        long totalSize,
        CancellationToken cancellationToken)
    {
        // https://docs.cloud.google.com/storage/docs/performing-resumable-uploads#resume-upload
        var content = new ByteArrayContent(buffer);
        // Example: bytes 0-262143/104857600
        content.Headers.ContentRange =
            new System.Net.Http.Headers.ContentRangeHeaderValue(
                offset,
                offset + buffer.Length - 1,
                totalSize);
        var response = await HttpClient.PutAsync(sessionUrl, content, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.PermanentRedirect)
            return false; // Upload is not finished yet

        switch (response.StatusCode) {
            case HttpStatusCode.NotFound:
                throw StandardError.Upload.NotFound();
            // NOTE: from docs: "If an upload request is terminated before receiving a response, or if you receive a 503 or 500 response,
            // then you need to resume the interrupted upload from where it left off."
            // See https://cloud.google.com/storage/docs/performing-resumable-uploads#handling_errors
            // Let's force the client to restart the upload procedure by reporting offset conflict.
            case HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable:
                throw StandardError.Upload.OffsetConflict();
            default:
                response.EnsureSuccessStatusCode();
                return true; // Upload is finished
        }
    }

    public async Task CancelUpload(string sessionUrl, CancellationToken cancellationToken)
    {
        // https://docs.cloud.google.com/storage/docs/performing-resumable-uploads#cancel-upload
        var request = new HttpRequestMessage(HttpMethod.Delete, sessionUrl);
        var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound)
            return; // If an upload is not found, consider it as canceled/deleted.
        var isSuccess = response.IsSuccessStatusCode || (int)response.StatusCode == 499;
        if (!isSuccess)
            throw new Exception($"Failed to cancel upload. Status: {response.StatusCode}");
    }
}
