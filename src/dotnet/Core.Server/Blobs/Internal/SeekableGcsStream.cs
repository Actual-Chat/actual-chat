using System.Net;

namespace ActualChat.Blobs.Internal;

/// <summary>
/// A seekable stream over GCS HTTP requests. Sequential reads stream from an open
/// connection without buffering. Seek disposes the current connection; the next
/// Read opens a new Range request from the new position.
/// </summary>
public sealed class SeekableGcsStream(
    HttpClient httpClient,
    string url,
    long length,
    HttpResponseMessage initialResponse,
    Stream initialStream) : Stream
{
    private long _position;
    private long _streamPosition;
    private HttpResponseMessage? _response = initialResponse;
    private Stream? _stream = initialStream;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length { get; } = length;

    public override long Position {
        get => _position;
        set {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => await ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position >= Length)
            return 0;

        if (_stream == null || _streamPosition != _position)
            await OpenConnectionAsync(_position, cancellationToken).ConfigureAwait(false);

        var bytesRead = await _stream!.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        _position += bytesRead;
        _streamPosition += bytesRead;
        return bytesRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var newPosition = origin switch {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        ArgumentOutOfRangeException.ThrowIfNegative(newPosition);
        _position = newPosition;
        return _position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _stream?.Dispose();
            _stream = null;
            _response?.Dispose();
            _response = null;
        }
        base.Dispose(disposing);
    }

    private async Task OpenConnectionAsync(long fromPosition, CancellationToken cancellationToken)
    {
        var oldStream = _stream;
        if (oldStream != null)
            await oldStream.DisposeAsync().ConfigureAwait(false);
        _stream = null;
        _response?.Dispose();
        _response = null;

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (fromPosition > 0)
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(fromPosition, null);

        HttpResponseMessage response;
        try {
            response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch {
            request.Dispose();
            throw;
        }

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) {
            response.Dispose();
            _streamPosition = fromPosition;
            return; // next Read will return 0 bytes (position >= Length)
        }

        try {
            response.EnsureSuccessStatusCode();
            _response = response;
            _stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        }
        catch {
            response.Dispose();
            throw;
        }
        _streamPosition = fromPosition;
    }
}
