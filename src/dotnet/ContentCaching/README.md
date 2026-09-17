# Content handlers

A MAUI-independent pipeline for immutable content. `IContentHandler.Handle` accepts a
`ContentRequest` and returns an owned `HttpResponseMessage`. A null response means the
native platform should keep handling the request. Callers must dispose returned responses.

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
as do private/no-store responses and `Vary` other than `Origin` or `Accept-Encoding`. A `Set-Cookie`
other than the load balancer's `GCLB` affinity cookie bypasses too; `Set-Cookie` is never stored
either way, so a shared entry cannot replay one reader's cookie. Credential-based cache identity is
not implemented.

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

`Options.ResponseMaxAge` shortens `Cache-Control` on every response this cache serves, so the
caller's own cache holds it only that long instead of keeping a second copy of what is already
stored here. It only ever shortens - `min(origin, configured)` - since this is the durable copy.
`ETag`, `Last-Modified` and `Expires` are dropped along with it: a conditional re-request carries a
header this cache bypasses on, so it would reach the network instead. Leave it null to serve the
stored headers unchanged. MAUI sets 1h; 10s made the WebView re-ask ~8x more often and pushed the
CDN into 503s.

`Options.MaxCachedLength` serves anything longer straight from the source and never stores it.
It only applies to a declared length - an unknown one still streams in, since there is nothing to
check up front. MAUI sets 250 MB, because nothing evicts yet.

`FileSystemContentHandler.Stats` counts per-request outcomes (`Hit`, `JoinedFill`, `StartedFill`,
`Bypass`, `Error`) plus served and fetched byte totals. Served counts bytes handed to readers
through the cache, fetched counts bytes pulled from the downstream handler, so a fill counts in
both and a hit counts only as served. The counters are cumulative and never reset.

MAUI enables the filesystem cache over `HttpContentHandler` on **Android, Windows and Apple**, for
GET requests to our own content origins (`UrlMapper.IsOwnContentUrl` — `cdn.*` and `media.*`). The
root key is `MauiEncryptionKeys.Primary` and the cache lives under `FileSystem.CacheDirectory`. Only
a single byte range is forwarded upstream; every other request header is dropped, `Accept-Encoding`
is pinned to `identity`, and a response carrying `Content-Encoding` is refused - a WebView hands an
intercepted body to the renderer undecoded. `Vary` handling keeps a negotiated representation out
of the cache. The upstream client negotiates HTTP/2: the 1.1 default serialized fills behind the
connection pool once the WebView's own pool was out of the picture.

Every platform uses the same shape: a hit is served from disk, and a miss streams one download that
feeds the WebView and fills the cache at the same time, publishing when the source ends. A second
request for a resource already being fetched goes back to the WebView rather than sharing the
download - `JoinedFill` measured 0 in every run, because Chromium already dedupes its own in-flight
loads. `ReadStream` is seekable during a fill (a read past it waits for that position, and a short
source surfaces as the fill's `EndOfStreamException`), which is what lets WebView2 accept it.

The one thing that must not happen is waiting on the network inside a native callback. Android
gives `shouldInterceptRequest` ~6 threads, so parking them on fills stalls every cache hit queued
behind them and a screenful of images lands at once instead of filling in. Android therefore reads
only what is already cached there, and hands back a body whose first read - on some other thread -
waits for the fetch; the `Content-Type` comes from the URL, since upstream headers haven't arrived.
Windows has no such limit: its `WebResourceRequested` runs under a deferral, so it awaits normally.
Never assign `null` to that event's `Response` - WebView2 access-violates on it; leaving it unset is
what makes the WebView fetch the request itself.

Apple cannot intercept at all: WKWebView refuses a `WKURLSchemeHandler` for `http`/`https`. So
`UrlMapper`'s media URLs return `content://media/<key>` there and `ContentSchemeHandler` resolves
the key back through `LocalContentRegistry`. `UrlMapper.ToOrigin` reverses it for the few consumers
that hand a URL to something outside the WebView, and the image proxy's own inputs, since the proxy
fetches them server-side. Each `DidReceiveData` is a main-thread hop, so the body goes over in 256KB
chunks - one hop covers most media, and a smaller first chunk still paints quickly on a slow link.

Measured on a ~2400-request media grid, cold: p50 0.048s, p90 1.56s, p99 5.0s. Earlier variants on
the same scroll were far worse - completing small fills first gave p90 5.3s, and streaming over
HTTP/1.1 gave p90 30.6s. The HTTP/2 negotiation above is what makes streaming the best option
rather than the worst.

`CoreConstants.DebugMode.ContentCache` turns on the per-request tracing used to reach those
numbers: hits, misses, fill start/finish with sizes and durations, and the native adapters' own
timings. It is off by default and costs nothing there. Note that a Release client pins the log
level to Information, so reading those lines needs `ActualChat_DevLog` set as well.
