# Content handlers

A MAUI-independent pipeline for immutable content. `IContentHandler.Handle` accepts a
`ContentRequest` and returns an owned `HttpResponseMessage`. A null response means the
native platform should keep handling the request. Callers must dispose returned responses.

- `LoggingContentHandler` logs the method, host, and a hash of the URL, then delegates.
  With no downstream it returns null synchronously, making it suitable for observation
  without changing native handling. URL paths, credentials, and query values are not logged.
- `HttpContentHandler` delegates to the supplied `HttpClient`, returns headers immediately,
  and leaves the body streaming. The caller owns the client; normal HTTP failures retain
  their status codes.
- `FileSystemContentHandler` decorates any downstream handler with an encrypted filesystem
  cache for immutable media. By default, the full URL identifies each entry. Configure
  `Options.CacheUrlNormalizer` to remove CDN-specific signing parameters from cache identity;
  the downloader still receives the original signed URL. Preserve parameters that change
  content, such as image size or version. Route only immutable content through this handler.

The first cache stores complete GET responses with known lengths up to 1 MiB (configurable).
Requests carrying headers, including ranges and credentials, and responses with private,
no-store, varying, or cookie state bypass persistence. Larger or unknown-length responses
retain their downstream streams without being read by the cache.

Payload and response metadata are encrypted together with AES-256-GCM, with a fresh nonce
per write and an HKDF key derived for this cache. The cache identity is authenticated to
prevent file swaps. Only a complete response is atomically published; corruption and
unavailable storage become misses. Last access is recorded in the file timestamp; stored
size is the file length. No index, Kvasar store, eviction worker, or sparse-range cache exists.

MAUI currently calls the logging-only pipeline from its existing native request hooks.
It does not enable the filesystem or HTTP handlers, change media URLs, or translate shared
responses into native responses yet. Android sees intercepted resources, Windows observes
image/media resources, and Apple observes its existing custom `content://` scheme.
Remote HTTPS resources on Apple require the later URL-projection step.
