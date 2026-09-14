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

GET bodies stream through the cache with bounded buffers, including large videos and unknown
lengths. Headers return before the download finishes. Each source read is encrypted and
published to readers immediately; it does not wait for a full buffer. The default buffer is
64 KiB, configurable downward. Request headers other than a single byte range bypass caching,
as do private/no-store responses, Set-Cookie, and Vary other than Origin. Cookie handling and
credential-based cache identity are not implemented.

A completed full response can serve single byte ranges locally. A cold range is sent directly
to the downstream fetcher and a valid 206 segment is cached separately under the normalized
URL plus its range. Identical ranges share a download; different or overlapping ranges remain
separate entries. Seeking to an uncached video tail does not first download the prefix.
Multipart ranges and conditional range requests retain downstream handling.

Cache fills are coordinated by absolute file path within one process, including across handler
instances. A concurrent dictionary tracks downloads, an AsyncLockSet serializes startup, and
The latest progress is stored separately from a shared Task<long> notification. Readers that
catch up register their wait under the same lock as publication. A write detaches and completes
that task with the available length; tasks do not link to later notifications. A task source is
allocated only when a reader needs to wait. Completion/failure also wakes waiters, even without
a length increase. Every response has its own reader and cursor. Canceling or disposing a
response releases just its reader; leaving no readers cancels the download. A failed download releases the entry for a later retry. Different
entries download independently.

Files use one level of 256 buckets: the first SHA-256 byte in lowercase hex is the directory,
and the complete Base64Url-encoded hash of the UTF-8 cache identity is the filename.
Encrypted staging files append `.p`; completed files have no extension. Stale partials are
never reopened as responses and are replaced by the next fill. Interrupted downloads are
discarded; resuming them or merging sparse ranges is future work.

Metadata and bounded payload records use AES-256-GCM. A fresh salt derives a per-file key
from the supplied root key using HKDF and cache identity. Each record authenticates its
position, and a final authenticated footer commits the total length. Readers authenticate
each record before returning plaintext. Only complete responses are atomically published.
Invalid metadata becomes a cache miss; corrupt payload records fail the current read and
invalidate the entry, including any active writer. Ordinary storage failures fall back to
the downstream representation at the reader's position, checking response metadata before
continuing. No plaintext staging files are written.

Last access is recorded in the file timestamp; stored size is the file length. No index,
Kvasar store, eviction worker, or cross-process coordination exists. Older whole-entry cache
files are treated as misses and replaced.

MAUI currently calls the logging-only pipeline from its existing native request hooks.
It does not enable the filesystem or HTTP handlers, change media URLs, or translate shared
responses into native responses yet. Android sees intercepted resources, Windows observes
image/media resources, and Apple observes its existing custom `content://` scheme.
Remote HTTPS resources on Apple require the later URL-projection step.
