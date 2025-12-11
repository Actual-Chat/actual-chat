using Google.Cloud.Storage.V1;

namespace ActualChat.Media;

internal class GoogleResumableUploads(StorageClient client)
{
    private HttpClient HttpClient => client.Service.HttpClient;

    public async Task<long?> GetUploadStatusAsync(string sessionUrl)
    {
        // https://docs.cloud.google.com/storage/docs/performing-resumable-uploads#status-check
        var request = new HttpRequestMessage(HttpMethod.Put, sessionUrl);
        request.Headers.TryAddWithoutValidation("Content-Length", "0");
        request.Headers.TryAddWithoutValidation("Content-Range", "bytes */*");

        var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

        // Not finished
        if ((int)response.StatusCode == 308)
        {
            if (response.Headers.TryGetValues("Range", out var values))
            {
                string range = values.First(); // "bytes=0-524287"
                var parts = range.Replace("bytes=", "").Split('-');
                long lastByte = long.Parse(parts[1]);
                return lastByte + 1; // кол-во загруженных байт
            }
            return 0;
        }

        // Finished (200 / 201)
        if (response.IsSuccessStatusCode)
            return null; // null = объект полностью загружен

        throw new Exception($"Unexpected status: {response.StatusCode}");
    }

    public async Task<bool> UploadChunk(
        string sessionUrl,
        byte[] buffer,
        long offset,
        long totalSize)
    {
        // https://docs.cloud.google.com/storage/docs/performing-resumable-uploads#resume-upload
        var content = new ByteArrayContent(buffer);

        // Пример: bytes 0-262143/104857600
        content.Headers.ContentRange =
            new System.Net.Http.Headers.ContentRangeHeaderValue(
                offset,
                offset + buffer.Length - 1,
                totalSize);

        var response = await HttpClient.PutAsync(sessionUrl, content).ConfigureAwait(false);

        // Google возвращает 308 если загрузка не завершена
        if ((int)response.StatusCode == 308)
            return false;

        // 200 OK или 201 Created = файл полностью загружен
        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task CancelUpload(string sessionUrl, CancellationToken cancellationToken)
    {
        // https://docs.cloud.google.com/storage/docs/performing-resumable-uploads#cancel-upload
        var request = new HttpRequestMessage(HttpMethod.Delete, sessionUrl);
        var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Failed to cancel upload. Status: {response.StatusCode}");
    }
}
