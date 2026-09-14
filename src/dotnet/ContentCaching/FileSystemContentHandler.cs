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
    private readonly byte[] _encryptionKey;

    private IContentHandler Downstream { get; }
    private ILogger? Log { get; }
    public Options Settings { get; }

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
            return await Downstream.Handle(request, cancellationToken).ConfigureAwait(false);

        var url = Settings.CacheUrlNormalizer(request.Url).AbsoluteUri;
        var (fullPath, fullKey) = GetPath(url);
        if (range != null) {
            var full = await TryRead(fullPath, fullKey, request, range, true, cancellationToken).ConfigureAwait(false);
            if (full != null)
                return full;
        }

        var (path, key) = range == null ? (fullPath, fullKey) : GetPath(url + "\nRange: " + range);
        using var fillLock = await FillLocks.Lock(path, cancellationToken).ConfigureAwait(false);
        while (true) {
            var cached = await TryRead(path, key, request, range, false, cancellationToken).ConfigureAwait(false);
            if (cached != null)
                return cached;

            if (!Downloads.TryGetValue(path, out var active))
                break;
            if (!active.HasEncryptionKey(_encryptionKey))
                return await Downstream.Handle(request, cancellationToken).ConfigureAwait(false);
            if (active.TryCreateResponse(cancellationToken) is { } shared)
                return shared;

            await active.WhenRunning!.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        var response = await Downstream.Handle(request, cancellationToken).ConfigureAwait(false);
        if (response == null || !CanCache(response, range))
            return response;

        EncryptedContentFile? file = null;
        var partialPath = path + ".p";
        Download? download = null;
        try {
            var metadata = ResponseMetadata.FromResponse(response);
            var bytes = metadata.Serialize();
            if (bytes.Length > 64 * 1024)
                return response;

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            try {
                Directory.CreateDirectory(path.DirectoryPath);
                Delete(partialPath);
                file = await EncryptedContentFile.Create(
                    partialPath, key, _encryptionKey, bytes, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (IsStorageError(e)) {
                Log?.LogDebug("Content cache unavailable: {Key}, {ErrorType}", key, e.GetType().Name);
                Delete(partialPath);
                return response;
            }

            download = new Download(
                this, path, request, response, stream, file,
                metadata);
            Downloads[path] = download;
            var result = download.TryCreateResponse(cancellationToken)!;
            file = null;
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

    // Private methods

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
            file = null;
            body.ActivateCancellation();
            cancellationToken.ThrowIfCancellationRequested();
            Touch(path);
            Log?.LogDebug("Content cache hit: {Key}", key);
            return response;
        }
        catch (Exception e) when (IsStorageError(e)
            || e is CryptographicException or InvalidDataException or FormatException or ArgumentException
                or OverflowException) {
            body?.Dispose();
            file?.Dispose();
            Log?.LogDebug("Content cache miss: {Key}, {ErrorType}", key, e.GetType().Name);
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

    private static bool CanCache(HttpResponseMessage response, RangeHeaderValue? range)
    {
        if (response.Headers.CacheControl is { NoStore: true } or { Private: true }
            || response.Headers.Vary.Any(x => !x.Equals("Origin", StringComparison.OrdinalIgnoreCase))
            || response.Headers.Contains("Set-Cookie"))
            return false;
        if (response.StatusCode == HttpStatusCode.OK)
            return response.Content.Headers.ContentRange == null;
        if (response.StatusCode != HttpStatusCode.PartialContent || range == null)
            return false;

        var contentRange = response.Content.Headers.ContentRange;
        return contentRange is { HasRange: true, HasLength: true }
            && contentRange.Unit.Equals("bytes", StringComparison.OrdinalIgnoreCase)
            && TryResolveRange(range, contentRange.Length!.Value, out var start, out var length)
            && contentRange.From == start && contentRange.To == start + length - 1
            && (response.Content.Headers.ContentLength == null || response.Content.Headers.ContentLength == length);
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
            Log?.LogDebug("Could not record content cache access: {ErrorType}", e.GetType().Name);
        }
    }

    private void Delete(FilePath path)
    {
        try {
            File.Delete(path);
        }
        catch (Exception e) when (IsStorageError(e)) {
            Log?.LogDebug("Could not remove content cache file: {ErrorType}", e.GetType().Name);
        }
    }

    private static bool IsStorageError(Exception error)
        => error is IOException or UnauthorizedAccessException;
}
