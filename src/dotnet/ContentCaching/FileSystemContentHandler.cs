using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using ActualChat.Hashing;
using ActualLab.Generators;
using ActualLab.IO;

namespace ActualChat.ContentCaching;

public sealed class FileSystemContentHandler : IContentHandler
{
    public sealed record Options
    {
        public required FilePath Directory { get; init; }
        public required byte[] EncryptionKey { get; init; }
        public int MaxContentLength { get; init; } = 1024 * 1024;
    }

    private const int EnvelopeOverhead = 29;
    private const int MaxMetadataLength = 64 * 1024;
    private readonly byte[] _encryptionKey;

    private IContentHandler Downstream { get; }
    private ILogger? Log { get; }
    public Options Settings { get; }

    public FileSystemContentHandler(Options settings, IContentHandler downstream, ILogger? log = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.MaxContentLength);
        if (settings.MaxContentLength > int.MaxValue - MaxMetadataLength - EnvelopeOverhead)
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
        if (request.Method != HttpMethod.Get || request.ImmutableKey.IsNullOrEmpty() || request.Headers.Count != 0)
            return await Downstream.Handle(request, cancellationToken).ConfigureAwait(false);

        var key = $"{request.ImmutableKey.Length}:{request.ImmutableKey}{request.Url.AbsoluteUri}"
            .Hash().SHA256().AlphaNumeric();
        var path = Settings.Directory & (key + ".cache");
        var cached = await TryRead(path, key, cancellationToken).ConfigureAwait(false);
        if (cached != null)
            return cached;

        var response = await Downstream.Handle(request, cancellationToken).ConfigureAwait(false);
        if (response == null || !CanCache(response))
            return response;

        try {
            var content = response.Content;
            var length = checked((int)content.Headers.ContentLength!.Value);
            var bytes = new byte[length];
            var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            var probe = new byte[1];
            if (await stream.ReadAsync(probe, cancellationToken).ConfigureAwait(false) != 0)
                throw new InvalidDataException("Content exceeded its declared length.");

            var replacement = new ByteArrayContent(bytes);
            CopyHeaders(content.Headers, replacement.Headers);
            response.Content = replacement;
            content.Dispose();
            await TryWrite(path, key, response, bytes, cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch {
            response.Dispose();
            throw;
        }
    }

    // Private methods

    private async Task<HttpResponseMessage?> TryRead(FilePath path, string key, CancellationToken cancellationToken)
    {
        try {
            await using var file = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                4096, FileOptions.Asynchronous);
            if (file.Length < EnvelopeOverhead
                || file.Length > Settings.MaxContentLength + MaxMetadataLength + EnvelopeOverhead)
                return null;

            var envelope = new byte[(int)file.Length];
            await file.ReadExactlyAsync(envelope, cancellationToken).ConfigureAwait(false);
            var plain = Unprotect(envelope, key);
            try {
                var response = Deserialize(plain);
                try {
                    File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
                }
                catch (Exception e) when (IsStorageError(e)) {
                    Log?.LogDebug(e, "Could not record content cache access");
                }
                Log?.LogDebug("Content cache hit: {Key}", key);
                return response;
            }
            finally {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch (Exception e) when (IsStorageError(e)
            || e is CryptographicException or InvalidDataException or FormatException or ArgumentException) {
            Log?.LogDebug("Content cache miss: {Key}, {ErrorType}", key, e.GetType().Name);
            return null;
        }
    }

    private async Task TryWrite(
        FilePath path, string key, HttpResponseMessage response, byte[] body,
        CancellationToken cancellationToken)
    {
        FilePath temporaryPath = path + "." + RandomStringGenerator.Default.Next() + ".tmp";
        try {
            var plain = Serialize(response, body);
            byte[] envelope;
            try {
                if (plain.Length > body.Length + MaxMetadataLength)
                    return;
                envelope = Protect(plain, key);
            }
            finally {
                CryptographicOperations.ZeroMemory(plain);
            }
            Directory.CreateDirectory(Settings.Directory);
            await File.WriteAllBytesAsync(temporaryPath, envelope, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, true);
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
            Log?.LogDebug("Content cache stored: {Key}, {Length} bytes", key, envelope.Length);
        }
        catch (Exception e) when (IsStorageError(e)) {
            Log?.LogWarning(e, "Could not persist content cache entry: {Key}", key);
        }
        finally {
            try {
                File.Delete(temporaryPath);
            }
            catch (Exception e) when (IsStorageError(e)) {
                Log?.LogDebug(e, "Could not remove content cache staging file");
            }
        }
    }

    private bool CanCache(HttpResponseMessage response)
        => response.StatusCode == HttpStatusCode.OK
            && response.Content.Headers.ContentLength is >= 0
            && response.Content.Headers.ContentLength <= Settings.MaxContentLength
            && response.Headers.CacheControl is not { NoStore: true }
            && response.Headers.CacheControl is not { Private: true }
            && response.Headers.Vary.Count == 0
            && !response.Headers.Contains("Set-Cookie")
            && response.Content.Headers.ContentRange == null;

    private byte[] Protect(byte[] plain, string key)
    {
        var envelope = new byte[plain.Length + EnvelopeOverhead];
        envelope[0] = 1;
        RandomNumberGenerator.Fill(envelope.AsSpan(1, 12));
        using var cipher = CreateCipher();
        cipher.Encrypt(envelope.AsSpan(1, 12), plain, envelope.AsSpan(EnvelopeOverhead),
            envelope.AsSpan(13, 16), Encoding.UTF8.GetBytes(key));
        return envelope;
    }

    private byte[] Unprotect(byte[] envelope, string key)
    {
        if (envelope[0] != 1)
            throw new InvalidDataException("Unknown content cache format.");

        var plain = new byte[envelope.Length - EnvelopeOverhead];
        using var cipher = CreateCipher();
        cipher.Decrypt(envelope.AsSpan(1, 12), envelope.AsSpan(EnvelopeOverhead), envelope.AsSpan(13, 16),
            plain, Encoding.UTF8.GetBytes(key));
        return plain;
    }

    private AesGcm CreateCipher()
    {
        var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, _encryptionKey, 32,
            info: "ActualChat.ContentCaching.v1"u8.ToArray());
        try {
            return new AesGcm(key, 16);
        }
        finally {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] Serialize(HttpResponseMessage response, byte[] body)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, Encoding.UTF8, true);
        writer.Write(response.ReasonPhrase ?? "OK");
        writer.Write(response.Version.Major);
        writer.Write(response.Version.Minor);
        WriteHeaders(writer, response.Headers);
        WriteHeaders(writer, response.Content.Headers);
        writer.Write(body.Length);
        writer.Write(body);
        return buffer.ToArray();
    }

    private HttpResponseMessage Deserialize(byte[] plain)
    {
        using var buffer = new MemoryStream(plain, false);
        using var reader = new BinaryReader(buffer, Encoding.UTF8, true);
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        try {
            response.ReasonPhrase = reader.ReadString();
            response.Version = new Version(reader.ReadInt32(), reader.ReadInt32());
            ReadHeaders(reader, response.Headers);
            var contentHeaders = new ByteArrayContent([]);
            response.Content = contentHeaders;
            ReadHeaders(reader, contentHeaders.Headers);
            var length = reader.ReadInt32();
            if (length < 0 || length > Settings.MaxContentLength || buffer.Length - buffer.Position != length)
                throw new InvalidDataException("Invalid cached content length.");

            var content = new ByteArrayContent(reader.ReadBytes(length));
            CopyHeaders(contentHeaders.Headers, content.Headers);
            response.Content = content;
            contentHeaders.Dispose();
            if (content.Headers.ContentLength != length)
                throw new InvalidDataException("Cached content length does not match its headers.");

            return response;
        }
        catch {
            response.Dispose();
            throw;
        }
    }

    private static void WriteHeaders(BinaryWriter writer, HttpHeaders headers)
    {
        var items = headers.ToArray();
        writer.Write(items.Length);
        foreach (var item in items) {
            writer.Write(item.Key);
            var values = item.Value.ToArray();
            writer.Write(values.Length);
            foreach (var value in values)
                writer.Write(value);
        }
    }

    private static void ReadHeaders(BinaryReader reader, HttpHeaders headers)
    {
        var count = ReadCount(reader);
        for (var i = 0; i < count; i++) {
            var name = reader.ReadString();
            var values = new string[ReadCount(reader)];
            for (var j = 0; j < values.Length; j++)
                values[j] = reader.ReadString();
            if (!headers.TryAddWithoutValidation(name, values))
                throw new InvalidDataException("Invalid cached response header.");
        }
    }

    private static int ReadCount(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        if (count is < 0 or > 1024)
            throw new InvalidDataException("Invalid cached header count.");

        return count;
    }

    private static void CopyHeaders(HttpHeaders source, HttpHeaders target)
    {
        foreach (var header in source)
            target.TryAddWithoutValidation(header.Key, header.Value);
    }

    private static bool IsStorageError(Exception error)
        => error is IOException or UnauthorizedAccessException;
}
