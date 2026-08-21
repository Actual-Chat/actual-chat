using System.Net;
using System.Net.Http.Headers;

namespace ActualChat.Transcription.UnitTests;

/// <summary>
/// A stand-in for the parts of the Soniox REST API <see cref="SonioxSweeper"/> depends on: cursor
/// pagination, the transcription delete that takes its file with it, and the refusal to delete one
/// that's still processing.
/// </summary>
public sealed class SonioxApiMock : HttpMessageHandler
{
    private readonly Dictionary<string, Item> _transcriptions = new();
    private readonly Dictionary<string, Item> _files = new();

    public IReadOnlyCollection<string> TranscriptionIds => _transcriptions.Keys;
    public IReadOnlyCollection<string> FileIds => _files.Keys;
    // -1 stands for a request that asked for no page size at all
    public List<int> RequestedPageSizes { get; } = new();
    // Recorded before the request is answered, so a refused delete still counts as attempted
    public List<string> DeleteAttempts { get; } = new();

    public SonioxApiMock AddTranscription(
        string id,
        DateTimeOffset createdAt,
        string status = "completed",
        string? fileId = null)
    {
        _transcriptions.Add(id, new Item(createdAt, status, fileId));
        return this;
    }

    public SonioxApiMock AddFile(string id, DateTimeOffset createdAt)
    {
        _files.Add(id, new Item(createdAt, null, null));
        return this;
    }

    // Protected methods

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var segments = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
        var isTranscription = segments[1] == "transcriptions";
        var items = isTranscription ? _transcriptions : _files;
        if (request.Method == HttpMethod.Delete)
            return Task.FromResult(Delete(items, segments[2], isTranscription));

        var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
        RequestedPageSizes.Add(int.TryParse(query["limit"], out var limit) ? limit : -1);
        return Task.FromResult(List(items, isTranscription, limit, query["cursor"]));
    }

    // Private methods

    private HttpResponseMessage List(
        Dictionary<string, Item> items,
        bool isTranscription,
        int limit,
        string? cursor)
    {
        // The cursor is the last id of the previous page rather than an offset, so a delete
        // between two pages can't shift the ones that follow out of the sweep.
        var page = items
            .Where(x => cursor.IsNullOrEmpty() || string.CompareOrdinal(x.Key, cursor) > 0)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
        var isLastPage = page.Count < limit;
        var json = JsonSerializer.Serialize(new Dictionary<string, object?> {
            [isTranscription ? "transcriptions" : "files"] = page
                .Select(x => ToJson(x.Key, x.Value, isTranscription))
                .ToArray(),
            ["next_page_cursor"] = isLastPage || page.Count == 0 ? null : page[^1].Key,
        });
        return Respond(HttpStatusCode.OK, json);
    }

    private HttpResponseMessage Delete(Dictionary<string, Item> items, string id, bool isTranscription)
    {
        DeleteAttempts.Add(id);
        if (!items.TryGetValue(id, out var item))
            return Respond(HttpStatusCode.NotFound, "");
        if (isTranscription && item.Status is not ("completed" or "error"))
            return Respond(HttpStatusCode.Conflict, "");

        items.Remove(id);
        if (isTranscription && item.FileId is { } fileId)
            _files.Remove(fileId);

        return Respond(HttpStatusCode.NoContent, "");
    }

    private static Dictionary<string, object?> ToJson(string id, Item item, bool isTranscription)
    {
        var json = new Dictionary<string, object?> {
            ["id"] = id,
            ["created_at"] = item.CreatedAt,
        };
        if (isTranscription) {
            json["status"] = item.Status;
            json["file_id"] = item.FileId;
        }
        return json;
    }

    private static HttpResponseMessage Respond(HttpStatusCode statusCode, string json)
    {
        var response = new HttpResponseMessage(statusCode) { Content = new StringContent(json) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return response;
    }

    // Nested types

    private sealed record Item(DateTimeOffset CreatedAt, string? Status, string? FileId);
}
