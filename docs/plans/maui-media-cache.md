# MAUI media asset cache

Status: first foundation implemented on `codex/maui-content-cache`; tracked by
[#4512](https://github.com/Actual-Chat/actual-chat/issues/4512).

## This branch

Extract a MAUI-independent content-handler pipeline into `ActualChat.ContentCaching`.
A null result leaves handling to the native platform; a non-null result owns a response
stream. Logging, HTTP fetching, and filesystem caching compose through the same contract.

MAUI calls only the logging handler in this phase, leaving media loading unchanged.
Android observes intercepted requests; Windows observes image/media requests; Apple observes
the existing `content://` handler. Remote HTTPS on Apple will need URL projection before
it can reach this pipeline. Native response adaptation and key/path wiring come later.

The filesystem handler progressively caches large and unknown-length GET bodies with bounded
buffers. Readers share an active download and independently consume authenticated records
as they arrive. Completed full responses serve byte ranges locally; cold ranges fetch directly
from the source and cache validated 206 segments separately. Identical ranges coalesce.
Multipart/conditional ranges, other request headers, and private/no-store/cookie responses
bypass persistence. Vary is accepted only for Origin when the request has no Origin header.

The full representation URL identifies each cache entry by default. A configurable URL
normalizer removes CDN-specific signing parameters while retaining content variants; the
downloader uses the original signed URL. Range entries include the requested range in their
identity. Header/cookie-based identity is outside this phase.

Use one directory level of 256 buckets: the first SHA-256 byte as two lowercase hex
characters, then the full Base64Url hash of the UTF-8 identity as the filename. Encrypted
staging uses .p; complete responses publish by atomic rename to extensionless files.

Coordinate startup with AsyncLockSet and active downloads with a concurrent dictionary.
AsyncState publishes available lengths and completion/failure without retaining a replay
history. Each reader owns its cursor and cancellation; the final departing reader cancels
the source. Coordination includes handler instances sharing an absolute path in one process.

Encrypt metadata and bounded records with AES-256-GCM and a per-file HKDF key bound to
cache identity and generation. Authenticate record positions and the final length. Corrupt
records fail the read and invalidate the entry; storage I/O failures can continue through
the downstream fetcher at the current position after checking representation metadata.
Record access time and physical file size for later eviction; no Kvasar index is needed.

## Next decisions

**URL projection:** extend `UrlMapper` with optional app-only projection, disabled by default.
Construct the complete canonical representation URL, including image-proxy/resize options,
before projecting it to a native cache route. Never embed a local route in a remote proxy URL.
Use app-issued opaque references as `LocalContentRegistry` does; the native adapter resolves
the original URL and passes it to the shared cache/fetcher pipeline. Cache identity uses the
normalized remote URL, not the local route. Persisted messages, RPC data, and native
download/share consumers retain canonical URLs or open the cache directly.

Derive scopes from `ContentBaseUrl` and `ImageProxyBaseUrl` using URI origin/path matching,
covering same-host content paths and local worktree hosts. Include generated-avatar GETs
under `ApiBaseUrl + "avatars/"`, preserving query variants. Local SVG/blob avatars and
upload previews need no remote cache.

Include Klipy picker previews, recents, and posted GIFs through `GifProxyUrl`; keep preview
and full renditions separate and stored URLs canonical. Existing limits: message detection
requires HTTPS `static.klipy.com` ending in `.gif`; without an image proxy, picker previews
are direct and messages are links. Legacy direct external pictures, including remaining
DiceBear bot/admin URLs, may remain uncached. Do not add a general external-origin rule.

**Server reuse:** the shared handler/fetcher contract operates on canonical URLs without
MAUI or local-route dependencies. Keep filesystem layout an implementation/configuration
choice: the one-level 256-bucket scheme targets MAUI, while a future API-server cache with
millions of entries may use deeper sharding or a different backend. Defer server/RPC
integration and coordination between server processes.

**Further range storage:** interrupted partials are currently discarded, and different ranges
remain separate files. Resume and sparse-range merging can follow once retention and
representation validation warrant the extra state. RPC message size must not dictate disk
record size or playback startup latency.

**Platform delivery:** Android can return a progressive stream. Apple can incrementally call
[`WKURLSchemeTask.didReceive`](https://developer.apple.com/documentation/webkit/wkurlschemetask/didreceive%28_%3A%29-8t5f8);
stopping a request must cancel its reader and prevent later native callbacks. Windows needs a
loopback response adapter: the [WebView2 response-content contract](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2webresourceresponse.content)
requires complete response data when the deferral ends. Validate loopback origin/CORS and
capability routing before enabling it. Preserve existing local-upload preview handling.

**Persistence and access:** use app-owned persistent storage excluded from backup. Reuse
`MauiPreferences.DbEncryptionKey` as the root key, retaining its current Preferences-backed
protection model. Keep session/access invalidation distinct from LRU retention. The eventual
background eviction worker uses last access and total physical size; it is outside this work.

**RPC source:** add an offset/length-capable RPC downloader that identifies the same original
or transformed representation as HTTP. Keep CDN reachability and fallback selection above
the fetchers. Do not combine bytes until range responses and representation identity agree.

## Reuse

**Existing abstractions:** use `FilePath`, `AsyncLockSet<FilePath>`, `AsyncState<T>`, `WorkerBase`, and SHA-256 /
Base64Url helpers from Core/Fusion; BCL `HttpRequestMessage`/`HttpResponseMessage` and
`HttpClient.ResponseHeadersRead` for the streaming HTTP boundary; BCL `AesGcm` and `HKDF`
for encryption with the same cipher family as the database cache. Existing native WebView
hooks supply the observation points. Fusion's `FileSystemCache` is a text key/value store;
it does not provide the encrypted response/stream contract. No existing abstraction fits
the complete pipeline.

Later reuse `UrlMapper`/`UrlMapperExt`, `LocalContentRegistry`, and
`MauiPreferences.DbEncryptionKey`. A separate `KvasarKvas` catalog and Kvasar page cipher
are options once sparse downloads and retention justify them, rather than dependencies of
the first file format.

**Placement:** the contract, logging decorator, HTTP fetcher, and file cache are reusable
outside MAUI. Both Core and a dedicated shared project are suitable; prefer
`ActualChat.ContentCaching` referencing Core to keep the subsystem independently testable
and its dependencies explicit. Keep native adapters and lifecycle/key wiring in App.Maui.
Put future media URL projection beside the shared mapper and enable it only for MAUI.
WebAssembly and new TypeScript infrastructure are outside scope.

## Verification boundary

This branch tests encrypted restart hits, identity isolation, corrupted/torn entries,
progressive large/unknown-length reads, byte ranges, shared downloads, cancellation, stream
ownership, failed publication, hash buckets, stale partials, and streaming bypass behavior.
Enabling media routing requires separate device tests for offline images, throttled video
startup, seeking into an uncached tail, and cancellation on each native platform.
