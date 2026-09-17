using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using ActualChat.Hashing;
using ActualLab.IO;
using ActualLab.Locking;

namespace ActualChat.ContentCaching;

public sealed partial class FileSystemContentHandler : IContentHandler
{
    public sealed record Options
    {
        public required FilePath Directory { get; init; }
        public required byte[] EncryptionKey { get; init; }
        public int DownloadBufferSize { get; init; } = 64 * 1024;
        public Func<Uri, Uri> CacheUrlNormalizer { get; init; } = static url => url;
        // Cache-Control advertised on responses this cache serves, so the caller's own cache
        // (a WebView's, typically) holds them briefly instead of duplicating this one.
        // Null leaves the stored headers alone. Validators are stripped when it's set:
        // a conditional re-request would bypass this cache and reach the network instead.
        public TimeSpan? ResponseMaxAge { get; init; }
        // A response longer than this is served straight from the source and never stored.
        // Only applies when the length is declared - an unknown one still streams into the cache.
        public long MaxCachedLength { get; init; } = long.MaxValue;
    }

    private static readonly IEqualityComparer<FilePath> PathComparer = EqualityComparer<FilePath>.Create(
        static (x, y) => StringComparer.OrdinalIgnoreCase.Equals(x.Value, y.Value),
        static path => StringComparer.OrdinalIgnoreCase.GetHashCode(path.Value));
    private static readonly AsyncLockSet<FilePath> FillLocks = new(
        LockReentryMode.Unchecked,
        AsyncLockSet<FilePath>.DefaultConcurrencyLevel,
        AsyncLockSet<FilePath>.DefaultCapacity,
        PathComparer);
    private static readonly ConcurrentDictionary<FilePath, Download> Downloads = new(PathComparer);

    // GCLB is the Google load balancer's affinity cookie: no identity, no bearing on the body.
    // It's dropped from the stored metadata either way, so no entry can ever replay one.
    private static readonly string[] IgnoredCookieNames = ["GCLB"];
    // Every fetch pins Accept-Encoding to identity, so it cannot select a representation
    private static readonly string[] IgnoredVaryNames = ["Origin", "Accept-Encoding"];
    private readonly byte[] _encryptionKey;

    private static bool DebugMode => CoreConstants.DebugMode.ContentCache;

    private IContentHandler Downstream { get; }
    private ILogger? Log { get; }
    private ILogger? DebugLog => DebugMode ? Log : null;
    public Options Settings { get; }
    public ContentCacheStats Stats { get; } = new();

    public FileSystemContentHandler(Options settings, IContentHandler downstream, ILogger? log = null)
    {
        if (settings.DownloadBufferSize is <= 0 or > 64 * 1024)
            throw new ArgumentOutOfRangeException(nameof(settings));
        if (settings.EncryptionKey.Length != 32)
            throw new ArgumentException("A 32-byte encryption key is required.", nameof(settings));
        if (settings.Directory.IsEmpty)
            throw new ArgumentException("A cache directory is required.", nameof(settings));

        Settings = settings;
        Downstream = downstream;
        Log = log;
        _encryptionKey = settings.EncryptionKey.ToArray();
    }

    public async ValueTask<HttpResponseMessage?> Handle(
        ContentRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetRange(request, out var range))
            return await Bypass(request, "request headers", cancellationToken).ConfigureAwait(false);

        var url = Settings.CacheUrlNormalizer(request.Url).AbsoluteUri;
        var (fullPath, fullKey) = GetPath(url);
        if (range != null) {
            var full = await TryRead(fullPath, fullKey, request, range, true, cancellationToken).ConfigureAwait(false);
            if (full != null) {
                Stats.Report(ContentCacheOutcome.Hit);
                return full;
            }
        }

        var (path, key) = range == null ? (fullPath, fullKey) : GetPath(url + "\nRange: " + range);
        using var fillLock = await FillLocks.Lock(path, cancellationToken).ConfigureAwait(false);
        while (true) {
            var cached = await TryRead(path, key, request, range, false, cancellationToken).ConfigureAwait(false);
            if (cached != null) {
                Stats.Report(ContentCacheOutcome.Hit);
                return cached;
            }

            if (!Downloads.TryGetValue(path, out var active))
                break;
            if (!active.HasEncryptionKey(_encryptionKey))
                return await Bypass(request, "encryption key", cancellationToken).ConfigureAwait(false);
            if (active.TryCreateResponse(cancellationToken) is { } shared) {
                Stats.Report(ContentCacheOutcome.JoinedFill);
                DebugLog?.LogDebug("Content cache fill joined: {Key}", key);
                return shared;
            }

            await active.WhenRunning!.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        var response = await Downstream.Handle(request, cancellationToken).ConfigureAwait(false);
        if (response == null) {
            Stats.Report(ContentCacheOutcome.Bypass);
            Log?.LogWarning("Content cache bypass (no response): {Key}", key);
            return null;
        }

        if (GetBypassReason(response, range) is { } bypassReason) {
            Stats.Report(ContentCacheOutcome.Bypass);
            // Path only: a signed URL carries its token in the query
            Log?.LogWarning("Content cache bypass: {StatusCode}, {Reason}, {Url}",
                response.StatusCode, bypassReason, request.Url.GetLeftPart(UriPartial.Path));
            return response;
        }

        EncryptedContentFile? file = null;
        var partialPath = path + ".p";
        Download? download = null;
        try {
            var metadata = ResponseMetadata.FromResponse(response);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var bytes = metadata.Serialize();
            if (bytes.Length > 64 * 1024) {
                Stats.Report(ContentCacheOutcome.Bypass);
                Log?.LogWarning("Content cache bypass (metadata size): {Key}, {Size}", key, bytes.Length);
                return response;
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            try {
                Directory.CreateDirectory(path.DirectoryPath);
                Delete(partialPath);
                file = await EncryptedContentFile.Create(
                    partialPath, key, _encryptionKey, bytes, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (IsStorageError(e)) {
                Stats.Report(ContentCacheOutcome.Bypass);
                Log?.LogWarning("Content cache unavailable: {Key}, {ErrorType}", key, e.GetType().Name);
                Delete(partialPath);
                return response;
            }

            download = new Download(
                this, path, request, response, stream, file,
                metadata);
            Downloads[path] = download;
            var result = download.TryCreateResponse(cancellationToken)!;
            file = null;
            Stats.Report(ContentCacheOutcome.StartedFill);
            DebugLog?.LogDebug("Content cache fill started: {Key}, {ContentType}, {Length}",
                key, contentType, metadata.ExpectedLength);
            return result;
        }
        catch {
            if (download != null) {
                await download.Stop().ConfigureAwait(false);
                Downloads.TryRemove(KeyValuePair.Create(path, download));
            }
            file?.Dispose();
            Delete(partialPath);
            response.Dispose();
            throw;
        }
    }

    // Returns a response only when the representation is already published, so its body
    // is entirely on disk and seekable; never fetches and never starts a fill.
    public async ValueTask<HttpResponseMessage?> TryHandleCached(
        ContentRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetRange(request, out var range))
            return null;

        var url = Settings.CacheUrlNormalizer(request.Url).AbsoluteUri;
        var (fullPath, fullKey) = GetPath(url);
        if (range != null) {
            var full = await TryRead(fullPath, fullKey, request, range, true, cancellationToken).ConfigureAwait(false);
            if (full != null) {
                Stats.Report(ContentCacheOutcome.Hit);
                return full;
            }
        }

        var (path, key) = range == null ? (fullPath, fullKey) : GetPath(url + "\nRange: " + range);
        var cached = await TryRead(path, key, request, range, false, cancellationToken).ConfigureAwait(false);
        if (cached == null)
            return null;

        Stats.Report(ContentCacheOutcome.Hit);
        return cached;
    }

    // Private methods

    private async ValueTask<HttpResponseMessage?> Bypass(
        ContentRequest request, string reason, CancellationToken cancellationToken)
    {
        Stats.Report(ContentCacheOutcome.Bypass);
        Log?.LogWarning("Content cache bypass ({Reason}): {Method} {Host}", reason, request.Method, request.Url.Host);
        return await Downstream.Handle(request, cancellationToken).ConfigureAwait(false);
    }

    private (FilePath Path, string Key) GetPath(string identity)
    {
        var hash = identity.Hash(Encoding.UTF8).SHA256();
        var key = hash.Base64Url();
        return ((Settings.Directory & hash.Bytes[0].ToString("x2") & key).FullPath, key);
    }

    private async Task<HttpResponseMessage?> TryRead(
        FilePath path, string key, ContentRequest request,
        RangeHeaderValue? range, bool mustSelectRange,
        CancellationToken cancellationToken)
    {
        EncryptedContentFile? file = null;
        ReadStream? body = null;
        try {
            // Opening an absent entry would throw on the most common path of all
            if (!File.Exists(path)) {
                DebugLog?.LogDebug("Content cache miss: {Key}", key);
                return null;
            }

            file = await EncryptedContentFile.Open(path, key, _encryptionKey, cancellationToken).ConfigureAwait(false);
            var metadata = ResponseMetadata.Deserialize(file.Metadata);
            if (metadata.ExpectedLength is { } expected && expected != file.Length)
                throw new InvalidDataException("Cached content length does not match its metadata.");

            using var probe = metadata.CreateResponse(Stream.Null, file.Length);
            if (!CanCache(probe, mustSelectRange ? null : range))
                throw new InvalidDataException("Invalid cached response metadata.");

            long offset = 0;
            var length = file.Length;
            if (mustSelectRange && !TryResolveRange(range!, length, out offset, out length)) {
                file.Dispose();
                file = null;
                return new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable) {
                    Content = new ByteArrayContent([]) {
                        Headers = {
                            ContentRange = new ContentRangeHeaderValue(probe.Content.Headers.ContentLength!.Value),
                        },
                    },
                };
            }

            var sourceRequest = mustSelectRange ? new ContentRequest(request.Url) : request;
            body = new ReadStream(
                this, path, file, offset, length, null,
                sourceRequest, metadata, cancellationToken);
            var response = metadata.CreateResponse(body, length);
            if (mustSelectRange) {
                response.StatusCode = HttpStatusCode.PartialContent;
                response.ReasonPhrase = null;
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(
                    offset, offset + length - 1, file.Length);
            }
            response.Headers.AcceptRanges.Clear();
            response.Headers.AcceptRanges.Add("bytes");
            ApplyResponseMaxAge(response);
            file = null;
            body.ActivateCancellation();
            cancellationToken.ThrowIfCancellationRequested();
            Touch(path);
            DebugLog?.LogDebug("Content cache hit: {Key}", key);
            return response;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) {
            body?.Dispose();
            file?.Dispose();
            DebugLog?.LogDebug("Content cache miss: {Key}", key);
            return null;
        }
        catch (Exception e) when (IsStorageError(e)
            || e is CryptographicException or InvalidDataException or FormatException or ArgumentException
                or OverflowException) {
            body?.Dispose();
            file?.Dispose();
            Stats.Report(ContentCacheOutcome.Error);
            Log?.LogWarning("Content cache entry is unusable: {Key}, {ErrorType}", key, e.GetType().Name);
            return null;
        }
        catch {
            body?.Dispose();
            file?.Dispose();
            throw;
        }
    }

    private static bool TryGetRange(ContentRequest request, out RangeHeaderValue? range)
    {
        range = null;
        if (request.Method != HttpMethod.Get)
            return false;
        if (request.Headers.Count == 0)
            return true;
        if (request.Headers.Count != 1)
            return false;

        var header = request.Headers.Single();
        if (!header.Key.Equals("Range", StringComparison.OrdinalIgnoreCase)
            || !RangeHeaderValue.TryParse(header.Value, out range)
            || !range.Unit.Equals("bytes", StringComparison.OrdinalIgnoreCase)
            || range.Ranges.Count != 1)
            return false;

        range.Unit = "bytes";
        return true;
    }

    private bool CanCache(HttpResponseMessage response, RangeHeaderValue? range)
        => GetBypassReason(response, range) == null;

    // Null when the response can be cached, otherwise the rule that rejected it
    private string? GetBypassReason(HttpResponseMessage response, RangeHeaderValue? range)
    {
        if (response.Content.Headers.ContentLength > Settings.MaxCachedLength)
            return "too long";
        if (response.Headers.CacheControl is { NoStore: true })
            return "no-store";
        if (response.Headers.CacheControl is { Private: true })
            return "private";
        var cookie = GetSetCookieNames(response).FirstOrDefault(
            x => !IgnoredCookieNames.Contains(x, StringComparer.OrdinalIgnoreCase));
        if (cookie != null)
            return "set-cookie " + cookie;

        var vary = response.Headers.Vary.FirstOrDefault(
            x => !IgnoredVaryNames.Contains(x, StringComparer.OrdinalIgnoreCase));
        if (vary != null)
            return "vary " + vary;
        if (response.StatusCode == HttpStatusCode.OK)
            return response.Content.Headers.ContentRange == null ? null : "content-range on 200";
        if (response.StatusCode != HttpStatusCode.PartialContent)
            return "status " + (int)response.StatusCode;
        if (range == null)
            return "unrequested 206";

        var contentRange = response.Content.Headers.ContentRange;
        return contentRange is { HasRange: true, HasLength: true }
            && contentRange.Unit.Equals("bytes", StringComparison.OrdinalIgnoreCase)
            && TryResolveRange(range, contentRange.Length!.Value, out var start, out var length)
            && contentRange.From == start && contentRange.To == start + length - 1
            && (response.Content.Headers.ContentLength == null || response.Content.Headers.ContentLength == length)
                ? null
                : "content-range mismatch";
    }

    private static bool TryResolveRange(RangeHeaderValue range, long total, out long start, out long length)
    {
        var item = range.Ranges.Single();
        start = item.From ?? Math.Max(0, total - item.To!.Value);
        var end = item.From == null ? total - 1 : Math.Min(item.To ?? total - 1, total - 1);
        length = start < total && end >= start ? end - start + 1 : 0;
        return length > 0;
    }

    private void Touch(FilePath path)
    {
        try {
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception e) when (IsStorageError(e)) {
            DebugLog?.LogDebug("Could not record content cache access: {ErrorType}", e.GetType().Name);
        }
    }

    private void Delete(FilePath path)
    {
        try {
            File.Delete(path);
        }
        catch (Exception e) when (IsStorageError(e)) {
            DebugLog?.LogDebug("Could not remove content cache file: {ErrorType}", e.GetType().Name);
        }
    }

    private HttpResponseMessage ApplyResponseMaxAge(HttpResponseMessage response)
    {
        if (Settings.ResponseMaxAge is not { } maxAge)
            return response;

        // Only ever shorten what the origin asked for: this cache is the durable copy, so the
        // caller's needs to hold the bytes for less time, never more
        var cacheControl = response.Headers.CacheControl;
        if (cacheControl?.MaxAge is { } originMaxAge && originMaxAge < maxAge)
            maxAge = originMaxAge;
        response.Headers.CacheControl = new CacheControlHeaderValue { Public = true, MaxAge = maxAge };
        // Without these the caller must re-request rather than revalidate, and a plain
        // re-request is the one this cache can answer
        response.Headers.ETag = null;
        response.Content.Headers.LastModified = null;
        response.Content.Headers.Expires = null;
        return response;
    }

    private static IEnumerable<string> GetSetCookieNames(HttpResponseMessage response)
        => response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(x => x.Split('=', 2)[0].Trim())
            : [];

    private static bool IsStorageError(Exception error)
        => error is IOException or UnauthorizedAccessException;
}
